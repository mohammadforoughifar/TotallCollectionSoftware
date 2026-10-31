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
//  • جای عنوان، شماره صورتجلسه، تاریخ جلسه، پیوست، حاضرین و غایبین از خودِ فایل طرح
//    شرکت خوانده می‌شود: Reports/MeetingMinutes/Companies/{CompanyId}/Minutes-{A4|A5}.mrt
//    (همان کاری که برای نامهٔ داخلی و نامهٔ صادره انجام شد).
//  • شماره/تاریخ/پیوست در کادرهای چاپ‌شدهٔ سربرگ می‌نشینند (لایهٔ Foreground، مختصات مطلق)
//    و بدنه از عنوان شروع می‌شود: عنوان زیر مستطیل آبی و راست‌چین، سپس حاضرین، غایبین و
//    بندها به‌صورت فهرست شماره‌دار (بدون جدول).
//  • اگر قالب نبود، چیدمان قبلی کد استفاده می‌شود؛ اگر سربرگی هم نبود، نسخهٔ ساده.
//  • امضای حاضرین: تصویر امضای ثبت‌شده در پنجرهٔ canvas اگر کاربر امضا کرده باشد، وگرنه
//    کادر خالی برای امضای دستی روی برگه («در انتظار امضا» روی کاغذ نمی‌آید).
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
        // صورتجلسه فیلد «پیوست» ندارد؛ «دارد/ندارد» از پیوست‌های ثبت‌شدهٔ همین صورتجلسه
        // (AppAttachments با Module=MeetingMinutes) شمرده می‌شود — همان قاعدهٔ فهرست صورتجلسه‌ها.
        var attachmentCount = await _db.AppAttachments.AsNoTracking()
            .CountAsync(a => a.Module == "MeetingMinutes" && a.RefId == minutesId);

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
        // اعداد «کاغذ» (رزرو پابرگ/سربرگ، ستون متا، چیدمان امضا) به‌ازای طرح سربرگ همان شرکت.
        var letterheadProfile = MinutesLetterheadProfile.For(companyId, isA5);
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
                    letterheadPath != null, attachmentCount, letterheadProfile)
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
    /// چاپ صورتجلسه با هندسهٔ قالب شرکت:
    /// • شماره، تاریخ جلسه و «پیوست» داخل کادرهای چاپ‌شدهٔ سربرگ (بالای صفحه) می‌نشینند —
    ///   در لایهٔ Foreground و با مختصات مطلقِ خودِ قالب.
    /// • عنوان صورتجلسه زیر مستطیل/روبان آبی سربرگ و راست‌چین است (درخواست کارفرما).
    /// • حاضرین، غایبین و بندها به‌صورت جاری بعد از عنوان؛ برچسب هر بخش چسبیده به مقدار و
    ///   بندها فهرستِ شماره‌دار ساده، بدون جدول.
    /// • امضای حاضرین در جریان صفحه (نه چسبیده به پایین) و یکجا (ShowEntire) می‌ماند؛ اگر
    ///   کاربر در پنجرهٔ canvas امضا کرده باشد همان تصویر چاپ می‌شود، وگرنه کادر خالی برای
    ///   امضای دستی. متن «در انتظار امضا» روی کاغذ نمی‌آید.
    /// چون همه‌چیز در جریان عادی صفحه است، صورتجلسهٔ بلند به صفحه‌های بعد می‌رود و متن از
    /// زیر نوار آبی/بالای پابرگ بیرون نمی‌زند.
    /// </summary>
    private byte[] BuildTemplateContentPdf(
        MeetingMinutes m,
        List<MeetingMinutesParticipant> parts,
        List<MeetingMinutesItem> items,
        MeetingMinutesDesignLayout design,
        bool isA5,
        string? companyName,
        bool hasLetterhead,
        int attachmentCount,
        MinutesLetterheadProfile letterheadProfile)
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
        // صورتجلسه فیلد پیوست ندارد؛ شمارش از AppAttachments می‌آید (Module=MeetingMinutes).
        var attachmentText = attachmentCount > 0 ? "دارد" : "ندارد";

        var signatureImages = new Dictionary<int, byte[]>();
        foreach (var p in attendees)
        {
            var png = TryDecodeSignaturePng(p.SignatureData);
            if (png != null) signatureImages[p.Id] = png;
        }

        var titleBox = design.Title;
        var itemsCaption = design.Caption(MeetingMinutesDesignLayout.Slot.Items);

        var pageWidth = design.PageWidth > 0 ? design.PageWidth : (isA5 ? 419.53f : 595.28f);
        var flowBox = design.Items ?? design.Absentees;
        var flowLeft = flowBox?.Left ?? 28f;
        var flowWidth = flowBox?.Width ?? (pageWidth - 2f * flowLeft);

        // رزرو پابرگ/سربرگ از پروفایل همان طرح سربرگ می‌آید (شرکت ۱: ۲۰۶/۱۲۱pt در A4).
        var footerReserve = hasLetterhead ? letterheadProfile.FooterReserve : (isA5 ? 24f : 32f);
        // سربرگ هر صفحه (نوار آبی/تصویر) چقدر از بالا را می‌گیرد؟ صفحهٔ اول با فاصلهٔ خودِ قالب
        // تنظیم می‌شود، ولی صفحهٔ دوم به بعد باید از زیر سربرگ شروع کند، وگرنه متن روی آن می‌رود.
        var headerReserve = hasLetterhead ? letterheadProfile.HeaderReserve : 0f;

        // ── متای سربرگ: شماره / تاریخ / پیوست روی برچسب‌های چاپ‌شدهٔ سربرگ ──
        var metaRows = BuildHeaderMetaRows(design, number, meetingDate, attachmentText, hasLetterhead);
        var metaBottom = metaRows.Count == 0 ? 0f : metaRows.Max(row => row.Top + row.Height);

        // عنوان: از قالب (۱۹۳pt در A4). بدون سربرگ، متا بالای صفحه است و عنوان بعد از آن می‌آید.
        var titleTop = titleBox?.Top ?? 96f;
        if (!hasLetterhead) titleTop = Math.Max(24f, metaBottom + 24f);

        // فاصلهٔ هر بخش تا بخش بعدی از دل قالب خوانده می‌شود؛ مقادیر جای‌گزین برای قالب‌های ناقص.
        var fallbackGap = isA5 ? 3f : 4f;
        var captionGap = isA5 ? 4f : 5f;
        var itemGap = isA5 ? 3f : 4f;
        var attendeesGap = GapAfter(design.Attendees, design.Absentees);
        var absenteesGap = GapAfter(design.Absentees, itemsCaption);
        var itemsGap = itemsCaption != null ? GapAfter(itemsCaption, design.Items) : fallbackGap;

        // شمارهٔ صفحه چاپ نمی‌شود (درخواست کارفرما)؛ بنابراین هیچ جزء پویایی در چاپ نیست.
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

                // یک نوار خالی هم‌اندازهٔ ناحیهٔ سربرگ؛ روی صفحه‌های دوم به بعد متن را از زیر
                // نوار آبی دور نگه می‌دارد.
                page.Header().Height(headerReserve);

                // مقادیر سربرگ در لایهٔ سراسری صفحه و با مختصات مطلق، روی برچسب‌های چاپ‌شده.
                page.Foreground().ContentFromLeftToRight()
                    .Element(host => DrawHeaderMeta(host, metaRows, letterheadProfile.MetaColumnLeft));

                page.ContentFromRightToLeft();
                page.Content().Column(col =>
                {
                    // ── عنوان صورتجلسه: راست‌چین، درست زیر مستطیل آبی سربرگ ──
                    Space(col, titleTop - headerReserve);
                    if (titleBox is { } title)
                    {
                        var titleText = string.IsNullOrWhiteSpace(m.Title) ? "بدون عنوان" : m.Title;
                        col.Item().MinHeight(title.Height).Element(cell => Write(cell, title, titleText));
                    }

                    // ── حاضرین و غایبین: برچسب چسبیده به نام‌ها، بدون ستون پهن ──
                    Pair(col, MeetingMinutesDesignLayout.Slot.Attendees, attendeeText, attendeesGap);
                    Pair(col, MeetingMinutesDesignLayout.Slot.Absentees, absentText, absenteesGap);

                    // ── بندها و تصمیمات: فهرست شماره‌دار ساده (بدون جدول) ──
                    if (itemsCaption is { } itemsLabel)
                    {
                        col.Item().PaddingBottom(itemsGap)
                            .MinHeight(Math.Max(12f, itemsLabel.Height))
                            .Element(cell => Write(cell, itemsLabel, NormalizeCaption(itemsLabel.Text)));
                    }
                    else
                    {
                        col.Item().PaddingBottom(itemsGap)
                            .Text(LetterPrintTemplate.RtlLabel("بندها و تصمیمات"))
                            .FontFamily(FntBold).FontSize(isA5 ? 9f : 10.5f).FontColor(BrandDark)
                            .DirectionFromRightToLeft();
                    }

                    if (items.Count == 0)
                    {
                        col.Item().Text("بندی ثبت نشده است.")
                            .FontSize(isA5 ? 8f : 9f).FontColor(Muted)
                            .DirectionFromRightToLeft();
                    }
                    else
                    {
                        foreach (var item in items) ItemRow(col, item);
                    }

                    // ── محل امضای حاضرین ──
                    SignatureBlock(col, attendees, signatureImages);
                });
            });
        });

        _ = companyName; // نام شرکت روی خودِ سربرگ چاپ شده است؛ در متن تکرار نمی‌شود

        return doc.GeneratePdf();

        // ── فاصلهٔ خالی به اندازهٔ اختلاف مختصات قالب ──
        static void Space(ColumnDescriptor col, float height)
        {
            if (height > 0.5f) col.Item().Height(height);
        }

        // ── فاصلهٔ پیش‌بینی‌شده در طرح بین دو کادر (۲ تا ۲۴pt) ──
        float GapAfter(MeetingMinutesDesignLayout.Box? current, MeetingMinutesDesignLayout.Box? next)
        {
            if (current == null || next == null) return fallbackGap;
            return Math.Clamp(next.Top - (current.Top + current.Height), 2f, 24f);
        }

        // ── یک کادر متنی با فونت/رنگ/اندازهٔ قالب ──
        // تراز افقی در چیدمان راست‌به‌چپِ استیمول‌سافت آینه می‌شود و با آنچه روی کاغذ می‌بینیم
        // یکی نیست؛ چون کارفرما «راست‌چین بودن» را صریح خواسته، تراز را خودِ چاپ تعیین می‌کند.
        void Write(IContainer cell, MeetingMinutesDesignLayout.Box box, string value)
        {
            var family = box.Bold ? FntBold : Fnt;
            cell.AlignMiddle().Text(text =>
            {
                text.DefaultTextStyle(style => style
                    .FontFamily(family)
                    .FontSize(box.FontSize)
                    .FontColor(box.Color)
                    .LineHeight(1.5f)
                    .DirectionFromRightToLeft());
                text.AlignRight();
                text.Span(value);
            });
        }

        // ── برچسب + مقدار: برچسب به عرض طبیعیِ متنِ خودش (نه ستون ثابت) ──
        void Pair(ColumnDescriptor col, MeetingMinutesDesignLayout.Slot slot, string value, float gapAfter)
        {
            var found = slot switch
            {
                MeetingMinutesDesignLayout.Slot.Attendees => design.Attendees,
                _ => design.Absentees
            };
            // متغیر الگو (غیرnullable) تا داخل lambda هم هشدار nullable ندهد.
            if (found is not { } valueBox) return;

            var caption = design.Caption(slot);
            var line = col.Item().PaddingBottom(gapAfter).MinHeight(Math.Max(10f, valueBox.Height));

            if (caption is { } captionBox)
            {
                line.Row(row =>
                {
                    // در Row راست‌به‌چپ، نخستین آیتم سمت راست صفحه می‌نشیند = جای برچسب در قالب.
                    row.AutoItem().PaddingLeft(captionGap)
                        .Element(cell => Write(cell, captionBox, NormalizeCaption(captionBox.Text)));
                    row.RelativeItem().Element(cell => Write(cell, valueBox, value));
                });
            }
            else
            {
                line.Element(cell => Write(cell, valueBox, value));
            }
        }

        // ── یک بند: شماره + شرح + مسئول/پیگیری/وضعیت، در جریان عادی متن ──
        void ItemRow(ColumnDescriptor col, MeetingMinutesItem item)
        {
            var details = new List<string>();
            if (!string.IsNullOrWhiteSpace(item.ResponsibleName))
                details.Add($"مسئول: {item.ResponsibleName}");
            if (!string.IsNullOrWhiteSpace(item.FollowUpName))
                details.Add($"پیگیری: {item.FollowUpName}");

            var status = MinutesItemStatus.ToFa(item.ItemStatus);
            var statusColor = item.ItemStatus == MinutesItemStatus.Done ? "#166534"
                : item.ItemStatus == MinutesItemStatus.Rejected ? "#991b1b" : BrandDark;
            var numberText = LetterPrintTemplate.PersianDigits(item.RowNo.ToString(CultureInfo.InvariantCulture));

            col.Item().PaddingBottom(itemGap).Row(line =>
            {
                line.ConstantItem(isA5 ? 13f : 15f).AlignTop()
                    .Text(numberText + ".")
                    .FontFamily(FntBold).FontSize(isA5 ? 8f : 9f).FontColor(Brand);

                line.RelativeItem().Text(text =>
                {
                    text.DefaultTextStyle(style => style
                        .FontFamily(Fnt)
                        .FontSize(isA5 ? 8.5f : 9.5f)
                        .FontColor(Ink)
                        .LineHeight(1.75f)
                        .DirectionFromRightToLeft());
                    text.AlignRight();
                    text.Span(item.Description ?? string.Empty);
                    if (details.Count > 0)
                        text.Span(" — " + string.Join("، ", details))
                            .FontSize(isA5 ? 7.5f : 8.5f).FontColor(Muted);
                    text.Span(" " + status)
                        .FontFamily(FntBold).FontSize(isA5 ? 7.5f : 8.5f).FontColor(statusColor);
                });
            });
        }

        // ── محل امضای حاضرین: کادر خالی برای امضای دستی، تصویر امضا برای امضاشده‌ها ──
        void SignatureBlock(ColumnDescriptor col, List<MeetingMinutesParticipant> people,
            Dictionary<int, byte[]> images)
        {
            if (people.Count == 0) return;

            // چیدمان کادرهای امضا از پروفایل طرح سربرگ: ۱۰۰۳ در A5 دو کادر، شرکت ۱ در هر دو
            // کاغذ سه کادر (A5: ۱۱۱×۳۸pt) تا پنج حاضر در یک صفحه جا شوند.
            var perRow = letterheadProfile.SignatureColumns;
            var boxHeight = letterheadProfile.SignatureBoxHeight;
            var cellGap = isA5 ? 6f : 8f;
            // A4: سه کادر ۱۶۸pt در ردیف؛ A5 باریک‌تر است و کادرها عرض مساوی می‌گیرند.
            var cellWidth = isA5 ? (flowWidth - cellGap * (perRow - 1)) / perRow : 168f;

            // ShowEntire = page-break-inside: avoid؛ بلوک امضا نصفه به صفحهٔ بعد نمی‌رود.
            col.Item().PaddingTop(isA5 ? 10f : 14f).ShowEntire().Column(block =>
            {
                block.Item().PaddingBottom(isA5 ? 4f : 6f)
                    .Text(LetterPrintTemplate.RtlLabel("محل امضای حاضرین"))
                    .FontFamily(FntBold).FontSize(isA5 ? 9f : 10.5f).FontColor(BrandDark)
                    .DirectionFromRightToLeft();

                for (var start = 0; start < people.Count; start += perRow)
                {
                    var group = people.Skip(start).Take(perRow).ToList();
                    block.Item().PaddingBottom(cellGap).Row(cells =>
                    {
                        for (var index = 0; index < perRow; index++)
                        {
                            var position = index;
                            cells.ConstantItem(Math.Max(60f, cellWidth)).Element(cell =>
                            {
                                if (position >= group.Count) return;
                                var person = group[position];
                                cell.PaddingLeft(position == perRow - 1 ? 0f : cellGap)
                                    .Column(card =>
                                    {
                                        if (images.TryGetValue(person.Id, out var signature))
                                        {
                                            // امضای ثبت‌شده در پنجرهٔ canvas: خودِ تصویر، بدون کادر.
                                            card.Item().Height(boxHeight).AlignCenter().AlignMiddle()
                                                .Image(signature).FitArea();
                                        }
                                        else
                                        {
                                            // کادر خالی برای امضای دستی روی برگه.
                                            card.Item().Height(boxHeight)
                                                .Border(0.5f).BorderColor("#cbd5e1").Background(Colors.White)
                                                .AlignCenter().AlignMiddle()
                                                .Text("محل امضای دستی")
                                                .FontSize(isA5 ? 6.5f : 7.5f).FontColor("#b6c2d2")
                                                .DirectionFromRightToLeft();
                                        }

                                        card.Item().PaddingTop(3).AlignCenter().Text(person.Name)
                                            .FontFamily(FntBold).FontSize(isA5 ? 7.5f : 8.5f)
                                            .FontColor(Ink).DirectionFromRightToLeft();

                                        // «در انتظار امضا» روی کاغذ نمی‌آید؛ فقط تاریخ امضای
                                        // ثبت‌شده می‌ماند (سندِ امضای الکترونیکی در گردش کار).
                                        if (person.SignedAt != null)
                                        {
                                            card.Item().AlignCenter()
                                                .Text("امضا شد: " + LetterPrintTemplate.PersianDigits(
                                                    FaDate(person.SignedAt)))
                                                .FontSize(isA5 ? 6.5f : 7f).FontColor("#166534");
                                        }
                                    });
                            });
                        }
                    });
                }
            });
        }
    }

    /// <summary>یک کادر متای سربرگ با مقدار نهایی‌اش (مختصات مطلق صفحه، point).</summary>
    private sealed record HeaderMetaRow(float Left, float Top, float Width, float Height, string Value,
        string Color, float FontSize, string? Fill);

    /// <summary>
    /// کادرهای شماره/تاریخ/پیوست از خودِ قالب: مقدار در همان جای برچسبِ چاپ‌شدهٔ سربرگ
    /// («شماره»، «تاریخ»، «پیوست») می‌نشیند. اگر قالب کادر پیوست را نداشته باشد، پیوست چاپ
    /// نمی‌شود (قالب‌های قدیمیِ نصب‌شده نباید چاپ را بشکنند).
    /// </summary>
    private static List<HeaderMetaRow> BuildHeaderMetaRows(MeetingMinutesDesignLayout design,
        string number, string date, string attachment, bool hasLetterhead)
    {
        var rows = new List<HeaderMetaRow>();

        void Add(MeetingMinutesDesignLayout.Box? box, string value)
        {
            if (box == null || string.IsNullOrWhiteSpace(value)) return;
            rows.Add(new HeaderMetaRow(box.Left, box.Top, box.Width, box.Height, value,
                hasLetterhead ? box.Color : Ink, box.FontSize, box.Fill));
        }

        Add(design.Number, number);
        Add(design.MeetingDate, date);
        Add(design.Attachment, attachment);
        return rows.OrderBy(row => row.Top).ToList();
    }

    /// <summary>
    /// چاپ مقادیر سربرگ در مختصات مطلق صفحه (لایهٔ Foreground) — همان روش نامهٔ داخلی/صادرهٔ
    /// ۱۰۰۳: ستون مقدارها راست‌چین است و ۴pt پیش از برچسب چاپ‌شده تمام می‌شود. عرض ستون با
    /// PaddingLeft + Width تعیین می‌شود (نه Row) تا در چیدمان راست‌به‌چپ آینه نشود.
    /// </summary>
    private static void DrawHeaderMeta(IContainer host, IReadOnlyList<HeaderMetaRow> rows,
        float columnFloor = 0f)
    {
        if (rows.Count == 0) return;

        var right = rows.Min(row => row.Left + row.Width) - 4f;
        // لبهٔ چپ ستون: کادرِ اعلام‌شده در قالب، ولی نه نزدیک‌تر از «کف» طرح (برای ۱۰۰۳ همان
        // حاشیهٔ ۱۵ میلی‌متری قبلی است؛ شرکت ۱ کادر پهن‌ترِ خودش را روی نقطه‌چین دارد).
        var left = Math.Max(rows.Min(row => row.Left), columnFloor);
        // در A5 که صفحه باریک است، ستون امن فقط تا جایی بزرگ می‌شود که مقدار خوانا بماند.
        if (right - left < 44f) left = Math.Max(0f, right - 44f);
        var width = Math.Max(20f, right - left);

        host.AlignTop().Column(column =>
        {
            var cursor = 0f;
            foreach (var row in rows.OrderBy(row => row.Top))
            {
                var gap = row.Top - cursor;
                if (gap > 0.5f) column.Item().Height(gap);

                var height = Math.Max(10f, row.Height);
                var cell = column.Item().Height(height)
                    .PaddingLeft(Math.Max(0f, left))
                    .AlignLeft()
                    .Width(width);
                // قالب اگر رنگ پس‌زمینه اعلام کرده باشد (شرکت ۱: سفید)، همان نوار پشت مقدار
                // کشیده می‌شود تا نقطه‌چین سربرگ متن را ناخوانا نکند.
                if (!string.IsNullOrWhiteSpace(row.Fill)) cell = cell.Background(row.Fill!);
                cell.AlignMiddle()
                    .AlignRight()
                    .Text(LetterPrintTemplate.IsolateMixedText(row.Value))
                    .FontFamily(FntBold)
                    .FontSize(FitMetaFontSize(row.Value, width, row.FontSize))
                    .FontColor(row.Color)
                    .LineHeight(1.15f)
                    .AlignRight()
                    .DirectionFromRightToLeft();
                cursor = row.Top + height;
            }
        });
    }

    /// <summary>بزرگ‌ترین اندازه‌ای که مقدار سربرگ در یک خط جا می‌شود (کف ۷pt، مثل نامهٔ صادره).</summary>
    private static float FitMetaFontSize(string text, float maxWidth, float preferred, float minimum = 7f)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0f || preferred <= 0f) return preferred > 0f ? preferred : 10f;
        var estimated = text.Length * preferred * 0.58f;
        return estimated <= maxWidth ? preferred : Math.Max(minimum, preferred * maxWidth / estimated);
    }

    /// <summary>
    /// برچسب ذخیره‌شده در قالب («:حاضرین») → متن قابل چاپ («حاضرین:»).
    /// دونقطه در پایانِ منطقی می‌نشیند و با RLM محصور می‌شود تا در چاپ «:حاضرین» دیده نشود
    /// (ایراد کارفرما: «موضوع:» به‌صورت «:موضوع» چاپ می‌شد). دونقطه‌ای که در طرح نبوده
    /// اضافه نمی‌شود؛ فقط ترتیب نمایش اصلاح می‌شود.
    /// </summary>
    private static string NormalizeCaption(string text) =>
        LetterPrintTemplate.RtlLabel(text, ensureTrailingColon: false);

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

                // پابرگ: فقط نام شرکت؛ شمارهٔ صفحه چاپ نمی‌شود (درخواست کارفرما).
                page.Footer().PaddingHorizontal(reserveLetterhead ? 0 : 28).Column(f =>
                {
                    f.Item().LineHorizontal(0.7f).LineColor(Line);
                    f.Item().PaddingTop(5).Text(string.IsNullOrWhiteSpace(companyName) ? "صورتجلسه" : companyName)
                        .FontSize(8).FontColor("#94a3b8");
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
