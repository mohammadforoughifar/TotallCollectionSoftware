namespace Inventory.Licensing.Entitlement;

/// <summary>
/// وضعیت یک ماژول. هر ماژول مستقل محاسبه می‌شود؛ انقضای یک آیتم فقط ماژول‌های
/// همان آیتم را تحت تأثیر می‌گذارد، نه کل سیستم.
/// </summary>
public enum ModuleState
{
    /// <summary>معتبر — همه‌چیز مجاز.</summary>
    Valid = 0,

    /// <summary>اعتبار تا ۳۰ روز دیگر تمام می‌شود — مثل Valid کار می‌کند، ولی بنر هشدار دارد.</summary>
    ExpiringSoon = 1,

    /// <summary>
    /// تا ۱۵ روز بعد از انقضا — مهلت تنفس. هنوز کامل کار می‌کند تا مشتری فرصت تمدید داشته باشد.
    /// </summary>
    Grace = 2,

    /// <summary>
    /// فقط مشاهده و خروجی. عملیات نوشتن رد می‌شود، ولی دادهٔ مشتری هرگز قفل نمی‌شود.
    /// </summary>
    ReadOnly = 3,

    /// <summary>در لایسنس این نصب نیست — منو پنهان و درخواست‌ها ۴۰۳ می‌گیرند.</summary>
    Disabled = 4
}

/// <summary>وضعیت یک ماژول به‌همراه اطلاعات نمایشی.</summary>
public sealed class ModuleEntitlement
{
    public required string Module { get; init; }

    /// <summary>وضعیت محاسبه‌شده.</summary>
    public ModuleState State { get; set; } = ModuleState.Disabled;

    /// <summary>دورترین انقضای آیتم‌های معتبر؛ null یعنی دائمی یا بدون انقضا.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>آیتمی که این وضعیت از آن آمده (برای نمایش «از کدام آیتم»).</summary>
    public string? PrimaryItemId { get; set; }

    /// <summary>نوع مدل زمانی آیتم غالب.</summary>
    public string? PrimaryTermType { get; set; }

    /// <summary>همهٔ آیتم‌هایی که این ماژول را پوشش می‌دهند (معتبر یا منقضی).</summary>
    public IReadOnlyList<string> ItemIds { get; set; } = Array.Empty<string>();

    /// <summary>ماژول پایه‌ی همیشه‌فعال است (core, dashboard, cartable, backup, licensing).</summary>
    public bool IsAlwaysOn { get; init; }

    /// <summary>آیا عملیات نوشتن مجاز است؟</summary>
    public bool IsWritable => State is ModuleState.Valid or ModuleState.ExpiringSoon or ModuleState.Grace;

    /// <summary>آیا مشاهده و خروجی مجاز است؟ (همهٔ حالت‌ها به‌جز Disabled)</summary>
    public bool IsReadable => State != ModuleState.Disabled;

    /// <summary>نام فارسی وضعیت — برای UI و پیام خطا.</summary>
    public string StateTitle => State switch
    {
        ModuleState.Valid => "معتبر",
        ModuleState.ExpiringSoon => "در آستانهٔ انقضا",
        ModuleState.Grace => "مهلت تمدید",
        ModuleState.ReadOnly => "فقط خواندنی",
        _ => "غیرفعال"
    };
}
