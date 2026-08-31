using Inventory.Shared;

namespace Inventory.Client.Extensions;

public static class PersianExtensions
{
    // ---------- قالب‌بندی ساعت (ایمن در حالت InvariantGlobalization) ----------
    // در این حالت، فرمت‌های سفارشی TimeSpan مثل ToString("HH:mm") با FormatException خطا می‌دهند؛
    // برای همین از اجزای عددی مستقیم قالب می‌سازیم (24 ساعته).
    /// <summary>HH:MM برای TimeSpan? (24 ساعته)</summary>
    public static string HHMM(this TimeSpan? t) => t.HasValue ? $"{t.Value.Hours:D2}:{t.Value.Minutes:D2}" : "—";
    /// <summary>HH:MM برای TimeSpan (24 ساعته)</summary>
    public static string HHMM(this TimeSpan t) => $"{t.Hours:D2}:{t.Minutes:D2}";
    /// <summary>HH:MM برای DateTime? (24 ساعته)</summary>
    public static string HHMM(this DateTime? dt) => dt.HasValue ? $"{dt.Value.Hour:D2}:{dt.Value.Minute:D2}" : "—";
    /// <summary>HH:MM برای DateTime (24 ساعته)</summary>
    public static string HHMM(this DateTime dt) => $"{dt.Hour:D2}:{dt.Minute:D2}";

    /// <summary>تاریخ شمسی کوتاه: 1403/05/12</summary>
    public static string ToFa(this DateTime dt) => PersianDate.ToShort(dt);

    /// <summary>تاریخ شمسی بلند: ۱۲ مرداد ۱۴۰۳</summary>
    public static string ToFaLong(this DateTime dt) => PersianDate.ToLong(dt);

    /// <summary>تاریخ و ساعت شمسی</summary>
    public static string ToFaDateTime(this DateTime dt) => PersianDate.ToShortDateTime(dt);

    /// <summary>مبلغ با ارقام فارسی و جداکننده هزارگان</summary>
    public static string Money(this decimal v) => Fa.Money(v);

    /// <summary>عدد صحیح با ارقام فارسی</summary>
    public static string Num(this decimal v) => Fa.Number(v);

    public static string Num(this int v) => Fa.Number(v);

    // ================= ماژول مدیریت پروژه‌ها (چاپ/گرید/ارقام فارسی) =================

    /// <summary>ارقام لاتین متن به فارسی — بدون جداکننده هزارگان (برای کد پروژه، سریال و…)</summary>
    public static string FaDigits(this string? s) => Fa.Digits(s);

    /// <summary>عدد صحیح با ارقام فارسی — بدون جداکننده هزارگان (برای شمارنده‌ها و ردیف جدول)</summary>
    public static string FaDigits(this int n) => Fa.Digits(n);

    /// <summary>عدد بلند با ارقام فارسی — بدون جداکننده هزارگان</summary>
    public static string FaDigits(this long n) => Fa.Digits(n);

    /// <summary>تاریخ شمسی کوتاه با ارقام فارسی: ۱۴۰۳/۰۵/۱۲</summary>
    public static string ToFaDate(this DateTime dt) => PersianDate.ToShortFa(dt);

    /// <summary>تاریخ شمسی کوتاه با ارقام فارسی — «—» در صورت خالی بودن</summary>
    public static string ToFaDate(this DateTime? dt) => dt.HasValue ? PersianDate.ToShortFa(dt.Value) : "—";

    /// <summary>ساعت ۲۴ساعته با ارقام فارسی: ۰۸:۳۰</summary>
    public static string TimeFa(this TimeOnly t) => Fa.Digits($"{t.Hour:D2}:{t.Minute:D2}");

    /// <summary>ساعت ۲۴ساعته با ارقام فارسی — «—» در صورت خالی بودن</summary>
    public static string TimeFa(this TimeOnly? t) => t.HasValue ? t.Value.TimeFa() : "—";

    /// <summary>
    /// مدت زمان به‌صورت «ساعت:دقیقه» با ارقام فارسی — برخلاف HHMM، ساعت‌های بیش از ۲۴
    /// را هم درست نشان می‌دهد (مثلاً ۱۲۵:۳۰ برای جمع ساعات یک پروژه).
    /// </summary>
    public static string HoursFa(this TimeSpan t)
    {
        var neg = t < TimeSpan.Zero;
        if (neg) t = t.Negate();
        var hours = (int)t.TotalHours;
        return (neg ? "-" : "") + Fa.Digits($"{hours:D2}:{t.Minutes:D2}");
    }

    /// <summary>مدت زمان با ارقام فارسی — «—» در صورت خالی بودن</summary>
    public static string HoursFa(this TimeSpan? t) => t.HasValue ? t.Value.HoursFa() : "—";
}
