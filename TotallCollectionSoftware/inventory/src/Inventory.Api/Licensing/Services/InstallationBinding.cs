using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace Inventory.Api.Licensing.Services;

/// <summary>مؤلفه‌های ثانویهٔ تشخیص جابه‌جایی نصب.</summary>
public static class BindingComponents
{
    public const string MachineName = "machineName";
    public const string MachineGuid = "machineGuid";
    public const string SqlServer = "sqlServer";

    /// <summary>
    /// اثر انگشت سه مؤلفهٔ ثانویه (هش SHA-256، ۱۶ کاراکتر اول).
    ///
    /// <para>
    /// چرا هش و نه مقدار خام؟ چون هش مؤلفه در فایل درخواست/گزارش هم می‌رود و نام
    /// کامل ماشین و شناسهٔ ویندوز نباید بی‌دلیل جابه‌جا شود؛ برای تشخیص «عوض شده یا نه»
    /// هش کافی است.
    /// </para>
    /// </summary>
    public static Dictionary<string, string> Capture(string? connectionString = null)
    {
        var raw = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [MachineName] = Safe(() => Environment.MachineName),
            [MachineGuid] = Safe(ReadMachineGuid),
            [SqlServer] = Safe(() => SqlDataSource(connectionString))
        };

        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in raw)
            hashes[name] = string.IsNullOrWhiteSpace(value) ? "" : Hash(value!);
        return hashes;
    }

    /// <summary>
    /// MachineGuid ویندوز؛ در لینوکس <c>/etc/machine-id</c> به‌عنوان هم‌ارز.
    /// اگر هیچ‌کدام خوانده نشد، رشتهٔ خالی (مؤلفه «نامعلوم» شمرده می‌شود و در مقایسه
    /// نادیده گرفته می‌شود تا یک خطای خواندن، هشدار جابه‌جایی نسازد).
    /// </summary>
    private static string? ReadMachineGuid()
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Microsoft.Win32.Registry.LocalMachine
                .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid")?.ToString();
        }

        const string linuxId = "/etc/machine-id";
        return File.Exists(linuxId) ? File.ReadAllText(linuxId).Trim() : null;
    }

    private static string? SqlDataSource(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            var source = builder.DataSource;
            if (string.IsNullOrWhiteSpace(source)) return null;
            // نام نمونه (MACHINE\SQLEXPRESS) هم بخشی از هویت است؛ فقط حالت و پورت را نادیده می‌گیریم.
            return source.Split(',')[0].Trim().ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim())))[..16].ToLowerInvariant();

    private static string? Safe(Func<string?> read)
    {
        try { return read(); }
        catch { return null; }
    }
}

/// <summary>نتیجهٔ سنجش بایند نصب.</summary>
public sealed record BindingAssessment(
    bool BaselineCreated,
    IReadOnlyList<string> Changed,
    IReadOnlyList<string> Compared,
    string Summary)
{
    /// <summary>
    /// آیا «انتقال نصب» پیشنهاد می‌شود؟ آستانه: <b>دو</b> مؤلفهٔ ثانویه یا بیشتر.
    ///
    /// <para>
    /// یک مؤلفه ممکن است با تغییر نام ماشین یا ارتقای سرور SQL عوض شود؛ تا زمانی که
    /// فقط یکی است، هشدار «جابه‌جایی» نمی‌دهیم. دو مؤلفه با هم یعنی عملاً ماشین دیگری
    /// است (Restore روی سرور تازه یا کلون VM).
    /// </para>
    /// </summary>
    public bool RehostRecommended => Changed.Count >= 2;
}

/// <summary>
/// بایند نصب: مقایسهٔ مؤلفه‌های ثانویهٔ فعلی با خط پایهٔ ثبت‌شده در اولین اجرا.
///
/// <para>
/// <b>قاعدهٔ سخت:</b> این سنجش <b>هرگز</b> اعتبار لایسنس را تغییر نمی‌دهد و هیچ
/// مهلت/زمان‌بندی نمی‌سازد. تنها اثرش یک رخداد <c>Warning</c> و پرچم
/// <c>rehostRecommended</c> در <c>/api/license/status</c> است تا تیم بداند این نصب
/// جابه‌جا شده و اگر لازم بود لایسنس را دوباره به شناسهٔ نصب ببندد.
/// </para>
///
/// <para>
/// مؤلفهٔ اصلی (GUID نصب) جای دیگری سنجیده می‌شود: ناهمخوانی GUID ⇒ لایسنس
/// <c>Invalid</c>. این‌جا عمداً به GUID دست نمی‌زنیم.
/// </para>
/// </summary>
public sealed class InstallationBinding
{
    /// <summary>کلید وضعیت برای «آخرین مجموعهٔ هشدار داده‌شده» (جلوگیری از تکرار رخداد).</summary>
    private const string WarnKey = "binding.lastWarnSet";

    private readonly AppDbContext _db;
    private readonly LicenseEventLogger _events;
    private readonly IConfiguration? _configuration;
    private readonly Func<string?> _connectionString;
    private readonly Func<Dictionary<string, string>> _capture;
    private readonly ILogger<InstallationBinding> _log;

    public InstallationBinding(AppDbContext db, LicenseEventLogger events, IConfiguration configuration,
        ILogger<InstallationBinding> log)
        : this(db, events, configuration, log, null, null) { }

    /// <summary>سازندهٔ قابل‌تست: بدون پیکربندی (رشتهٔ اتصال صریح یا هیچ).</summary>
    public InstallationBinding(AppDbContext db, LicenseEventLogger events,
        Func<Dictionary<string, string>> capture, ILogger<InstallationBinding> log, string? connectionString = null)
        : this(db, events, null, log, capture, connectionString) { }

    /// <summary>سازندهٔ قابل‌تست: ضبط مؤلفه‌ها و رشتهٔ اتصال قابل جایگزینی است.</summary>
    public InstallationBinding(AppDbContext db, LicenseEventLogger events, IConfiguration? configuration,
        ILogger<InstallationBinding> log,
        Func<Dictionary<string, string>>? capture, string? connectionString)
    {
        _db = db;
        _events = events;
        _configuration = configuration;
        _log = log;
        _connectionString = () => connectionString ?? configuration?.GetConnectionString("Default");
        _capture = capture ?? (() => BindingComponents.Capture(connectionString ?? configuration?.GetConnectionString("Default")));
    }

    /// <summary>خط پایه را (اگر نبود) می‌سازد و وضعیت فعلی را می‌سنجد.</summary>
    public async Task<BindingAssessment> AssessAsync(CancellationToken cancellationToken = default)
    {
        var current = _capture();
        var row = await _db.LicenseInstallations.OrderBy(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        if (row is null)
            return new BindingAssessment(false, Array.Empty<string>(), Array.Empty<string>(),
                "ردیف نصب هنوز ساخته نشده است.");

        var baseline = ParseBaseline(row.MachineFingerprint);
        if (baseline.Count == 0)
        {
            row.MachineFingerprint = Serialize(current);
            row.UpdatedAt = DateTime.Now;
            await _db.SaveChangesAsync(cancellationToken);
            _log.LogInformation("[License] خط پایهٔ بایند نصب ثبت شد ({Count} مؤلفه).", current.Count);
            return new BindingAssessment(true, Array.Empty<string>(), current.Keys.ToList(),
                "خط پایه ثبت شد؛ مقایسه از اجرای بعد.");
        }

        var compared = new List<string>();
        var changed = new List<string>();
        foreach (var (name, hash) in current)
        {
            // مؤلفه‌ای که در یک طرف «نامعلوم» است، اصلاً مقایسه نمی‌شود: خطای خواندن
            // نباید به‌شکل «جابه‌جایی نصب» گزارش شود.
            if (string.IsNullOrEmpty(hash)) continue;
            if (!baseline.TryGetValue(name, out var old) || string.IsNullOrEmpty(old)) continue;
            compared.Add(name);
            if (!string.Equals(old, hash, StringComparison.OrdinalIgnoreCase)) changed.Add(name);
        }

        var assessment = new BindingAssessment(false, changed, compared, BuildSummary(changed, compared));

        if (assessment.RehostRecommended)
        {
            var signature = string.Join(",", changed.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            await WarnOnceAsync(signature, changed, cancellationToken);
        }

        return assessment;
    }

    private async Task WarnOnceAsync(string signature, IReadOnlyList<string> changed, CancellationToken cancellationToken)
    {
        // هر مجموعهٔ متفاوت فقط یک‌بار رخداد می‌سازد؛ وگرنه هر بار باز کردن صفحهٔ لایسنس
        // یک ردیف Warning تازه می‌نوشت و لاگ را بی‌فایده می‌کرد.
        var marker = await _db.LicenseRuntime.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == WarnKey, cancellationToken);
        if (marker is not null && string.Equals(marker.Value, signature, StringComparison.Ordinal))
            return;

        _log.LogWarning("[License] بایند نصب: {Count} مؤلفهٔ ثانویه عوض شده ({Components}) — " +
                        "اعتبار لایسنس تغییر نکرد؛ فقط بررسی انتقال نصب لازم است.",
            changed.Count, string.Join(", ", changed));

        await _events.WriteAsync("INSTALL_BINDING_WARNING",
            $"مؤلفه‌های ثانویهٔ نصب عوض شده‌اند ({string.Join(", ", changed)}). " +
            "اعتبار لایسنس تغییر نکرد؛ اگر سرور واقعاً جابه‌جا شده، لایسنس را دوباره به شناسهٔ نصب ببندید.",
            severity: "Warning", cancellationToken: cancellationToken);

        if (marker is null)
            _db.LicenseRuntime.Add(new LicenseRuntime { Key = WarnKey, Value = signature, UpdatedAt = DateTime.Now });
        else
        {
            var tracked = await _db.LicenseRuntime.FirstAsync(x => x.Key == WarnKey, cancellationToken);
            tracked.Value = signature;
            tracked.UpdatedAt = DateTime.Now;
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string BuildSummary(IReadOnlyList<string> changed, IReadOnlyList<string> compared) =>
        changed.Count switch
        {
            0 => $"همهٔ {compared.Count} مؤلفهٔ قابل‌مقایسه بدون تغییرند.",
            1 => $"یک مؤلفه عوض شده ({changed[0]}) — طبیعی است (تغییر نام ماشین/ارتقای SQL) و اقدامی لازم نیست.",
            _ => $"{changed.Count} مؤلفه از {compared.Count} عوض شده ({string.Join(", ", changed)}) — «انتقال نصب» محتمل است."
        };

    private static Dictionary<string, string> ParseBaseline(string? json)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("components", out var components)) return result;
            foreach (var property in components.EnumerateObject())
                if (property.Value.ValueKind == JsonValueKind.String)
                    result[property.Name] = property.Value.GetString() ?? "";
        }
        catch
        {
            // فرمت قدیمی (نسخهٔ ۱) — به‌عنوان «بدون خط پایه» رفتار می‌شود و یک‌بار بازنویسی می‌گردد.
        }
        return result;
    }

    private static string Serialize(Dictionary<string, string> components) =>
        JsonSerializer.Serialize(new
        {
            version = 2,
            capturedAt = DateTime.UtcNow.ToString("O"),
            components = components.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)
        });
}
