using System.Globalization;

namespace LicenseSigner.Services;

/// <summary>
/// تاریخ شمسی بدون وابستگی به ICU (برنامه با InvariantGlobalization اجرا می‌شود و
/// <c>PersianCalendar</c>/منطقهٔ زمانی در دسترس نیست). آفست تهران ثابت +03:30 است.
/// </summary>
public static class JalaliDate
{
    private static readonly TimeSpan TehranOffset = TimeSpan.FromMinutes(210);

    public static DateTime TehranNow() => DateTime.UtcNow + TehranOffset;

    /// <summary>تبدیل میلادی به شمسی (الگوریتم استاندارد).</summary>
    public static (int Year, int Month, int Day) ToJalali(DateTime date)
    {
        var gy = date.Year;
        var gm = date.Month;
        var gd = date.Day;
        var gDaysInMonth = new[] { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
        if (gy % 4 == 0 && (gy % 100 != 0 || gy % 400 == 0)) gDaysInMonth[1] = 29;

        var gy2 = gm > 2 ? gy + 1 : gy;
        var days = 355666 + (365 * gy) + ((gy2 + 3) / 4) - ((gy2 + 99) / 100) + ((gy2 + 399) / 400) + gd;
        for (var i = 0; i < gm - 1; i++) days += gDaysInMonth[i];

        var jy = -1595 + (33 * (days / 12053));
        days %= 12053;
        jy += 4 * (days / 1461);
        days %= 1461;
        if (days > 365)
        {
            jy += (days - 1) / 365;
            days = (days - 1) % 365;
        }

        var jm = 1;
        var jDays = new[] { 31, 31, 31, 31, 31, 31, 30, 30, 30, 30, 30, 29 };
        while (jm <= 12 && days >= jDays[jm - 1])
        {
            days -= jDays[jm - 1];
            jm++;
        }

        return (jy, jm, days + 1);
    }

    /// <summary>1405/08/23</summary>
    public static string Short(DateTime? date)
    {
        if (date is null) return "—";
        var (y, m, d) = ToJalali(date.Value);
        return PersianDigits($"{y:0000}/{m:00}/{d:00}");
    }

    /// <summary>1405/08/23 ۱۰:۳۱ — تاریخ و ساعت تهران.</summary>
    public static string Full(DateTime? utc)
    {
        if (utc is null) return "—";
        var local = utc.Value.Kind == DateTimeKind.Utc ? utc.Value + TehranOffset : utc.Value;
        var (y, m, d) = ToJalali(local);
        return PersianDigits($"{y:0000}/{m:00}/{d:00} {local:HH:mm}");
    }

    public static string FullNow() => Full(DateTime.UtcNow);

    /// <summary>تجزیهٔ تاریخ شمسی «1405/08/23» یا «1405-08-23» به میلادی.</summary>
    public static bool TryParse(string? text, out DateTime result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var normalized = NormalizeDigits(text).Replace('-', '/').Replace('.', '/').Trim();
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out var jy) || !int.TryParse(parts[1], out var jm) || !int.TryParse(parts[2], out var jd))
            return false;
        if (jy < 1300 || jy > 1500 || jm < 1 || jm > 12 || jd < 1 || jd > 31) return false;
        result = ToGregorian(jy, jm, jd);
        return true;
    }

    /// <summary>تبدیل شمسی به میلادی.</summary>
    public static DateTime ToGregorian(int jy, int jm, int jd)
    {
        var jy2 = jy + 1595;
        var days = -355668 + (365 * jy2) + ((jy2 / 33) * 8) + (((jy2 % 33) + 3) / 4) + jd;
        var jDays = new[] { 31, 31, 31, 31, 31, 31, 30, 30, 30, 30, 30, 29 };
        for (var i = 0; i < jm - 1; i++) days += jDays[i];
        if (jm > 7 || (jm == 7 && jd > 30)) days += 0;

        var gy = 400 * (days / 146097);
        days %= 146097;
        if (days > 36524)
        {
            gy += 100 * (--days / 36524);
            days %= 36524;
            if (days >= 365) days++;
        }

        gy += 4 * (days / 1461);
        days %= 1461;
        if (days > 365)
        {
            gy += (days - 1) / 365;
            days = (days - 1) % 365;
        }

        var gd = days + 1;
        var gDays = new[] { 31, (gy % 4 == 0 && (gy % 100 != 0 || gy % 400 == 0)) ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
        var gm = 0;
        while (gm < 12 && gd > gDays[gm])
        {
            gd -= gDays[gm];
            gm++;
        }

        return new DateTime(gy, gm + 1, gd, 0, 0, 0, DateTimeKind.Unspecified);
    }

    public static string PersianDigits(string text)
    {
        var map = new[] { '۰', '۱', '۲', '۳', '۴', '۵', '۶', '۷', '۸', '۹' };
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (chars[i] is >= '0' and <= '9') chars[i] = map[chars[i] - '0'];
        return new string(chars);
    }

    public static string NormalizeDigits(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is >= '۰' and <= '۹') sb.Append((char)('0' + (c - '۰')));
            else if (c is >= '٠' and <= '٩') sb.Append((char)('0' + (c - '٠')));
            else sb.Append(c);
        }

        return sb.ToString();
    }

    public static string MonthName(int month) => month switch
    {
        1 => "فروردین", 2 => "اردیبهشت", 3 => "خرداد", 4 => "تیر", 5 => "مرداد", 6 => "شهریور",
        7 => "مهر", 8 => "آبان", 9 => "آذر", 10 => "دی", 11 => "بهمن", 12 => "اسفند",
        _ => month.ToString(CultureInfo.InvariantCulture)
    };
}
