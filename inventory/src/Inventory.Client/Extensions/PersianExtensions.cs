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
}
