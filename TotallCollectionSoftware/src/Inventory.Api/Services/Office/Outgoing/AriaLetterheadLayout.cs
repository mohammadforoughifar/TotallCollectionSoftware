namespace Inventory.Api.Services.Office.Outgoing;

/// <summary>
/// Company 1005 (Aria Hamyar Generator) outgoing-letter layout, calibrated against its A4 letterhead PDF.
/// Field coordinates are points from the page-header origin, independent of body spacing.
/// A5 coordinates follow the registered letterhead's proportional scale (×0.707).
///
/// اعداد از اندازه‌گیری پیکسلی روی خودِ هنر سربرگ آمده‌اند (همان هنری که در
/// <c>Reports/OutgoingLetters/Companies/1005/Outgoing-A{4,5}.mrt</c> جاسازی شده است):
///   • خط نقطه‌چین یک خط نازک در y≈۹۷٫۶pt (A4) و ۶۹٫۰pt (A5) است.
///   • خط‌چین‌ها تا x = ۶۴٫۳ / ۱۵۶٫۷ / ۲۵۲٫۱ تمام می‌شوند و دونقطهٔ برچسب‌ها روی
///     ۶۶٫۲ / ۱۵۸٫۶ / ۲۵۴٫۰ است (A4).
/// پس مقدار با لبهٔ راستِ ۶۲ / ۱۵۳٫۵ / ۲۴۹٫۵ **روی خط‌چین** و ۲pt سمت چپِ دونقطه می‌نشیند
/// (قبلاً «پیوست» ۶pt داخلِ برچسب می‌رفت و «تاریخ» روی دونقطه می‌افتاد).
/// برای اینکه خط‌چین از میان رقم‌ها رد نشود، پشت هر مقدار یک «پلاک سفید» کشیده می‌شود.
/// </summary>
internal static class AriaLetterheadLayout
{
    internal readonly record struct Field(float Left, float Top, float Width, float Height);

    /// <summary>رنگ مقدارها: مشکی (سربرگ روشن است؛ خاکستریِ قبلی کم‌رنگ دیده می‌شد).</summary>
    internal static string ValueColor => "#000000";

    // Header height controls only where flowing content begins; field rectangles below are absolute.
    internal static float HeaderHeight(bool isA5) => isA5 ? 70f : 95f;

    /// <summary>
    /// Top of the flowing body (receiver row) in points from the top of the paper — the same
    /// number the company template stores for <c>GirandeAsli</c> (۱۶۵٫۶ در A4 و ۱۱۶٫۶ در A5)،
    /// so the printed letter and the Stimulsoft design cannot drift apart.
    /// </summary>
    internal static float ContentTop(bool isA5) => isA5 ? 116.64f : 165.6f;

    /// <summary>
    /// فاصلهٔ بلوک روان از پایین کادر سربرگ: مبدأ صفحه (۲۴/۱۶pt) + ارتفاع سربرگ (۹۵/۷۰pt) و
    /// بعد تا <see cref="ContentTop"/>. قبلاً ۱۲pt بود و گیرنده ۳۵pt بالاتر از طرح چاپ می‌شد.
    /// </summary>
    internal static float BodyTopPadding(bool isA5) => isA5 ? 30.64f : 46.6f;

    /// <summary>
    /// رزرو پابرگ: نوار تیره/زرد پایین کاغذ ۱۰۰۵ از ۷۶۲pt (A4) شروع می‌شود؛ ۹۷pt فاصله
    /// می‌دهد تا متن روی آن ننشیند (A5 همان نسبت: ۶۸pt).
    /// </summary>
    internal static float FooterReserve(bool isA5) => isA5 ? 68f : 97f;

    /// <summary>اندازهٔ قلم مقدارها: ۹pt در A4 (قبلاً ۸) و ۶٫۴pt در A5 (قبلاً ۶).</summary>
    internal static float FieldFontSize(bool isA5) => isA5 ? 6.4f : 9f;

    // Dotted line regions, read from left to right: attachment, number, date.
    // The text is right-aligned inside each dotted region so it does not overlap its caption.
    // Top = (خط نقطه‌چین ۹۷٫۶ − نیم‌ارتفاع) − مبدأ page.Header (۲۴pt در A4 / ۱۶pt در A5).
    internal static Field Date(bool isA5) => isA5
        ? new(142.8f, 48.2f, 33.6f, 9.7f)
        : new(202f, 66.8f, 47.5f, 13.7f);

    internal static Field Number(bool isA5) => isA5
        ? new(77.7f, 48.2f, 30.8f, 9.7f)
        : new(110f, 66.8f, 43.5f, 13.7f);

    internal static Field Attachment(bool isA5) => isA5
        ? new(14.8f, 48.2f, 29f, 9.7f)
        : new(21f, 66.8f, 41f, 13.7f);
}
