namespace Inventory.Client.Pages.Moadian;

/// <summary>ابزارهای نمایش مشترک بخش مودیان (تاریخ شمسی، اعداد فارسی، برچسب وضعیت).</summary>
public static class MoadianUi
{
    private static readonly System.Globalization.PersianCalendar Pc = new();

    public static string Shamsi(DateTime d)
        => $"{Pc.GetYear(d)}/{Pc.GetMonth(d):00}/{Pc.GetDayOfMonth(d):00}";

    public static string Shamsi(DateOnly? d)
        => d is null ? "—" : Shamsi(d.Value.ToDateTime(TimeOnly.MinValue));

    public static string Money(decimal? v)
        => v is null ? "—" : Inventory.Shared.Fa.Money(v.Value);

    public static string Num(int v) => Inventory.Shared.Fa.Number(v);

    public static string Num(decimal? v)
        => v is null ? "—" : Inventory.Shared.Fa.Number(v.Value);

    /// <summary>کد وضعیت → برچسب فارسی (مطابق تعریف سرویس مودیان).</summary>
    public static string StatusLabel(string? code) => code switch
    {
        "0" => "ارسال نشده",
        "1" => "ارسال شده در انتظار استعلام",
        "2" => "در انتظار بررسی کارپوشه",
        "3" => "ارسال شده",
        "4" => "ارسال شده در انتظار واکنش",
        "5" => "دارای خطا",
        "6" => "بدون نیاز به واکنش",
        "7" => "تأیید شده",
        null or "" => "—",
        _ => code
    };

    /// <summary>کد وضعیت → کلاس رنگی برچسب.</summary>
    public static string StatusBadge(string? code) => code switch
    {
        "0" => "badge-soft-muted",
        "1" => "badge-soft-primary",
        "2" => "badge-soft-warning",
        "3" => "badge-soft-primary",
        "4" => "badge-soft-warning",
        "5" => "badge-soft-danger",
        "6" => "badge-soft-muted",
        "7" => "badge-soft-success",
        _ => "badge-soft-muted"
    };
}
