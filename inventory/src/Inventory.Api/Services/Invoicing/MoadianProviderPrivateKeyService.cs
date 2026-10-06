using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Text;
using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Inventory.Api.Services.Invoicing;

public interface IMoadianProviderPrivateKeyService
{
    Task<MoadianPrivateKeyStatusDto> UploadAsync(int connectionId, IFormFile file, CancellationToken cancellationToken);
    Task<MoadianPrivateKeyStatusDto> RemoveAsync(int connectionId, CancellationToken cancellationToken);
    bool IsManagedFile(int connectionId, string? path);
}

/// <summary>
/// Stores a single PKCS#8 PEM-encoded private key with a .key extension per ConnectInfo outside web root.
/// هر خدمات‌دهنده پوشهٔ خودش را دارد: {root}/{نام خدمات‌دهنده}/moadian-p{providerId}-c{connectionId}-{guid}.key
/// SQL contains only the server path; PEM bytes are not returned by any API and never logged.
/// </summary>
public sealed class MoadianProviderPrivateKeyService : IMoadianProviderPrivateKeyService
{
    public const long MaxPemBytes = 64 * 1024;

    private readonly AppDbContext _db;
    private readonly ILogger<MoadianProviderPrivateKeyService> _logger;
    private readonly string _root;
    private readonly string _webRoot;
    private readonly bool _hasExplicitRoot;
    private readonly bool _rootIsOutsideContentRoot;

    public MoadianProviderPrivateKeyService(
        AppDbContext db,
        IWebHostEnvironment environment,
        MoadianRuntimeSettings settings,
        ILogger<MoadianProviderPrivateKeyService> logger)
    {
        _db = db;
        _logger = logger;
        _webRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot")));

        var contentRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(environment.ContentRootPath));
        var configuredRoot = string.IsNullOrWhiteSpace(settings.PrivateKeyRoot) ? null : settings.PrivateKeyRoot.Trim();
        _hasExplicitRoot = configuredRoot is not null;

        string rootCandidate;
        if (configuredRoot is not null)
        {
            rootCandidate = Path.IsPathRooted(configuredRoot)
                ? configuredRoot
                : Path.Combine(contentRoot, configuredRoot);
        }
        else if (OperatingSystem.IsWindows())
        {
            // پیش‌فرض ویندوز: %ProgramData%\TotallCollection\MoadianPrivateKeys
            // خارج از ContentRoot و wwwroot تا کلید خصوصی هرگز از سایت/وب‌روت سرو یا با انتشار نسخه بازنویسی نشود.
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            rootCandidate = string.IsNullOrWhiteSpace(programData)
                ? Path.Combine(contentRoot, "App_Data", "MoadianPrivateKeys")
                : Path.Combine(programData, "TotallCollection", "MoadianPrivateKeys");
        }
        else
        {
            rootCandidate = Path.Combine(contentRoot, "App_Data", "MoadianPrivateKeys");
        }

        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootCandidate));

        if (IsSameOrChild(_webRoot, _root))
            throw new InvalidOperationException("Files:MoadianPrivateKeyRoot must be outside wwwroot.");

        _rootIsOutsideContentRoot = !IsSameOrChild(contentRoot, _root);
        var defaultPrivateRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Path.Combine(contentRoot, "App_Data", "MoadianPrivateKeys")));
        if (_hasExplicitRoot
            && IsSameOrChild(contentRoot, _root)
            && !IsSameOrChild(defaultPrivateRoot, _root))
            throw new InvalidOperationException("Custom Files:MoadianPrivateKeyRoot must be outside ContentRoot/Git; use the ignored App_Data/MoadianPrivateKeys path or a separate server directory.");
    }

    public async Task<MoadianPrivateKeyStatusDto> UploadAsync(
        int connectionId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Length is <= 0 or > MaxPemBytes)
            throw new InvalidOperationException("اندازهٔ فایل کلید باید بین ۱ بایت و ۶۴ کیلوبایت باشد.");
        if (!string.Equals(Path.GetExtension(file.FileName), ".key", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("فقط یک فایل کلید خصوصی با پسوند .key پذیرفته می‌شود؛ محتوای آن باید PKCS#8 PEM باشد.");
        if (!IsRootUsable)
            throw new InvalidOperationException("در Windows، Files:MoadianPrivateKeyRoot را به پوشه‌ای خارج از ContentRoot و web root با ACL محدود به حساب سرویس تنظیم کنید.");

        var connection = await _db.MoadianProviderConnections
            .SingleOrDefaultAsync(x => x.Id == connectionId, cancellationToken)
            ?? throw new InvalidOperationException("اتصال خدمات‌دهنده یافت نشد.");
        var provider = await _db.MoadianServiceProviderProfiles
            .SingleOrDefaultAsync(x => x.Id == connection.ServiceProviderId && !x.IsDeleted, cancellationToken)
            ?? throw new InvalidOperationException("برای بارگذاری کلید، خدمات‌دهنده باید فعال باشد.");

        var providerTitle = provider.PersianName ?? provider.EnglishName ?? provider.NationalID;
        var providerDirectory = ResolveProviderDirectory(provider.Id, providerTitle);
        var pemBytes = await ReadAndValidatePemAsync(file, cancellationToken);
        var oldPath = connection.PrivateKeyPath;
        var tempPath = "";
        var newPath = "";

        try
        {
            var privateDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(_root);
                Directory.CreateDirectory(providerDirectory);
            }
            else
            {
                Directory.CreateDirectory(_root, privateDirectoryMode);
                Directory.CreateDirectory(providerDirectory, privateDirectoryMode);
            }
            RestrictUnixPermissions(_root, privateDirectoryMode);
            RestrictUnixPermissions(providerDirectory, privateDirectoryMode);

            var fileId = $"moadian-p{provider.Id}-c{connection.Id}-{Guid.NewGuid():N}.key";
            newPath = Path.Combine(providerDirectory, fileId);
            if (newPath.Length > 2048)
                throw new InvalidOperationException("مسیر ذخیره‌گاه PEM از حد مجاز پایگاه‌داده بلندتر است.");
            tempPath = newPath + ".tmp";

            await using (var output = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 8192,
                             options: FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                RestrictUnixPermissions(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                await output.WriteAsync(pemBytes.AsMemory(), cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            File.Move(tempPath, newPath);
            RestrictUnixPermissions(newPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            connection.PrivateKeyPath = newPath;
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            TryDelete(tempPath);
            TryDelete(newPath);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            TryDelete(tempPath);
            TryDelete(newPath);
            throw new InvalidOperationException("ذخیرهٔ کلید در پوشهٔ خصوصی سرور ناموفق بود؛ مجوزها و Files:MoadianPrivateKeyRoot را بررسی کنید.");
        }
        catch
        {
            TryDelete(tempPath);
            TryDelete(newPath);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pemBytes);
        }

        if (IsManagedPath(oldPath, connection.Id) && !string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(newPath), PathComparison))
            TryDelete(oldPath);

        return new MoadianPrivateKeyStatusDto { HasPrivateKey = true };
    }

    public bool IsManagedFile(int connectionId, string? path)
        => IsRootUsable
           && IsManagedPath(path, connectionId)
           && string.Equals(Path.GetExtension(path), ".key", StringComparison.OrdinalIgnoreCase)
           && File.Exists(path);

    public async Task<MoadianPrivateKeyStatusDto> RemoveAsync(int connectionId, CancellationToken cancellationToken)
    {
        var connection = await _db.MoadianProviderConnections
            .SingleOrDefaultAsync(x => x.Id == connectionId, cancellationToken)
            ?? throw new InvalidOperationException("اتصال خدمات‌دهنده یافت نشد.");

        var oldPath = connection.PrivateKeyPath;
        connection.PrivateKeyPath = "";
        await _db.SaveChangesAsync(cancellationToken);

        if (IsManagedPath(oldPath, connection.Id))
            TryDelete(oldPath);

        return new MoadianPrivateKeyStatusDto { HasPrivateKey = false };
    }

    private async Task<byte[]> ReadAndValidatePemAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var input = file.OpenReadStream();
        using var buffer = new MemoryStream((int)Math.Min(file.Length, MaxPemBytes));
        var chunk = new byte[8192];
        while (true)
        {
            var count = await input.ReadAsync(chunk.AsMemory(), cancellationToken);
            if (count == 0) break;
            if (buffer.Length + count > MaxPemBytes)
                throw new InvalidOperationException("اندازهٔ فایل PEM از سقف ۶۴ کیلوبایت بیشتر است.");
            buffer.Write(chunk, 0, count);
        }

        byte[] bytes = buffer.ToArray();
        string text;
        try { text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes); }
        catch (DecoderFallbackException) { throw new InvalidOperationException("محتوای فایل باید متن PEM معتبر باشد."); }

        text = text.Trim();
        if (!PemEncoding.TryFind(text.AsSpan(), out var fields)
            || !text.AsSpan(fields.Label).SequenceEqual("PRIVATE KEY"))
            throw new InvalidOperationException("فایل باید یک کلید خصوصی بدون رمزگذاری با قالب PKCS#8 PEM باشد.");

        var (blockStart, blockLength) = fields.Location.GetOffsetAndLength(text.Length);
        var blockEnd = blockStart + blockLength;
        if (!string.IsNullOrWhiteSpace(text[..blockStart]) || !string.IsNullOrWhiteSpace(text[blockEnd..]))
            throw new InvalidOperationException("فایل باید دقیقاً شامل یک بلوک PKCS#8 PEM باشد.");

        byte[] der;
        try { der = Convert.FromBase64String(text.AsSpan(fields.Base64Data).ToString()); }
        catch (FormatException) { throw new InvalidOperationException("دادهٔ Base64 فایل PEM معتبر نیست."); }

        try
        {
            var root = new AsnReader(der, AsnEncodingRules.DER);
            var privateKeyInfo = root.ReadSequence();
            var version = privateKeyInfo.ReadInteger();
            if (version.Sign < 0 || version > 1)
                throw new AsnContentException();
            var algorithm = privateKeyInfo.ReadSequence();
            _ = algorithm.ReadObjectIdentifier();
            while (algorithm.HasData) _ = algorithm.ReadEncodedValue();
            var privateKey = privateKeyInfo.ReadOctetString();
            var privateKeyLength = privateKey.Length;
            CryptographicOperations.ZeroMemory(privateKey);
            if (privateKeyLength == 0)
                throw new AsnContentException();
            while (privateKeyInfo.HasData) _ = privateKeyInfo.ReadEncodedValue();
            root.ThrowIfNotEmpty();
        }
        catch (AsnContentException)
        {
            throw new InvalidOperationException("ساختار فایل با PrivateKeyInfo قالب PKCS#8 سازگار نیست.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(der);
        }

        return bytes;
    }

    private bool IsManagedPath(string? path, int connectionId)
    {
        if (string.IsNullOrWhiteSpace(path) || connectionId <= 0) return false;
        try
        {
            var fullPath = Path.GetFullPath(path);
            var extension = Path.GetExtension(fullPath);
            if (!string.Equals(extension, ".key", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".pem", StringComparison.OrdinalIgnoreCase))
                return false;

            var fileName = Path.GetFileName(fullPath);
            var directory = Path.GetDirectoryName(fullPath);

            // چیدمان جاری: <root>/<پوشهٔ خدمات‌دهنده>/moadian-p{providerId}-c{connectionId}-{guid}.key
            const string providerFileMarker = "moadian-p";
            if (fileName.StartsWith(providerFileMarker, StringComparison.Ordinal))
            {
                var connectionMarker = $"-c{connectionId}-";
                var markerIndex = fileName.IndexOf(connectionMarker, StringComparison.Ordinal);
                if (markerIndex <= providerFileMarker.Length) return false;
                if (!IsAllDigits(fileName.AsSpan(providerFileMarker.Length, markerIndex - providerFileMarker.Length)))
                    return false;
                // فایل باید دقیقاً یک سطح زیر ریشهٔ خصوصی و داخل پوشهٔ خدمات‌دهنده باشد.
                return directory is not null
                       && Path.GetDirectoryName(directory)?.Equals(_root, PathComparison) == true;
            }

            // چیدمان قبلی: <root>/moadian-{connectionId}-{guid}.key (برای پاک‌سازی فایل‌های قدیمی)
            return directory?.Equals(_root, PathComparison) == true
                   && fileName.StartsWith($"moadian-{connectionId}-", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// پوشهٔ ذخیره‌سازی کلید هر خدمات‌دهنده با نام همان خدمات‌دهنده ساخته می‌شود و در صورت
    /// تکراری بودن نام، شناسهٔ خدمات‌دهنده به نام پوشه افزوده می‌شود تا کلیدها قاطی نشوند.
    /// </summary>
    private string ResolveProviderDirectory(int providerId, string? providerTitle)
    {
        var folderName = ProviderFolderName(providerTitle);
        if (folderName.Length == 0) return Path.Combine(_root, $"provider-{providerId}");

        var candidate = Path.Combine(_root, folderName);
        if (!Directory.Exists(candidate)) return candidate;

        return DirectoryBelongsToProvider(candidate, providerId)
            ? candidate
            : Path.Combine(_root, $"{folderName}-{providerId}");
    }

    private static bool DirectoryBelongsToProvider(string directory, int providerId)
    {
        try
        {
            var files = Directory.EnumerateFiles(directory, "moadian-p*").ToList();
            if (files.Count == 0) return true; // پوشهٔ تازه‌ساخته‌شده یا خالی

            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                var remaining = name.AsSpan("moadian-p".Length);
                var separatorIndex = remaining.IndexOf('-');
                if (separatorIndex <= 0) continue;
                if (!IsAllDigits(remaining[..separatorIndex])) continue;
                if (int.TryParse(remaining[..separatorIndex], out var ownerId) && ownerId == providerId)
                    return true;
            }

            return false; // نام پوشه متعلق به خدمات‌دهندهٔ دیگری است
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// نام پوشه را از نام خدمات‌دهنده می‌سازد؛ حروف فارسی (و نیم‌فاصله) و ارقام حفظ می‌شوند و
    /// فقط نویسه‌های غیرمجاز نام فایل حذف و فاصله‌های تکراری یکی می‌شوند.
    /// </summary>
    private static string ProviderFolderName(string? providerTitle)
    {
        if (string.IsNullOrWhiteSpace(providerTitle)) return "";

        // نویسه‌های غیرمجاز ویندوز همیشه حذف می‌شوند تا پوشه در هر سیستم‌عاملی قابل انتقال باشد.
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
        foreach (var ch in "<>:\"/\\|?*") invalid.Add(ch);

        var builder = new StringBuilder();
        var lastWasSpace = false;
        foreach (var ch in providerTitle.Trim())
        {
            if (char.IsControl(ch) || invalid.Contains(ch)) continue;
            if (char.IsWhiteSpace(ch))
            {
                if (builder.Length == 0 || lastWasSpace) continue;
                builder.Append(' ');
                lastWasSpace = true;
                continue;
            }

            builder.Append(ch);
            lastWasSpace = false;
        }

        var name = builder.ToString().Trim(' ', '.');
        return name.Length > 60 ? name[..60].Trim(' ', '.') : name;
    }

    private static bool IsAllDigits(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty) return false;
        foreach (var ch in value)
            if (ch is < '0' or > '9')
                return false;
        return true;
    }

    private static bool IsSameOrChild(string parent, string candidate)
    {
        try
        {
            var relative = Path.GetRelativePath(parent, candidate);
            return relative == "."
                   || (!Path.IsPathRooted(relative)
                       && !relative.Equals("..", PathComparison)
                       && !relative.StartsWith(".." + Path.DirectorySeparatorChar, PathComparison)
                       && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, PathComparison));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Could not remove a managed Moadian PEM file for cleanup (connection id not included).");
        }
    }

    private static void RestrictUnixPermissions(string path, UnixFileMode mode)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, mode);
    }

    private bool IsRootUsable
        => !OperatingSystem.IsWindows() || _rootIsOutsideContentRoot;

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
