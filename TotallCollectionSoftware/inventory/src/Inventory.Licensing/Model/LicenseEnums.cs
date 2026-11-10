namespace Inventory.Licensing.Model;

/// <summary>نوع استقرار — در فایل امضاشده نوشته می‌شود و مبنای اعتبارسنجی است.</summary>
public enum DeploymentKind
{
    /// <summary>نصب روی سرور مشتری، بدون اینترنت</summary>
    OnPrem = 0,

    /// <summary>میزبانی چندمستأجری روی سرور ما</summary>
    Saas = 1
}

/// <summary>مدل زمانی یک آیتم لایسنس.</summary>
public enum LicenseTermType
{
    /// <summary>اشتراکی — تاریخ شروع و پایان اجباری است.</summary>
    Subscription = 0,

    /// <summary>دائمی — تاریخ پایان ندارد؛ فقط «آخرین نسخهٔ قابل دریافت» دارد.</summary>
    Perpetual = 1,

    /// <summary>آزمایشی — مثل اشتراکی زمان‌دار، ولی معمولاً کوتاه و با وسعت کمتر.</summary>
    Trial = 2
}

/// <summary>سیاست کاربر در یک آیتم.</summary>
public enum LicenseUserMode
{
    /// <summary>این آیتم کاربری تعریف نمی‌کند (مثلاً فقط سهمیهٔ فاکتور می‌فروشد).</summary>
    None = 0,

    /// <summary>کاربر نام‌دار — به تعداد حساب‌های فعال شمرده می‌شود.</summary>
    Named = 1,

    /// <summary>کاربر هم‌زمان — به تعداد نشست‌های فعال شمرده می‌شود.</summary>
    Concurrent = 2
}

/// <summary>دامنهٔ اعمال سیاست کاربر.</summary>
public enum LicenseUserScope
{
    /// <summary>سراسری — همهٔ کاربران سیستم شمرده می‌شوند.</summary>
    Global = 0,

    /// <summary>فقط کاربرانی که به ماژول‌های scope همان آیتم دسترسی دارند.</summary>
    Modules = 1
}

/// <summary>دورهٔ سهمیه.</summary>
public enum QuotaPeriod
{
    /// <summary>یک‌بار برای همیشه (کل عمر نصب).</summary>
    Lifetime = 0,

    /// <summary>ماهانه — کلید دوره بر اساس تقویم شمسی (مثل 1405-08).</summary>
    Monthly = 1,

    /// <summary>سالانه — کلید دوره سال شمسی (مثل 1405).</summary>
    Yearly = 2
}
