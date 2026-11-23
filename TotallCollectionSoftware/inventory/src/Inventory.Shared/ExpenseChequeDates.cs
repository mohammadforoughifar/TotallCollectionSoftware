namespace Inventory.Shared;

/// <summary>
/// قاعدهٔ تاریخ‌های چک هزینه (چک صادره / چک دریافتی خرج‌شده).
/// سررسید چک صادره کوتاه‌ترین مهلتِ پرداخت است؛ اگر پیش از تحویل چک به دریافت‌کننده باشد
/// یعنی چکی با مهلت گذشته تحویل داده می‌شود و ثبت آن باید با هشدار رد شود.
/// این کلاس در سرور (اعتبارسنجی نهایی) و کلاینت (هشدار فوری فرم) مشترک است.
/// </summary>
public static class ExpenseChequeDates
{
    /// <summary>پیام هشدار مشترک؛ متن آن در فرم و در سرور یکسان است تا کاربر پیام متناقض نبیند.</summary>
    public const string DueBeforeHandoverMessage =
        "تاریخ سررسید چک صادره نمی‌تواند پیش از تاریخ تحویل چک باشد.";

    /// <summary>سررسید پیش از تحویل است؟ (فقط بخش تاریخ مقایسه می‌شود، ساعت بی‌اثر است)</summary>
    public static bool DueIsBeforeHandover(DateTime? dueDate, DateTime? handedAt)
        => dueDate.HasValue && handedAt.HasValue && dueDate.Value.Date < handedAt.Value.Date;

    /// <summary>اعتبار تاریخ‌ها برای چک صادره؛ null یعنی معتبر.</summary>
    public static string? ValidateIssued(DateTime? dueDate, DateTime? handedAt)
        => DueIsBeforeHandover(dueDate, handedAt) ? DueBeforeHandoverMessage : null;

    /// <summary>کمینهٔ مجاز انتخاب سررسید در تقویم: روز تحویل چک.</summary>
    public static DateTime? MinDueDate(DateTime? handedAt) => handedAt?.Date;
}
