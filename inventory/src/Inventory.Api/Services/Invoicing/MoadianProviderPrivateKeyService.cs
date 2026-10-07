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

    /// <summary>پوشهٔ اختصاصی خدمات‌دهنده در <c>wwwroot/uploads/moadian/{نام خدمات‌دهنده}</c> را اطمینان/ایجاد می‌کند.</summary>
    Task EnsureProviderFolderAsync(int providerId, CancellationToken cancellationToken);

    /// <summary>یک‌بار: کلیدهای موجود از ذخیره‌گاه‌های قدیمی به <c>wwwroot/uploads/moadian/{نام خدمات‌دهنده}/</c> منتقل می‌شود.</summary>
    Task MigrateLegacyKeyFilesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// کلید خصوصی هر خدمات‌دهنده به‌صورت یک فایل PKCS#8 PEM با پسوند .key نگهداری می‌شود:
/// <c>wwwroot/uploads/moadian/{نام خدمات‌دهنده}/moadian-c{connectionId}-{guid}.key</c>
/// هر وقت خدمات‌دهنده‌ای جدید تعریف می‌شود، پوشه‌ای به نام آن ساخته می‌شود و کلید در همان پوشه قرار می‌گیرد.
/// SQL فقط مسیر کامل فایل را نگه می‌دارد؛ بایت‌های PEM با هیچ API بازگشت داده نمی‌شوند و لاگ نمی‌شوند.
/// مسیر /uploads/moadian از سرو استاتیک (StaticFiles) مستثنی است و هرگز از وب قابل دانلود نیست.
/// </summary>
public sealed class MoadianProviderPrivateKeyService : IMoadianProviderPrivateKeyService
{
    public const long MaxPemBytes = 64 * 1024;
    public const string UploadsSubFolder = "moadian";

    private readonly AppDbContext _db;
    private readonly ILogger<MoadianProviderPrivateKeyService> _logger;
    private readonly string _root;

    public MoadianProviderPrivateKeyService(
        AppDbContext db,
        IWebHostEnvironment environment,
        MoadianRuntimeSettings settings,
        ILogger<MoadianProviderPrivateKeyService> logger)
    {
        _db = db;
        _logger = logger;

        var contentRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(environment.ContentRootPath));
        var configuredRoot = string.IsNullOrWhiteSpace(settings.PrivateKeyRoot) ? null : settings.PrivateKeyRoot.Trim();

        // پیش‌فرض: wwwroot/uploads/moadian — پوشهٔ moadian زیر آپلودها، با پوشهٔ جدا برای هر خدمات‌دهنده.
        var rootCandidate = configuredRoot is not null
            ? (Path.IsPathRooted(configuredRoot) ? configuredRoot : Path.Combine(contentRoot, configuredRoot))
            : Path.Combine(contentRoot, "wwwroot", "uploads", UploadsSubFolder);

        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootCandidate));
    }

    public async Task EnsureProviderFolderAsync(int providerId, CancellationToken cancellationToken)
    {
        if (providerId <= 0) return;
        var directory = await GetProviderDirectoryAsync(providerId, cancellationToken);
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);
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

        var connection = await _db.MoadianProviderConnections
            .SingleOrDefaultAsync(x => x.Id == connectionId, cancellationToken)
            ?? throw new InvalidOperationException("اتصال خدمات‌دهنده یافت نشد.");
        var provider = await _db.MoadianServiceProviderProfiles
            .SingleOrDefaultAsync(x => x.Id == connection.ServiceProviderId && !x.IsDeleted, cancellationToken)
            ?? throw new InvalidOperationException("برای بارگذاری کلید، خدمات‌دهنده باید فعال باشد.");

        var pemBytes = await ReadAndValidatePemAsync(file, cancellationToken);
        var oldPath = connection.PrivateKeyPath;
        var tempPath = "";
        var newPath = "";

        try
        {
            // کلید در پوشهٔ اختصاصی خدمات‌دهنده: {root}/{نام خدمات‌دهنده}/moadian-c{connectionId}-{guid}.key
            var providerDirectory = await GetProviderDirectoryAsync(provider.Id, cancellationToken);
            var privateDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(providerDirectory);
            }
            else
            {
                Directory.CreateDirectory(providerDirectory, privateDirectoryMode);
            }
            RestrictUnixPermissions(providerDirectory, privateDirectoryMode);

            var fileId = $"moadian-c{connection.Id}-{Guid.NewGuid():N}.key";
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
            throw new InvalidOperationException("ذخیرهٔ کلید در پوشهٔ moadian ناموفق بود؛ مجوزهای wwwroot/uploads/moadian را بررسی کنید.");
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
        => IsManagedPath(path, connectionId)
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

    /// <summary>
    /// مهاجرت یک‌باره کلیدها از ذخیره‌گاه‌های قبلی (مثل %ProgramData%\TotallCollection\MoadianPrivateKeys)
    /// به چیدمان جدید {root}/{نام خدمات‌دهنده}/ — مسیر ذخیره‌شده در SQL به‌روز می‌شود.
    /// </summary>
    public async Task MigrateLegacyKeyFilesAsync(CancellationToken cancellationToken)
    {
        var connections = await _db.MoadianProviderConnections.AsNoTracking()
            .Where(x => x.PrivateKeyPath != null && x.PrivateKeyPath != "")
            .ToListAsync(cancellationToken);
        if (connections.Count == 0) return;

        var providers = await _db.MoadianServiceProviderProfiles.AsNoTracking()
            .ToDictionaryAsync(x => x.Id, cancellationToken: cancellationToken);
        var moved = 0;

        foreach (var snapshot in connections)
        {
            try
            {
                var oldPath = snapshot.PrivateKeyPath;
                if (string.IsNullOrWhiteSpace(oldPath) || !File.Exists(oldPath)) continue;

                if (!providers.TryGetValue(snapshot.ServiceProviderId, out var provider)) continue;
                var providerDirectory = await GetProviderDirectoryAsyncFor(provider, cancellationToken);
                var target = Path.Combine(providerDirectory, Path.GetFileName(oldPath));

                if (string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(target), PathComparison))
                    continue; // already at the new location

                if (File.Exists(target))
                {
                    // نسخهٔ جدید در چیدمان جدید وجود دارد؛ فایل قدیمی یتیم را حذف می‌کنیم.
                    TryDelete(oldPath);
                }
                else
                {
                    Directory.CreateDirectory(providerDirectory);
                    File.Move(oldPath, target);
                    var tracked = await _db.MoadianProviderConnections.FindAsync(new object[] { snapshot.Id }, cancellationToken);
                    if (tracked is null) continue;
                    tracked.PrivateKeyPath = target;
                    moved++;
                    _logger.LogInformation(
                        "Moadian private key migrated to the provider folder (connection {ConnectionId}).",
                        snapshot.Id);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                _logger.LogWarning(ex, "Could not migrate a legacy Moadian private key file (connection {ConnectionId}).", snapshot.Id);
            }
        }

        if (moved > 0)
            await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> GetProviderDirectoryAsync(int providerId, CancellationToken cancellationToken)
    {
        var provider = await _db.MoadianServiceProviderProfiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == providerId, cancellationToken);
        return await GetProviderDirectoryAsyncFor(provider, cancellationToken);
    }

    private async Task<string> GetProviderDirectoryAsyncFor(MoadianServiceProviderProfile? provider, CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(provider?.PersianName)
            ? provider?.EnglishName
            : provider!.PersianName;
        var folderName = SanitizeFolderName(name ?? $"service-provider-{provider?.Id ?? 0}");
        var directory = Path.Combine(_root, folderName);

        if (Directory.Exists(directory) && provider is not null
            && await FolderBelongsToDifferentProviderAsync(directory, provider.Id, cancellationToken))
        {
            // دو خدمات‌دهنده هم‌نام: پوشهٔ این خدمات‌دهنده با شناسه‌اش متمایز می‌شود.
            directory = Path.Combine(_root, folderName + "-" + provider.Id);
        }

        return directory;
    }

    private async Task<bool> FolderBelongsToDifferentProviderAsync(string directory, int providerId, CancellationToken cancellationToken)
    {
        try
        {
            var files = Directory.EnumerateFiles(directory, "moadian-c*.key");
            var connectionIds = new List<int>();
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var marker = "moadian-c";
                var startIndex = marker.Length;
                var dashIndex = fileName.IndexOf('-', startIndex);
                if (dashIndex <= startIndex) continue;
                if (int.TryParse(fileName.AsSpan(startIndex, dashIndex - startIndex), out var connectionId))
                    connectionIds.Add(connectionId);
            }
            if (connectionIds.Count == 0) return false;

            var otherProviders = await _db.MoadianProviderConnections.AsNoTracking()
                .Where(x => connectionIds.Contains(x.Id) && x.ServiceProviderId != providerId)
                .AnyAsync(cancellationToken);
            return otherProviders;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not inspect an existing Moadian provider folder for name collision.");
            return false;
        }
    }

    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (invalid.Contains(ch) || ch is ' ' or '\t')
                sb.Append('_');
            else
                sb.Append(ch);
        }

        var result = sb.ToString().Trim('_').TrimEnd('.');
        return result.Length > 0 ? result : "provider";
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
            if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(directory))
                return false;

            // چیدمان جاری: {root}/{پوشهٔ خدمات‌دهنده}/moadian-c{connectionId}-{guid}.key
            if (fileName.StartsWith($"moadian-c{connectionId}-", StringComparison.Ordinal))
            {
                var parent = Path.GetDirectoryName(directory);
                var directoryIsProviderFolder = directory.Equals(_root, PathComparison)
                    || parent?.Equals(_root, PathComparison) == true;
                return directoryIsProviderFolder;
            }

            // چیدمان‌های قدیمی (برای پاک‌سازی فایل‌های باقیمانده): مستقیم زیر ریشه یا با پیشوند moadian-
            if (directory.Equals(_root, PathComparison)
                && fileName.StartsWith($"moadian-{connectionId}-", StringComparison.Ordinal))
                return true;

            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
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

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
