using System.Globalization;
using Inventory.Api.Services.Office.Outgoing;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Inventory.Api.Services.Office;

internal sealed record InnerLetterHameshRow(string Receiver, string Instruction, string Action);
internal sealed record InnerLetterPrintData(string Number, DateTime Date, string Title, string Body,
    string Receivers, string Creator, IReadOnlyList<InnerLetterHameshRow> Hamesh, string ReferralDates = "");

/// <summary>Pure PDF composition: no database, signatures, closing phrase, stamp or page-number footer.</summary>
internal static class InnerLetterPdfRenderer
{
    private static readonly object FontLock = new();
    private static bool _fontsRegistered;

    /// <param name="meta">
    /// هندسهٔ خوانده‌شده از خودِ قالب MRT نامهٔ داخلی (همان کادرهای شماره/تاریخ/پیوست قالب
    /// صادرهٔ ۱۰۰۳). اگر قالبِ نصب‌شده روی سرور قدیمی باشد و این کادرها را نداشته باشد، از
    /// مختصات مطلق کالیبرهٔ <see cref="HomepaLetterheadLayout"/> استفاده می‌شود.
    /// </param>
    /// <param name="companyId">
    /// شرکت صادرکننده؛ فقط برای انتخاب «اعداد کاغذ» همان طرح (رزرو پابرگ و شروع متن) لازم است.
    /// </param>
    internal static byte[] Render(InnerLetterPrintData data, bool a5, bool withHamesh,
        byte[]? homepaBackground, bool attachment, string fontDirectory,
        OutgoingLetterDesignLayout? meta = null, int? companyId = null)
    {
        EnsureFonts(fontDirectory);
        QuestPDF.Settings.License = LicenseType.Community;
        var homepa = homepaBackground is { Length: > 0 };
        // اندازهٔ قلم مثل نامهٔ صادرهٔ ۱۰۰۳: ۱۱pt در A4 و ۹pt در A5 (یکدستی دو چاپ).
        var fontSize = a5 ? 9f : 11f;
        var pc = new PersianCalendar();
        var date = LetterPrintTemplate.PersianDigits($"{pc.GetYear(data.Date):0000}/{pc.GetMonth(data.Date):00}/{pc.GetDayOfMonth(data.Date):00}");
        var number = LetterPrintTemplate.PersianDigits(data.Number);
        var body = PlainText(data.Body);
        var attachmentText = attachment ? "دارد" : "ندارد";

        // مقادیر سربرگ: از قالب داخلی (اگر کادرها را داشته باشد) یا مختصات کالیبرهٔ ۱۰۰۳.
        // ۱۰۰۵ سه مقدار را در یک ردیف افقی اعلام می‌کند؛ HeaderMetaPainter هر دو شکل را می‌فهمد.
        var metaRows = homepa ? BuildMetaRows(meta, number, date, attachmentText, a5) : new List<HeaderMetaCell>();
        // هندسهٔ بلوک اطلاعات (گیرنده/موضوع) از خودِ قالب MRT خوانده می‌شود؛ جابه‌جا کردن کادرها
        // در Stimulsoft Designer مستقیماً روی چاپ اثر می‌گذارد. اگر قالب این کادرها را نداشته
        // باشد، به مختصات کالیبرهٔ نامهٔ صادرهٔ ۱۰۰۳ (پایین نوار آبی + ۹۷pt) برمی‌گردد.
        var designTop = new[] { meta?.Receiver?.Top ?? 0f, meta?.Title?.Top ?? 0f }
            .Where(top => top > 0f).DefaultIfEmpty(0f).Min();
        // عرض ستون برچسب و فاصلهٔ برچسب↔مقدار هم از قالب: کادر «موضوع:» و کادر مقدارش.
        var captionBox = meta?.HeaderBoxes
            .Where(box => box.Slot == OutgoingLetterDesignLayout.Slot.Caption)
            .OrderBy(box => box.Top).FirstOrDefault();
        var titleBox = meta?.Title;
        var labelWidth = captionBox is { Width: > 12f } ? captionBox.Width : (float?)null;
        var labelGap = captionBox is not null && titleBox is not null
            ? captionBox.Left - (titleBox.Left + titleBox.Width)
            : 0f;
        if (captionBox is null || titleBox is null || labelGap is <= 0f or > 24f) labelGap = InfoLabelGap;
        // شروع متن: از قالب MRT، وگرنه اعداد همان طرح (۱۰۰۳: پایین «نوار آبی» + ۹۷pt،
        // ۱۰۰۵: ۱۳۳pt زیر خط تماس سربرگ).
        var contentTop = designTop > 0f ? designTop : InnerLetterPaperProfile.ContentTop(companyId, a5);
        if (metaRows.Count > 0)
            contentTop = Math.Max(contentTop, metaRows.Max(row => row.Top + row.Height) + 6f);
        var sideMargin = LetterPrintTemplate.MmToPoints(LetterPrintTemplate.SideMarginMillimeters);

        return Document.Create(root => root.Page(page =>
        {
            page.Size(a5 ? PageSizes.A5 : PageSizes.A4);
            // Non-Homepa backgrounds are overlaid by PDFsharp after rendering.
            page.PageColor(Colors.Transparent);
            // جهت چیدمان مثل نامهٔ صادرهٔ ۱۰۰۳: چپ‌به‌راست. با راست‌به‌چپ، QuestPDF ترتیب
            // فرزندانِ Row را آینه می‌کرد؛ نتیجه‌اش همان ایراد گزارش‌شده بود: برچسب‌های
            // «موضوع/ایجادکننده/تاریخ ارجاع» به لبهٔ چپ می‌رفتند و مقدارها به لبهٔ راست.
            // متن نامه همچنان راست‌چین و راست‌به‌چپ است (DirectionFromRightToLeft روی خودِ متن).
            page.ContentFromLeftToRight();
            page.DefaultTextStyle(x => x.FontFamily("Vazirmatn").FontSize(fontSize)
                .LineHeight(LetterPrintTemplate.BodyLineHeight).DirectionFromRightToLeft());
            if (homepa)
            {
                // سربرگ ۱۰۰۳: مختصات مطلق صفحه، بدون حاشیهٔ بالا، مثل مسیر «از دل طرح» نامهٔ صادره.
                page.MarginTop(0);
                page.MarginHorizontal(sideMargin);
                // رزرو پابرگ هر شرکت جداگانه است؛ ۱۰۰۵ نوار تیره/زرد پایین کاغذ دارد.
                page.MarginBottom(InnerLetterPaperProfile.FooterReserve(companyId, a5));
                // QuestPDF's page background and header are repeated on EVERY page, including overflow.
                // UseOriginalImage: تصویر در InnerLetterheadTemplates یک بار آماده شده است؛
                // اگر اینجا هم تغییر اندازه/کدگذاری شود، QuestPDF در هر چاپ یک نسخهٔ
                // ۲۳۸۱×۳۳۶۷ را از نو می‌سازد (چند صد میلی‌ثانیه CPU و ده‌ها مگابایت حافظه).
                page.Background().Image(homepaBackground!).UseOriginalImage().FitUnproportionally();
                // ناحیهٔ سربرگ فقط تا شروع متن رزرو می‌شود (نه یک ارتفاع ثابت ۱۶۰pt)؛ بدنه
                // دقیقاً از پایین نوار آبی + ۹۷pt شروع می‌شود.
                page.Header().Height(contentTop);
                // شماره/تاریخ/پیوست در لایهٔ سراسری صفحه و با مختصات مطلق، روی خط برچسب‌های
                // چاپ‌شدهٔ سربرگ («موضوع:»/«تاریخ:»/«پیوست:») — روی همهٔ صفحه‌ها.
                if (metaRows.Count > 0)
                    page.Foreground().ContentFromLeftToRight()
                        // حاشیهٔ امن ۱۵mm همان قاعدهٔ قبلی نامهٔ داخلی است؛ برای سربرگ ردیفی
                        // (۱۰۰۵) مختصات مطلقِ خود کادرها ملاک است و این کف اثری ندارد.
                        .Element(host => HeaderMetaPainter.Draw(host, metaRows,
                            LetterPrintTemplate.MmToPoints(LetterPrintTemplate.SideMarginMillimeters)));
            }
            else
            {
                page.MarginTop(a5 ? 90 : 130);
                page.MarginHorizontal(a5 ? 30 : 46);
                page.MarginBottom(a5 ? 42 : 60);
                page.Header().AlignLeft().Column(c =>
                {
                    c.Item().Text($"شماره نامه: {number}");
                    c.Item().Text($"تاریخ ثبت: {date}");
                    c.Item().Text($"پیوست: {attachmentText}");
                });
            }

            var content = page.Content();
            if (!homepa) content = content.PaddingTop(a5 ? 12 : 16).PaddingHorizontal(a5 ? 32 : 46);
            content.Column(c =>
            {
                if (homepa) AddHomepaBody(c, data, body, withHamesh, a5, labelWidth, labelGap);
                else AddPlainBody(c, data, body, withHamesh, a5);
            });
        })).GeneratePdf();
    }

    /// <summary>
    /// بلوک اطلاعات و متن، دقیقاً با همان چیدمان نامهٔ صادرهٔ ۱۰۰۳ و **بدون بلوک امضا**
    /// (نامهٔ داخلی است): خط اول گیرندگان بدون برچسب، خط دوم «موضوع:» به‌همراه عنوان،
    /// سپس متن نامه و در پایان هامش.
    /// </summary>
    /// <param name="labelWidth">عرض ستون برچسب از قالب MRT (null = تخمین از طول برچسب).</param>
    /// <param name="labelGap">فاصلهٔ برچسب↔مقدار از قالب MRT.</param>
    private static void AddHomepaBody(ColumnDescriptor c, InnerLetterPrintData data, string body,
        bool withHamesh, bool a5, float? labelWidth, float labelGap)
    {
        var infoFontSize = a5 ? 9f : 11f;
        // ترتیب دقیقاً مثل نامهٔ صادرهٔ ۱۰۰۳ (درخواست کارفرما):
        //   خط اول  → گیرنده، بدون برچسب و تمام‌عرض
        //   خط دوم  → «موضوع:» و روبه‌رویش عنوان نامه
        //   سپس     → متن نامه و در پایان «هامش» (اگر خواسته شده باشد)
        // ردیف‌های «ایجادکننده» و «تاریخ ارجاع» در نامهٔ داخلی چاپ نمی‌شوند (درخواست کارفرما).
        var rows = new List<LetterPrintTemplate.InfoRow>();
        if (!string.IsNullOrWhiteSpace(data.Receivers))
            rows.Add(new LetterPrintTemplate.InfoRow("گیرندگان", data.Receivers, HideLabel: true));
        rows.Add(new LetterPrintTemplate.InfoRow("موضوع",
            string.IsNullOrWhiteSpace(data.Title) ? "—" : data.Title, BoldValue: true));

        LetterPrintTemplate.InfoBlock(c, rows, infoFontSize,
            Math.Max(LetterPrintTemplate.ParagraphSpacing, infoFontSize * 0.45f), labelGap, labelWidth);
        // خط جداکنندهٔ خاکستری، مثل نامهٔ صادرهٔ ۱۰۰۳، برداشته شده؛ فقط همان فاصله می‌ماند.
        LetterPrintTemplate.Spacer(c, a5 ? 10f : 12f);
        // Never ShowEntire/Height/ClampLines the body: it must flow to subsequent pages.
        c.Item().Text(body).LineHeight(LetterPrintTemplate.BodyLineHeight).AlignRight();
        AddHamesh(c, data, withHamesh);
    }

    /// <summary>چیدمان پیش‌فرض شرکت‌های دیگر (بدون قالب ۱۰۰۳) — دست‌نخورده.</summary>
    private static void AddPlainBody(ColumnDescriptor c, InnerLetterPrintData data, string body,
        bool withHamesh, bool a5)
    {
        c.Spacing(a5 ? 5 : 7);
        c.Item().Text($"عنوان: {data.Title}").Bold().AlignRight();
        c.Item().Text($"گیرندگان: {(string.IsNullOrWhiteSpace(data.Receivers) ? "—" : data.Receivers)}").AlignRight();
        c.Item().Text($"ایجادکننده: {(string.IsNullOrWhiteSpace(data.Creator) ? "—" : data.Creator)}").AlignRight();
        LetterPrintTemplate.Separator(c);
        c.Item().Text(body).AlignRight();
        AddHamesh(c, data, withHamesh);
    }

    /// <summary>«با هامش» فقط ردیف‌های هامش را اضافه می‌کند؛ امضا/مهر/عبارت پایانی چاپ نمی‌شود.</summary>
    private static void AddHamesh(ColumnDescriptor c, InnerLetterPrintData data, bool withHamesh)
    {
        if (!withHamesh || data.Hamesh.Count == 0) return;
        c.Item().PaddingTop(8).Text(LetterPrintTemplate.RtlLabel("هامش")).Bold().AlignRight();
        foreach (var row in data.Hamesh)
        {
            var details = new[] { PlainText(row.Action), PlainText(row.Instruction) }
                .Where(x => !string.IsNullOrWhiteSpace(x));
            var detail = string.Join(" — ", details);
            c.Item().Text($"• {row.Receiver}{(detail.Length > 0 ? " — " + detail : "")}").AlignRight();
        }
    }

    /// <summary>فاصلهٔ برچسب↔مقدار در بلوک اطلاعات — همان مقدار نامهٔ صادره («یک فاصله بعد از دونقطه»).</summary>
    internal const float InfoLabelGap = 7.2f;

    /// <summary>
    /// کادرهای شماره/تاریخ/پیوست با مقدار نهایی‌شان. اولویت با هندسه و ظاهرِ خودِ قالب MRT است
    /// (مستطیل، اندازهٔ قلم، رنگ، پس‌زمینه)؛ اگر قالب (نسخهٔ قدیمی نصب‌شده) آن کادرها را نداشته
    /// باشد، مختصات مطلق کالیبرهٔ ۱۰۰۳ با متن سفید.
    /// </summary>
    private static List<HeaderMetaCell> BuildMetaRows(OutgoingLetterDesignLayout? meta, string number,
        string date, string attachment, bool a5)
    {
        var rows = new List<HeaderMetaCell>();
        var boxes = meta?.HeaderBoxes
            .Where(box => box.Slot is OutgoingLetterDesignLayout.Slot.Number
                or OutgoingLetterDesignLayout.Slot.Date
                or OutgoingLetterDesignLayout.Slot.Attachment)
            .ToList() ?? new List<OutgoingLetterDesignLayout.Box>();

        foreach (var box in boxes)
        {
            var value = box.Slot switch
            {
                OutgoingLetterDesignLayout.Slot.Number => number,
                OutgoingLetterDesignLayout.Slot.Date => date,
                _ => attachment
            };
            rows.Add(new HeaderMetaCell(
                box.Left,
                box.Top,
                box.Width,
                box.Height,
                value,
                string.IsNullOrWhiteSpace(box.Color) ? "#000000" : box.Color,
                box.FontSize,
                box.Fill));
        }

        if (rows.Count > 0) return rows;

        // نسخهٔ پشتیبان: همان کادرهای قالب صادرهٔ ۱۰۰۳، در مختصات مطلق صفحه.
        static HeaderMetaCell Fallback(HomepaLetterheadLayout.Field field, string value)
            => new(field.Left, field.Top, field.Width, field.Height, value, LetterPrintTemplate.MetaValueColor, 10f);

        rows.Add(Fallback(HomepaLetterheadLayout.AbsoluteNumber(a5), number));
        rows.Add(Fallback(HomepaLetterheadLayout.AbsoluteDate(a5), date));
        rows.Add(Fallback(HomepaLetterheadLayout.AbsoluteAttachment(a5), attachment));
        return rows;
    }

    /// <summary>
    /// بدنهٔ HTML نامه → متن چاپی. جزئیات و چرایی در <see cref="OfficeHtmlText"/>:
    /// سرِ سند (head/style/xml و کامنت‌های شرطی Word) با محتوایش حذف می‌شود و شکست‌خط‌های
    /// سورس HTML پاک می‌شوند تا نه متن انگلیسی روی کاغذ بیاید و نه صفحهٔ خالی.
    /// </summary>
    internal static string PlainText(string? html) => OfficeHtmlText.ToPlainText(html);

    private static void EnsureFonts(string directory)
    {
        lock (FontLock)
        {
            if (_fontsRegistered) return;
            using var regular = File.OpenRead(Path.Combine(directory, "Vazirmatn-Regular.ttf"));
            using var bold = File.OpenRead(Path.Combine(directory, "Vazirmatn-Bold.ttf"));
            FontManager.RegisterFontWithCustomName("Vazirmatn", regular);
            FontManager.RegisterFontWithCustomName("Vazirmatn-Bold", bold);
            _fontsRegistered = true;
        }
    }
}
