using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Licensing.Services;

/// <summary>
/// زمان‌های «مقاوم» خودِ نصب — امروز فقط «اولین استارت این بیلد».
///
/// <para>
/// <b>مسئله‌ای که حل می‌کند:</b> توکن‌های صادرشده توسط نسخهٔ قبلی، claim
/// <c>sid</c> ندارند. اگر بعد از استقرار بیلد تازه برای همیشه پذیرفته شوند،
/// کاربران می‌توانند نسخهٔ قدیمیِ توکن را نگه دارند و از سقف صندلی فرار کنند؛
/// اگر همان لحظه رد شوند، همهٔ کاربران بدون هیچ توضیحی بیرون می‌افتند.
/// راه میانه: پنجرهٔ کوتاه (پیش‌فرض ۱۳ ساعت، از «اولین استارت همین بیلد»).
/// </para>
///
/// <para>
/// <b>چرا در دیتابیس؟</b> در حافظه با ری‌استارت صفر می‌شود (پنجره تا ابد باز
/// می‌ماند) و فایل کنار برنامه با انتشار بعدی پاک می‌شود. دیتابیس همان جایی است
/// که بقیهٔ وضعیت لایسنس هست.
/// </para>
///
/// <para>
/// عمداً <b>هیچ</b> راه پیکربندی برای تمدید پنجره یا ریست آن وجود ندارد: نه
/// endpoint، نه appsettings. فقط پاسخ درست، اصلاح ساعت/بیلد است.
/// </para>
/// </summary>
public sealed class LicenseRuntimeState
{
    /// <summary>کلید ردیف «اولین استارت این بیلد».</summary>
    public const string FirstStartKey = "build.firstStart";

    /// <summary>
    /// طول پنجرهٔ پذیرش توکن بدون <c>sid</c> (ساعت). حداکثر عمر توکن ۱۲ ساعت است،
    /// پس ۱۳ ساعت یعنی «همهٔ توکن‌های معتبر امروز یک فرصت کامل برای ورود مجدد دارند».
    /// </summary>
    public const int LegacyTokenWindowHours = 13;

    /// <summary>کد خطای پاسخ به توکن بدون sid در بیرون از پنجره.</summary>
    public const string SessionEndedCode = "LICENSE_SESSION_ENDED";

    private readonly AppDbContext _db;
    private readonly ILogger<LicenseRuntimeState> _log;

    public LicenseRuntimeState(AppDbContext db, ILogger<LicenseRuntimeState> log)
    {
        _db = db;
        _log = log;
    }

    /// <summary>
    /// زمان اولین استارت این بیلد؛ اگر ردیف نبود، همین حالا ساخته و برگردانده می‌شود.
    ///
    /// <para>
    /// زمان با <see cref="ClockAnchor.Now"/> گرفته می‌شود تا تست بتواند زمان را
    /// جلو ببرد، ولی هیچ مسیر تنظیماتی در تولید وجود ندارد.
    /// </para>
    /// </summary>
    public async Task<DateTime> EnsureFirstStartAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _db.LicenseRuntime.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == FirstStartKey, cancellationToken);
        if (existing is not null && DateTime.TryParse(existing.Value, out var parsed))
            return parsed;

        var now = ClockAnchor.Now();
        _db.LicenseRuntime.Add(new LicenseRuntime
        {
            Key = FirstStartKey,
            Value = now.ToString("o"),
            UpdatedAt = now
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // دو اپلیکیشن‌اینستنس هم‌زمان: یکی درج می‌کند، دیگری کلید تکراری می‌گیرد.
            // پاسخ درست «همان مقدار برنده» است، نه خطا.
            _db.ChangeTracker.Clear();
            var winner = await _db.LicenseRuntime.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Key == FirstStartKey, cancellationToken);
            if (winner is not null && DateTime.TryParse(winner.Value, out var won))
                return won;
            throw;
        }

        _log.LogInformation("[License] اولین استارت این بیلد: {At:o} (پنجرهٔ توکن بدون sid: {Hours} ساعت)",
            now, LegacyTokenWindowHours);
        return now;
    }

    /// <summary>
    /// آیا در پنجرهٔ پذیرش توکن بدون <c>sid</c> هستیم؟
    /// </summary>
    /// <param name="now">«اکنونِ مؤثر» (ساعت سیستم یا لنگر، هرکدام جلوتر).</param>
    public async Task<bool> IsLegacyTokenWindowOpenAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var firstStart = await EnsureFirstStartAsync(cancellationToken);
        var closesAt = firstStart.AddHours(LegacyTokenWindowHours);
        return now <= closesAt;
    }
}
