using LicenseSigner.Data;

namespace LicenseSigner.Services;

/// <summary>کمک‌های نمایشی — همهٔ متن‌ها فارسی و در یک جا، تا هیچ صفحه‌ای متن خام نشان ندهد.</summary>
public static class Ui
{
    public static string E(string? text) =>
        string.IsNullOrEmpty(text) ? "" : System.Net.WebUtility.HtmlEncode(text);

    public static string N(int value) => JalaliDate.PersianDigits(value.ToString("N0"));

    public static string Bytes(long bytes) => bytes switch
    {
        < 1024 => JalaliDate.PersianDigits($"{bytes} B"),
        < 1024 * 1024 => JalaliDate.PersianDigits($"{bytes / 1024.0:0.#} KB"),
        _ => JalaliDate.PersianDigits($"{bytes / (1024.0 * 1024):0.#} MB")
    };

    public static string Clip(string? text, int max = 80) =>
        string.IsNullOrEmpty(text) ? "" : (text.Length <= max ? text : text[..max] + "…");

    public static string Status(RequestStatus status) => status switch
    {
        RequestStatus.New => "جدید",
        RequestStatus.Reviewing => "در بررسی",
        RequestStatus.NeedsInfo => "نیاز به اطلاعات",
        RequestStatus.Approved => "تأییدشده",
        RequestStatus.Issued => "صادرشده",
        RequestStatus.Delivered => "تحویل‌شده",
        RequestStatus.Closed => "بسته",
        RequestStatus.Rejected => "ردشده",
        RequestStatus.Cancelled => "لغوشده",
        RequestStatus.Duplicate => "تکراری",
        _ => status.ToString()
    };

    public static string Badge(RequestStatus status) => status switch
    {
        RequestStatus.New => "new",
        RequestStatus.Reviewing or RequestStatus.NeedsInfo => "review",
        RequestStatus.Approved or RequestStatus.Issued or RequestStatus.Delivered or RequestStatus.Closed => "ok",
        RequestStatus.Rejected or RequestStatus.Cancelled => "err",
        _ => ""
    };

    public static string Type(RequestType type) => type switch
    {
        RequestType.NewActivation => "فعال‌سازی اولیه",
        RequestType.Renewal => "تمدید",
        RequestType.AddModule => "افزودن ماژول",
        RequestType.QuotaPack => "بستهٔ سهمیه",
        RequestType.Rehost => "جابه‌جایی نصب",
        RequestType.Trial => "آزمایشی",
        RequestType.Bridge => "پل",
        _ => "سایر"
    };

    public static string Channel(RequestChannel channel) => channel switch
    {
        RequestChannel.Email => "ایمیل",
        RequestChannel.Paste => "متن چسبانده‌شده",
        RequestChannel.Manual => "ثبت دستی",
        RequestChannel.Phone => "تلفن",
        RequestChannel.InPerson => "حضوری",
        _ => "سایر"
    };

    public static string Role(SupportRole role) => role switch
    {
        SupportRole.Operator => "اپراتور",
        SupportRole.Signer => "امضاکننده",
        _ => "مدیر"
    };

    public static string IdentityMethodText(IdentityMethod method) => method switch
    {
        IdentityMethod.Phone => "تماس تلفنی",
        IdentityMethod.Email => "ایمیل",
        IdentityMethod.Sms => "پیامک",
        IdentityMethod.InPerson => "حضوری",
        _ => "سایر"
    };

    public static string DeliveryChannelText(DeliveryChannel channel) => channel switch
    {
        DeliveryChannel.EmailSmtp => "SMTP",
        DeliveryChannel.EmlDraft => "پیش‌نویس ایمیل",
        DeliveryChannel.Download => "دانلود",
        _ => "دستی"
    };

    public static string LicenseStatus(LicenseSigner.Data.LicenseStatus status) => status switch
    {
        LicenseSigner.Data.LicenseStatus.Current => "جاری",
        LicenseSigner.Data.LicenseStatus.Expired => "منقضی",
        LicenseSigner.Data.LicenseStatus.Superseded => "فوگرفته",
        _ => "یادداشت ابطال"
    };

    public static string LicenseBadge(LicenseSigner.Data.LicenseStatus status) => status switch
    {
        LicenseSigner.Data.LicenseStatus.Current => "ok",
        LicenseSigner.Data.LicenseStatus.Expired => "err",
        LicenseSigner.Data.LicenseStatus.Superseded => "review",
        _ => "err"
    };

    public static string D(DateTime? date, bool withTime = true) =>
        date is null ? "—" : (withTime ? JalaliDate.Full(date) : JalaliDate.Short(date));

    public static string DTehran(DateTime? utc) => JalaliDate.Full(utc);

    public static string Truncate(string? text, int max) => Clip(text, max);
}
