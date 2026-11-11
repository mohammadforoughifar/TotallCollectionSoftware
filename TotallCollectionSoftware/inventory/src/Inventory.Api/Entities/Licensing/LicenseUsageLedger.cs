namespace Inventory.Api.Data;

/// <summary>
/// دفتر تغییرات مصرف سهمیه — <b>فقط الحاقی</b> (append-only).
///
/// <para>
/// چرا جدا از <see cref="LicenseUsage"/>؟ جدول <c>LicenseUsages</c> فقط «عدد فعلی» را
/// نگه می‌دارد و هیچ ردی از گذشته ندارد. اما در ممیزی باید بتوانیم بگوییم «این ۴۰ مصرف از
/// کجا آمد و کدام‌ها بعداً برگشت خوردند». این جدول همان رد است: هر مصرف (+۱) و هر
/// بازگشت (−۱) یک ردیف می‌گیرد و هیچ ردیفی ویرایش یا حذف نمی‌شود.
/// </para>
///
/// <para>
/// قاعدهٔ «Lifetime = مصرف‌شده، بدون بازگشت» این‌جا اعمال می‌شود: برای دورهٔ Lifetime ردیف
/// بازگشت (Delta = −۱) <b>درج نمی‌شود</b>؛ فقط رخداد حذف در <see cref="LicenseEvent"/> با
/// توضیح «سهمیهٔ دائمی بازگردانده نشد» ثبت می‌شود.
/// </para>
/// </summary>
public class LicenseUsageLedger
{
    public long Id { get; set; }

    /// <summary>ماژول صاحب متریک</summary>
    public string Module { get; set; } = "";

    public string Metric { get; set; } = "";

    /// <summary>کلید دوره (شمسی): <c>1405-08</c> یا <c>1405</c> یا <c>-</c> برای دائمی</summary>
    public string PeriodKey { get; set; } = "-";

    /// <summary>+n برای مصرف، −n برای بازگشت</summary>
    public int Delta { get; set; }

    /// <summary>نوع رکورد مرتبط (مثلاً <c>FacInvoice</c>، <c>IncomingLetter</c>)</summary>
    public string? EntityType { get; set; }

    /// <summary>شناسهٔ رکورد مرتبط</summary>
    public string? EntityId { get; set; }

    /// <summary>علت: <c>create</c>، <c>delete</c>، <c>manual</c></summary>
    public string Reason { get; set; } = "create";

    /// <summary>HMAC ردیف — جلوگیری از دست‌کاری مستقیم در دیتابیس.</summary>
    public string? Hmac { get; set; }

    public DateTime At { get; set; } = DateTime.Now;

    public int? UserId { get; set; }
}
