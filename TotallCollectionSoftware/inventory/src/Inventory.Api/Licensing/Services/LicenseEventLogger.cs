using System.Text.Json;
using Inventory.Api.Data;

namespace Inventory.Api.Licensing.Services;

/// <summary>
/// ثبت رخداد لایسنس.
///
/// <para>
/// قاعده: ثبت رخداد <b>هرگز</b> نباید عملیات اصلی را بشکند (مثلاً اگر دیتابیس در آن لحظه
/// خطا داد، صدور فاکتور نباید شکست بخورد). پس همه‌چیز در try/catch است و خطا فقط
/// در لاگ برنامه می‌ماند.
/// </para>
/// </summary>
public sealed class LicenseEventLogger
{
    private readonly AppDbContext _db;
    private readonly ILogger<LicenseEventLogger> _log;

    public LicenseEventLogger(AppDbContext db, ILogger<LicenseEventLogger> log)
    {
        _db = db;
        _log = log;
    }

    public async Task WriteAsync(string kind, string message, string severity = "Info",
        string? dataJson = null, int? userId = null, string? serial = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _db.LicenseEvents.Add(new LicenseEvent
            {
                At = DateTime.Now,
                Kind = Truncate(kind, 60),
                Severity = severity,
                Message = Truncate(message, 600),
                DataJson = dataJson,
                UserId = userId,
                Serial = serial
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[License] ثبت رخداد «{Kind}» ناموفق بود.", kind);
        }
    }

    /// <summary>
    /// ثبت رخداد با دادهٔ ساختاریافته.
    ///
    /// <para>
    /// نام عمداً «WriteDataAsync» است و نه اورلودِ WriteAsync: با اورلود، فراخوانی
    /// <c>WriteAsync(kind, message, "Warning")</c> می‌توانست به‌جای severity به نسخهٔ
    /// داده‌دار با T=string وصل شود و بی‌صدا داده را در ستون severity بریزد.
    /// </para>
    /// </summary>
    public Task WriteDataAsync<T>(string kind, string message, T data, string severity = "Info",
        int? userId = null, string? serial = null, CancellationToken cancellationToken = default)
    {
        string? json = null;
        try { json = JsonSerializer.Serialize(data); } catch { /* بی‌صدا */ }
        return WriteAsync(kind, message, severity, json, userId, serial, cancellationToken);
    }

    private static string Truncate(string value, int max)
        => string.IsNullOrEmpty(value) ? "" : (value.Length <= max ? value : value[..max]);
}
