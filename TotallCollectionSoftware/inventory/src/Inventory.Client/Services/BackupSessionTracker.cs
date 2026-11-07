using Microsoft.JSInterop;

namespace Inventory.Client.Services;

public interface IBackupSessionTracker
{
    Task StartAsync();
    Task StopAsync();
}

/// <summary>ثبت heartbeat نشست مرورگر برای ماشه «آخرین کاربر فعال» بک‌آپ خودکار.</summary>
public sealed class BackupSessionTracker : IBackupSessionTracker
{
    private readonly IJSRuntime _js;
    private readonly IAuthState _auth;
    private readonly ApiOptions _options;
    private string? _sessionId;

    public BackupSessionTracker(IJSRuntime js, IAuthState auth, ApiOptions options)
    { _js = js; _auth = auth; _options = options; }

    public async Task StartAsync()
    {
        if (!_auth.IsLoggedIn || string.IsNullOrWhiteSpace(_auth.Token)) return;
        try
        {
            _sessionId = Guid.NewGuid().ToString("N");
            await _js.InvokeVoidAsync("backupSession.start", _options.BaseUrl, _auth.Token, _sessionId);
        }
        catch { /* نبود JS یا خطای شبکه نباید برنامه را بشکند */ }
    }

    public async Task StopAsync()
    {
        try { await _js.InvokeVoidAsync("backupSession.stop"); } catch { }
        _sessionId = null;
    }
}
