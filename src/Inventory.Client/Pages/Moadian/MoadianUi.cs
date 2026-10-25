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
        "8" => "رد شده",
        "9" => "باطل",
        "10" => "اصلاحی",
        "11" => "تأیید سیستمی",
        null or "" => "وضعیت نامشخص",
        _ => $"وضعیت نامعتبر ({code})"
    };

    // ---------- ماتریس «هر دکمه چه‌وقتی» — مطابق گزارش R&D (MOADIAN-STATUS-BUTTONS-RD.md) ----------
    /// <summary>ویرایش: ارسال‌نشده (0) + دارای خطا (5) — بعد از خطای قطعی، رفع خطا و ارسال مجدد.</summary>
    public static bool CanEdit(string? code) => code == "0" || code == "5";
    /// <summary>ارسال: ارسال اولیه (0) + ارسال مجدد پس از رفع خطا (5). وضعیت 1 ممنوع — مرجع دارد و در سامانه است
    /// (ارسال مجدد = ریسک ثبت دوتایی؛ ابتدا «استعلام» زده شود). ShowButtons گیت سمت سرور است.</summary>
    public static bool CanSend(string? code, bool showButtons)
        => showButtons && (code == "0" || code == "5");
    /// <summary>حذف: فقط ردیف ارسال‌نشده (بعد از ارسال، حذف قانوناً ممکن نیست).</summary>
    public static bool CanDelete(string? code, bool showButtons) => showButtons && code == "0";
    public static bool HasReference(string? referenceId) => !string.IsNullOrWhiteSpace(referenceId);
    /// <summary>استعلام: هر وضعیتی که مرجع دارد (پیگیری تا قطعی). استعلام خواندنی است — ShowButtons شرط نیست.</summary>
    public static bool CanInquiry(string? code, string? referenceId)
        => HasReference(referenceId) && code is "1" or "2" or "3" or "4" or "5" or "7" or "8" or "11";

    /// <summary>ثبت دستی واکنش خریدار: فقط وقتی سند در کارپوشه منتظر واکنش است (۲/۳/۴) و مرجع سامانه دارد.
    /// بعد از هر واکنش (۷/۸/۱۱) یا پایان‌خطگاه‌های دیگر، وضعیت پایانی است و این گیت بسته می‌ماند.</summary>
    public static bool CanReactManually(string? code, string? referenceId)
        => HasReference(referenceId) && code is "2" or "3" or "4";
    /// <summary>خطاها: فقط خطای قطعی (5) — فهرست خطا از دادهٔ FAILED استعلام می‌آید.</summary>
    public static bool CanShowErrors(string? code) => code == "5";
    /// <summary>اصلاحی/برگشت: مرجع باید در کارپوشه ثبت‌شده و غیرباطل باشد.
    /// ShowButtons شرط نیست — سرور بلافاصله بعد از ارسال آن را false می‌کند و عملیات سامانه‌ای را می‌بست.
    /// «۱۱ = تأیید سیستمی» از منظر سامانه همان اعتبار «۷» را دارد.</summary>
    public static bool CanReference(string? code, string? referenceId)
        => HasReference(referenceId) && code is "3" or "4" or "7" or "8" or "11";
    /// <summary>ابطال: قاعدهٔ رسمی «هر وضعیتی غیر از باطل‌شده» (ثبت‌شده در سامانه). «۱۱ = تأیید سیستمی» با «۷» یکسان.</summary>
    public static bool CanEbtal(string? code, string? referenceId)
        => HasReference(referenceId) && code is "2" or "3" or "4" or "7" or "8" or "11";

    /// <summary>آدرس رسمی درگاه ملی خدمات مالیاتی (ورودی کارپوشهٔ سامانهٔ مودیان) — نیازمند تأیید از سوی کارفرما.</summary>
    public const string WorkFolderUrl = "https://my.tax.gov.ir";

    /// <summary>کارپوشه برای صورتحساب‌هایی قابل مشاهده است که در سامانه ثبت شده‌اند (در حال بررسی / در انتظار واکنش / تأیید شده).</summary>
    public static bool CanViewWorkFolder(string? code, string? referenceId)
        => HasReference(referenceId) && code is "2" or "4" or "7" or "8";

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
        "8" => "badge-soft-danger",
        "9" => "badge-soft-danger",
        "10" => "badge-soft-warning",
        "11" => "badge-soft-success",
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
        "8" => "mi-s8",
        "9" => "mi-s8",
        "10" => "mi-s4",
        "11" => "mi-s7",
        _ => "mi-s0"
    };

    /// <summary>کد وضعیت → کلاس رنگِ تأکیدی کارت موبایل.</summary>
    public static string StatusAccent(string? code)
        => StatusPillClass(code).Replace("mi-s", "mi-acc");
}
