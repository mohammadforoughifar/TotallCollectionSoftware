using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// شمارندهٔ مصرف سهمیه.
///
/// <para>
/// کلید طبیعی: <b>ماژول + متریک + دوره</b> (نه آیتم). چرا نه آیتم؟ چون با تمدید یا
/// تغییر آیتم‌ها، مصرف گذشته باید همان‌جا بماند و از صفر شروع نشود؛ وگرنه مشتری با
/// تمدید، سهمیهٔ مصرف‌شده را «ریست» می‌کرد.
/// </para>
///
/// <para>
/// ستون <see cref="Limit"/> عکس لحظه‌ای سقف مؤثر است (برای اینکه گزارش بگوید «از ۱۰۰۰
/// تا چندتا استفاده شد»). ملاک اجازه‌دادن در مرحلهٔ ۲ همان سقف مؤثرِ محاسبه‌شده است.
/// </para>
/// </summary>
public class LicenseUsage
{
    public int Id { get; set; }

    [MaxLength(32)]
    public string Module { get; set; } = "";

    [MaxLength(64)]
    public string Metric { get; set; } = "";

    /// <summary>کلید دوره: «-» برای یک‌بار-برای-همیشه، «1405-08» ماهانه، «1405» سالانه.</summary>
    [MaxLength(16)]
    public string PeriodKey { get; set; } = "-";

    /// <summary>سقف مؤثر در لحظهٔ آخرین به‌روزرسانی.</summary>
    public long Limit { get; set; }

    public long Used { get; set; }

    /// <summary>HMAC ردیف — ضد دست‌کاری مستقیم در دیتابیس (مرحله ۲).</summary>
    [MaxLength(88)]
    public string? Hmac { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
