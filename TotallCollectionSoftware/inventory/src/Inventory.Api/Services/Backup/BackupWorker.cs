using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Backup;

public sealed class BackupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BackupWorker> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BackupWorker(IServiceScopeFactory scopes, ILogger<BackupWorker> log) { _scopes = scopes; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _log.LogError(ex, "خطا در چرخه بک‌آپ خودکار"); }
            try { await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct)) return;
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settingsSvc = scope.ServiceProvider.GetRequiredService<IBackupSettingsService>();
            var sessions = scope.ServiceProvider.GetRequiredService<IBackupSessionService>();
            var settings = await settingsSvc.GetEntityAsync();
            await sessions.CleanupAsync();
            await RecoverStaleJobsAsync(db, ct);
            if (settings.Enabled)
                await MaybeTriggerAsync(db, settings, sessions, ct);

            // صف موجود حتی اگر ماشه خودکار موقتاً خاموش باشد پردازش می‌شود (مثلاً بک‌آپ دستی).
            await ProcessJobsAsync(scope.ServiceProvider, db, settings, settingsSvc, ct);
            await CleanupLocalAsync(db, settings);
        }
        finally { _gate.Release(); }
    }

    private static async Task RecoverStaleJobsAsync(AppDbContext db, CancellationToken ct)
    {
        var cutoff = DateTime.Now.AddHours(-2);
        var stale = await db.BackupJobs.Where(x => (x.Status == "Running" || x.Status == "Uploading") && x.StartedAt < cutoff).ToListAsync(ct);
        foreach (var j in stale)
        {
            j.Status = "Failed";
            j.LastError = "بک‌آپ/ارسال بیش از حد طول کشید و برای تلاش مجدد علامت خورد.";
            j.NextAttemptAt = DateTime.Now;
        }
        if (stale.Count > 0) await db.SaveChangesAsync(ct);
    }

    private static async Task MaybeTriggerAsync(AppDbContext db, BackupSetting settings, IBackupSessionService sessions, CancellationToken ct)
    {
        var now = DateTime.Now;
        var busy = await db.BackupJobs.AnyAsync(x => x.Status == "Pending" || x.Status == "Running" || x.Status == "Uploading", ct);
        if (busy) return;

        if (settings.TriggerOnLastSession)
        {
            var active = await sessions.ActiveCountAsync(settings.LastSessionGraceSeconds);
            var last = await sessions.LastActivityAsync();
            var cooldownOk = settings.LastAutoTriggerAt == null || now - settings.LastAutoTriggerAt.Value >= TimeSpan.FromMinutes(settings.CooldownMinutes);
            if (active == 0 && last != null && now - last.Value >= TimeSpan.FromSeconds(settings.LastSessionGraceSeconds) && cooldownOk)
            {
                await EnqueueAsync(db, settings, "LastSession", ct);
                settings.LastAutoTriggerAt = now;
                await db.SaveChangesAsync(ct);
                return;
            }
        }

        if (settings.TriggerDaily && TimeSpan.TryParse(settings.DailyTime, out var daily))
        {
            if (now.TimeOfDay >= daily && settings.LastDailyRunDate?.Date != now.Date)
            {
                await EnqueueAsync(db, settings, "Daily", ct);
                settings.LastDailyRunDate = now.Date;
                settings.LastAutoTriggerAt = now;
                await db.SaveChangesAsync(ct);
            }
        }
    }

    private static async Task EnqueueAsync(AppDbContext db, BackupSetting settings, string reason, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        db.BackupJobs.Add(new BackupJob
        {
            Id = id,
            ExternalId = id,
            Reason = reason,
            Status = "Pending",
            DatabaseName = "",
            CreatedAt = DateTime.Now,
            NextAttemptAt = DateTime.Now
        });
        await db.SaveChangesAsync(ct);
    }

    private static async Task ProcessJobsAsync(IServiceProvider sp, AppDbContext db, BackupSetting settings, IBackupSettingsService settingsSvc, CancellationToken ct)
    {
        var job = await db.BackupJobs
            .Where(x => (x.Status == "Pending" || x.Status == "Failed") && (x.NextAttemptAt == null || x.NextAttemptAt <= DateTime.Now))
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (job == null) return;

        var backup = sp.GetRequiredService<ISqlServerBackupService>();
        var upload = sp.GetRequiredService<IBackupUploadService>();
        job.Status = "Running";
        job.StartedAt = DateTime.Now;
        job.Attempts++;
        await db.SaveChangesAsync(ct);

        try
        {
            if (string.IsNullOrWhiteSpace(job.FilePath) || !File.Exists(job.FilePath))
            {
                var info = await backup.DetectAsync();
                job.DatabaseName = info.DatabaseName ?? "";
                var file = await backup.CreateBackupAsync(job, settings, ct);
                job.FilePath = file.FilePath;
                job.FileName = file.FileName;
                job.SizeBytes = file.SizeBytes;
                job.Sha256 = file.Sha256;
                job.Compression = file.Compression;
                job.SqlVersion = file.SqlVersion;
                job.SqlEdition = file.SqlEdition;
                job.CompletedAt = DateTime.Now;
                job.Status = "Uploading";
                await db.SaveChangesAsync(ct);
            }

            var key = settingsSvc.UnprotectKey(settings);
            if (string.IsNullOrWhiteSpace(settings.CentralServerUrl) || string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("آدرس سرور مرکزی یا کلید API تنظیم نشده است.");

            await upload.UploadAsync(job, settings, key, ct);
            job.Status = "Sent";
            job.SentAt = DateTime.Now;
            job.LastError = null;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            job.Status = "Failed";
            job.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            job.NextAttemptAt = DateTime.Now.AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(job.Attempts, 6))));
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task CleanupLocalAsync(AppDbContext db, BackupSetting settings)
    {
        var keep = Math.Max(1, settings.LocalKeepCount);
        var old = await db.BackupJobs.Where(x => x.Status == "Sent" && x.FilePath != null)
            .OrderByDescending(x => x.SentAt)
            .Skip(keep)
            .ToListAsync();
        foreach (var j in old)
        {
            try { if (j.FilePath != null && File.Exists(j.FilePath)) File.Delete(j.FilePath); } catch { }
            j.FilePath = null;
        }
        if (old.Count > 0) await db.SaveChangesAsync();
    }
}
