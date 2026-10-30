using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
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

    /// <summary>
    /// یک کادر شماره/تاریخ/پیوست با مختصات مطلق صفحه (از گوشهٔ بالا-چپ کاغذ) و مقدار نهایی آن.
    /// </summary>
    private readonly record struct MetaRow(
        float Left, float Top, float Width, float Height, string Value, string Color);

    /// <param name="meta">
    /// هندسهٔ خوانده‌شده از خودِ قالب MRT نامهٔ داخلی (همان کادرهای شماره/تاریخ/پیوست قالب
    /// صادرهٔ ۱۰۰۳). اگر قالبِ نصب‌شده روی سرور قدیمی باشد و این کادرها را نداشته باشد، از
    /// مختصات مطلق کالیبرهٔ <see cref="HomepaLetterheadLayout"/> استفاده می‌شود.
    /// </param>
    internal static byte[] Render(InnerLetterPrintData data, bool a5, bool withHamesh,
        byte[]? homepaBackground, bool attachment, string fontDirectory,
        OutgoingLetterDesignLayout? meta = null)
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
        var metaRows = homepa ? BuildMetaRows(meta, number, date, attachmentText, a5) : new List<MetaRow>();
        // شروع متن: پایین «نوار آبی» + ۹۷pt تأییدشدهٔ کارفرما (دقیقاً مثل نامهٔ صادرهٔ ۱۰۰۳)؛
        // هرگز روی کادرهای شماره/تاریخ/پیوست نمی‌افتد.
        var contentTop = HomepaLetterheadLayout.ContentTop(a5);
        if (metaRows.Count > 0)
            contentTop = Math.Max(contentTop, metaRows.Max(row => row.Top + row.Height) + 6f);
        var sideMargin = LetterPrintTemplate.MmToPoints(LetterPrintTemplate.SideMarginMillimeters);

        return Document.Create(root => root.Page(page =>
        {
            page.Size(a5 ? PageSizes.A5 : PageSizes.A4);
            // Non-Homepa backgrounds are overlaid by PDFsharp after rendering.
            page.PageColor(Colors.Transparent);
            page.ContentFromRightToLeft();
            page.DefaultTextStyle(x => x.FontFamily("Vazirmatn").FontSize(fontSize)
                .LineHeight(LetterPrintTemplate.BodyLineHeight).DirectionFromRightToLeft());
            if (homepa)
            {
                // سربرگ ۱۰۰۳: مختصات مطلق صفحه، بدون حاشیهٔ بالا، مثل مسیر «از دل طرح» نامهٔ صادره.
                page.MarginTop(0);
                page.MarginHorizontal(sideMargin);
                page.MarginBottom(HomepaLetterheadLayout.DesignFooterReserve(a5));
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
                if (metaRows.Count > 0) page.Foreground().Element(host => DrawMetaRows(host, metaRows));
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
                    c.Item().Text($"تاریخ ارجاع: {(string.IsNullOrWhiteSpace(data.ReferralDates) ? "—" : data.ReferralDates)}");
                    c.Item().Text($"پیوست: {attachmentText}");
                });
            }

            var content = page.Content();
            if (!homepa) content = content.PaddingTop(a5 ? 12 : 16).PaddingHorizontal(a5 ? 32 : 46);
            content.Column(c =>
            {
                if (homepa) AddHomepaBody(c, data, body, withHamesh, a5);
                else AddPlainBody(c, data, body, withHamesh, a5);
            });
        })).GeneratePdf();
    }

    /// <summary>
    /// بلوک اطلاعات و متن، دقیقاً با همان چیدمان نامهٔ صادرهٔ ۱۰۰۳ و **بدون بلوک امضا**
    /// (نامهٔ داخلی است): خط اول گیرندگان بدون برچسب، خط دوم «موضوع:» به‌همراه عنوان،
    /// سپس متن نامه و در پایان هامش.
    /// </summary>
    private static void AddHomepaBody(ColumnDescriptor c, InnerLetterPrintData data, string body,
        bool withHamesh, bool a5)
    {
        var infoFontSize = a5 ? 9f : 11f;
        var rows = new List<LetterPrintTemplate.InfoRow>();
        if (!string.IsNullOrWhiteSpace(data.Receivers))
            rows.Add(new LetterPrintTemplate.InfoRow("گیرندگان", data.Receivers, HideLabel: true));
        rows.Add(new LetterPrintTemplate.InfoRow("موضوع",
            string.IsNullOrWhiteSpace(data.Title) ? "—" : data.Title, BoldValue: true));
        if (!string.IsNullOrWhiteSpace(data.Creator))
            rows.Add(new LetterPrintTemplate.InfoRow("ایجادکننده", data.Creator));
        // «تاریخ ارجاع» فقط وقتی داده‌ای دارد چاپ می‌شود (ردیف خالی چاپ نمی‌شود).
        if (!string.IsNullOrWhiteSpace(data.ReferralDates))
            rows.Add(new LetterPrintTemplate.InfoRow("تاریخ ارجاع", data.ReferralDates));

        LetterPrintTemplate.InfoBlock(c, rows, infoFontSize,
            Math.Max(LetterPrintTemplate.ParagraphSpacing, infoFontSize * 0.45f), InfoLabelGap);
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
    /// کادرهای شماره/تاریخ/پیوست با مقدار نهایی‌شان. اولویت با هندسهٔ خودِ قالب MRT است؛
    /// اگر قالب (نسخهٔ قدیمی نصب‌شده) آن کادرها را نداشته باشد، مختصات مطلق کالیبرهٔ ۱۰۰۳.
    /// </summary>
    private static List<MetaRow> BuildMetaRows(OutgoingLetterDesignLayout? meta, string number,
        string date, string attachment, bool a5)
    {
        var rows = new List<MetaRow>();
        var boxes = meta?.HeaderBoxes
            .Where(box => box.Slot is OutgoingLetterDesignLayout.Slot.Number
                or OutgoingLetterDesignLayout.Slot.Date
                or OutgoingLetterDesignLayout.Slot.Attachment)
            .ToList() ?? new List<OutgoingLetterDesignLayout.Box>();

        foreach (var box in boxes)
        {
            var (value, color) = box.Slot switch
            {
                OutgoingLetterDesignLayout.Slot.Number => (number, box.Color),
                OutgoingLetterDesignLayout.Slot.Date => (date, box.Color),
                _ => (attachment, box.Color)
            };
            rows.Add(new MetaRow(box.Left, box.Top, box.Width, box.Height, value,
                string.IsNullOrWhiteSpace(color) ? "#FFFFFF" : color));
        }

        if (rows.Count > 0) return rows;

        // نسخهٔ پشتیبان: همان کادرهای قالب صادره، در مختصات مطلق صفحه.
        static MetaRow Fallback(HomepaLetterheadLayout.Field field, string value)
            => new(field.Left, field.Top, field.Width, field.Height, value, "#FFFFFF");

        rows.Add(Fallback(HomepaLetterheadLayout.AbsoluteNumber(a5), number));
        rows.Add(Fallback(HomepaLetterheadLayout.AbsoluteDate(a5), date));
        rows.Add(Fallback(HomepaLetterheadLayout.AbsoluteAttachment(a5), attachment));
        return rows;
    }

    /// <summary>
    /// چاپ مقادیر سربرگ در مختصات مطلق صفحه؛ راست‌چین در ستونی که ۴pt پیش از برچسب چاپ‌شده
    /// تمام می‌شود (همان قاعدهٔ مسیر «از دل طرح» نامهٔ صادره).
    /// </summary>
    private static void DrawMetaRows(IContainer host, IReadOnlyList<MetaRow> rows)
    {
        if (rows.Count == 0) return;

        var right = rows.Min(row => row.Left + row.Width) - 4f;
        var left = Math.Max(rows.Min(row => row.Left),
            LetterPrintTemplate.MmToPoints(LetterPrintTemplate.SideMarginMillimeters));
        // در A5 که صفحه باریک است، حاشیهٔ امن فقط تا جایی بزرگ می‌شود که مقدار خوانا بماند.
        if (right - left < 44f) left = Math.Max(0f, right - 44f);
        var width = Math.Max(20f, right - left);

        host.AlignLeft().AlignTop().Column(column =>
        {
            var cursor = 0f;
            foreach (var row in rows.OrderBy(row => row.Top))
            {
                var gap = row.Top - cursor;
                if (gap > 0.5f) column.Item().Height(gap);
                var height = Math.Max(10f, row.Height);
                var text = string.IsNullOrWhiteSpace(row.Value) ? "—" : row.Value;
                column.Item().Height(height).Row(line =>
                {
                    if (left > 0.5f) line.ConstantItem(left);
                    line.ConstantItem(width).AlignMiddle().AlignRight()
                        .Text(LetterPrintTemplate.IsolateMixedText(text))
                        .FontFamily("Vazirmatn-Bold")
                        .FontSize(FitMetaFontSize(text, width))
                        .FontColor(row.Color)
                        .LineHeight(1.15f)
                        .AlignRight()
                        .DirectionFromRightToLeft();
                });
                cursor = row.Top + height;
            }
        });
    }

    /// <summary>بزرگ‌ترین اندازه‌ای که مقدار سربرگ در یک خط جا می‌شود (کف ۷pt، مثل نامهٔ صادره).</summary>
    private static float FitMetaFontSize(string text, float maxWidth, float preferred = 10f,
        float minimum = 7f)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0f) return preferred;
        var estimated = text.Length * preferred * 0.58f;
        if (estimated <= maxWidth) return preferred;
        return Math.Max(minimum, preferred * maxWidth / estimated);
    }

    internal static string PlainText(string? html)
    {
        var text = Regex.Replace(html ?? "", @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</\s*(p|div|li|h[1-6]|tr)\s*>", "\n", RegexOptions.IgnoreCase);
        text = WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]+>", ""));
        return Regex.Replace(text, @"[^\S\r\n]+", " ").Trim();
    }

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
