using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Backup;

public interface IBackupSessionService
{
    Task HeartbeatAsync(string sessionId, int userId, string userName, string? userAgent);
    Task CloseAsync(string sessionId, int userId, string reason);
    Task<int> ActiveCountAsync(int graceSeconds);
    Task<DateTime?> LastActivityAsync();
    Task CleanupAsync();
}

public sealed class BackupSessionService : IBackupSessionService
{
    private readonly AppDbContext _db;
    public BackupSessionService(AppDbContext db) => _db = db;

    public async Task HeartbeatAsync(string sessionId, int userId, string userName, string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 32) return;
        var now = DateTime.Now;
        var s = await _db.BackupBrowserSessions.FirstOrDefaultAsync(x => x.Id == sessionId);
        if (s == null)
        {
            s = new BackupBrowserSession { Id = sessionId, UserId = userId, UserName = userName, StartedAt = now };
            _db.BackupBrowserSessions.Add(s);
        }
        s.UserId = userId;
        s.UserName = userName;
        s.LastHeartbeatAt = now;
        s.ClosedAt = null;
        s.CloseReason = null;
        s.UserAgent = userAgent;
        await _db.SaveChangesAsync();
    }

    public async Task CloseAsync(string sessionId, int userId, string reason)
    {
        var s = await _db.BackupBrowserSessions.FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId);
        if (s == null) return;
        s.ClosedAt = DateTime.Now;
        s.CloseReason = reason;
        await _db.SaveChangesAsync();
    }

    public async Task<int> ActiveCountAsync(int graceSeconds)
    {
        var cutoff = DateTime.Now.AddSeconds(-Math.Max(30, graceSeconds));
        return await _db.BackupBrowserSessions.CountAsync(x => x.ClosedAt == null && x.LastHeartbeatAt >= cutoff);
    }

    public async Task<DateTime?> LastActivityAsync()
    {
        return await _db.BackupBrowserSessions
            .OrderByDescending(x => x.LastHeartbeatAt)
            .Select(x => (DateTime?)x.LastHeartbeatAt)
            .FirstOrDefaultAsync();
    }

    public async Task CleanupAsync()
    {
        var cutoff = DateTime.Now.AddDays(-7);
        var old = await _db.BackupBrowserSessions.Where(x => x.LastHeartbeatAt < cutoff).ToListAsync();
        if (old.Count > 0)
        {
            _db.BackupBrowserSessions.RemoveRange(old);
            await _db.SaveChangesAsync();
        }
    }
}
