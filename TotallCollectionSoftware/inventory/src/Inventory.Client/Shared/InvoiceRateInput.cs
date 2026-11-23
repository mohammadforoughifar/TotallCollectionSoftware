using System.Globalization;
using Inventory.Shared;

namespace Inventory.Client.Shared;

/// <summary>
/// قاعدهٔ فیلدهای عددی «تخفیف» و «ارزش افزوده» در فرم‌های فاکتور فروش/خرید:
/// مقدار صفر به‌صورت «0» نمایش داده می‌شود، با فوکوس فیلد خالی می‌شود تا کاربر
/// بدون پاک‌کردن دستی تایپ کند، و اگر چیزی تایپ نشود با خروج از فیلد دوباره «0»
/// می‌شود. مقدار مدل همیشه با <see cref="Value"/> از همین متن خوانده می‌شود.
/// </summary>
internal static class InvoiceRateInput
{
    /// <summary>مقدار عددی برای محاسبهٔ زنده؛ متن خالی یا نامعتبر صفر است.</summary>
    public static decimal Value(string? text) => Math.Max(0m, Fa.ParseDecimal(text));

    /// <summary>
    /// متن نمایشی مقدار: صفر «0» است (نه خالی) تا فیلد وضعیت واقعی را نشان دهد؛
    /// عدد مثبت بدون ارقام فارسی چون فیلد dir=ltr است.
    /// </summary>
    public static string Text(decimal value, bool grouped = false)
    {
        if (value <= 0) return "0";
        var text = value.ToString("0.##", CultureInfo.InvariantCulture);
        return grouped ? Fa.FormatTyping(text) : text;
    }

    /// <summary>متن فیلد در لحظهٔ فوکوس: صفر پاک می‌شود، عدد موجود دست‌نخورده می‌ماند.</summary>
    public static string Focused(decimal value, bool grouped = false) => value > 0 ? Text(value, grouped) : "";

    /// <summary>
    /// متن فیلد پس از خروج: اگر کاربر چیزی تایپ نکرده باشد همان «0» برمی‌گردد؛
    /// در غیر این صورت متن تمیزشدهٔ همان مقدار.
    /// </summary>
    public static string AfterBlur(string? typed, decimal value, bool grouped = false)
        => string.IsNullOrWhiteSpace(typed) ? Text(0) : Text(value, grouped);

    /// <summary>
    /// متن فیلد حین تایپ: ارقام فارسی/عربی به لاتین تبدیل و نویسه‌های غیرعددی حذف
    /// می‌شوند (به‌جز یک جداکنندهٔ اعشار). متنی که هیچ رقمی ندارد دست‌نخورده می‌ماند
    /// تا کاربر بتواند اصلاحش کند و «0.0» به «0» جمع می‌شود.
    /// </summary>
    public static string Typing(string? raw, bool grouped = false)
    {
        var latin = Fa.ToEn(raw ?? "").Trim();
        if (latin.Length == 0) return "";
        var filtered = new System.Text.StringBuilder(latin.Length);
        var dot = false;
        foreach (var c in latin)
        {
            if (char.IsAsciiDigit(c)) { filtered.Append(c); continue; }
            if (c is ('.' or '٫') && !dot) { dot = true; filtered.Append('.'); }
            // جداکنندهٔ هزارگان حین تایپ دوباره ساخته می‌شود.
            else if (c is not (',' or '٬')) filtered.Append(c);
        }
        var text = filtered.ToString();
        if (!text.Any(char.IsAsciiDigit)) return latin;   // ورودی نامعتبر برای اصلاح می‌ماند
        if (text.All(c => c is '0' or '.')) return "0";   // «0»، «00»، «0.0» همه صفرند
        return grouped ? Fa.FormatTyping(text) : text;
    }
}
