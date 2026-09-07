using System.Globalization;

namespace RadisHr.Client.Services;

/// <summary>کمک‌تابع‌های نمایش فارسی — معادل fa()/format() در app.js</summary>
public static class Fa
{
    private static readonly CultureInfo Persian = CultureInfo.GetCultureInfo("fa-IR");

    public static readonly string[] MonthNames =
    {
        "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
        "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
    };

    public static readonly string[] WeekDays = { "شنبه", "یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه" };

    /// <summary>عدد با جداکنندهٔ سه‌رقمی و ارقام فارسی</summary>
    public static string N(decimal value) => value.ToString("#,##0", Persian);
    public static string N(int value) => value.ToString("#,##0", Persian);
    public static string N(double value) => value.ToString("#,##0.##", Persian);

    /// <summary>عدد لاتین با جداکنندهٔ سه‌رقمی (برای ورودی‌های ریالی)</summary>
    public static string Rial(decimal value) => value.ToString("#,##0", CultureInfo.InvariantCulture);

    /// <summary>تبدیل رشتهٔ ورودی ریالی (با کاما و ارقام فارسی) به عدد</summary>
    public static decimal ParseRial(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0m;
        var digits = new string(ToLatin(text).Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? 0m : decimal.Parse(digits, CultureInfo.InvariantCulture);
    }

    public static string ToLatin(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] >= '۰' && chars[i] <= '۹') chars[i] = (char)('0' + (chars[i] - '۰'));
            else if (chars[i] >= '٠' && chars[i] <= '٩') chars[i] = (char)('0' + (chars[i] - '٠'));
        }
        return new string(chars);
    }

    public static string ToPersianDigits(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (chars[i] >= '0' && chars[i] <= '9') chars[i] = (char)('۰' + (chars[i] - '0'));
        return new string(chars);
    }

    /// <summary>«1405/03» → «خرداد ۱۴۰۵»</summary>
    public static string MonthLabel(string monthKey)
    {
        var parts = (monthKey ?? "").Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var m) || m < 1 || m > 12) return monthKey ?? "";
        return $"{MonthNames[m - 1]} {ToPersianDigits(parts[0])}";
    }

    public static string Percent(double value) => $"{value.ToString("0.#", Persian)}٪";
    public static string Percent(decimal value) => $"{value.ToString("0.##", Persian)}٪";

    /// <summary>نمایش رشته با ارقام فارسی (کد پرسنلی، تاریخ، ساعت)</summary>
    public static string N(string? text) => ToPersianDigits(text ?? "");

    /// <summary>مبلغ ریالی با ارقام فارسی</summary>
    public static string Money(decimal value) => N(value);

    /// <summary>normalize + تشخیص تأهل — همان قاعدهٔ نسخهٔ اصلی (آ → ا و شامل «متاهل»)</summary>
    public static bool IsMarried(string? married) =>
        (married ?? "").Replace('\u0622', '\u0627').Contains("متاهل");

    /// <summary>دقیقه → «ساعت:دقیقه»</summary>
    public static string Hm(int minutes)
    {
        var sign = minutes < 0 ? "-" : "";
        minutes = Math.Abs(minutes);
        return $"{sign}{ToPersianDigits((minutes / 60).ToString())}:{ToPersianDigits((minutes % 60).ToString("00"))}";
    }
}
