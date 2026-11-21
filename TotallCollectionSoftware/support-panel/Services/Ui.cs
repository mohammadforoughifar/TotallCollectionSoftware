using SupportPanel.Data;

namespace SupportPanel.Services;

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

    public static string Role(SupportRole role) => role switch
    {
        SupportRole.Operator => "اپراتور",
        SupportRole.Signer => "پشتیبان ارشد",
        _ => "مدیر"
    };

    public static string D(DateTime? date, bool withTime = true) =>
        date is null ? "—" : (withTime ? JalaliDate.Full(date) : JalaliDate.Short(date));

    public static string DTehran(DateTime? utc) => JalaliDate.Full(utc);

    public static string Truncate(string? text, int max) => Clip(text, max);
}
