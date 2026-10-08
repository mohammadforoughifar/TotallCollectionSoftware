using System.Globalization;

namespace Inventory.Shared;

/// <summary>ابزارهای متن فارسی: تبدیل ارقام و قالب‌بندی عدد/مبلغ.</summary>
public static class Fa
{
    private const string EN = "0123456789";
    private const string FA = "۰۱۲۳۴۵۶۷۸۹";

    /// <summary>تبدیل ارقام انگلیسی یک رشته به فارسی.</summary>
    public static string Digits(string? input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        var chars = input.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            int idx = EN.IndexOf(chars[i]);
            if (idx >= 0) chars[i] = FA[idx];
        }
        return new string(chars);
    }

    /// <summary>تبدیل ارقام انگلیسی به فارسی (برای اعداد).</summary>
    public static string Digits(long n) => Digits(n.ToString(CultureInfo.InvariantCulture));
    public static string Digits(decimal n) => Digits(n.ToString(CultureInfo.InvariantCulture));

    /// <summary>تبدیل ارقام فارسی/عربی به انگلیسی (برای پردازش ورودی کاربر).</summary>
    public static string ToEn(string? input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        var chars = input.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            int idx = FA.IndexOf(chars[i]);
            if (idx >= 0) chars[i] = EN[idx];
            else if (chars[i] == '٠') chars[i] = '0';
            else if (chars[i] == '١') chars[i] = '1';
            else if (chars[i] == '٢') chars[i] = '2';
            else if (chars[i] == '٣') chars[i] = '3';
            else if (chars[i] == '٤') chars[i] = '4';
            else if (chars[i] == '٥') chars[i] = '5';
            else if (chars[i] == '٦') chars[i] = '6';
            else if (chars[i] == '٧') chars[i] = '7';
            else if (chars[i] == '٨') chars[i] = '8';
            else if (chars[i] == '٩') chars[i] = '9';
        }
        return new string(chars);
    }

    /// <summary>تبدیل امن رشته به decimal (با پشتیبانی از ارقام فارسی).</summary>
    public static decimal ParseDecimal(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return 0;
        var s = ToEn(input).Replace(",", "").Replace("٬", "").Replace("٫", ".");
        if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)) return v;
        return 0;
    }

    /// <summary>تبدیل امن رشته به int.</summary>
    public static int ParseInt(string? input)
    {
        var s = ToEn(input).Trim();
        return int.TryParse(s, out var v) ? v : 0;
    }

    /// <summary>قالب‌بندی مبلغ با جداکننده هزارگان و ارقام فارسی.</summary>
    public static string Money(decimal amount)
    {
        var n = decimal.Round(amount, MidpointRounding.AwayFromZero);
        var s = n.ToString("#,##0", CultureInfo.InvariantCulture);
        return Digits(s);
    }

    /// <summary>قالب‌بندی عدد صحیح با جداکننده هزارگان و ارقام فارسی.</summary>
    public static string Number(decimal n)
    {
        var s = decimal.Round(n, MidpointRounding.AwayFromZero).ToString("#,##0", CultureInfo.InvariantCulture);
        return Digits(s);
    }

    /// <summary>مبلغ به‌صورت عدد و حروف فارسی (ساده).</summary>
    public static string MoneyWithWords(decimal amount) => $"{Money(amount)} ریال";

    private static readonly string[] WordUnits =
        { "", "یک", "دو", "سه", "چهار", "پنج", "شش", "هفت", "هشت", "نُه", "ده",
          "یازده", "دوازده", "سیزده", "چهارده", "پانزده", "شانزده", "هفده", "هجده", "نوزده" };
    private static readonly string[] WordTens =
        { "", "", "بیست", "سی", "چهل", "پنجاه", "شصت", "هفتاد", "هشتاد", "نود" };
    private static readonly string[] WordScales = { "", "هزار", "میلیون", "میلیارد", "تریلیون" };

    /// <summary>مبلغ به حروف فارسی (مانند «مجموع به حروف» روی صورتحساب).</summary>
    public static string Words(decimal amount)
    {
        var negative = amount < 0;
        var abs = decimal.Abs(amount);
        var n = decimal.ToInt64(decimal.Truncate(abs));
        var frac = decimal.ToInt64(decimal.Round((abs - n) * 100));

        if (n == 0 && frac == 0) return "صفر";

        var groups = new List<long>();
        while (n > 0)
        {
            groups.Insert(0, n % 1000);
            n /= 1000;
        }

        var parts = new List<string>();
        for (var i = 0; i < groups.Count; i++)
        {
            var g = groups[i];
            if (g == 0) continue;
            var scale = groups.Count - 1 - i;
            var groupText = GroupToWords((int)g);
            if (scale > 0 && scale < WordScales.Length)
                groupText = scale == 2 && g == 1 ? "یک میلیون" : groupText + " " + WordScales[scale];
            parts.Add(groupText);
        }

        var result = string.Join(" و ", parts);
        if (frac > 0)
            result += " و " + GroupToWords((int)frac) + " قرش";
        return negative ? "منفی " + result : result;
    }

    private static string GroupToWords(int g)
    {
        if (g == 0) return "";
        var parts = new List<string>();
        var h = g / 100;
        var rest = g % 100;
        if (h > 0) parts.Add(h == 1 ? "صد" : WordUnits[h] + "صد");
        if (rest > 0)
        {
            if (rest < 20) parts.Add(WordUnits[rest]);
            else
            {
                var t = rest / 10;
                var o = rest % 10;
                parts.Add(o == 0 ? WordTens[t] : WordTens[t] + " و " + WordUnits[o]);
            }
        }
        return string.Join(" و ", parts);
    }

    /// <summary>قالب‌بندی زنده ورودی عددی حین تایپ با جداکننده سه‌رقمی (حفظ اعشار + ارقام فارسی).</summary>
    public static string FormatTyping(string? value)
    {
        var raw = ToEn(value ?? "").Replace(",", "").Trim();
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var parts = raw.Split('.', 2);
        var intDigits = new string(parts[0].Where(char.IsDigit).ToArray());
        if (intDigits.Length == 0)
        {
            if (parts.Length == 2) intDigits = "0";
            else return "";
        }

        if (!decimal.TryParse(intDigits, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            return "";

        var formatted = parsed.ToString("#,##0", CultureInfo.InvariantCulture);
        if (parts.Length == 2)
        {
            var frac = new string(parts[1].Where(char.IsDigit).ToArray());
            return $"{formatted}.{frac}";
        }
        return formatted;
    }
}
