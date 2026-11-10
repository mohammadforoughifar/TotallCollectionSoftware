using Inventory.Licensing.Entitlement;
using Inventory.Licensing.Model;

namespace Inventory.Licensing.Abstractions;

/// <summary>نتیجهٔ بررسی سهمیه.</summary>
public sealed class QuotaConsumeResult
{
    public bool Allowed { get; init; }

    /// <summary>متریکی که سقفش پر شده (یا مصرف شده).</summary>
    public string? Metric { get; init; }

    /// <summary>مصرف پس از این عملیات.</summary>
    public long Used { get; init; }

    /// <summary>سقف مؤثر.</summary>
    public long Limit { get; init; }

    /// <summary>کلید دوره (شمسی یا «-» برای یک‌بار-برای-همیشه).</summary>
    public string? PeriodKey { get; init; }

    /// <summary>پیام فارسیِ آماده برای نمایش به کاربر.</summary>
    public string Message { get; init; } = "";

    /// <summary>آیا به ۸۰٪ سقف رسیده‌ایم؟ (هشدار مرحله ۲)</summary>
    public bool IsNearLimit => Allowed && Limit > 0 && Used * 100 >= Limit * 80;

    public static QuotaConsumeResult Ok(string metric, long used, long limit, string periodKey)
        => new() { Allowed = true, Metric = metric, Used = used, Limit = limit, PeriodKey = periodKey };

    public static QuotaConsumeResult Unlimited() => new() { Allowed = true };

    public static QuotaConsumeResult Denied(string metric, long used, long limit, string periodKey, string message)
        => new() { Allowed = false, Metric = metric, Used = used, Limit = limit, PeriodKey = periodKey, Message = message };
}

/// <summary>نتیجهٔ بررسی اجازهٔ ورود کاربر.</summary>
public sealed class SeatCheckResult
{
    public bool Allowed { get; init; }

    /// <summary>کد ماشین‌خوان پیام (برای UI).</summary>
    public string Code { get; init; } = "";

    public string Message { get; init; } = "";

    /// <summary>صندلی‌های مصرف‌شده و سقف (برای نمایش).</summary>
    public int Used { get; init; }
    public int? Limit { get; init; }

    public static SeatCheckResult Ok(int used = 0, int? limit = null)
        => new() { Allowed = true, Code = "OK", Used = used, Limit = limit };

    public static SeatCheckResult Denied(string code, string message, int used, int? limit)
        => new() { Allowed = false, Code = code, Message = message, Used = used, Limit = limit };
}

/// <summary>
/// سرویس اصلی لایسنس — تنها مرجع تصمیم‌گیری.
///
/// <para>
/// تمام تصمیم‌های ایمنی از همین‌جا می‌آید: وضعیت ماژول، سقف سهمیه، اجازهٔ ورود.
/// کلاینت Blazor هیچ‌کدام از این تصمیم‌ها را نمی‌گیرد؛ فقط نتیجهٔ همین سرویس را
/// نمایش می‌دهد (مخفی‌کردن منو و نمایش بنر).
/// </para>
/// </summary>
public interface ILicenseService
{
    /// <summary>وضعیت یک ماژول (ماژول همیشه‌فعال: همیشه Valid).</summary>
    ModuleState GetModuleState(string module);

    /// <summary>اطلاعات نمایشی یک ماژول.</summary>
    ModuleEntitlement? GetModule(string module);

    /// <summary>
    /// Entitlement مؤثر — ادغام همهٔ اسناد.
    /// کش می‌شود و با هر تغییر لایسنس باطل می‌گردد.
    /// </summary>
    EffectiveEntitlement GetEffectiveEntitlement();

    /// <summary>همان نتیجه ولی با اطمینان از بارگذاری (اولین درخواست پس از راه‌اندازی).</summary>
    Task<EffectiveEntitlement> GetEffectiveEntitlementAsync(CancellationToken cancellationToken = default);

    /// <summary>وضعیت کلی نصب (Missing / Invalid / Valid).</summary>
    LicenseOverallStatus GetOverallStatus();

    /// <summary>
    /// مصرف یک واحد از متریک — اتمیک و مقاوم در برابر درخواست‌های هم‌زمان.
    /// اگر سقف پر باشد <see cref="QuotaConsumeResult.Allowed"/> = false برمی‌گردد و
    /// فراخوان موظف است ثبت رکورد را متوقف کند.
    /// </summary>
    Task<QuotaConsumeResult> TryConsumeAsync(string module, string metric, int amount = 1, CancellationToken cancellationToken = default);

    /// <summary>آیا این کاربر اجازهٔ ورود دارد؟ (سیاست صندلی Named/Concurrent)</summary>
    Task<SeatCheckResult> CanLoginUserAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>ابطال کش — پس از بارگذاری/تمدید/غیرفعال‌سازی فایل لایسنس (بدون ری‌استارت).</summary>
    void Invalidate();
}
