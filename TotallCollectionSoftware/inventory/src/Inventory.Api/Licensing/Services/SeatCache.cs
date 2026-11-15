using System.Collections.Concurrent;

namespace Inventory.Api.Licensing.Services;

/// <summary>
/// کش تصمیم صندلی — بندهای ۲ و ۴ صورت‌جلسه.
///
/// <para>
/// دو کار می‌کند:
/// <list type="number">
/// <item>اعتبار <c>sid</c> را برای چند ثانیه کش می‌کند تا هر درخواست یک کوئری دیتابیس
/// نزند (پنجرهٔ ۱۵..۱۲۰ ثانیه؛ از <c>SeatCacheSeconds</c> و با کفِ کد).</item>
/// <item>لنگر پنجرهٔ تخریب را نگه می‌دارد: آخرین بررسی موفق و اولین خطا.</item>
/// </list>
/// </para>
///
/// <para>
/// ⚠️ کش فقط «سرعت» است نه «امنیت»: هر ورود/خروج فوراً ورودی مربوطه را باطل می‌کند و
/// حداکثر عمر یک ورودی، پنجرهٔ کوتاه بالا است.
/// </para>
/// </summary>
public sealed class SeatCache
{
    /// <summary>طول پنجرهٔ تخریب — ۱۰ دقیقه (بند ۲).</summary>
    public const int DegradedWindowMinutes = 10;

    private readonly ConcurrentDictionary<string, Entry> _sessions = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private int _hits;
    private int _misses;

    private bool _structural;
    private DateTime? _lastSuccessLocal;
    private DateTime? _firstFailureLocal;

    /// <summary>تعداد اصابت‌های کش (برای تست و عیب‌یابی).</summary>
    public int Hits => Volatile.Read(ref _hits);

    /// <summary>تعداد عدم‌اصابت‌ها (یعنی کوئری واقعی).</summary>
    public int Misses => Volatile.Read(ref _misses);

    /// <summary>آیا خطای ساختاری دیده شده است؟</summary>
    public bool IsStructural
    {
        get { lock (_gate) return _structural; }
    }

    /// <summary>ثبت خطای ساختاری — تا رفع (پاک‌شدن پرچم) ورود جدید رد می‌شود.</summary>
    public void MarkStructural()
    {
        lock (_gate) _structural = true;
    }

    /// <summary>پاک‌کردن پرچم ساختاری پس از یک بررسی موفق.</summary>
    public void ClearStructural()
    {
        lock (_gate) _structural = false;
    }

    /// <summary>ثبت یک بررسی موفق (لنگر پنجرهٔ تخریب).</summary>
    public void MarkSuccess()
    {
        lock (_gate)
        {
            _lastSuccessLocal = DateTime.Now;
            _firstFailureLocal = null;
            _structural = false;
        }
    }

    /// <summary>ثبت یک خطای گذرا و برگرداندن «آیا هنوز داخل پنجره هستیم؟»</summary>
    public bool MarkTransientFailure(DateTime nowLocal)
    {
        lock (_gate)
        {
            var anchor = _lastSuccessLocal ?? _firstFailureLocal ?? nowLocal;
            if (_firstFailureLocal is null) _firstFailureLocal = nowLocal;
            var elapsed = nowLocal - anchor;
            return elapsed <= TimeSpan.FromMinutes(DegradedWindowMinutes);
        }
    }

    /// <summary>
    /// آیا پنجرهٔ تخریب باز است؟ (بدون ثبت خطای تازه)
    /// </summary>
    public bool IsWithinDegradedWindow(DateTime nowLocal)
    {
        lock (_gate)
        {
            var anchor = _lastSuccessLocal ?? _firstFailureLocal;
            if (anchor is null) return true;
            return nowLocal - anchor.Value <= TimeSpan.FromMinutes(DegradedWindowMinutes);
        }
    }

    /// <summary>حذف یک نشست از کش (خروج، بسته‌شدن، تخلیه).</summary>
    public void Remove(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        _sessions.TryRemove(sessionId, out _);
    }

    /// <summary>خالی‌کردن کامل کش نشست‌ها (مثلاً پس از تغییر لایسنس).</summary>
    public void ClearSessions() => _sessions.Clear();

    /// <summary>
    /// تلاش برای خواندن از کش. اصابت: هیت می‌شود؛ عدم اصابت: میس.
    /// </summary>
    public bool TryGet(string sessionId, DateTime nowLocal, out Entry entry)
    {
        if (!string.IsNullOrWhiteSpace(sessionId) && _sessions.TryGetValue(sessionId, out entry!) && entry.ExpiresAt > nowLocal)
        {
            Interlocked.Increment(ref _hits);
            return true;
        }

        Interlocked.Increment(ref _misses);
        entry = null!;
        return false;
    }

    /// <summary>گذاشتن نتیجهٔ بررسی در کش برای <paramref name="seconds"/> ثانیه.</summary>
    public void Set(string sessionId, int userId, string status, int seconds, DateTime nowLocal)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        _sessions[sessionId] = new Entry
        {
            UserId = userId,
            Status = status,
            ExpiresAt = nowLocal.AddSeconds(Math.Clamp(seconds, 1, 3600)),
            TouchedAt = nowLocal
        };
    }

    /// <summary>یک ورودی کش.</summary>
    public sealed class Entry
    {
        public int UserId { get; init; }
        public string Status { get; init; } = "Active";
        public DateTime ExpiresAt { get; init; }
        public DateTime TouchedAt { get; set; }
    }
}
