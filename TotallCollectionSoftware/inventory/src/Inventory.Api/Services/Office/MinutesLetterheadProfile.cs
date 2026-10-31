namespace Inventory.Api.Services.Office;

using Inventory.Api.Services.Office.Outgoing;

/// <summary>
/// اعداد «کاغذ» برای چاپ صورتجلسه، به‌ازای هر طرح سربرگ.
///
/// هندسهٔ خودِ متن (جای عنوان، شماره، تاریخ، پیوست، حاضرین، غایبین و بندها) از فایل قالب
/// شرکت خوانده می‌شود (<c>MeetingMinutesDesignLayout</c> + <c>Minutes-{A4|A5}.mrt</c>).
/// آنچه در قالب قابل بیان نیست، این‌جاست:
/// • <see cref="FooterReserve"/> — چقدر از پایین کاغذ باید خالی بماند تا متن روی شکل تزئینی
///   پابرگ (مثلث/روبانی که در تصویر سربرگ چاپ شده) نیفتد.
/// • <see cref="HeaderReserve"/> — صفحهٔ دوم به بعد از کجا شروع شود تا زیر نوار/تصویر سربرگ
///   و زیر «شماره/تاریخ/پیوست»ِ تکرارشده نرود.
/// • <see cref="MetaColumnLeft"/> — لبهٔ چپِ ستون مقدارهای سربرگ (مقادیر راست‌چین‌اند).
/// • <see cref="SignatureColumns"/> و <see cref="SignatureBoxHeight"/> — چیدمان کادرهای امضا.
///
/// طرح‌های موجود:
/// • شرکت ۱۰۰۳ و ۳ (هومپا): مقدارهای سربرگ با متن سفید داخل کادرهای آبی؛ بلوک امضا
///   A4 سه‌نفره ۵۲pt و A5 دونفره ۴۴pt — همان مقادیر کالیبره‌شدهٔ <see cref="HomepaLetterheadLayout"/>.
/// • شرکت ۱ (فروغ آریا): سربرگ تصویری با نقطه‌چین؛ مرز محتوا ۶۶۸pt در A4 و ۴۸۰٫۳pt در A5
///   (رزرو ۱۷۴ و ۱۱۵pt). اندازه‌گیری روی خودِ تصویر: مثلث قرمز از y=۶۴۱٫۵ شروع می‌شود ولی
///   شیبش طوری است که تا y=۶۸۲ به ستون متن (x<۵۵۸) نمی‌رسد؛ در ۶۶۸ فاصلهٔ ۱۴pt با مثلث
///   می‌ماند. مرز قبلی (۶۳۶) بیش از حد محافظه‌کارانه بود و امضا را با ۷pt کم‌آوردن به صفحهٔ
///   دوم می‌انداخت. بلوک امضا تا پنج کادر در یک ردیف و در حالت چندردیفی جمع‌وجورتر می‌شود تا
///   همیشه بلافاصله پس از آخرین بند بنشیند، نه در صفحهٔ بعد.
/// </summary>
internal readonly record struct MinutesLetterheadProfile(
    float FooterReserve,
    float HeaderReserve,
    float MetaColumnLeft,
    // حداکثر کادر امضا در یک ردیف؛ برای تعداد کمتر، ردیف با همان تعداد پر می‌شود.
    int SignatureColumns,
    float SignatureBoxHeight,
    // عرض ثابت کادر (۰ = تقسیم عرض متن بین ستون‌ها).
    float SignatureCellWidth = 0f,
    // ارتفاع کادر وقتی بلوک چند ردیفی می‌شود (۰ = فشرده‌سازی نکن)؛ برای اینکه بلوک
    // «بلافاصله بعد از پایان متن» جا شود، نه در صفحهٔ بعد.
    float SignatureCompactBoxHeight = 0f,
    // فاصلهٔ بلوک امضا از آخرین بند.
    float SignatureTopPadding = 14f)
{
    /// <summary>شناسهٔ شرکت «فروغ آریا» (همان شرکتی که درخواست این طرح از او بود).</summary>
    private const int AriaCompanyId = 1;

    /// <summary>
    /// پروفایل چاپ صورتجلسهٔ همان شرکت. شرکت‌های بدون ردیف، چیدمان کالیبره‌شدهٔ هومپا
    /// (۱۰۰۳/۳) را می‌گیرند — رفتار قبلی هیچ شرکتی عوض نمی‌شود.
    /// </summary>
    internal static MinutesLetterheadProfile For(int? companyId, bool isA5)
        => companyId == AriaCompanyId ? Aria(isA5) : Homepa(isA5);

    /// <summary>طرح ۱۰۰۳/۳ — دقیقاً همان اعدادی که پیش از این در سرویس چاپ بود.</summary>
    private static MinutesLetterheadProfile Homepa(bool isA5) => isA5
        ? new MinutesLetterheadProfile(
            FooterReserve: HomepaLetterheadLayout.DesignFooterReserve(true),
            HeaderReserve: HomepaLetterheadLayout.BandBottom(true) + 24f,
            MetaColumnLeft: LetterPrintTemplate.MmToPoints(LetterPrintTemplate.SideMarginMillimeters),
            SignatureColumns: 2,
            SignatureBoxHeight: 44f,
            SignatureCellWidth: 0f,
            SignatureCompactBoxHeight: 0f,
            SignatureTopPadding: 10f)
        : new MinutesLetterheadProfile(
            FooterReserve: HomepaLetterheadLayout.DesignFooterReserve(false),
            HeaderReserve: HomepaLetterheadLayout.BandBottom(false) + 24f,
            MetaColumnLeft: LetterPrintTemplate.MmToPoints(LetterPrintTemplate.SideMarginMillimeters),
            SignatureColumns: 3,
            SignatureBoxHeight: 52f,
            SignatureCellWidth: 168f,
            SignatureCompactBoxHeight: 0f,
            SignatureTopPadding: 14f);

    /// <summary>طرح شرکت ۱ (فروغ آریا) — اعداد اندازه‌گیری‌شده روی خودِ تصویر سربرگ.</summary>
    private static MinutesLetterheadProfile Aria(bool isA5) => isA5
        ? new MinutesLetterheadProfile(
            FooterReserve: 115f,     // مرز محتوا ۴۸۰٫۳pt؛ مثلث تا y≈۴۹۹ به ستون متن نمی‌رسد
            HeaderReserve: 85f,      // زیر «پیوست» (۶۳٫۸pt) با فاصلهٔ امن
            MetaColumnLeft: 18f,
            SignatureColumns: 5,     // تا پنج حاضر در یک ردیف
            SignatureBoxHeight: 38f,
            SignatureCompactBoxHeight: 29f,
            SignatureTopPadding: 5f)
        : new MinutesLetterheadProfile(
            FooterReserve: 174f,     // مرز محتوا ۶۶۸pt؛ مثلث از y≈۶۸۲ به ستون متن می‌رسد
            HeaderReserve: 121f,     // زیر «پیوست» (۹۰٫۵pt) با فاصلهٔ امن
            MetaColumnLeft: 18f,
            SignatureColumns: 5,     // تا پنج حاضر در یک ردیف
            SignatureBoxHeight: 46f,
            SignatureCompactBoxHeight: 34f,
            SignatureTopPadding: 6f);
}
