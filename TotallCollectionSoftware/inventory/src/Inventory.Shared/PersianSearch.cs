using System.Globalization;
using System.Text;

namespace Inventory.Shared;

/// <summary>
/// جست‌وجوی فارسی داخل فهرست‌ها: ارقام فارسی/عربی، «ی/ي/ى»، «ک/ك»، اعراب و نیم‌فاصله
/// یکسان می‌شوند تا کاربر لازم نباشد شکل دقیق نوشته را بداند.
/// فقط برای مقایسه است؛ مقدار ذخیره‌شده هرگز تغییر نمی‌کند.
/// </summary>
public static class PersianSearch
{
    /// <summary>کلید مقایسه: متن‌های معادل یک کلید می‌گیرند.</summary>
    public static string Key(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var result = new StringBuilder(text.Length);
        foreach (var ch in text.Normalize(NormalizationForm.FormKC))
        {
            var category = char.GetUnicodeCategory(ch);
            if (char.IsWhiteSpace(ch) || ch == '\u0640' || ch == '\u200c' ||
                category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format)
                continue;
            result.Append(ch switch
            {
                '\u064a' or '\u0649' => '\u06cc', // ي / ى → ی
                '\u0643' => '\u06a9',             // ك → ک
                >= '\u06f0' and <= '\u06f9' => (char)('0' + ch - '\u06f0'),
                >= '\u0660' and <= '\u0669' => (char)('0' + ch - '\u0660'),
                _ => char.ToLowerInvariant(ch)
            });
        }
        return result.ToString();
    }

    /// <summary>
    /// آیا عبارت جست‌وجو با یکی از فیلدها می‌خواند؟ عبارت خالی یعنی «همه».
    /// هر واژهٔ عبارت جدا بررسی می‌شود تا «مشتری تسنی ۵۰۰» هم کار کند.
    /// </summary>
    /// <param name="query">متن تایپ‌شده در کادر جست‌وجو.</param>
    /// <param name="fields">مقادیر قابل جست‌وجوی همان ردیف (متن، عدد، تاریخ…).</param>
    public static bool Matches(string? query, params object?[] fields)
    {
        var tokens = Tokenize(query);
        if (tokens.Length == 0) return true;
        var keys = new List<string>(fields.Length);
        foreach (var field in fields)
        {
            var text = FieldText(field);
            if (text.Length == 0) continue;
            if (!keys.Contains(text)) keys.Add(text);
        }
        if (keys.Count == 0) return false;
        foreach (var token in tokens)
        {
            var hit = false;
            foreach (var key in keys)
            {
                if (!key.Contains(token, StringComparison.Ordinal)) continue;
                hit = true;
                break;
            }
            if (!hit) return false;
        }
        return true;
    }

    /// <summary>واژه‌های کلیدشدهٔ عبارت جست‌وجو (تکراری‌ها حذف می‌شوند).</summary>
    public static string[] Tokenize(string? query)
        => Key(query).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// متن قابل جست‌وجوی یک مقدار. اعداد با جداکنندهٔ سه‌رقمی و بدون آن هر دو کلید
    /// می‌شوند تا «500,000,000» و «500000000» هر دو پیدا شوند؛ تاریخ میلادی هم با
    /// ارقام لاتین کلید می‌شود.
    /// </summary>
    private static string FieldText(object? value)
    {
        switch (value)
        {
            case null:
                return string.Empty;
            case string s:
                return Key(s);
            case DateTime date:
                return Key(Fa.Digits(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            case decimal money:
                var plain = Key(money.ToString("0.##", CultureInfo.InvariantCulture));
                var grouped = Key(money.ToString("#,0.##", CultureInfo.InvariantCulture));
                return plain == grouped ? plain : plain + " " + grouped;
            case IFormattable formattable:
                return Key(formattable.ToString(null, CultureInfo.InvariantCulture));
            default:
                return Key(value.ToString());
        }
    }
}
