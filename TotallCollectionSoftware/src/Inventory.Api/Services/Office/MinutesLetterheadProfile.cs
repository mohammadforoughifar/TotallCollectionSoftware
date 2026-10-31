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
/// • شرکت ۱ (فروغ آریا): سربرگ تصویری با نقطه‌چین؛ مرز پابرگ ۶۳۶pt در A4 (شروع مثلث قرمز
///   ۶۴۱٫۵pt) و ۴۴۸٫۲pt در A5، یعنی رزرو ۲۰۶ و ۱۴۷pt. بلوک امضا در هر دو کاغذ سه‌نفره است
///   (A5: ۱۱۱×۳۸pt) تا پنج حاضر در یک صفحه جا شوند.
/// </summary>
internal readonly record struct MinutesLetterheadProfile(
    float FooterReserve,
    float HeaderReserve,
    float MetaColumnLeft,
    int SignatureColumns,
    float SignatureBoxHeight)
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
            SignatureBoxHeight: 44f)
        : new MinutesLetterheadProfile(
            FooterReserve: HomepaLetterheadLayout.DesignFooterReserve(false),
            HeaderReserve: HomepaLetterheadLayout.BandBottom(false) + 24f,
            MetaColumnLeft: LetterPrintTemplate.MmToPoints(LetterPrintTemplate.SideMarginMillimeters),
            SignatureColumns: 3,
            SignatureBoxHeight: 52f);

    /// <summary>طرح شرکت ۱ (فروغ آریا) — اعداد اندازه‌گیری‌شده روی خودِ تصویر سربرگ.</summary>
    private static MinutesLetterheadProfile Aria(bool isA5) => isA5
        ? new MinutesLetterheadProfile(
            FooterReserve: 147f,     // مرز محتوا ۴۴۸٫۲pt از ۵۹۵٫۲۸pt
            HeaderReserve: 85f,      // زیر «پیوست» (۶۳٫۸pt) با فاصلهٔ امن
            MetaColumnLeft: 18f,
            SignatureColumns: 3,
            SignatureBoxHeight: 38f)
        : new MinutesLetterheadProfile(
            FooterReserve: 206f,     // مرز محتوا ۶۳۶pt از ۸۴۱٫۸۹pt (مثلث از ۶۴۱٫۵ شروع می‌شود)
            HeaderReserve: 121f,     // زیر «پیوست» (۹۰٫۵pt) با فاصلهٔ امن
            MetaColumnLeft: 18f,
            SignatureColumns: 3,
            SignatureBoxHeight: 52f);
}
