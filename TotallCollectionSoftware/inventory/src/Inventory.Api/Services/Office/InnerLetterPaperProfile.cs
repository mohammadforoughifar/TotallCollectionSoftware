namespace Inventory.Api.Services.Office;

using Inventory.Api.Services.Office.Outgoing;

/// <summary>
/// اعداد «کاغذ» نامهٔ داخلی به‌ازای هر طرح سربرگ — چیزهایی که در قالب MRT قابل بیان نیستند:
///
/// • <see cref="FooterReserve"/> — چقدر از پایین کاغذ خالی بماند تا متن روی شکل تزئینی پابرگ
///   (نوار تیره/زرد سربرگ ۱۰۰۵، مثلث قرمز شرکت ۱) نیفتد.
/// • <see cref="ContentTop"/> — اگر قالب جای گیرنده/موضوع را اعلام نکند، متن از کجا شروع شود.
///
/// همهٔ چیزهای دیگر (جای شماره/تاریخ/پیوست، گیرنده، موضوع، عرض برچسب) از خودِ قالب خوانده
/// می‌شود (`InnerLetterPdfRenderer` + `OutgoingLetterDesignLayout`). طرح‌های موجود:
///
/// • ۱۰۰۳ و ۳ (هومپا): رزرو ۱۴۲/۱۰۰pt و شروع متن ۲۰۹٫۸/۱۷۶٫۴pt — همان اعداد کالیبره‌شدهٔ
///   <see cref="HomepaLetterheadLayout"/> (رفتار قبلی، بدون تغییر).
/// • ۱۰۰۵ (آریا همیار ژنراتور): سربرگ تصویری با سه نقطه‌چین افقی. اندازه‌گیری روی خودِ هنر:
///   نوار تیره/زرد پایین از y≈۷۶۲pt شروع می‌شود (۸۴۲pt کاغذ) و ناحیهٔ تماس از همان‌جا
///   اشغال است؛ پس مرز محتوا ۷۴۵pt و رزرو ۹۷pt. بالای متن ۱۳۳pt است (زیر خط تماس سربرگ
///   که تا ≈۱۰۹pt می‌آید). A5 همان نسبت (۰٫۷۰۷): مرز ۵۲۷pt، رزرو ۶۲pt، شروع ۹۴pt.
/// </summary>
internal static class InnerLetterPaperProfile
{
    /// <summary>شناسهٔ شرکت «آریا همیار ژنراتور» — هنر سربرگش ردیف افقی نقطه‌چین دارد.</summary>
    private const int AriaArtworkCompanyId = 1005;

    /// <summary>رزرو پابرگ: متن تا این اندازه از پایین کاغذ ننویسد.</summary>
    internal static float FooterReserve(int? companyId, bool a5) => companyId == AriaArtworkCompanyId
        ? (a5 ? 62f : 97f)
        : HomepaLetterheadLayout.DesignFooterReserve(a5);

    /// <summary>شروع متن وقتی قالب کادر گیرنده/موضوع را ندارد.</summary>
    internal static float ContentTop(int? companyId, bool a5) => companyId == AriaArtworkCompanyId
        ? (a5 ? 94f : 133f)
        : HomepaLetterheadLayout.ContentTop(a5);
}
