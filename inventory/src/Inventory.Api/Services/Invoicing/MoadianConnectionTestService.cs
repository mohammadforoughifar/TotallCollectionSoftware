using System.Diagnostics;
using System.Net;
using System.Security.Authentication;
using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TaxCollectData.Library.Abstraction;
using TaxCollectData.Library.Dto.Config;
using TaxCollectData.Library.Dto.Properties;
using TaxCollectData.Library.Enums;
using TaxCollectData.Library.Extensions;

namespace Inventory.Api.Services.Invoicing;

public interface IMoadianConnectionTestService
{
    /// <summary>
    /// تست اتصال و احراز هویت یک نسخهٔ ConnectInfo با کلید خصوصی همان نسخه.
    /// هیچ اطلاعات حساسی (کلید، مسیر کلید، توکن) در نتیجه یا لاگ ظاهر نمی‌شود.
    /// </summary>
    Task<MoadianConnectionTestResultDto> TestAsync(int connectionId, CancellationToken cancellationToken);
}

/// <summary>
/// تست اتصال خدمات‌دهنده به سامانهٔ مودیان در سه گام:
/// ۱) برقراری ارتباط با سامانه (GET_SERVER_INFORMATION) — DNS/TLS/مسیر سرویس.
/// ۲) احراز هویت (GET_TOKEN) — امضای درخواست با کلید خصوصی همان نسخه و شناسهٔ خدمات‌دهنده.
/// ۳) استعلام شناسهٔ حافظهٔ مالیاتی (GET_FISCAL_INFORMATION) با توکن دریافت‌شده.
/// تست هیچ فاکتوری ارسال نمی‌کند و هیچ وضعیتی را در سامانه تغییر نمی‌دهد.
/// </summary>
public sealed class MoadianConnectionTestService : IMoadianConnectionTestService
{
    /// <summary>مهلت هر درخواست HTTP و مهلت کل تست.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan OverallTimeout = TimeSpan.FromSeconds(100);

    private readonly AppDbContext _db;
    private readonly MoadianRuntimeSettings _settings;
    private readonly ILogger<MoadianConnectionTestService> _logger;

    public MoadianConnectionTestService(
        AppDbContext db,
        MoadianRuntimeSettings settings,
        ILogger<MoadianConnectionTestService> logger)
    {
        _db = db;
        _settings = settings;
        _logger = logger;
    }

    public async Task<MoadianConnectionTestResultDto> TestAsync(int connectionId, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new MoadianConnectionTestResultDto();

        var connection = await _db.MoadianProviderConnections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == connectionId, cancellationToken)
            ?? throw new InvalidOperationException("اتصال خدمات‌دهنده یافت نشد.");
        if (connection.IsDeleted)
            throw new InvalidOperationException("این نسخهٔ اتصال غیرفعال است؛ برای تست، نسخهٔ فعال را انتخاب کنید.");

        var provider = await _db.MoadianServiceProviderProfiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == connection.ServiceProviderId && !x.IsDeleted, cancellationToken)
            ?? throw new InvalidOperationException("برای تست اتصال، خدمات‌دهنده باید فعال باشد.");

        // ---------- پیش‌نیازها: کلید خصوصی، آدرس سرویس، نسخهٔ API ----------
        if (string.IsNullOrWhiteSpace(connection.PrivateKeyPath) || !File.Exists(connection.PrivateKeyPath))
        {
            result.Message = "برای این نسخهٔ اتصال فایل کلید خصوصی ثبت نشده است (یا فایل در سرور موجود نیست)؛ ابتدا فایل .key را بارگذاری کنید.";
            return await RecordResultAsync(connectionId, result, success: false, stopwatch, cancellationToken);
        }

        var privateKeyPath = connection.PrivateKeyPath;
        var clientType = ResolveClientType(_settings.ClientType);
        result.ClientType = clientType == ClientType.TSP ? "TSP" : "SELF_TSP";

        if (!TryBuildBaseUrl(connection.WebServiceAddress, _settings.BaseUrl, clientType, out var baseUrl, out var urlError))
        {
            result.Message = urlError;
            return await RecordResultAsync(connectionId, result, success: false, stopwatch, cancellationToken);
        }
        result.BaseUrl = baseUrl;

        var apiVersion = (_settings.ApiVersion ?? "").Trim();
        result.ApiVersion = apiVersion.Length == 0 ? "(بدون نسخه — سبک v2)" : apiVersion;
        if (!IsSupportedApiVersion(apiVersion))
        {
            result.Message = "مقدار Moadian:ApiVersion باید خالی (سبک v2) یا v1 باشد؛ SDK نسخهٔ 0.0.34 برای سایر مقادیر سرویس انتقال را ثبت نمی‌کند.";
            return await RecordResultAsync(connectionId, result, success: false, stopwatch, cancellationToken);
        }

        var keyId = (_settings.SignatureKeyId ?? "").Trim();
        result.TaxMemoryId = connection.TaxMemoryID;

        using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        overallCts.CancelAfter(OverallTimeout);
        var token = overallCts.Token;

        // ---------- گام ۱: برقراری ارتباط با سامانه ----------
        var endpoints = BuildEndpoints(baseUrl, clientType, apiVersion);
        result.Endpoints.AddRange(endpoints.Values);

        var serverInformation = await RunServerInformationAsync(baseUrl, privateKeyPath, keyId, clientType, apiVersion, result, token);
        if (serverInformation is null)
            return await RecordResultAsync(connectionId, result, success: false, stopwatch, token);

        // ---------- گام ۲ و ۳: احراز هویت با شناسهٔ خدمات‌دهنده ----------
        // شناسهٔ احراز هویت (username در GET_TOKEN) در قراردادهای مختلف یکی از این سه مقدار است؛
        // اولویت با شناسهٔ خدمات‌دهنده و در نهایت شناسهٔ حافظهٔ مالیاتی همین نسخه است.
        var identities = BuildIdentityCandidates(provider.NationalID, provider.EconomicNumber, connection.TaxMemoryID);
        if (identities.Count == 0)
        {
            result.Message = "برای تست احراز هویت، «شناسه ملی»، «شماره اقتصادی» یا «شناسه حافظهٔ مالیاتی» باید ثبت شده باشد.";
            return await RecordResultAsync(connectionId, result, success: false, stopwatch, token);
        }

        var authenticated = false;
        foreach (var (fieldLabel, identity) in identities)
        {
            var authenticationSucceeded = await RunAuthenticationAsync(
                baseUrl, privateKeyPath, keyId, clientType, apiVersion, identity, fieldLabel, connection.TaxMemoryID, result, token);
            if (!authenticationSucceeded) continue;

            result.ClientIdUsed = identity;
            result.IdentityField = fieldLabel;
            authenticated = true;
            break;
        }

        if (!authenticated)
        {
            result.Message = identities.Count == 1
                ? "ارتباط با سامانه برقرار شد، اما احراز هویت این خدمات‌دهنده ناموفق بود (جزئیات در گام‌ها)."
                : "ارتباط با سامانه برقرار شد، اما احراز هویت با هیچ‌کدام از شناسه‌های خدمات‌دهنده (شناسه ملی / شماره اقتصادی) موفق نبود (جزئیات در گام‌ها).";
            return await RecordResultAsync(connectionId, result, success: false, stopwatch, token);
        }

        // گام ۳ داخل RunAuthenticationAsync اجرا و در صورت موفقیت، به نتیجه اضافه می‌شود.
        var fiscalStep = result.Steps.LastOrDefault(x => x.Key == "fiscal");
        if (fiscalStep is null)
        {
            result.Message = "ارتباط برقرار شد و احراز هویت موفق بود؛ اما استعلام شناسهٔ حافظهٔ مالیاتی در این نسخه از SDK پاسخ نداد.";
        }
        else if (fiscalStep.Ok)
        {
            result.Message = $"ارتباط با سامانه برقرار است و احراز هویت خدمات‌دهنده معتبر است" +
                             (string.IsNullOrWhiteSpace(result.TaxpayerName) ? "." : $" (نام تجاری: {result.TaxpayerName}).");
        }
        else
        {
            result.Message = $"ارتباط برقرار شد و احراز هویت موفق بود؛ اما استعلام شناسهٔ حافظهٔ مالیاتی ناموفق بود: {fiscalStep.Detail}";
        }

        return await RecordResultAsync(connectionId, result, success: true, stopwatch, token);
    }

    private async Task<object?> RunServerInformationAsync(
        string baseUrl,
        string privateKeyPath,
        string keyId,
        ClientType clientType,
        string apiVersion,
        MoadianConnectionTestResultDto result,
        CancellationToken cancellationToken)
    {
        var step = new MoadianConnectionTestStepDto { Key = "server-info", Title = "برقراری ارتباط با سامانه (GET_SERVER_INFORMATION)" };
        var stopwatch = Stopwatch.StartNew();
        var (sdk, provider) = CreateTaxApis(baseUrl, privateKeyPath, keyId, clientType, apiVersion, Guid.NewGuid().ToString("N"));
        try
        {
            var serverInformation = await sdk.GetServerInformationAsync().WaitAsync(cancellationToken);
            step.DurationMs = stopwatch.ElapsedMilliseconds;
            if (serverInformation is null)
            {
                step.Ok = false;
                step.Detail = "سامانه پاسخ قابل تفسیری برنگرداند (آدرس یا مسیر سرویس را بررسی کنید).";
                result.Steps.Add(step);
                return null;
            }

            step.Ok = true;
            result.PublicKeyId = serverInformation.PublicKeys?.FirstOrDefault()?.Id;
            step.Detail = serverInformation.PublicKeys is { Count: > 0 }
                ? "پاسخ سامانه دریافت شد (کلید عمومی سازمان مالیاتی دریافت شد)."
                : "پاسخ سامانه دریافت شد.";
            result.Steps.Add(step);
            return serverInformation;
        }
        catch (Exception ex)
        {
            step.DurationMs = stopwatch.ElapsedMilliseconds;
            step.Ok = false;
            step.Detail = DescribeFailure(ex, cancellationToken);
            result.Steps.Add(step);
            _logger.LogWarning("Moadian connection test step {Step} failed with {ExceptionType}.", step.Key, ex.GetType().Name);
            return null;
        }
        finally
        {
            DisposeProvider(provider);
        }
    }

    private async Task<bool> RunAuthenticationAsync(
        string baseUrl,
        string privateKeyPath,
        string keyId,
        ClientType clientType,
        string apiVersion,
        string identity,
        string identityField,
        string taxMemoryId,
        MoadianConnectionTestResultDto result,
        CancellationToken cancellationToken)
    {
        var step = new MoadianConnectionTestStepDto { Key = "auth", Title = $"احراز هویت با {identityField}" };
        var stopwatch = Stopwatch.StartNew();
        var (sdk, provider) = CreateTaxApis(baseUrl, privateKeyPath, keyId, clientType, apiVersion, identity);
        try
        {
            var token = await sdk.RequestTokenAsync().WaitAsync(cancellationToken);
            step.DurationMs = stopwatch.ElapsedMilliseconds;
            if (token is null || string.IsNullOrWhiteSpace(token.Token))
            {
                step.Ok = false;
                step.Detail = "سامانه توکن برنگرداند؛ یعنی امضا/شناسهٔ ارسالی پذیرفته نشد.";
                result.Steps.Add(step);
                DisposeProvider(provider);
                return false;
            }

            // توکن هرگز در نتیجه یا لاگ ثبت نمی‌شود؛ فقط دریافت آن گزارش می‌شود.
            sdk.SetToken(token);
            step.Ok = true;
            step.Detail = $"توکن دسترسی با موفقیت دریافت شد (اعتبار: {token.ExpiresIn} ثانیه).";
            result.Steps.Add(step);
        }
        catch (Exception ex)
        {
            step.DurationMs = stopwatch.ElapsedMilliseconds;
            step.Ok = false;
            step.Detail = DescribeFailure(ex, cancellationToken);
            result.Steps.Add(step);
            _logger.LogWarning("Moadian connection test step {Step} failed with {ExceptionType}.", step.Key, ex.GetType().Name);
            DisposeProvider(provider);
            return false;
        }

        await RunFiscalInformationAsync(sdk, taxMemoryId, result, cancellationToken);
        DisposeProvider(provider);
        return true;
    }

    private static async Task RunFiscalInformationAsync(
        ITaxApis sdk,
        string taxMemoryId,
        MoadianConnectionTestResultDto result,
        CancellationToken cancellationToken)
    {
        var step = new MoadianConnectionTestStepDto { Key = "fiscal", Title = "استعلام شناسهٔ حافظهٔ مالیاتی (GET_FISCAL_INFORMATION)" };
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var fiscal = await sdk.GetFiscalInformationAsync(taxMemoryId).WaitAsync(cancellationToken);
            step.DurationMs = stopwatch.ElapsedMilliseconds;
            if (fiscal is null)
            {
                step.Ok = false;
                step.Detail = "سامانه برای این شناسهٔ حافظهٔ مالیاتی اطلاعاتی برنگرداند؛ شناسهٔ حافظهٔ مالیاتی این نسخه را بررسی کنید.";
            }
            else
            {
                step.Ok = true;
                step.Detail = "شناسهٔ حافظهٔ مالیاتی تأیید شد.";
                result.TaxpayerName = fiscal.NameTrade;
                result.FiscalStatus = fiscal.FiscalStatus == FiscalStatus.ACTIVE ? "فعال (ACTIVE)" : "غیرفعال (INACTIVE)";
            }
        }
        catch (Exception ex)
        {
            step.DurationMs = stopwatch.ElapsedMilliseconds;
            step.Ok = false;
            step.Detail = DescribeFailure(ex, cancellationToken);
        }

        result.Steps.Add(step);
    }

    /// <summary>
    /// ساخت کلاینت SDK مستقل (بدون TaxApiService.Instance سراسری) همراه با فرستندهٔ امن.
    /// </summary>
    private static void DisposeProvider(IServiceProvider provider)
    {
        if (provider is IDisposable disposable)
            disposable.Dispose();
    }

    private static (ITaxApis TaxApis, IServiceProvider Provider) CreateTaxApis(
        string baseUrl,
        string privateKeyPath,
        string keyId,
        ClientType clientType,
        string apiVersion,
        string clientId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTaxApi(
            baseUrl,
            clientId,
            new NormalProperties(clientType, apiVersion),
            new Pkcs8SignatoryConfig(privateKeyPath, keyId),
            contentSignatoryConfig: null,
            encryptionConfig: new EncryptionConfig("", ""));

        // فرستندهٔ پیش‌فرض SDK اعتبارسنجی گواهی TLS را غیرفعال می‌کند؛ جایگزین امن زیر آن را کنار می‌گذارد.
        var sender = new MoadianSecureHttpRequestSender(baseUrl, RequestTimeout);
        services.RemoveAll<IHttpRequestSender>();
        services.AddSingleton<IHttpRequestSender>(sender);

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<ITaxApis>(), provider);
    }

    private async Task<MoadianConnectionTestResultDto> RecordResultAsync(
        int connectionId,
        MoadianConnectionTestResultDto result,
        bool success,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        result.Success = success;
        result.DurationMs = stopwatch.ElapsedMilliseconds;

        var summary = BuildSummary(result);
        result.Message = string.IsNullOrWhiteSpace(result.Message) ? summary : result.Message;

        try
        {
            var connection = await _db.MoadianProviderConnections.SingleOrDefaultAsync(x => x.Id == connectionId, cancellationToken);
            if (connection is not null)
            {
                connection.LastConnectionTestAt = DateTime.Now;
                connection.LastConnectionTestSucceeded = success;
                connection.LastConnectionTestMessage = Truncate(result.Message, 500);
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // نتیجهٔ تست در پایگاه‌داده ذخیره نشد؛ خود تست اما معتبر است.
            _db.ChangeTracker.Clear();
            _logger.LogWarning("Could not persist the Moadian connection test result ({ExceptionType}).", ex.GetType().Name);
        }

        return result;
    }

    private static string BuildSummary(MoadianConnectionTestResultDto result)
    {
        var parts = result.Steps.Select(step => $"{step.Title}: {(step.Ok ? "موفق" : "ناموفق")}");
        return string.Join(" | ", parts);
    }

    /// <summary>پیام خطا را برای نمایش به کاربر خلاصه و پاک‌سازی می‌کند (بدون مسیر کلید/توکن/متن کامل پاسخ).</summary>
    private static string DescribeFailure(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
                return "مهلت تست یا درخواست کاربر پایان یافت.";
            return "مهلت پاسخ سامانه به پایان رسید (Timeout).";
        }

        if (exception is HttpRequestException http)
        {
            if (ContainsTlsFailure(http))
                return "برقراری اتصال امن (TLS) با سامانه ناموفق بود؛ گواهی سرور یا خطای شبکه را بررسی کنید.";
            return "دسترسی به سامانه برقرار نشد (DNS/شبکه/فایروال یا HTTPS را بررسی کنید).";
        }

        if (exception is InvalidOperationException)
            return Sanitize(exception.Message);

        // خطای سامانه یا قالب پاسخ: پیام خود سرویس مفیدترین راهنماست.
        return Sanitize(exception.Message);
    }

    private static bool ContainsTlsFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is AuthenticationException)
                return true;
        return false;
    }

    private static string Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "خطای نامشخص در تماس با سامانه.";
        var single = string.Join(' ', message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return Truncate(single, 400);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static ClientType ResolveClientType(string? configured)
        => string.Equals(configured?.Trim(), "TSP", StringComparison.OrdinalIgnoreCase) ? ClientType.TSP : ClientType.SELF_TSP;

    private static bool IsSupportedApiVersion(string apiVersion)
        => apiVersion.Length == 0 || string.Equals(apiVersion, "v1", StringComparison.OrdinalIgnoreCase);

    private static List<(string FieldLabel, string Value)> BuildIdentityCandidates(
        string? nationalId,
        string? economicNumber,
        string? taxMemoryId)
    {
        var candidates = new List<(string, string)>();
        void Add(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var trimmed = value.Trim();
            if (candidates.Any(x => string.Equals(x.Item2, trimmed, StringComparison.Ordinal))) return;
            candidates.Add((label, trimmed));
        }

        Add("شناسه ملی", nationalId);
        Add("شماره اقتصادی", economicNumber);
        Add("شناسه حافظهٔ مالیاتی", taxMemoryId);
        return candidates;
    }

    /// <summary>
    /// ساخت آدرس پایهٔ سامانه از WebServiceAddress (و در نبود آن Moadian:BaseUrl).
    /// اگر آدرس با tsp یا self-tsp تمام شود، آن بخش حذف می‌شود چون SDK خودش آن را به مسیر اضافه می‌کند.
    /// </summary>
    private static bool TryBuildBaseUrl(string? webServiceAddress, string? fallbackBaseUrl, ClientType clientType, out string baseUrl, out string error)
    {
        baseUrl = "";
        error = "";

        var candidate = (string.IsNullOrWhiteSpace(webServiceAddress) ? fallbackBaseUrl : webServiceAddress)?.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            error = "آدرس وب‌سرویس سامانه در این اتصال تنظیم نشده است.";
            return false;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || uri.Host.Length == 0)
        {
            error = "آدرس وب‌سرویس سامانه معتبر نیست.";
            return false;
        }

        var isLoopback = uri.IsLoopback;
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && isLoopback))
        {
            error = "برای تست، آدرس سامانه باید HTTPS باشد (HTTP فقط از loopback مجاز است).";
            return false;
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        var clientTypeSegment = clientType == ClientType.TSP ? "tsp" : "self-tsp";
        var lastSegment = path.Length == 0 ? "" : path[(path.LastIndexOf('/') + 1)..];
        if (lastSegment.Equals(clientTypeSegment, StringComparison.OrdinalIgnoreCase))
            path = path[..^lastSegment.Length].TrimEnd('/');

        var builder = new UriBuilder(uri) { Path = path + "/", Query = "", Fragment = "" };
        baseUrl = builder.Uri.AbsoluteUri;
        return true;
    }

    private static Dictionary<string, string> BuildEndpoints(string baseUrl, ClientType clientType, string apiVersion)
    {
        var properties = new NormalProperties(clientType, apiVersion);
        var baseUri = new Uri(baseUrl, UriKind.Absolute);
        // Path.Combine داخل SDK روی ویندوز \ تولید می‌کند؛ برای نمایش و فراخوانی به / تبدیل می‌شود.
        static string Resolve(Uri baseUri, string address)
            => new Uri(baseUri, (address ?? "").Replace('\\', '/')).AbsoluteUri;

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["server-info"] = Resolve(baseUri, properties.GetServerInformationApiAddress),
            ["auth"] = Resolve(baseUri, properties.GetTokenApiAddress),
            ["fiscal"] = Resolve(baseUri, properties.GetFiscalInformationApiAddress)
        };
    }
}
