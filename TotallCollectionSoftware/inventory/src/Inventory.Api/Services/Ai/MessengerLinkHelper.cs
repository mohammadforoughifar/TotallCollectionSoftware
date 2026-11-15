namespace Inventory.Api.Services.Ai;

/// <summary>
/// کمک‌تابعِ مشترک برای تشخیص «لینک‌بودنِ کاربر به یک پیام‌رسان».
///
/// <para>
/// چرا این کمک‌تابع لازم است؟ کاربر ممکن است به دو شکل به بله متصل باشد:
/// <list type="bullet">
/// <item><b>مسیر تعاملی</b> — کاربر خودش ربات را /start کرده و شناسهٔ چت (<c>BaleChatId</c>) ذخیره شده است.</item>
/// <item><b>مسیر شماره</b> — مدیر سامانه کاربر را از مسیر «سفیر» متصل کرده؛
/// شمارهٔ نرمال‌شده در <c>BalePhone</c> و پرچم <c>BalePhoneLinked</c> ذخیره شده است.
/// این مسیر بدون هیچ اقدامی از سوی کاربر کار می‌کند.</item>
/// </list>
/// </para>
///
/// <para>
/// اگر فقط <c>BaleChatId</c> بررسی شود، کاربرانی که فقط از مسیر شماره متصل شده‌اند
/// از گزارش‌ها و اعلان‌های خودکار حذف می‌شوند — درست خلافِ هدفِ «کاربر هرگز
/// مجبور به اتصال به بله نشود». پس هر جایی که «مخاطبِ پیام‌رسان» انتخاب می‌شود
/// باید از این کمک‌تابع استفاده کند.
/// </para>
/// </summary>
public static class MessengerLinkHelper
{
    /// <summary>
    /// آیا کاربر به بله یا ایتا لینک است؟ (بله: چتِ تعاملی یا شماره؛ ایتا: شناسهٔ چت)
    /// </summary>
    /// <param name="baleChatId">شناسهٔ چت بله (مسیر تعاملی) — ممکن است null باشد.</param>
    /// <param name="balePhoneLinked">آیا اتصال از مسیر شماره (سفیر) برقرار شده است؟</param>
    /// <param name="eitaaChatId">شناسهٔ چت ایتا — ممکن است null باشد.</param>
    public static bool HasMessengerLink(string? baleChatId, bool balePhoneLinked, string? eitaaChatId)
        => !string.IsNullOrWhiteSpace(baleChatId)
           || balePhoneLinked
           || !string.IsNullOrWhiteSpace(eitaaChatId);
}
