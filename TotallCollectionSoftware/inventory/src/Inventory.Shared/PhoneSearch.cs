namespace Inventory.Shared;

/// <summary>
/// همسانسازی شمارهٔ تلفن برای «جستجوی مشتری».
/// <para>
/// کاربر ممکن است شماره را با ارقام فارسی/عربی، با یا بدون صفر آغازین، با پیششمارهٔ
/// 0098/+98 یا با خط تیره و فاصله تایپ کند؛ در حالی که شمارهٔ ذخیرهشده شکل دیگری دارد.
/// این کلاس فقط برای <b>جستجو</b> است و هیچ دادهٔ ذخیرهشدهای را تغییر نمیدهد.
/// </para>
/// </summary>
public static class PhoneSearch
{
    /// <summary>
    /// فقط ارقام لاتین متن: ارقام فارسی (۰–۹) و عربی (٠–٩) تبدیل میشوند،
    /// بقیهٔ نویسهها (فاصله، خط تیره، پرانتز، +) حذف میشوند و پیششمارهٔ کشور
    /// 0098 / +98 به صفر آغازین تبدیل میشود.
    /// </summary>
    public static string Digits(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch >= '0' && ch <= '9') sb.Append(ch);
            else if (ch >= '\u06F0' && ch <= '\u06F9') sb.Append((char)('0' + (ch - '\u06F0')));
            else if (ch >= '\u0660' && ch <= '\u0669') sb.Append((char)('0' + (ch - '\u0660')));
        }
        var d = sb.ToString();
        if (d.StartsWith("0098", StringComparison.Ordinal)) return "0" + d.Substring(4);
        if (d.StartsWith("98", StringComparison.Ordinal) && d.Length >= 12) return "0" + d.Substring(2);
        return d;
    }

    /// <summary>کلید یکسان برای 0912... / 912... / +98 912... — بدون صفر آغازین.</summary>
    public static string Key(string? value) => Digits(value).TrimStart('0');

    /// <summary>
    /// آیا متن جستجو با یکی از فیلدها مطابقت دارد؟ ابتدا تطبیق معمولی (بیتفاوت به
    /// بزرگی/کوچکی حروف)، سپس تطبیق رقمبهرقم پس از همسانسازی. حداقل ۳ رقم لازم است
    /// تا اعداد کوتاه داخل نام، نتایج تلفنی ناخواسته نسازند.
    /// </summary>
    public static bool Matches(string? query, params string?[] fields)
    {
        var q = (query ?? "").Trim();
        if (q.Length == 0) return true;
        var key = Key(q);
        foreach (var f in fields)
        {
            if (string.IsNullOrWhiteSpace(f)) continue;
            if (f.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
            if (key.Length >= 3 && Key(f).Contains(key, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    /// کلیدهای قابلترجمه در پرسوجوهای EF (همه ناخالی؛ در نبود رقم، متن خام تکرار
    /// میشود تا <c>Contains("")</c> باعث تطبیق همهٔ ردیفها نشود).
    /// </summary>
    public static (string Digits, string NoZero, string WithZero) SqlKeys(string? search)
    {
        var s = (search ?? "").Trim();
        var d = Digits(s);
        var k = Key(s);
        return (
            d.Length >= 3 ? d : s,
            k.Length >= 3 ? k : s,
            k.Length >= 3 ? "0" + k : s);
    }
}
