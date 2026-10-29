using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Inventory.Api.Data;
using Inventory.Api.Services.Office.Outgoing;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;

namespace Inventory.Api.Services.Office;

// ============================================================
//  قالب چاپی صورتجلسه روی سربرگ شرکت — A4 / A5
//  • چاپ روی سربرگ «همان شرکتی» انجام می‌شود که صورتجلسه برایش ثبت شده است
//    (MeetingMinutes.CompanyId؛ اگر خالی باشد، شرکت فعال در هدر درخواست).
//  • جای عنوان، شماره صورتجلسه، تاریخ جلسه، حاضرین و غایبین از خودِ فایل طرح شرکت
//    خوانده می‌شود: Reports/MeetingMinutes/Companies/{CompanyId}/Minutes-{A4|A5}.mrt
//    (همان کاری که برای نامهٔ داخلی و نامهٔ صادره انجام شد).
//  • اگر قالب نبود، چیدمان قبلی کد استفاده می‌شود؛ اگر سربرگی هم نبود، نسخهٔ ساده.
//  • امضای الکترونیکی حاضرین به‌صورت تصویر در صفحهٔ چاپ درج می‌شود.
// ============================================================

public interface IMeetingMinutesPrintService
{
    /// <summary>
    /// تولید PDF صورتجلسه روی سربرگ شرکتی که صورتجلسه برایش ثبت شده است —
    /// اگر سربرگی نباشد، بدون پس‌زمینه برمی‌گردد. <paramref name="size"/> = "A4" یا "A5".
    /// </summary>
    Task<byte[]?> GeneratePdfAsync(int minutesId, string size = "A4");
}

public class MeetingMinutesPrintService : IMeetingMinutesPrintService
{
    private static bool _fontsRegistered;
    private static readonly object FontLock = new();

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IHttpContextAccessor _http;
    private readonly IConfiguration _configuration;

    // مسیر سربرگِ استخراج‌شده از قالب MRT، به‌ازای (مسیر قالب + اندازهٔ فایل + زمان تغییر).
    private static readonly ConcurrentDictionary<string, string> TemplateLetterheads = new(StringComparer.Ordinal);

    public MeetingMinutesPrintService(AppDbContext db, IWebHostEnvironment env, IHttpContextAccessor http,
        IConfiguration configuration)
    {
        _db = db;
        _env = env;
        _http = http;
        _configuration = configuration;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // ==================== فونت فارسی ====================

    private static bool _vazirReady;
    private static string Fnt => _vazirReady ? "Vazirmatn" : "Lato";
    private static string FntBold => _vazirReady ? "Vazirmatn-Bold" : "Lato";

    private void EnsureFonts()
    {
        if (_fontsRegistered) return;
        lock (FontLock)
        {
            if (_fontsRegistered) return;
            // نبودِ یک نویسه (ایموجی و…) نباید چاپ را قطع کند
            QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = false;

            var dirs = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Resources", "fonts"),
                Path.Combine(_env.ContentRootPath ?? "", "Resources", "fonts"),
                Path.Combine(Directory.GetCurrentDirectory(), "Resources", "fonts"),
                Path.Combine(AppContext.BaseDirectory, "fonts"),
            };
            string? reg = null, bold = null;
            foreach (var dir in dirs)
            {
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;
                var r = Path.Combine(dir, "Vazirmatn-Regular.ttf");
                var b = Path.Combine(dir, "Vazirmatn-Bold.ttf");
                if (reg == null && File.Exists(r)) reg = r;
                if (bold == null && File.Exists(b)) bold = b;
            }
            if (reg != null) FontManager.RegisterFontWithCustomName("Vazirmatn", File.OpenRead(reg));
            if (bold != null) FontManager.RegisterFontWithCustomName("Vazirmatn-Bold", File.OpenRead(bold));
            _vazirReady = reg != null;
            _fontsRegistered = true;
        }
    }

    // ==================== سربرگ ====================

    private string? ResolveLetterheadPath(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var clean = fileName.Replace('\\', '/').TrimStart('/');
        if (clean.Contains("..")) return null;

        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var uploadClean = clean.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase)
            ? clean["uploads/".Length..]
            : clean;

        var candidates = new[]
        {
            Path.Combine(webRoot, "uploads", uploadClean),
            Path.Combine(webRoot, clean),
            Path.Combine(_env.ContentRootPath, clean),
            Path.Combine(_env.ContentRootPath, Path.GetFileName(clean)),
            Path.Combine(AppContext.BaseDirectory, clean),
            Path.Combine(AppContext.BaseDirectory, Path.GetFileName(clean))
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string FaDate(DateTime? d)
    {
        if (d is null) return "—";
        var v = d.Value;
        // تاریخ‌های خارج از بازهٔ تقویم جلالی نباید کل چاپ را متوقف کنند
        try
        {
            var pc = new PersianCalendar();
            if (v < pc.MinSupportedDateTime || v > pc.MaxSupportedDateTime) return "—";
            return $"{pc.GetYear(v)}/{pc.GetMonth(v):00}/{pc.GetDayOfMonth(v):00}";
        }
        catch (ArgumentOutOfRangeException)
        {
            return "—";
        }
    }

    // ==================== تولید PDF ====================

    public async Task<byte[]?> GeneratePdfAsync(int minutesId, string size = "A4")
    {
        var m = await _db.MeetingMinutes.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == minutesId && !x.IsDeleted);
        if (m == null) return null;

        var parts = await _db.MeetingMinutesParticipants.AsNoTracking()
            .Where(p => p.MinutesId == minutesId)
            .OrderBy(p => p.Kind).ThenBy(p => p.Name)
            .ToListAsync();
        var items = await _db.MeetingMinutesItems.AsNoTracking()
            .Where(i => i.MinutesId == minutesId)
            .OrderBy(i => i.RowNo).ThenBy(i => i.Id)
            .ToListAsync();

        var isA5 = string.Equals(size?.Trim(), "A5", StringComparison.OrdinalIgnoreCase);

        // ── شرکت: «همان شرکتی که صورتجلسه برایش ثبت شده» ──
        // MeetingMinutes.CompanyId در لحظهٔ ثبت ست می‌شود و مرجع اصلی است؛ فقط اگر خالی
        // باشد (صورتجلسه‌های قدیمی) شرکت فعالِ درخواست جای آن را می‌گیرد.
        string? companyName = null;
        string? registeredLetterhead = null;
        int? companyId = null;
        try
        {
            var rawCompanyId = _http.HttpContext?.Request.Headers["X-Company-Id"].FirstOrDefault();
            var requestCompanyId = int.TryParse(rawCompanyId, out var parsedCompanyId) && parsedCompanyId > 0
                ? parsedCompanyId
                : 0;
            companyId = m.CompanyId is > 0 ? m.CompanyId : (requestCompanyId > 0 ? requestCompanyId : null);

            if (companyId is int id)
            {
                var company = await _db.SystemCompanies.AsNoTracking()
                    .Where(c => c.Id == id && c.IsActive)
                    .Select(c => new { c.Name, c.LetterheadFileName })
                    .FirstOrDefaultAsync();
                companyName = company?.Name;
                registeredLetterhead = ResolveLetterheadPath(company?.LetterheadFileName);
            }
        }
        catch
        {
            registeredLetterhead = null;
        }

        // ── قالب طرح همان شرکت: Reports/MeetingMinutes/Companies/{CompanyId}/Minutes-{A4|A5}.mrt ──
        var templatePath = companyId is int templateCompanyId ? FindTemplate(templateCompanyId, isA5) : null;
        var design = MeetingMinutesDesignLayout.TryRead(templatePath);

        // سربرگ: اول تصویر جاسازی‌شده در خودِ قالب (پس چاپ به فایل‌های wwwroot وابسته نیست)،
        // بعد فایل سربرگ ثبت‌شدهٔ شرکت.
        var letterheadPath = templatePath != null ? ExtractTemplateLetterhead(templatePath) : null;
        letterheadPath ??= registeredLetterhead;

        byte[] contentPdf;
        try
        {
            contentPdf = design is { HasRequiredSlots: true } templateLayout
                ? BuildTemplateContentPdf(m, parts, items, templateLayout, isA5, companyName,
                    letterheadPath != null)
                : BuildContentPdf(m, parts, items, letterheadPath != null, companyName);
        }
        catch
        {
            // اگر چیدمان (جدول/امضا) جا نشد، نسخهٔ ساده بدون جدول ثابت برمی‌گردد
            contentPdf = BuildFallbackPdf(m, parts, items, companyName);
            letterheadPath = null;
        }

        if (letterheadPath == null) return contentPdf;

        try
        {
            return OverlayOnLetterhead(contentPdf, letterheadPath);
        }
        catch
        {
            // فایل سربرگ خراب — دوباره بدون حاشیهٔ مخصوص سربرگ
            try { return BuildContentPdf(m, parts, items, false, companyName); }
            catch { return BuildFallbackPdf(m, parts, items, companyName); }
        }
    }

    // ==================== قالب طرح صورتجلسه (Stimulsoft MRT) ====================

    /// <summary>ریشهٔ پوشهٔ قالب‌ها؛ با Reporting:MeetingMinutes:TemplateRoot قابل تغییر است.</summary>
    private string TemplateRoot
    {
        get
        {
            var configured = _configuration["Reporting:MeetingMinutes:TemplateRoot"];
            return string.IsNullOrWhiteSpace(configured)
                ? Path.Combine("Reports", "MeetingMinutes", "Companies")
                : configured.Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar);
        }
    }

    /// <summary>
    /// فایل قالب صورتجلسهٔ همان شرکت (Minutes-A4.mrt / Minutes-A5.mrt). اگر طراح نام دیگری
    /// گذاشته باشد، همان پوشه بررسی می‌شود تا قالب «بی‌صدا نادیده» گرفته نشود.
    /// </summary>
    private string? FindTemplate(int companyId, bool isA5)
    {
        var size = isA5 ? "A5" : "A4";
        var roots = new[] { _env.ContentRootPath, AppContext.BaseDirectory, Directory.GetCurrentDirectory() }
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Distinct();
        var folder = Path.Combine(TemplateRoot, companyId.ToString(CultureInfo.InvariantCulture));

        foreach (var root in roots)
        {
            var directory = Path.IsPathRooted(folder) ? folder : Path.Combine(root, folder);
            if (!Directory.Exists(directory)) continue;

            var expected = Path.Combine(directory, $"Minutes-{size}.mrt");
            if (File.Exists(expected)) return expected;

            var fallback = Directory.EnumerateFiles(directory, "*.mrt")
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(file => Path.GetFileNameWithoutExtension(file)
                    .Contains(size, StringComparison.OrdinalIgnoreCase));
            if (fallback != null) return fallback;
        }

        return null;
    }

    /// <summary>
    /// سربرگ جاسازی‌شده در قالب (Watermark.ImageBytes) را در فایل موقت ذخیره می‌کند تا
    /// صورتجلسه روی همان تصویر سربرگِ قالب چاپ شود — مستقل از wwwroot و رکورد شرکت.
    /// </summary>
    private string? ExtractTemplateLetterhead(string templatePath)
    {
        try
        {
            var info = new FileInfo(templatePath);
            var key = $"{Path.GetFullPath(templatePath)}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
            if (TemplateLetterheads.TryGetValue(key, out var cached) && File.Exists(cached)) return cached;

            using var stream = File.OpenRead(templatePath);
            var document = XDocument.Load(stream, LoadOptions.None);
            var base64 = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Watermark")?
                .Elements().FirstOrDefault(element => element.Name.LocalName == "ImageBytes")?.Value;
            if (string.IsNullOrWhiteSpace(base64)) return null;

            var bytes = Convert.FromBase64String(base64);
            if (bytes.Length == 0) return null;

            // پسوند باید با محتوای واقعی بخواند، وگرنه PDFsharp تصویر را باز نمی‌کند.
            var extension = bytes.Length > 4 && bytes[0] == 0x89 && bytes[1] == 0x50 ? ".png"
                : bytes.Length > 4 && Encoding.ASCII.GetString(bytes, 0, 4) == "%PDF" ? ".pdf"
                : ".jpg";

            var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16]
                .ToLowerInvariant();
            var directory = Path.Combine(Path.GetTempPath(), "totall-minutes-letterheads");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"minutes-{identity}{extension}");
            if (!File.Exists(path)) File.WriteAllBytes(path, bytes);

            TemplateLetterheads[key] = path;
            return path;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// چاپ صورتجلسه با هندسهٔ قالب شرکت: هر بخش در جای خودش (همان فاصله‌ها و عرضی که طراح در
    /// Stimulsoft گذاشته) و بندها/امضاها به‌صورت جاری بعد از آن. چون همه‌چیز در جریان عادی
    /// صفحه است، صورتجلسهٔ بلند به صفحهٔ دوم می‌رود و سربرگ/عنوان دوباره تکرار نمی‌شود.
    /// </summary>
    private byte[] BuildTemplateContentPdf(
        MeetingMinutes m,
        List<MeetingMinutesParticipant> parts,
        List<MeetingMinutesItem> items,
        MeetingMinutesDesignLayout design,
        bool isA5,
        string? companyName,
        bool hasLetterhead)
    {
        EnsureFonts();

        var attendees = parts.Where(p => p.Kind == MinutesParticipantKind.Attendee).ToList();
        var absent = parts.Where(p => p.Kind == MinutesParticipantKind.Absent).ToList();
        var number = LetterPrintTemplate.PersianDigits(m.Id.ToString(CultureInfo.InvariantCulture));
        var meetingDate = LetterPrintTemplate.PersianDigits(FaDate(m.MeetingDate));
        var attendeeText = attendees.Count > 0
            ? string.Join("، ", attendees.Select(p => p.Name))
            : "—";
        var absentText = absent.Count > 0
            ? string.Join("، ", absent.Select(p => p.Name))
            : "—";

        var signatureImages = new Dictionary<int, byte[]>();
        foreach (var p in attendees)
        {
            var png = TryDecodeSignaturePng(p.SignatureData);
            if (png != null) signatureImages[p.Id] = png;
        }

        var titleBox = design.Title;
        var itemsCaption = design.Caption(MeetingMinutesDesignLayout.Slot.Items);
        var flowBox = design.Items ?? design.Absentees;
        var flowLeft = flowBox?.Left ?? 28f;
        var flowWidth = flowBox?.Width ?? (design.PageWidth - 2f * flowLeft);

        var footerReserve = hasLetterhead ? HomepaLetterheadLayout.DesignFooterReserve(isA5) : (isA5 ? 24f : 32f);
        var pageWidth = design.PageWidth > 0 ? design.PageWidth : (isA5 ? 419.53f : 595.28f);
        // مکان عمودی جاری (point) — با مختصات قالب جلو می‌رود تا فاصله‌ها همان فاصله‌های MRT باشد.
        var cursor = new float[1];

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(isA5 ? PageSizes.A5 : PageSizes.A4);
                page.MarginTop(0);
                page.MarginLeft(Math.Max(0f, flowLeft));
                page.MarginRight(Math.Max(0f, pageWidth - flowLeft - flowWidth));
                page.MarginBottom(footerReserve);
                // PDF نهایی روی تصویر سربرگ کشیده می‌شود؛ صفحهٔ سفید آن را نمی‌پوشاند.
                page.PageColor(hasLetterhead ? Colors.Transparent : Colors.White);
                page.DefaultTextStyle(style => style
                    .FontFamily(Fnt)
                    .FontSize(isA5 ? 9f : 10f)
                    .FontColor(Ink)
                    .LineHeight(1.6f)
                    .DirectionFromRightToLeft());

                page.ContentFromRightToLeft();
                page.Content().Column(col =>
                {
                    // ── عنوان صورتجلسه (سرصفحهٔ وسط‌چین قالب) ──
                    if (titleBox != null)
                    {
                        Space(col, titleBox.Top - cursor[0]);
                        col.Item().MinHeight(titleBox.Height).Element(cell => Write(cell, titleBox,
                            string.IsNullOrWhiteSpace(m.Title) ? "بدون عنوان" : m.Title, isA5));
                        cursor[0] = titleBox.Top + titleBox.Height;
                    }

                    // ── شماره صورتجلسه و تاریخ جلسه: برچسب کنارِ مقدار، هر دو از قالب ──
                    Pair(col, MeetingMinutesDesignLayout.Slot.Number, number, isA5);
                    Pair(col, MeetingMinutesDesignLayout.Slot.MeetingDate, meetingDate, isA5);

                    // ── حاضرین و غایبین ──
                    Pair(col, MeetingMinutesDesignLayout.Slot.Attendees, attendeeText, isA5);
                    Pair(col, MeetingMinutesDesignLayout.Slot.Absentees, absentText, isA5);

                    // ── بندها و تصمیمات ──
                    if (itemsCaption != null)
                    {
                        Space(col, itemsCaption.Top - cursor[0]);
                        col.Item().Height(Math.Max(12f, itemsCaption.Height)).Element(cell =>
                            Write(cell, itemsCaption, NormalizeCaption(itemsCaption.Text), isA5));
                        cursor[0] = itemsCaption.Top + itemsCaption.Height;
                    }
                    else
                    {
                        col.Item().PaddingTop(4).Text("بندها و تصمیمات:")
                            .FontFamily(FntBold).FontSize(10f).FontColor(BrandDark);
                        cursor[0] = design.ContentEnd;
                    }

                    if (design.Items != null) Space(col, design.Items.Top - cursor[0]);

                    if (items.Count == 0)
                    {
                        col.Item().PaddingTop(2).Text("بندی ثبت نشده است.").FontSize(9f).FontColor(Muted);
                    }
                    else
                    {
                        col.Item().Width(flowWidth).Table(table => ItemsTable(table, items, isA5));
                    }

                    // ── امضای حاضرین ──
                    if (attendees.Count > 0)
                    {
                        col.Item().PaddingTop(12).Text("امضای حاضرین:")
                            .FontFamily(FntBold).FontSize(10f).FontColor(BrandDark);
                        Signatures(col, attendees, signatureImages, isA5, flowWidth);
                    }
                });

                // «صفحه ۲ از ۲» — فقط وقتی صورتجلسه از یک صفحه بیشتر شد.
                page.Footer().Height(16).AlignTop().AlignCenter()
                    .Dynamic(new LetterPrintTemplate.PageNumberFooter(8f));
            });
        });

        _ = companyName; // نام شرکت روی خودِ سربرگ چاپ شده است؛ در متن تکرار نمی‌شود
        return doc.GeneratePdf();

        // ── فاصلهٔ خالی به اندازهٔ اختلاف مختصات قالب ──
        static void Space(ColumnDescriptor col, float height)
        {
            if (height > 0.5f) col.Item().Height(height);
        }

        // ── یک کادر متنی با فونت/رنگ/ترازِ قالب ──
        void Write(IContainer cell, MeetingMinutesDesignLayout.Box box, string value, bool small)
        {
            var target = cell;
            target = box.AlignCenter ? target.AlignCenter()
                : box.AlignRight ? target.AlignRight()
                : target.AlignLeft();

            target.AlignTop().Text(text =>
            {
                text.DefaultTextStyle(style => style
                    .FontFamily(box.Bold ? FntBold : Fnt)
                    .FontSize(box.FontSize)
                    .FontColor(box.Color)
                    .LineHeight(1.6f)
                    .DirectionFromRightToLeft());
                if (box.AlignCenter) text.AlignCenter();
                else if (box.AlignRight) text.AlignRight();
                else text.AlignLeft();
                text.Span(value);
            });
            _ = small;
        }

        // ── برچسب + مقدار: برچسب از قالب (اگر بود)، مقدار در همان ردیف ──
        void Pair(ColumnDescriptor col, MeetingMinutesDesignLayout.Slot slot, string value, bool small)
        {
            var valueBox = slot switch
            {
                MeetingMinutesDesignLayout.Slot.Number => design.Number,
                MeetingMinutesDesignLayout.Slot.MeetingDate => design.MeetingDate,
                MeetingMinutesDesignLayout.Slot.Attendees => design.Attendees,
                _ => design.Absentees
            };
            if (valueBox == null) return;

            Space(col, valueBox.Top - cursor[0]);
            var caption = design.Caption(slot);
            var rowHeight = Math.Max(valueBox.Height, caption?.Height ?? 0f);

            col.Item().Row(row =>
            {
                // در Row راست‌به‌چپ، نخستین آیتم سمت راست صفحه می‌نشیند = محل برچسب در قالب.
                if (caption != null)
                {
                    row.ConstantItem(Math.Max(20f, caption.Width)).Element(cell =>
                        Write(cell, caption, NormalizeCaption(caption.Text), small));
                    // فاصلهٔ افقی بین مقدار و برچسب، همان فاصلهٔ قالب
                    var gap = Math.Max(0f, caption.Left - (valueBox.Left + valueBox.Width));
                    var inset = Math.Max(0f, valueBox.Left - flowLeft);
                    row.RelativeItem().PaddingRight(gap).PaddingLeft(inset).Element(cell =>
                        cell.MinHeight(Math.Max(10f, rowHeight)).Element(inner => Write(inner, valueBox, value, small)));
                }
                else
                {
                    row.RelativeItem().Element(cell =>
                        cell.MinHeight(Math.Max(10f, rowHeight)).Element(inner => Write(inner, valueBox, value, small)));
                }
            });

            cursor[0] = valueBox.Top + rowHeight;
        }

        // ── جدول بندها ──
        void ItemsTable(TableDescriptor table, List<MeetingMinutesItem> rows, bool small)
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(small ? 20 : 24);
                columns.RelativeColumn(3.4f);
                columns.RelativeColumn(1.35f);
                columns.RelativeColumn(1.35f);
                columns.ConstantColumn(small ? 48 : 58);
            });

            table.Header(header =>
            {
                void Head(string text) => header.Cell()
                    .Background(BrandDark).PaddingVertical(5).PaddingHorizontal(4)
                    .AlignCenter().AlignMiddle()
                    .Text(text).FontFamily(FntBold).FontSize(7.5f).FontColor("#ffffff")
                    .DirectionFromRightToLeft();
                Head("ردیف");
                Head("شرح تصمیم");
                Head("مسئول انجام");
                Head("پیگیری");
                Head("وضعیت");
            });

            for (var n = 0; n < rows.Count; n++)
            {
                var item = rows[n];
                var background = n % 2 == 0 ? "#ffffff" : "#f8fafc";
                var statusBackground = item.ItemStatus == MinutesItemStatus.Done ? "#dcfce7"
                    : item.ItemStatus == MinutesItemStatus.Rejected ? "#fee2e2" : "#eef2ff";
                var statusColor = item.ItemStatus == MinutesItemStatus.Done ? "#166534"
                    : item.ItemStatus == MinutesItemStatus.Rejected ? "#991b1b" : BrandDark;

                table.Cell().Background(background).BorderBottom(0.6f).BorderColor(Line)
                    .PaddingVertical(5).AlignCenter().AlignMiddle()
                    .Text(LetterPrintTemplate.PersianDigits(item.RowNo.ToString(CultureInfo.InvariantCulture)))
                    .FontFamily(FntBold).FontSize(8.5f).FontColor(Brand);
                table.Cell().Background(background).BorderBottom(0.6f).BorderColor(Line)
                    .PaddingVertical(5).PaddingHorizontal(5).AlignMiddle()
                    .Text(item.Description ?? string.Empty)
                    .FontSize(8.5f).LineHeight(1.5f).FontColor(Ink)
                    .DirectionFromRightToLeft();

                void Person(string? name, DateTime? date)
                {
                    table.Cell().Background(background).BorderBottom(0.6f).BorderColor(Line)
                        .PaddingVertical(4).PaddingHorizontal(3).AlignCenter().AlignMiddle()
                        .Column(cell =>
                        {
                            cell.Item().AlignCenter()
                                .Text(string.IsNullOrWhiteSpace(name) ? "—" : name)
                                .FontFamily(FntBold).FontSize(7.5f).FontColor(Ink)
                                .DirectionFromRightToLeft();
                            cell.Item().PaddingTop(1).AlignCenter()
                                .Text(LetterPrintTemplate.PersianDigits(FaDate(date)))
                                .FontSize(7f).FontColor(Muted);
                        });
                }

                Person(item.ResponsibleName, item.DueDate);
                Person(item.FollowUpName, item.FollowUpDate);

                table.Cell().Background(statusBackground).BorderBottom(0.6f).BorderColor(Line)
                    .PaddingVertical(4).AlignCenter().AlignMiddle()
                    .Text(MinutesItemStatus.ToFa(item.ItemStatus))
                    .FontFamily(FntBold).FontSize(7f).FontColor(statusColor);
            }
        }

        // ── امضای حاضرین، سه در هر ردیف ──
        void Signatures(ColumnDescriptor col, List<MeetingMinutesParticipant> people,
            Dictionary<int, byte[]> images, bool small, float width)
        {
            const int perRow = 3;
            for (var i = 0; i < people.Count; i += perRow)
            {
                var group = people.Skip(i).Take(perRow).ToList();
                col.Item().PaddingTop(2).Row(row =>
                {
                    for (var k = 0; k < perRow; k++)
                    {
                        var index = k;
                        row.RelativeItem().Padding(3).Element(box =>
                        {
                            if (index >= group.Count) return;
                            var person = group[index];
                            box.Border(0.7f).BorderColor(Line).Background("#fcfcfd")
                                .Padding(6).Column(cell =>
                                {
                                    if (images.TryGetValue(person.Id, out var signature))
                                        cell.Item().Height(small ? 34 : 42).AlignCenter().AlignMiddle()
                                            .Image(signature).FitArea();
                                    else
                                        cell.Item().Height(small ? 34 : 42).AlignBottom()
                                            .PaddingHorizontal(12).PaddingBottom(6)
                                            .LineHorizontal(0.7f).LineColor("#cbd5e1");
                                    cell.Item().PaddingTop(3).AlignCenter().Text(person.Name)
                                        .FontFamily(FntBold).FontSize(8f).FontColor(Ink)
                                        .DirectionFromRightToLeft();
                                    cell.Item().AlignCenter()
                                        .Text(person.SignedAt != null
                                            ? LetterPrintTemplate.PersianDigits(FaDate(person.SignedAt))
                                            : "در انتظار امضا")
                                        .FontSize(7f)
                                        .FontColor(person.SignedAt != null ? "#166534" : Muted);
                                });
                        });
                    }
                });
            }
            _ = width;
        }
    }

    /// <summary>برچسب ذخیره‌شده در قالب («:حاضرین») → متن قابل چاپ («حاضرین:»).</summary>
    private static string NormalizeCaption(string text)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.StartsWith(':') || value.StartsWith('：')) value = value[1..].Trim() + ":";
        return value;
    }

    private const string Ink = "#0f172a";
    private const string Muted = "#64748b";
    private const string Line = "#e2e8f0";
    private const string Soft = "#f8fafc";
    private const string Brand = "#4f46e5";
    private const string BrandDark = "#312e81";

    private static string FaDigits(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        const string map = "۰۱۲۳۴۵۶۷۸۹";
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (chars[i] is >= '0' and <= '9')
                chars[i] = map[chars[i] - '0'];
        return new string(chars);
    }

    private static string FaDatePretty(DateTime? d) => FaDigits(FaDate(d));

    private static void SectionHead(ColumnDescriptor col, string title, string? hint = null)
    {
        col.Item().PaddingTop(14).PaddingBottom(6).Row(r =>
        {
            r.ConstantItem(3).Background(Brand);
            r.ConstantItem(8);
            r.RelativeItem().AlignMiddle().Text(title).FontFamily(FntBold).FontSize(11.5f).FontColor(BrandDark);
            if (!string.IsNullOrWhiteSpace(hint))
                r.AutoItem().AlignMiddle().Text(hint).FontSize(8).FontColor(Muted);
        });
    }

    private byte[] BuildContentPdf(MeetingMinutes m, List<MeetingMinutesParticipant> parts, List<MeetingMinutesItem> items, bool reserveLetterhead, string? companyName)
    {
        EnsureFonts();

        const float fs = 10f;
        var number = FaDigits(m.Id.ToString());
        var attendees = parts.Where(p => p.Kind == MinutesParticipantKind.Attendee).ToList();
        var absent = parts.Where(p => p.Kind == MinutesParticipantKind.Absent).ToList();

        var signatureImages = new Dictionary<int, byte[]>();
        foreach (var p in attendees)
        {
            var png = TryDecodeSignaturePng(p.SignatureData);
            if (png != null) signatureImages[p.Id] = png;
        }

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.ContentFromRightToLeft();
                page.DefaultTextStyle(x => x.FontFamily(Fnt).FontSize(fs).FontColor(Ink).LineHeight(1.45f));

                if (reserveLetterhead)
                {
                    page.MarginTop(118);
                    page.MarginBottom(52);
                    page.MarginHorizontal(36);
                }
                else
                {
                    page.MarginTop(0);
                    page.MarginHorizontal(0);
                    page.MarginBottom(16);
                }

                if (!reserveLetterhead)
                {
                    page.Header().Column(h =>
                    {
                        h.Item().Height(7).Background(Brand);
                        h.Item().Background("#eef2ff").PaddingHorizontal(28).PaddingVertical(8).Row(row =>
                        {
                            row.RelativeItem().AlignMiddle().Text(string.IsNullOrWhiteSpace(companyName) ? "صورتجلسه" : companyName)
                                .FontFamily(FntBold).FontSize(11).FontColor(BrandDark);
                            row.AutoItem().AlignMiddle().Text($"شماره  {number}")
                                .FontFamily(FntBold).FontSize(9).FontColor(Brand);
                        });
                    });
                }

                page.Content().PaddingHorizontal(reserveLetterhead ? 0 : 28).PaddingTop(reserveLetterhead ? 0 : 14).Column(col =>
                {
                    col.Item().AlignCenter().Text("صورتجلسه")
                        .FontFamily(FntBold).FontSize(9).FontColor(Brand);
                    col.Item().PaddingTop(3).AlignCenter().Text(string.IsNullOrWhiteSpace(m.Title) ? "بدون عنوان" : m.Title)
                        .FontFamily(FntBold).FontSize(16).FontColor(Ink).LineHeight(1.35f);
                    col.Item().PaddingTop(6).AlignCenter().Width(56).Height(2.5f).Background(Brand);
                    if (!string.IsNullOrWhiteSpace(m.CreatedByName))
                        col.Item().PaddingTop(2).AlignCenter().Text($"ثبت‌کننده: {m.CreatedByName}")
                            .FontSize(8.5f).FontColor(Muted);

                    col.Item().PaddingTop(10).Row(row =>
                    {
                        void Meta(string label, string value)
                        {
                            row.RelativeItem().PaddingHorizontal(3).Border(0.7f).BorderColor(Line).Background(Soft)
                                .PaddingVertical(6).PaddingHorizontal(4).Column(c =>
                                {
                                    c.Item().AlignCenter().Text(label).FontSize(7.5f).FontColor(Muted);
                                    c.Item().PaddingTop(2).AlignCenter().Text(value).FontFamily(FntBold).FontSize(9.5f).FontColor(Ink);
                                });
                        }
                        Meta("شماره", number);
                        Meta("تاریخ جلسه", FaDatePretty(m.MeetingDate));
                        Meta("تاریخ ثبت", FaDatePretty(m.DateRegistered));
                        Meta("وضعیت", MeetingMinutesStatus.ToFa(m.Status));
                    });

                    SectionHead(col, "حاضرین جلسه", attendees.Count > 0 ? FaDigits(attendees.Count.ToString()) + " نفر" : null);
                    col.Item().Border(0.7f).BorderColor(Line).Background("#fcfcfd").Padding(8).Column(box =>
                    {
                        box.Item().Text(attendees.Count > 0 ? string.Join("    ·    ", attendees.Select(p => p.Name)) : "—")
                            .FontSize(9.5f).LineHeight(1.6f).FontColor(Ink);
                        if (absent.Count > 0)
                            box.Item().PaddingTop(4).Text(t =>
                            {
                                t.Span("غایبین: ").FontFamily(FntBold).FontSize(8.5f).FontColor("#b91c1c");
                                t.Span(string.Join("، ", absent.Select(p => p.Name))).FontSize(8.5f).FontColor(Muted);
                            });
                    });

                    SectionHead(col, "بندها و تصمیمات", items.Count > 0 ? FaDigits(items.Count.ToString()) + " بند" : null);
                    if (items.Count == 0)
                    {
                        col.Item().Border(0.7f).BorderColor(Line).Padding(10)
                            .AlignCenter().Text("بندی ثبت نشده است.").FontSize(9).FontColor(Muted);
                    }
                    else
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(28);
                                c.RelativeColumn(3.4f);
                                c.RelativeColumn(1.35f);
                                c.RelativeColumn(1.35f);
                                c.ConstantColumn(62);
                            });

                            table.Header(h =>
                            {
                                void Head(string txt) => h.Cell().Background(BrandDark).PaddingVertical(6).PaddingHorizontal(5)
                                    .AlignCenter().AlignMiddle()
                                    .Text(txt).FontFamily(FntBold).FontSize(8).FontColor("#ffffff");
                                Head("ردیف");
                                Head("شرح تصمیم");
                                Head("مسئول انجام");
                                Head("پیگیری");
                                Head("وضعیت");
                            });

                            for (var n = 0; n < items.Count; n++)
                            {
                                var it = items[n];
                                var bg = n % 2 == 0 ? "#ffffff" : "#f8fafc";
                                var statusBg = it.ItemStatus == MinutesItemStatus.Done ? "#dcfce7"
                                    : it.ItemStatus == MinutesItemStatus.Rejected ? "#fee2e2" : "#eef2ff";
                                var statusFg = it.ItemStatus == MinutesItemStatus.Done ? "#166534"
                                    : it.ItemStatus == MinutesItemStatus.Rejected ? "#991b1b" : BrandDark;

                                table.Cell().Background(bg).BorderBottom(0.6f).BorderColor(Line)
                                    .PaddingVertical(7).AlignCenter().AlignMiddle()
                                    .Text(FaDigits(it.RowNo.ToString())).FontFamily(FntBold).FontSize(9).FontColor(Brand);
                                table.Cell().Background(bg).BorderBottom(0.6f).BorderColor(Line)
                                    .PaddingVertical(7).PaddingHorizontal(6).AlignMiddle()
                                    .Text(it.Description).FontSize(9).LineHeight(1.5f).FontColor(Ink);
                                void Person(string? name, DateTime? date)
                                {
                                    table.Cell().Background(bg).BorderBottom(0.6f).BorderColor(Line)
                                        .PaddingVertical(6).PaddingHorizontal(4).AlignCenter().AlignMiddle()
                                        .Column(c =>
                                        {
                                            c.Item().AlignCenter().Text(string.IsNullOrWhiteSpace(name) ? "—" : name)
                                                .FontFamily(FntBold).FontSize(8).FontColor(Ink);
                                            c.Item().PaddingTop(1).AlignCenter().Text(FaDatePretty(date))
                                                .FontSize(7.5f).FontColor(Muted);
                                        });
                                }
                                Person(it.ResponsibleName, it.DueDate);
                                Person(it.FollowUpName, it.FollowUpDate);
                                table.Cell().Background(statusBg).BorderBottom(0.6f).BorderColor(Line)
                                    .PaddingVertical(6).AlignCenter().AlignMiddle()
                                    .Text(MinutesItemStatus.ToFa(it.ItemStatus)).FontFamily(FntBold).FontSize(7.5f).FontColor(statusFg);
                            }
                        });
                    }

                    if (attendees.Count > 0)
                    {
                        var signed = attendees.Count(p => p.SignedAt != null);
                        SectionHead(col, "امضای حاضرین", FaDigits(signed.ToString()) + " از " + FaDigits(attendees.Count.ToString()));

                        const int perRow = 3;
                        for (var i = 0; i < attendees.Count; i += perRow)
                        {
                            var row = attendees.Skip(i).Take(perRow).ToList();
                            col.Item().PaddingTop(2).Row(r =>
                            {
                                for (var k = 0; k < perRow; k++)
                                {
                                    var idx = k;
                                    r.RelativeItem().Padding(4).Element(box =>
                                    {
                                        if (idx >= row.Count) return;
                                        var person = row[idx];
                                        box.Border(0.7f).BorderColor(Line).Background("#fcfcfd").Padding(8).Column(sc =>
                                        {
                                            if (signatureImages.TryGetValue(person.Id, out var sig))
                                                sc.Item().Height(44).AlignCenter().AlignMiddle().Image(sig).FitArea();
                                            else
                                                sc.Item().Height(44).AlignBottom().PaddingHorizontal(14).PaddingBottom(8)
                                                    .LineHorizontal(0.7f).LineColor("#cbd5e1");
                                            sc.Item().PaddingTop(4).AlignCenter().Text(person.Name)
                                                .FontFamily(FntBold).FontSize(8.5f).FontColor(Ink);
                                            sc.Item().AlignCenter().Text(person.SignedAt != null ? FaDatePretty(person.SignedAt) : "در انتظار امضا")
                                                .FontSize(7.5f).FontColor(person.SignedAt != null ? "#166534" : Muted);
                                        });
                                    });
                                }
                            });
                        }
                    }
                });

                page.Footer().PaddingHorizontal(reserveLetterhead ? 0 : 28).Column(f =>
                {
                    f.Item().LineHorizontal(0.7f).LineColor(Line);
                    f.Item().PaddingTop(5).Row(r =>
                    {
                        r.RelativeItem().AlignMiddle().Text(string.IsNullOrWhiteSpace(companyName) ? "صورتجلسه" : companyName)
                            .FontSize(8).FontColor("#94a3b8");
                        r.AutoItem().AlignMiddle().Text(t =>
                        {
                            t.Span("صفحه ").FontSize(8).FontColor("#94a3b8");
                            t.CurrentPageNumber().FontSize(8).FontColor("#64748b").Format(n => FaDigits(n?.ToString() ?? "0"));
                            t.Span(" از ").FontSize(8).FontColor("#94a3b8");
                            t.TotalPages().FontSize(8).FontColor("#64748b").Format(n => FaDigits(n?.ToString() ?? "0"));
                        });
                    });
                });
            });
        });

        return doc.GeneratePdf();
    }

    /// <summary>
    /// امضای ذخیره‌شده را به PNG معتبر تبدیل می‌کند.
    /// پیشوند data:، فاصله و base64 ناقص را می‌پذیرد. اگر تصویر خراب باشد null برمی‌گرداند
    /// تا چاپ صورتجلسه به‌خاطر یک امضا متوقف نشود.
    /// </summary>
    private static byte[]? TryDecodeSignaturePng(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var sig = raw.Trim();
        var comma = sig.IndexOf(',');
        if (sig.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0)
            sig = sig[(comma + 1)..];

        sig = sig.Replace(" ", "").Replace("\r", "").Replace("\n", "").Replace("\t", "");
        sig = sig.Replace('-', '+').Replace('_', '/');
        switch (sig.Length % 4)
        {
            case 2: sig += "=="; break;
            case 3: sig += "="; break;
        }

        byte[] bytes;
        try { bytes = Convert.FromBase64String(sig); }
        catch (FormatException) { return null; }
        if (bytes.Length < 8) return null;

        try
        {
            using var bmp = SKBitmap.Decode(bytes);
            if (bmp == null || bmp.Width <= 0 || bmp.Height <= 0) return null;
            using var image = SKImage.FromBitmap(bmp);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data?.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>نسخهٔ ساده وقتی جدول یا امضا در صفحه جا نمی‌شود — باز هم بدون نیاز به سربرگ.</summary>
    private byte[] BuildFallbackPdf(MeetingMinutes m, List<MeetingMinutesParticipant> parts, List<MeetingMinutesItem> items, string? companyName)
    {
        EnsureFonts();
        var attendees = parts.Where(p => p.Kind == MinutesParticipantKind.Attendee).Select(p => p.Name).ToList();
        var absent = parts.Where(p => p.Kind == MinutesParticipantKind.Absent).Select(p => p.Name).ToList();
        var number = FaDigits(m.Id.ToString());

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.ContentFromRightToLeft();
                page.DefaultTextStyle(x => x.FontFamily(Fnt).FontSize(10.5f).FontColor(Ink).LineHeight(1.5f));
                page.Header().Column(h =>
                {
                    h.Item().Height(6).Background(Brand);
                    h.Item().PaddingTop(8).Text(string.IsNullOrWhiteSpace(companyName) ? "صورتجلسه" : companyName)
                        .FontFamily(FntBold).FontSize(12).FontColor(BrandDark);
                    h.Item().PaddingTop(4).PaddingBottom(6).LineHorizontal(0.6f).LineColor(Line);
                });
                page.Content().PaddingTop(8).Column(col =>
                {
                    col.Item().Text("صورتجلسه").FontFamily(FntBold).FontSize(9).FontColor(Brand);
                    col.Item().PaddingTop(2).Text(string.IsNullOrWhiteSpace(m.Title) ? "بدون عنوان" : m.Title)
                        .FontFamily(FntBold).FontSize(15);
                    col.Item().PaddingTop(4).Text($"شماره {number}    ·    جلسه {FaDatePretty(m.MeetingDate)}    ·    ثبت {FaDatePretty(m.DateRegistered)}    ·    {MeetingMinutesStatus.ToFa(m.Status)}")
                        .FontSize(9).FontColor(Muted);
                    col.Item().PaddingTop(8).Text("حاضرین: " + (attendees.Count > 0 ? string.Join("، ", attendees) : "—"));
                    if (absent.Count > 0)
                        col.Item().Text("غایبین: " + string.Join("، ", absent)).FontColor(Muted);
                    col.Item().PaddingTop(8).LineHorizontal(0.6f).LineColor(Line);
                    if (items.Count == 0)
                        col.Item().PaddingTop(8).Text("بندی ثبت نشده است.").FontColor(Muted);
                    foreach (var it in items)
                    {
                        col.Item().PaddingTop(8).Text($"{FaDigits(it.RowNo.ToString())}.  {it.Description}").FontFamily(FntBold).FontSize(10.5f);
                        col.Item().Text($"انجام: {it.ResponsibleName ?? "—"}  ·  {FaDatePretty(it.DueDate)}      پیگیری: {it.FollowUpName ?? "—"}  ·  {FaDatePretty(it.FollowUpDate)}      {MinutesItemStatus.ToFa(it.ItemStatus)}")
                            .FontSize(8.5f).FontColor(Muted);
                    }
                });
            });
        });
        return doc.GeneratePdf();
    }

    /// <summary>قرار دادن صفحات روی سربرگ (PDF یا تصویر)</summary>
    private static byte[] OverlayOnLetterhead(byte[] contentPdf, string letterheadPath)
    {
        using var contentStream = new MemoryStream(contentPdf);
        using var contentDoc = PdfReader.Open(contentStream, PdfDocumentOpenMode.Import);

        using var output = new PdfDocument();

        var isPdf = letterheadPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        using var letterheadForm = isPdf ? XPdfForm.FromFile(letterheadPath) : null;
        using var letterheadImg = !isPdf ? XImage.FromFile(letterheadPath) : null;

        using var contentFormStream = new MemoryStream(contentPdf);
        using var contentForm = XPdfForm.FromStream(contentFormStream);

        for (int i = 0; i < contentDoc.PageCount; i++)
        {
            var srcPage = contentDoc.Pages[i];
            var page = output.AddPage();
            page.Width = srcPage.Width;
            page.Height = srcPage.Height;

            using var gfx = XGraphics.FromPdfPage(page);
            var rect = new XRect(0, 0, page.Width.Point, page.Height.Point);

            if (isPdf && letterheadForm != null)
            {
                letterheadForm.PageNumber = 1;
                gfx.DrawImage(letterheadForm, rect);
            }
            else if (letterheadImg != null)
            {
                gfx.DrawImage(letterheadImg, rect);
            }

            contentForm.PageNumber = i + 1;
            gfx.DrawImage(contentForm, rect);
        }

        using var ms = new MemoryStream();
        output.Save(ms);
        return ms.ToArray();
    }
}
