using LicenseSigner.Data;
using Microsoft.EntityFrameworkCore;

namespace LicenseSigner.Services;

/// <summary>یک فایل پشتیبان در پوشهٔ کاری.</summary>
public sealed record BackupFile(string FileName, long SizeBytes, DateTime CreatedAt);

/// <summary>
/// پشتیبان‌گیری ساده از پایگاه‌دادهٔ پنل (SQLite: کپی فایل با checkpoint). فقط محلی، بدون
/// ارسال به بیرون. کلید خصوصی هرگز در این پشتیبان نیست (بیرون از DB نگه داشته می‌شود).
/// </summary>
public sealed class BackupService(SupportDbContext db, SignerOptions options, ILogger<BackupService> log)
{
    public string BackupFolder
    {
        get
        {
            var folder = Path.Combine(options.EffectiveWorkFolder(), "backups");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    public string SqlitePath => Path.Combine(options.EffectiveWorkFolder(), "support.db");

    public List<BackupFile> List()
    {
        var folder = BackupFolder;
        return Directory.EnumerateFiles(folder, "support-*.db")
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.CreationTimeUtc)
            .Select(info => new BackupFile(info.Name, info.Length, info.CreationTimeUtc))
            .ToList();
    }

    public async Task<(bool Ok, string? Error, string? FileName)> CreateAsync(SupportUser actor,
        AuditChainService audit, CancellationToken ct = default)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(FULL);", ct);
            var name = $"support-{JalaliDate.TehranNow():yyyyMMdd-HHmmss}.db";
            var target = Path.Combine(BackupFolder, name);
            File.Copy(SqlitePath, target, overwrite: false);

            await audit.AppendAsync(actor.UserName, "BACKUP_CREATE", "SupportDb", name, null, actor.Id, ct);
            log.LogInformation("پشتیبان ساخته شد: {Name}", name);
            return (true, null, name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, "ساخت پشتیبان ناموفق بود: " + ex.Message, null);
        }
    }

    /// <summary>بازگردانی: فایل جاری با نام «before-restore» کنار گذاشته می‌شود.</summary>
    public async Task<(bool Ok, string? Error)> RestoreAsync(string fileName, SupportUser actor,
        AuditChainService audit, CancellationToken ct = default)
    {
        var safe = Path.GetFileName(fileName ?? "");
        var source = Path.Combine(BackupFolder, safe);
        if (!File.Exists(source)) return (false, "فایل پشتیبان پیدا نشد.");

        try
        {
            await db.Database.CloseConnectionAsync();
            var aside = SqlitePath + ".before-restore";
            if (File.Exists(SqlitePath)) File.Copy(SqlitePath, aside, overwrite: true);
            File.Copy(source, SqlitePath, overwrite: true);
            await audit.AppendAsync(actor.UserName, "BACKUP_RESTORE", "SupportDb", safe, null, actor.Id, ct);
            return (true, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, "بازگردانی ناموفق بود: " + ex.Message);
        }
    }
}
