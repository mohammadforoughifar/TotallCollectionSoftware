namespace Inventory.Api.Services.Office.Outgoing;

/// <summary>
/// Homepa value coordinates relative to QuestPDF's page-header origin.
/// The Homepa artwork is used by company 1003 in the main database and by company 3 in the
/// sample one (Companies/3 and Companies/1003 embed the same image); both ids therefore
/// share this layout — see Reporting:OutgoingLetters:HomepaCompanyIds.
/// Physical artwork coordinates come from the MRT: page margin + ClientRectangle, in points.
/// Header content begins after Page.MarginTop (24pt on A4 / 16pt on A5), so subtract that
/// origin to keep values on the matching raster-label row rather than one row too low.
/// Date/attachment retain their calibrated 4pt (A4) / 3pt (A5) left shift.
/// Keep each field independent; text height/wrapping must not move the following fields.
/// </summary>
internal static class HomepaLetterheadLayout
{
    internal readonly record struct Field(float Left, float Top, float Width, float Height);

    // Header field placement and body start spacing are deliberately independent.
    // These fixed header bounds keep values on company 1003's printed labels;
    // only the body content offset may change without moving those values.
    internal static float HeaderHeight(bool isA5) => isA5 ? 150f : 160f;
    // The art ends at about 232pt on A4 and scales to about 163pt on A5.
    // Keep roughly 32pt (11mm) clear below it in both formats.
    internal static float ReceiverTopPadding(bool isA5) => isA5 ? 28f : 80f;
    internal static float SubjectBodyGap(bool isA5) => isA5 ? 8f : 12f;
    internal static float FooterReserve(bool isA5) => isA5 ? 68f : 90f;

    /// <summary>
    /// رزرو پابرگ برای چیدمان «از دل طرح» (فعلاً فقط شرکت ۱۰۰۳). اندازه‌گیری روی خودِ تصویر
    /// سربرگ: روبان‌های روشنِ گوشهٔ پایین-چپ از ≈۶۸۹pt و پله‌های تیره از ≈۷۳۶pt شروع می‌شوند؛
    /// با ۱۴۲ رزرو، متن و بلوک امضا تا ۷۰۰pt می‌نویسند و ۳۶pt از شکل تزئینی فاصله دارند
    /// (رزرو قبلی ۹۰/۶۸ اجازه می‌داد نام امضاکنندگان روی همان شکل بیفتد).
    /// </summary>
    internal static float DesignFooterReserve(bool isA5) => isA5 ? 100f : 142f;
    internal static float HeaderValueFontSize(bool isA5) => isA5 ? 8.5f : 10.5f;

    /// <summary>
    /// پایین گرافیک سربرگ (قوس آبی): ≈۲۳۲pt در A4 و ≈۱۶۳pt در A5. متن و کپشن‌ها باید از
    /// پایین همین نقطه شروع شوند؛ مختصات قدیمی طرح (y≈۱۷۴) روی قوس می‌افتاد.
    /// </summary>
    internal static float HeaderArtBottom(bool isA5) => isA5 ? 163f : 232f;

    /// <summary>
    /// پایین «نوار آبی» سربرگ، اندازه‌گیری‌شده روی خودِ تصویر: ۱۱۲٫۸pt در A4 و ۷۹٫۴pt در A5.
    /// (مقدار ۲۳۲/۱۶۳ برای «پایین کل گرافیک» است، نه پایین نوار؛ متن نباید از آن‌جا شروع شود.)
    /// </summary>
    internal static float BandBottom(bool isA5) => isA5 ? 79.4f : 112.8f;

    /// <summary>
    /// شروع بلوک اطلاعات (گیرنده/موضوع/متن). کارفرما خواست متن کمی پایین‌تر از نوار آبی باشد؛
    /// درخواست نهایی: **۹۷pt در هر دو کاغذ** (A4 و A5) از پایین نوار آبی.
    /// </summary>
    internal static float ContentTop(bool isA5) => BandBottom(isA5) + ContentStartGap;

    /// <summary>فاصلهٔ شروع متن از پایین نوار آبی — یکی برای A4 و A5 (۹۷pt).</summary>
    internal const float ContentStartGap = 97f;

    // Top دقیقاً برابر «MRT منهای مبدأ page.Header» است تا هم چاپ نامهٔ داخلی و هم نامهٔ صادره
    // روی خط برچسبِ چاپ‌شده بنشینند (مرکز برچسب‌ها روی تصویر: ۶۶٫۵ / ۸۴٫۵ / ۱۰۳٫۲pt در A4).
    internal static Field Number(bool isA5) => isA5
        ? new(7.2f, 24.5216f, 59.04f, 12.96f)
        : new(14.4f, 35.2776f, 82.08f, 14.4f);

    internal static Field Date(bool isA5) => isA5
        ? new(4.2f, 37.2512f, 59.04f, 12.96f)
        : new(10.4f, 53.2776f, 82.08f, 14.4f);

    internal static Field Attachment(bool isA5) => isA5
        ? new(4.2f, 50.4848f, 59.04f, 12.96f)
        : new(10.4f, 71.9976f, 82.08f, 14.4f);

    // ── مختصات **مطلق صفحه** (از گوشهٔ بالا-چپ کاغذ) ──
    // همان کادرهای شماره/تاریخ/پیوستِ قالب ۱۰۰۳: (حاشیهٔ صفحه + ClientRectangle) در point.
    // نامهٔ داخلی این مقدارها را در لایهٔ Foreground و با همین مختصات مطلق چاپ می‌کند؛ پس
    // مبدأ page.Header (۲۴pt در A4 و ۱۶pt در A5) از آن‌ها کسر نمی‌شود — کسر شدنش همان
    // ایرادی بود که مقادیر را یک ردیف بالاتر از برچسب‌ها می‌نشاند.
    // مرکز برچسب‌های چاپ‌شده روی تصویر: ۶۶٫۵ / ۸۴٫۵ / ۱۰۳٫۲pt در A4.
    internal static Field AbsoluteNumber(bool isA5) => isA5
        ? new(7.2f, 40.5216f, 59.04f, 12.96f)
        : new(14.4f, 59.2776f, 82.08f, 14.4f);

    internal static Field AbsoluteDate(bool isA5) => isA5
        ? new(7.2f, 53.2512f, 59.04f, 12.96f)
        : new(14.4f, 77.2776f, 82.08f, 14.4f);

    internal static Field AbsoluteAttachment(bool isA5) => isA5
        ? new(7.2f, 66.4848f, 59.04f, 12.96f)
        : new(14.4f, 95.9976f, 82.08f, 14.4f);
}
