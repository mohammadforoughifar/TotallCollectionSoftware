using Microsoft.AspNetCore.SignalR.Client;

namespace Inventory.Client.Services;

/// <summary>
/// ================== هسته‌ی بلادرنگ سراسری (SignalR) ==================
/// تمام ماژول‌های فعلی و آینده از همین یک سرویس استفاده می‌کنند:
///  • یک اتصال واحد برای کل برنامه (بهینه — نه یک اتصال به ازای هر صفحه)
///  • NotifyReceived: دریافت اعلان شخصی کاربر (زنگ + توست)
///  • DataChanged(scope): اعلام تغییر داده‌ها برای رفرش خودکار کارتابل‌ها/لیست‌ها
///    (مثلاً scope = "itrequests" — هر ماژول جدید scope خودش را دارد)
/// سمت سرور: INotifyService.SendAsync / BroadcastChangedAsync(scope)
/// نحوه استفاده در هر صفحه:
///   Rt.DataChanged += OnChanged;  →  در Dispose:  Rt.DataChanged -= OnChanged;
/// </summary>
public class RealtimeService : IAsyncDisposable
{
    /// <summary>اعلان شخصی جدید برای کاربر جاری رسید.</summary>
    public event Action<RtNotification>? NotifyReceived;

    /// <summary>داده‌های یک ماژول تغییر کرد — صفحات مرتبط خودشان را رفرش کنند.</summary>
    public event Action<string>? DataChanged;

    public bool IsConnected => _hub?.State == HubConnectionState.Connected;

    private HubConnection? _hub;
    private string? _startedFor;

    /// <summary>برقراری اتصال (فقط یک‌بار — فراخوانی‌های بعدی بی‌اثرند).</summary>
    public async Task EnsureStartedAsync(string baseUrl, int userId)
    {
        var key = $"{baseUrl}|{userId}";
        if (_hub != null && _startedFor == key && IsConnected) return;

        await StopAsync();
        if (userId <= 0 || string.IsNullOrWhiteSpace(baseUrl)) return;

        _hub = new HubConnectionBuilder()
            .WithUrl($"{baseUrl.TrimEnd('/')}/hubs/notify?userId={userId}")
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
            .Build();

        _hub.On<RtNotification>("notify", n => NotifyReceived?.Invoke(n));
        _hub.On<string>("datachanged", s => DataChanged?.Invoke(s ?? ""));

        try
        {
            await _hub.StartAsync();
            _startedFor = key;
        }
        catch { /* اتصال بلادرنگ اختیاری است — برنامه بدون آن هم کار می‌کند */ }
    }

    public async Task StopAsync()
    {
        if (_hub != null)
        {
            try { await _hub.DisposeAsync(); } catch { }
            _hub = null;
            _startedFor = null;
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}

/// <summary>مدل اعلان بلادرنگ — هماهنگ با AppNotification سرور.</summary>
public class RtNotification
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Body { get; set; }
    public string FromName { get; set; } = "";
    public string FormName { get; set; } = "";
    public string? Link { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
