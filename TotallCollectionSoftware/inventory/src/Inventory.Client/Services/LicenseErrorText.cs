namespace Inventory.Client.Services;

/// <summary>
/// پیام فارسی برای کدهای خطای لایسنس + «هیچ‌کدام خروج از حساب نیست».
///
/// <para>
/// <b>قاعدهٔ ثابت:</b> خطای لایسنس هیچ‌وقت کاربر را از حساب خارج نمی‌کند. خروج خودکار
/// فقط برای ۴۰۱ توکن نامعتبر است و آن هم در لایهٔ احراز هویت انجام می‌شود، نه این‌جا.
/// کاربرها باید بفهمند «چه چیزی بسته است و چه کسی می‌تواند بازش کند».
/// </para>
/// </summary>
public static class LicenseErrorText
{
    public const string ReadOnly = "LICENSE_READONLY";
    public const string ModuleDisabled = "MODULE_DISABLED";
    public const string QuotaExceeded = "QUOTA_EXCEEDED";
    public const string SeatFull = "SEAT_FULL";
    public const string SeatEnded = "SEAT_ENDED";
    public const string SessionEnded = "LICENSE_SESSION_ENDED";
    public const string AdminOnly = "LICENSE_ADMIN_ONLY";

    /// <summary>آیا این کد، خطای لایسنس است؟ (برای اینکه UI پیام مناسب نشان دهد)</summary>
    public static bool IsLicenseCode(string? code)
        => !string.IsNullOrWhiteSpace(code) && code!.StartsWith("LICENSE_", StringComparison.OrdinalIgnoreCase)
           || code is ModuleDisabled or QuotaExceeded or SeatFull or SeatEnded;

    /// <summary>
    /// پیام راهنما. <paramref name="serverMessage"/> اگر آمده باشد ترجیح داده می‌شود
    /// (متن دقیق سرور) و در غیر این صورت پیام پیش‌فرض.
    /// </summary>
    public static string For(string? code, string? serverMessage = null)
    {
        if (!string.IsNullOrWhiteSpace(serverMessage) && string.IsNullOrWhiteSpace(code)) return serverMessage!;

        var text = code switch
        {
            ReadOnly => "سامانه در حالت فقط‌خواندنی است؛ ثبت و ویرایش بسته است. مدیر باید وضعیت لایسنس را بررسی کند.",
            ModuleDisabled => "این بخش در لایسنس فعلی فعال نیست. برای فعال‌سازی با پشتیبانی تماس بگیرید.",
            QuotaExceeded => "سقف مصرف این بخش پر شده است. تا پایان دوره یا ارتقای بسته، امکان ثبت بیشتر نیست.",
            SeatFull => "ظرفیت کاربران هم‌زمان پر است. یکی از نشست‌ها را ببندید یا صبر کنید.",
            SeatEnded => "نشست شما پایان یافته است؛ کارهای ذخیره‌نشده را نگه دارید و دوباره وارد شوید.",
            SessionEnded => "نشست این نسخه از برنامه به پایان رسیده است. صفحه را تازه کنید و دوباره وارد شوید.",
            AdminOnly => "این کار فقط برای مدیر سیستم مجاز است.",
            _ => "درخواست انجام نشد."
        };

        if (!string.IsNullOrWhiteSpace(serverMessage) && !string.Equals(serverMessage, text, StringComparison.Ordinal))
            return text + " (" + serverMessage + ")";
        return text;
    }

    /// <summary>
    /// آیا باید کاربر را دوباره به ورود فرستاد؟ <c>false</c> برای همهٔ کدهای لایسنس —
    /// هیچ خطای لایسنسی مجوز خروج خودکار نمی‌دهد.
    /// </summary>
    public static bool RequiresRelogin(string? code) => false;
}
