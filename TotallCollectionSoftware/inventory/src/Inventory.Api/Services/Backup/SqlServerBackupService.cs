using System.IO.Compression;
using System.Security.Cryptography;
using Inventory.Api.Data;
using Microsoft.Data.SqlClient;

namespace Inventory.Api.Services.Backup;

public sealed record SqlServerInfo(string Provider, string? Version, string? Edition, bool NativeCompressionSupported, string? DatabaseName);
public sealed record BackupFileResult(string FilePath, string FileName, long SizeBytes, string Sha256, string Compression, string? SqlVersion, string? SqlEdition);

public interface ISqlServerBackupService
{
    Task<SqlServerInfo> DetectAsync();
    Task<BackupFileResult> CreateBackupAsync(BackupJob job, BackupSetting settings, CancellationToken ct);
}

public sealed class SqlServerBackupService : ISqlServerBackupService
{
    private readonly IConfiguration _cfg;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SqlServerBackupService> _log;

    public SqlServerBackupService(IConfiguration cfg, IWebHostEnvironment env, ILogger<SqlServerBackupService> log)
    {
        _cfg = cfg; _env = env; _log = log;
    }

    private string ConnectionString => _cfg.GetConnectionString("Default")
        ?? throw new InvalidOperationException("رشته اتصال دیتابیس پیدا نشد.");

    public async Task<SqlServerInfo> DetectAsync()
    {
        var provider = _cfg["Database:Provider"] ?? "SqlServer";
        if (!provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            return new SqlServerInfo(provider, null, null, false, null);

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(100)), CAST(SERVERPROPERTY('Edition') AS nvarchar(100)), DB_NAME();";
        await using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync()) return new SqlServerInfo(provider, null, null, false, null);
        var version = rd.IsDBNull(0) ? null : rd.GetString(0);
        var edition = rd.IsDBNull(1) ? null : rd.GetString(1);
        var db = rd.IsDBNull(2) ? null : rd.GetString(2);
        var native = !string.IsNullOrWhiteSpace(edition) && !edition.Contains("Express", StringComparison.OrdinalIgnoreCase);
        return new SqlServerInfo(provider, version, edition, native, db);
    }

    public async Task<BackupFileResult> CreateBackupAsync(BackupJob job, BackupSetting settings, CancellationToken ct)
    {
        var info = await DetectAsync();
        if (!info.Provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("بک‌آپ خودکار فقط برای SQL Server پشتیبانی می‌شود.");
        if (string.IsNullOrWhiteSpace(info.DatabaseName))
            throw new InvalidOperationException("نام دیتابیس قابل تشخیص نیست.");

        var dir = string.IsNullOrWhiteSpace(settings.LocalBackupPath)
            ? Path.Combine(_env.ContentRootPath, "App_Data", "Backups")
            : Environment.ExpandEnvironmentVariables(settings.LocalBackupPath);
        Directory.CreateDirectory(dir);
        var safeDb = new string(info.DatabaseName.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
        var baseName = $"{settings.CompanyCode ?? "LOCAL"}_{safeDb}_{DateTime.Now:yyyyMMdd_HHmmss}_{job.ExternalId[..8]}";
        var bakPath = Path.Combine(dir, baseName + ".bak");

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync(ct);
        var compression = info.NativeCompressionSupported;
        try
        {
            await BackupAsync(conn, info.DatabaseName, bakPath, compression, ct);
        }
        catch (SqlException ex) when (compression && (ex.Number == 3013 || ex.Number == 1809 || ex.Message.Contains("COMPRESSION", StringComparison.OrdinalIgnoreCase)))
        {
            _log.LogWarning(ex, "فشرده‌سازی Native پشتیبانی نشد؛ تلاش مجدد بدون COMPRESSION");
            TryDelete(bakPath);
            compression = false;
            await BackupAsync(conn, info.DatabaseName, bakPath, false, ct);
        }

        await VerifyAsync(conn, bakPath, ct);

        string finalPath;
        string compressionLabel;
        if (compression)
        {
            finalPath = bakPath;
            compressionLabel = "Native";
        }
        else
        {
            finalPath = Path.Combine(dir, baseName + ".zip");
            using (var zip = ZipFile.Open(finalPath, ZipArchiveMode.Create))
                zip.CreateEntryFromFile(bakPath, Path.GetFileName(bakPath), CompressionLevel.Optimal);
            TryDelete(bakPath);
            compressionLabel = "Zip";
        }

        var size = new FileInfo(finalPath).Length;
        var hash = await Sha256FileAsync(finalPath, ct);
        return new BackupFileResult(finalPath, Path.GetFileName(finalPath), size, hash, compressionLabel, info.Version, info.Edition);
    }

    private static async Task BackupAsync(SqlConnection conn, string database, string path, bool compression, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 0;
        cmd.CommandText = $"BACKUP DATABASE [{database.Replace("]", "]]")}] TO DISK = @path WITH COPY_ONLY, CHECKSUM, INIT" + (compression ? ", COMPRESSION" : "") + ";";
        cmd.Parameters.AddWithValue("@path", path);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task VerifyAsync(SqlConnection conn, string path, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 0;
        cmd.CommandText = "RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM;";
        cmd.Parameters.AddWithValue("@path", path);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<string> Sha256FileAsync(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(fs, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
