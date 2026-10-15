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
        null or "" => "وضعیت نامشخص",
        _ => $"وضعیت نامعتبر ({code})"
    };

    /// <summary>شرط‌های صرفاً نمایشی عملیات ردیف؛ هیچ فراخوانی یا قاعدهٔ ارسال در این helper نیست.</summary>
    public static bool CanEdit(string? code) => code == "0";
    public static bool CanSend(string? code, bool showButtons) => showButtons && code == "0";
    public static bool CanDelete(string? code, bool showButtons) => showButtons && code == "0";
    public static bool HasReference(string? referenceId) => !string.IsNullOrWhiteSpace(referenceId);
    public static bool CanUseReferenceActions(string? referenceId, bool showButtons)
        => HasReference(referenceId) && showButtons;

    /// <summary>آدرس رسمی درگاه ملی خدمات مالیاتی (ورودی کارپوشهٔ سامانهٔ مودیان) — نیازمند تأیید از سوی کارفرما.</summary>
    public const string WorkFolderUrl = "https://my.tax.gov.ir";

    /// <summary>کارپوشه برای صورتحساب‌هایی قابل مشاهده است که در سامانه ثبت شده‌اند (در حال بررسی / در انتظار واکنش / تأیید شده).</summary>
    public static bool CanViewWorkFolder(string? code, string? referenceId)
        => HasReference(referenceId) && code is "2" or "4" or "7";

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

    /// <summary>موضوع صورتحساب → کلاس برچسب موضوع (نسخهٔ متمرکز سه‌تایی قبل).</summary>
    public static string SubjectBadge(string? subject) => subject switch
    {
        "اصلی" => "badge-soft-primary",
        "اصلاحی" => "badge-soft-warning",
        _ => "badge-soft-danger"
    };

    /// <summary>کد وضعیت → کلاس رنگی پیل وضعیت (صفحه‌های لیست و جزئیات).</summary>
    public static string StatusPillClass(string? code) => code switch
    {
        "0" => "mi-s0",
        "1" => "mi-s1",
        "2" => "mi-s2",
        "3" => "mi-s3",
        "4" => "mi-s4",
        "5" => "mi-s5",
        "6" => "mi-s6",
        "7" => "mi-s7",
        _ => "mi-s0"
    };

    /// <summary>کد وضعیت → کلاس رنگِ تأکیدی کارت موبایل.</summary>
    public static string StatusAccent(string? code)
        => StatusPillClass(code).Replace("mi-s", "mi-acc");
}
