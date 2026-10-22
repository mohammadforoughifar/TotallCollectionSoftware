using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Inventory.Api.Services.Office.Outgoing;

// ============================================================
//  چاپ نامه صادره روی سربرگ شرکت — سایز A4 و A5
//  • منبع سربرگ به ترتیب:
//    ۱) فایل ثبت‌شده در SystemCompanies.LetterheadFileName (نسبت به wwwroot)
//    ۲) طرح سربرگ جاسازی‌شده در قالب چاپ خودِ شرکت
//       (Reports/OutgoingLetters/Companies/{CompanyId}/Outgoing-{Size}.mrt —
//        Watermark.ImageBytes)؛ این طرح همیشه همراه خروجی سرور منتشر می‌شود
//        و چاپ را از تنظیمات بانک اطلاعاتی مستقل می‌کند.
//    ۳) سربرگ فوروث / لوگوی HR — فقط برای نامه‌های بدون شرکت.
//  • متن نامه با QuestPDF (فونت فارسی وزیرمتن، راست‌به‌چپ) تولید و سپس
//    با PDFsharp روی صفحه سربرگ قرار می‌گیرد (Overlay).
//  • نامه‌ای که «شرکت» دارد هرگز بدون سربرگ (روی صفحهٔ سفید) چاپ نمی‌شود؛
//    اگر سربرگ پیدا نشود یا اعمال آن شکست بخورد، چاپ با پیام دقیق متوقف می‌شود.
// ============================================================

public interface IOutgoingLetterPrintService
{
    /// <summary>
    /// تولید PDF نامه صادره روی سربرگ شرکت
    /// </summary>
    /// <param name="letterId">شناسه نامه صادره</param>
    /// <param name="size">"A4" یا "A5"</param>
    /// <param name="withCopy">
    /// نسخهٔ چاپ: true = «با رونوشت» (بلوک رونوشت چاپ می‌شود)،
    /// false = «بدون رونوشت» (بلوک رونوشت حذف می‌شود — برای نسخه‌ای که به
    /// سازمان مقصد تحویل داده می‌شود و نیازی به اطلاع رونوشت‌گیرندگان ندارد)
    /// </param>
    Task<byte[]?> GeneratePdfAsync(int letterId, string size, bool withCopy = true);
}

/// <summary>Raised when a company-required letterhead cannot be resolved or applied.</summary>
public sealed class RequiredCompanyLetterheadPrintException : Exception
{
    public RequiredCompanyLetterheadPrintException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public class OutgoingLetterPrintService : IOutgoingLetterPrintService
{
    // These companies must use their own registered letterheads; never substitute Forough's.
    private const int HomepaCompanyId = 1003;
    private const int AriaCompanyId = 1005;

    private static bool _fontsRegistered;
    private static readonly object FontLock = new();

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<OutgoingLetterPrintService> _logger;
    private readonly IConfiguration _configuration;

    public OutgoingLetterPrintService(
        AppDbContext db,
        IWebHostEnvironment env,
        ILogger<OutgoingLetterPrintService> logger,
        IConfiguration? configuration = null)
    {
        _db = db;
        _env = env;
        _logger = logger;
        _configuration = configuration ?? new ConfigurationBuilder().Build();
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // ==================== فونت فارسی ====================

    private static string FontDir => Path.Combine(AppContext.BaseDirectory, "Resources", "fonts");

    private static void EnsureFonts()
    {
        if (_fontsRegistered) return;
        lock (FontLock)
        {
            if (_fontsRegistered) return;
            var reg = Path.Combine(FontDir, "Vazirmatn-Regular.ttf");
            var bold = Path.Combine(FontDir, "Vazirmatn-Bold.ttf");
            if (File.Exists(reg)) FontManager.RegisterFontWithCustomName("Vazirmatn", File.OpenRead(reg));
            if (File.Exists(bold)) FontManager.RegisterFontWithCustomName("Vazirmatn-Bold", File.OpenRead(bold));
            _fontsRegistered = true;
        }
    }

    // ==================== سربرگ (PDF از مسیر روت API) ====================

    /// <summary>یافتن سربرگ نسبت به wwwroot، با پشتیبانی از نام فایل‌های قدیمی</summary>
    private string? ResolveLetterheadPath(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var clean = fileName.Replace('\\', '/').TrimStart('/');
        if (clean.Contains("..")) return null;

        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var uploadClean = clean.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase)
            ? clean["uploads/".Length..]
            : clean;

        var uploadFileName = Path.GetFileName(uploadClean);
        var candidates = new[]
        {
            // Supports canonical values such as uploads/letterheads/name.pdf.
            Path.Combine(webRoot, "uploads", uploadClean),
            // Older company records may store only the filename. Letterheads live
            // in this subfolder, not directly under wwwroot/uploads.
            Path.Combine(webRoot, "uploads", "letterheads", uploadFileName),
            Path.Combine(webRoot, clean),
            Path.Combine(_env.ContentRootPath, "wwwroot", "uploads", "letterheads", uploadFileName),
            Path.Combine(_env.ContentRootPath, clean),
            Path.Combine(_env.ContentRootPath, Path.GetFileName(clean)),
            Path.Combine(AppContext.BaseDirectory, "wwwroot", "uploads", "letterheads", uploadFileName),
            Path.Combine(AppContext.BaseDirectory, clean),
            Path.Combine(AppContext.BaseDirectory, Path.GetFileName(clean))
        };

        var found = candidates.FirstOrDefault(File.Exists);
        if (found == null) return null;

        // Only renderable letterhead files (PDF/image) are accepted. Values that
        // point at an MRT report template or any other file type are ignored —
        // the template's embedded design is used through ExtractTemplateLetterhead.
        var extension = Path.GetExtension(found).ToLowerInvariant();
        return extension is ".pdf" or ".png" or ".jpg" or ".jpeg" ? found : null;
    }

    /// <summary>
    /// فایل سربرگ ثبت‌شده روی رکورد شرکت — بدون جانشین‌سازی. مقدار نامعتبر
    /// (مسیر قالب MRT، فایل غایب یا نوع غیرقابل‌رندر) نادیده گرفته می‌شود تا
    /// سربرگ جاسازی‌شده در قالب همان شرکت جایگزین شود.
    /// </summary>
    private string? ResolveRegisteredLetterheadFile(SystemCompany? company)
        => ResolveLetterheadPath(company?.LetterheadFileName);

    /// <summary>برای پیام خطا: فایل ثبت‌شده به هر نوعی که باشد، اگر جایی موجود است مسیرش برمی‌گردد.</summary>
    private string? TryResolveAnyPath(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var clean = fileName.Replace('\\', '/').TrimStart('/');
        if (clean.Contains("..")) return null;

        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var uploadClean = clean.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase)
            ? clean["uploads/".Length..]
            : clean;
        var uploadFileName = Path.GetFileName(uploadClean);
        var candidates = new[]
        {
            Path.Combine(webRoot, "uploads", uploadClean),
            Path.Combine(webRoot, "uploads", "letterheads", uploadFileName),
            Path.Combine(webRoot, clean),
            Path.Combine(_env.ContentRootPath, clean),
            Path.Combine(AppContext.BaseDirectory, clean)
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    // ==================== سربرگ جاسازی‌شده در قالب MRT شرکت ====================

    /// <summary>
    /// طرح سربرگ طراحی‌شدهٔ هر شرکت داخل فایل قالب چاپ همان شرکت
    /// (Reports/OutgoingLetters/Companies/{CompanyId}/Outgoing-{Size}.mrt)
    /// به‌صورت Watermark.ImageBytes ذخیره شده است. این متد همان تصویر را
    /// استخراج و در پوشهٔ موقت کش می‌کند تا نامهٔ هر شرکت روی سربرگ خودش
    /// چاپ شود — حتی اگر رکورد شرکت در بانک اطلاعاتی سربرگی ثبت نکرده باشد
    /// یا فایل‌های wwwroot روی سرور موجود نباشند (قالب‌ها همراه بیلد منتشر می‌شوند).
    /// </summary>
    private string? ExtractTemplateLetterhead(int companyId, bool isA5)
    {
        var sizes = isA5 ? new[] { "A5", "A4" } : new[] { "A4", "A5" };
        foreach (var templateSize in sizes)
        {
            var templatePath = FindCompanyTemplate(companyId, templateSize);
            if (templatePath == null) continue;

            try
            {
                var watermarkBytes = ReadTemplateWatermark(templatePath);
                if (watermarkBytes is not { Length: > 0 })
                {
                    _logger.LogWarning(
                        "قالب MRT شرکت سربرگ جاسازی‌شده ندارد. CompanyId={CompanyId}, Path={Path}",
                        companyId, templatePath);
                    continue;
                }

                var cachePath = Path.Combine(
                    Path.GetTempPath(), "totall-letterheads",
                    $"company-{companyId}-outgoing-{templateSize.ToLowerInvariant()}.jpg");
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                try
                {
                    File.WriteAllBytes(cachePath, watermarkBytes);
                }
                catch (IOException)
                {
                    // چاپ هم‌زمان دو نامه: فایل کش ممکن است در استفاده باشد — با نام یکتا بنویس.
                    cachePath = Path.Combine(
                        Path.GetDirectoryName(cachePath)!,
                        $"company-{companyId}-outgoing-{templateSize.ToLowerInvariant()}-{Guid.NewGuid():N}.jpg");
                    File.WriteAllBytes(cachePath, watermarkBytes);
                }

                _logger.LogInformation(
                    "سربرگ طراحی‌شده از قالب MRT شرکت استخراج شد. CompanyId={CompanyId}, TemplateSize={TemplateSize}, TemplatePath={TemplatePath}, Bytes={Bytes}",
                    companyId, templateSize, templatePath, watermarkBytes.Length);
                return cachePath;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "استخراج سربرگ از قالب MRT شرکت ناموفق بود. CompanyId={CompanyId}, Path={Path}",
                    companyId, templatePath);
            }
        }

        return null;
    }

    private string? FindCompanyTemplate(int companyId, string size)
    {
        // The active QuestPDF pipeline must honor the same template-root setting as the legacy renderer.
        var configuredRoot = _configuration["Reporting:Stimulsoft:TemplateRoot"];
        var templateRoot = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine("Reports", "OutgoingLetters", "Companies")
            : configuredRoot.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var template = Path.Combine(templateRoot,
            companyId.ToString(CultureInfo.InvariantCulture), $"Outgoing-{size}.mrt");
        var roots = new[] { _env.ContentRootPath, AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        var candidates = Path.IsPathRooted(templateRoot)
            ? new[] { template }
            : roots.Select(root => Path.Combine(root, template)).Distinct().ToArray();
        var found = candidates.FirstOrDefault(File.Exists);
        if (found == null)
            _logger.LogDebug("قالب سربرگ شرکت یافت نشد. CompanyId={CompanyId}, Size={Size}, SearchedPaths={SearchedPaths}",
                companyId, size, string.Join(" | ", candidates));
        return found;
    }

    /// <summary>خواندن Watermark.ImageBytes از فایل قالب MRT (XML استیمول‌سافت).</summary>
    private static byte[]? ReadTemplateWatermark(string templatePath)
    {
        using var stream = File.OpenRead(templatePath);
        var document = XDocument.Load(stream, LoadOptions.None);
        var watermark = document.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "Watermark");
        var imageBytesValue = watermark?.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "ImageBytes")?.Value;
        if (string.IsNullOrWhiteSpace(imageBytesValue)) return null;
        return Convert.FromBase64String(imageBytesValue);
    }

    // ==================== تبدیل HTML ادیتور به متن ساده ====================

    /// <summary>متن نامه در ادیتور غنی HTML است — برای PDF به متن ساده با حفظ پاراگراف‌ها تبدیل می‌شود</summary>
    private static string HtmlToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var text = html;
        text = Regex.Replace(text, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</\s*(p|div|li|h[1-6]|tr)\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<\s*li[^>]*>", "• ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\u00a0', ' ')
            .Replace("\u00ad", string.Empty).Replace("\u200b", string.Empty);
        text = Regex.Replace(text, @"[ \t]+\n", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        // Keep U+200C (ZWNJ) intact for Persian half-spaces; convert all digits for output.
        return ToPersianDigits(text.Trim());
    }

    private static string ToPersianDigits(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;

        var chars = value.ToCharArray();
        for (var index = 0; index < chars.Length; index++)
        {
            if (chars[index] is >= '0' and <= '9')
                chars[index] = (char)('\u06F0' + (chars[index] - '0'));
            else if (chars[index] is >= '\u0660' and <= '\u0669')
                chars[index] = (char)('\u06F0' + (chars[index] - '\u0660'));
        }

        return new string(chars);
    }

    private static string FaDate(DateTime? date)
    {
        if (date is null) return "—";
        var calendar = new PersianCalendar();
        var value = date.Value;
        return ToPersianDigits($"{calendar.GetYear(value)}/{calendar.GetMonth(value):00}/{calendar.GetDayOfMonth(value):00}");
    }

    // ==================== تولید PDF ====================

    /// <summary>
    /// تولید PDF نامه صادره — در دو نسخهٔ «با رونوشت» و «بدون رونوشت».
    /// نسخهٔ بدون رونوشت برای تحویل به سازمان مقصد است؛ نسخهٔ با رونوشت
    /// در پروندهٔ دبیرخانه باقی می‌ماند.
    /// </summary>
    public async Task<byte[]?> GeneratePdfAsync(int letterId, string size, bool withCopy = true)
    {
        // Resolve the letter and its company-registered letterhead before rendering.
        var letter = await _db.OutgoingLetters.AsNoTracking()
            .Include(l => l.Creator)
            .FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete);
        if (letter == null) return null;

        var letterCompanyId = letter.CompanyId;
        var hasCompanyId = letterCompanyId is > 0;
        SystemCompany? company;
        if (hasCompanyId)
        {
            company = await _db.SystemCompanies.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == letterCompanyId);
        }
        else
        {
            // If an old letter has no company, retain the existing default selection.
            company = await _db.SystemCompanies.AsNoTracking()
                .Where(c => c.IsActive && c.LetterheadFileName != null && c.LetterheadFileName != "")
                .OrderBy(c => c.Id)
                .FirstOrDefaultAsync();
        }

        if (!hasCompanyId)
            _logger.LogWarning(
                "نامه {LetterId} CompanyId ندارد؛ برای چاپ از شرکت پیش‌فرض {SelectedCompanyId} استفاده می‌شود.",
                letterId, company?.Id);

        var selectedCompanyId = company?.Id ?? letterCompanyId;
        var companyRecordMissing = hasCompanyId && company == null;
        bool isA5 = string.Equals(size, "A5", StringComparison.OrdinalIgnoreCase);

        // زنجیرهٔ منبع سربرگ:
        // ۱) فایل ثبت‌شده روی رکورد شرکت
        var registeredPath = companyRecordMissing ? null : ResolveRegisteredLetterheadFile(company);

        // ۲) طرح سربرگ جاسازی‌شده در قالب MRT خود شرکت (Reports/OutgoingLetters/Companies/{id})
        //    — هم استخراج می‌شود تا اگر فایل ثبت‌شده خراب بود هم جایگزین باشد.
        var templateLetterheadPath = selectedCompanyId is > 0
            ? ExtractTemplateLetterhead(selectedCompanyId.Value, isA5)
            : null;
        var letterheadPath = registeredPath ?? templateLetterheadPath;
        var letterheadFromTemplate = letterheadPath != null && letterheadPath == templateLetterheadPath;

        // ۳) سربرگ فوروث / لوگوی HR — فقط برای نامه‌های «بدون شرکت».
        if (letterheadPath == null && !hasCompanyId)
        {
            letterheadPath = ResolveLetterheadPath("uploads/letterheads/forough-letterhead.pdf");
            if (letterheadPath == null)
            {
                var hrLogoPath = await _db.HrMainCompanies.AsNoTracking()
                    .Select(c => c.LogoPath)
                    .FirstOrDefaultAsync();
                letterheadPath = ResolveLetterheadPath(hrLogoPath);
            }
        }

        _logger.LogInformation(
            "سربرگ نامه انتخاب شد. LetterId={LetterId}, LetterCompanyId={LetterCompanyId}, SelectedCompanyId={SelectedCompanyId}, ConfiguredPath={ConfiguredPath}, RegisteredPath={RegisteredPath}, TemplatePath={TemplatePath}, FinalPath={FinalPath}, FromTemplate={FromTemplate}, WebRoot={WebRoot}, ContentRoot={ContentRoot}",
            letterId, letterCompanyId, selectedCompanyId, company?.LetterheadFileName, registeredPath,
            templateLetterheadPath, letterheadPath, letterheadFromTemplate, _env.WebRootPath, _env.ContentRootPath);

        // قاعدهٔ سخت‌گیرانه: نامه‌ای که «شرکت» دارد هرگز بدون سربرگ چاپ نمی‌شود.
        // اگر هیچ سربرگی (فایل ثبت‌شده یا طرح قالب MRT) پیدا نشد، چاپ با پیام دقیق متوقف می‌شود
        // تا به‌جای نامهٔ سفید، علت واقعی دیده شود.
        if (letterheadPath == null && hasCompanyId)
        {
            var companyName = string.IsNullOrWhiteSpace(company?.Name)
                ? letterCompanyId?.ToString(CultureInfo.InvariantCulture) ?? "شرکت"
                : company!.Name;
            var configuredStatus = companyRecordMissing
                ? "رکورد شرکت در بانک اطلاعاتی پیدا نشد"
                : string.IsNullOrWhiteSpace(company?.LetterheadFileName)
                    ? "روی رکورد شرکت فایلی ثبت نشده است"
                    : TryResolveAnyPath(company?.LetterheadFileName) is { } existing
                        ? $"فایل ثبت‌شده یافت شد ({existing}) اما نوع قابل‌چاپ نیست؛ فقط PDF/تصویر مجاز است"
                        : "فایل ثبت‌شده روی سرور پیدا نشد";
            var templateStatus = templateLetterheadPath != null
                ? "طرح قالب خوانده شد"
                : "قالب MRT شرکت یافت نشد یا سربرگ جاسازی‌شده ندارد";
            var message =
                $"سربرگ شرکت «{companyName}» پیدا نشد؛ چاپ متوقف شد تا نامه روی صفحهٔ سفید چاپ نشود. " +
                $"وضعیت: {configuredStatus}؛ قالب: {templateStatus}. " +
                $"مسیرهای جستجوی قالب: {_env.ContentRootPath} | {AppContext.BaseDirectory}";
            _logger.LogError(
                "چاپ نامه {LetterId} متوقف شد: سربرگ شرکت {CompanyId} یافت نشد. Configured={Configured}, ContentRoot={ContentRoot}, BaseDirectory={BaseDirectory}, WebRoot={WebRoot}",
                letterId, selectedCompanyId, company?.LetterheadFileName,
                _env.ContentRootPath, AppContext.BaseDirectory, _env.WebRootPath);
            throw new RequiredCompanyLetterheadPrintException(message);
        }

        // Outgoing letters always use QuestPDF. This avoids Stimulsoft trial marks
        // and keeps all Persian shaping/layout on one rendering path.
        var signers = await _db.OutgoingLetterSigners.AsNoTracking()
            .Include(s => s.User)
            .Where(s => s.SourceId == letterId && !s.IsDelete)
            .OrderBy(s => s.Order).ThenBy(s => s.Id)
            .ToListAsync();

        var sematIds = signers.Where(s => s.SematId.HasValue)
            .Select(s => s.SematId!.Value)
            .Distinct()
            .ToList();
        var sematTitles = sematIds.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Semats.AsNoTracking()
                .Where(s => sematIds.Contains(s.SematId) && !s.IsDelete)
                .ToDictionaryAsync(
                    s => s.SematId,
                    s => string.IsNullOrWhiteSpace(s.OnvanMokatebati) ? s.Title : s.OnvanMokatebati!,
                    cancellationToken: default);

        // رونوشت‌گیرندگان از جدول مستقل — منبع اصلی برای چاپ «با رونوشت».
        // برای نامه‌های قدیمی که ردیفی در این جدول ندارند، ستون متنی CopyTo به کار می‌رود.
        var copyTos = await _db.OutgoingLetterCopyToes.AsNoTracking()
            .Where(c => c.OutgoingLetterId == letterId && !c.IsDelete)
            .OrderBy(c => c.RowNo).ThenBy(c => c.Id)
            .ToListAsync();

        var hasAttachment = await _db.AppAttachments.AsNoTracking()
            .AnyAsync(a => a.Module == "OutgoingLetters" && a.RefId == letterId);

        // Keep the content PDF transparent; the company letterhead (registered
        // file or the design embedded in the company's print template) is
        // placed underneath it by OverlayOnLetterhead below.
        var contentPdf = BuildContentPdf(
            letter, signers, sematTitles, hasAttachment, isA5, withCopy, copyTos,
            hasLetterhead: letterheadPath != null,
            letterheadCompanyId: selectedCompanyId);

        // Use the same company record and letterhead path for all rendered pages.
        if (letterheadPath == null)
        {
            // فقط نامه‌های «بدون شرکت» به اینجا می‌رسند (سربرگ فوروث/HR هم موجود نیست)
            // — همان حالت قدیمی: هدر مشکی چاپ می‌شود.
            _logger.LogWarning(
                "سربرگ نامه پیدا نشد. LetterId={LetterId}, CompanyId={CompanyId}, Company={CompanyName}, LetterheadFileName={LetterheadFileName}, ContentRoot={ContentRoot}, WebRoot={WebRoot}, BaseDirectory={BaseDirectory}",
                letterId,
                company?.Id ?? letterCompanyId,
                company?.Name,
                company?.LetterheadFileName,
                _env.ContentRootPath,
                _env.WebRootPath,
                AppContext.BaseDirectory);
            return contentPdf;
        }

        try
        {
            _logger.LogInformation(
                "اعمال سربرگ نامه. LetterId={LetterId}, CompanyId={CompanyId}, Size={Size}, Path={LetterheadPath}, FromTemplate={FromTemplate}",
                letterId, company?.Id ?? letterCompanyId, size, letterheadPath, letterheadFromTemplate);
            var overlaidPdf = OverlayOnLetterhead(contentPdf, letterheadPath);
            _logger.LogInformation(
                "سربرگ روی PDF نامه اعمال شد. LetterId={LetterId}, CompanyId={CompanyId}, Size={Size}, PdfBytes={PdfBytes}",
                letterId, company?.Id ?? letterCompanyId, size, overlaidPdf.Length);
            return overlaidPdf;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "قرار دادن سربرگ روی نامه ناموفق بود. LetterId={LetterId}, CompanyId={CompanyId}, Path={LetterheadPath}, FromTemplate={FromTemplate}",
                letterId, company?.Id ?? letterCompanyId, letterheadPath, letterheadFromTemplate);

            // اگر فایل سربرگ انتخاب‌شده (مثلاً فایل ثبت‌شدهٔ شرکت) قابل رندر نبود،
            // طرح سربرگ جاسازی‌شده در قالب خودِ شرکت را امتحان کن تا نامه بی‌سربرگ نماند.
            if (templateLetterheadPath != null && !letterheadFromTemplate)
            {
                try
                {
                    var retriedPdf = OverlayOnLetterhead(contentPdf, templateLetterheadPath);
                    _logger.LogInformation(
                        "سربرگ قالب MRT شرکت جایگزین شد. LetterId={LetterId}, CompanyId={CompanyId}, Path={TemplatePath}",
                        letterId, company?.Id ?? letterCompanyId, templateLetterheadPath);
                    return retriedPdf;
                }
                catch (Exception retryEx)
                {
                    _logger.LogError(retryEx,
                        "اعمال سربرگ قالب MRT شرکت هم ناموفق بود. LetterId={LetterId}, CompanyId={CompanyId}, Path={TemplatePath}",
                        letterId, company?.Id ?? letterCompanyId, templateLetterheadPath);
                    ex = retryEx;
                }
            }

            // نامهٔ دارای شرکت هرگز بدون سربرگ تحویل نمی‌شود — خطای شفاف به کاربر.
            if (hasCompanyId)
            {
                var companyName = string.IsNullOrWhiteSpace(company?.Name)
                    ? letterCompanyId?.ToString(CultureInfo.InvariantCulture) ?? "شرکت"
                    : company!.Name;
                throw new RequiredCompanyLetterheadPrintException(
                    $"اعمال سربرگ شرکت «{companyName}» ناموفق بود؛ چاپ متوقف شد تا نامه روی صفحهٔ سفید چاپ نشود. جزئیات: {ex.Message}", ex);
            }
            return contentPdf;
        }
    }

    private byte[]? TryReadSignature(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var clean = relativePath.Replace('\\', '/').TrimStart('/');
        if (clean.Contains("..", StringComparison.Ordinal)) return null;
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var path = new[]
        {
            Path.Combine(webRoot, clean),
            Path.Combine(webRoot, "uploads", clean),
            Path.Combine(_env.ContentRootPath, clean),
            Path.Combine(AppContext.BaseDirectory, clean)
        }.FirstOrDefault(File.Exists);
        return path == null ? null : File.ReadAllBytes(path);
    }

    /// <summary>ساخت محتوای نامه با RTL صریح، فونت وزیرمتن و سربرگ ثبت‌شده.</summary>
    private byte[] BuildContentPdf(
        OutgoingLetter letter,
        List<OutgoingLetterSigner> signers,
        IReadOnlyDictionary<int, string> sematTitles,
        bool hasAttachment,
        bool isA5,
        bool withCopy,
        List<OutgoingLetterCopyTo>? copyTos,
        bool hasLetterhead,
        int? letterheadCompanyId)
    {
        EnsureFonts();

        var fontSize = isA5 ? 8f : 10.5f;
        var titleFontSize = isA5 ? 9.5f : 12f;
        var smallFontSize = isA5 ? 7f : 9f;
        var lineHeight = isA5 ? 1.5f : 1.55f;

        // Print the authored body as-is; do not append a generated closing phrase.
        var bodyText = HtmlToPlainText(letter.Text);

        var number = ToPersianDigits(
            string.IsNullOrWhiteSpace(letter.SadereNumber)
                ? letter.LetterNumber ?? "—"
                : letter.SadereNumber);
        var date = FaDate(letter.DateSadere ?? letter.DateSabt);
        var receiverOrganization = ToPersianDigits(letter.ReceiverOrganization);
        var receiverLine = receiverOrganization;
        if (!string.IsNullOrWhiteSpace(letter.ReceiverName))
        {
            var receiverTitle = string.IsNullOrWhiteSpace(letter.ReceiverTitle)
                ? string.Empty
                : ToPersianDigits(letter.ReceiverTitle) + " ";
            receiverLine += $" — {receiverTitle}{ToPersianDigits(letter.ReceiverName)}";
        }
        else if (!string.IsNullOrWhiteSpace(letter.ReceiverTitle))
        {
            receiverLine += $" — {ToPersianDigits(letter.ReceiverTitle)}";
        }

        var title = ToPersianDigits(letter.Title);
        var externalReference = ToPersianDigits(letter.ExternalRefNumber);
        var signerCards = signers.Select(s =>
        {
            var role = s.SematId is int sematId && sematTitles.TryGetValue(sematId, out var sematTitle)
                ? sematTitle
                : "سمت ثبت نشده";
            return (
                Name: ToPersianDigits(FullName(s.User)),
                Role: ToPersianDigits(role),
                Signature: s.IsSigned ? TryReadSignature(s.User?.SignaturePath) : null);
        }).ToList();
        var signerRows = signerCards.Chunk(3).ToList();
        var signerCardWidth = isA5 ? 98f : 143f;
        var signatureHeight = isA5 ? 30f : 44f;

        void AddSignerRow(IContainer container, (string Name, string Role, byte[]? Signature)[] rowSigners)
        {
            container.ShowEntire().Row(row =>
            {
                foreach (var signer in rowSigners)
                {
                    row.ConstantItem(signerCardWidth).PaddingHorizontal(isA5 ? 2 : 4).Column(card =>
                    {
                        if (signer.Signature is { Length: > 0 })
                        {
                            card.Item().Height(signatureHeight).Padding(2).AlignCenter()
                                .Image(signer.Signature).FitArea();
                        }
                        else
                        {
                            card.Item().Height(signatureHeight).BorderBottom(0.7f).BorderColor("#9ca3af");
                        }

                        card.Item().PaddingTop(3).AlignCenter()
                            .Text(signer.Name).FontFamily("Vazirmatn-Bold").FontSize(fontSize)
                            .LineHeight(1.25f).DirectionFromRightToLeft();
                        card.Item().AlignCenter()
                            .Text(signer.Role).FontFamily("Vazirmatn").FontSize(smallFontSize)
                            .LineHeight(1.25f).DirectionFromRightToLeft();
                    });
                }

                // Keep a partial final row aligned to the right on RTL pages.
                row.RelativeItem();
            });
        }

        void AddHomepaField(IContainer container, HomepaLetterheadLayout.Field field, string value, bool center = false)
        {
            var box = container.PaddingLeft(field.Left).PaddingTop(field.Top)
                .Width(field.Width).Height(field.Height).AlignMiddle();
            // The enclosing header is physically left-to-right; only the field text is RTL.
            // Do not allow a long letter number to push the date/attachment down a flowing column.
            box = center ? box.AlignCenter() : box.AlignRight();
            box.ScaleToFit().Text(value)
                .FontFamily("Vazirmatn-Bold").FontSize(smallFontSize)
                .FontColor(Colors.White).LineHeight(1.2f)
                .DirectionFromRightToLeft();
        }

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(isA5 ? PageSizes.A5 : PageSizes.A4);
                // QuestPDF defaults to an opaque white page. When this PDF is drawn OVER the
                // company background by PDFsharp, that white fill would hide the entire letterhead.
                page.PageColor(hasLetterhead ? Colors.Transparent : Colors.White);
                page.MarginTop(0);
                // Reserve the printed company footer area on every page.
                page.MarginBottom(isA5 ? 52 : 90);
                page.MarginHorizontal(0);

                page.ContentFromRightToLeft();
                page.DefaultTextStyle(style => style
                    .FontFamily("Vazirmatn")
                    .FontSize(fontSize)
                    .LineHeight(lineHeight)
                    .DirectionFromRightToLeft());

                var isAriaLetterhead = hasLetterhead && letterheadCompanyId == AriaCompanyId;
                var contentTopPadding = isAriaLetterhead
                    ? (isA5 ? 26f : 36f)
                    : (isA5 ? 40f : 30f);

                if (isAriaLetterhead)
                {
                    // Aria's registered PDF has its labels printed in one gray row.
                    // Fill the existing dotted fields (date, number, attachment) without
                    // repeating labels, keeping the logo and company details untouched.
                    var fieldScale = isA5 ? 0.704f : 1f;
                    var fieldWidth = 90f * fieldScale;
                    var dateFieldInset = 13f * fieldScale;
                    var numberFieldInset = 27f * fieldScale;
                    var attachmentFieldInset = 21f * fieldScale;
                    page.Header()
                        .Height(isA5 ? 94 : 134)
                        .PaddingTop(isA5 ? 64 : 90)
                        .AlignLeft()
                        .Width(fieldWidth * 3)
                        .Row(fields =>
                        {
                            fields.ConstantItem(fieldWidth).PaddingRight(dateFieldInset)
                                .AlignRight().Text(date)
                                .FontFamily("Vazirmatn-Bold").FontSize(isA5 ? 6 : 8)
                                .FontColor("#666666").LineHeight(1.2f)
                                .DirectionFromRightToLeft();
                            fields.ConstantItem(fieldWidth).PaddingRight(numberFieldInset)
                                .AlignRight().Text(number)
                                .FontFamily("Vazirmatn-Bold").FontSize(isA5 ? 6 : 8)
                                .FontColor("#666666").LineHeight(1.2f)
                                .DirectionFromRightToLeft();
                            fields.ConstantItem(fieldWidth).PaddingRight(attachmentFieldInset)
                                .AlignRight().Text(hasAttachment ? "دارد" : "ندارد")
                                .FontFamily("Vazirmatn-Bold").FontSize(isA5 ? 6 : 8)
                                .FontColor("#666666").LineHeight(1.2f)
                                .DirectionFromRightToLeft();
                        });
                }
                else if (hasLetterhead && letterheadCompanyId == HomepaCompanyId)
                {
                    // Match company 1003's MRT rectangles, not the RTL body's default right edge.
                    // Preserve the existing reserved header height so body/signature layout does not move.
                    page.Header().ContentFromLeftToRight()
                        .Height(isA5 ? 150 : 160)
                        .Layers(layers =>
                        {
                            layers.PrimaryLayer().Extend();
                            AddHomepaField(layers.Layer(), HomepaLetterheadLayout.Number(isA5), number);
                            AddHomepaField(layers.Layer(), HomepaLetterheadLayout.Date(isA5), date);
                            AddHomepaField(layers.Layer(), HomepaLetterheadLayout.Attachment(isA5),
                                hasAttachment ? "دارد" : "ندارد", center: true);
                        });
                }
                else if (hasLetterhead)
                {
                    // Preserve the existing layout for other company letterheads; place
                    // only the values beside them, without duplicating the captions.
                    page.Header()
                        .Height(isA5 ? 150 : 160)
                        .PaddingTop(isA5 ? 40 : 60)
                        .PaddingLeft(isA5 ? 7 : 14)
                        .Column(meta =>
                        {
                            meta.Spacing(isA5 ? 4 : 6);
                            meta.Item().Width(isA5 ? 59 : 82).Text(number)
                                .FontFamily("Vazirmatn-Bold").FontSize(smallFontSize)
                                .FontColor(Colors.White).LineHeight(1.3f)
                                .AlignRight().DirectionFromRightToLeft();
                            meta.Item().Width(isA5 ? 59 : 82).Text(date)
                                .FontFamily("Vazirmatn-Bold").FontSize(smallFontSize)
                                .FontColor(Colors.White).LineHeight(1.3f)
                                .AlignRight().DirectionFromRightToLeft();
                            meta.Item().Width(isA5 ? 59 : 82).Text(hasAttachment ? "دارد" : "ندارد")
                                .FontFamily("Vazirmatn-Bold").FontSize(smallFontSize)
                                .FontColor(Colors.White).LineHeight(1.3f)
                                .AlignRight().DirectionFromRightToLeft();
                        });
                }
                else
                {
                    page.Header()
                        .Height(isA5 ? 150 : 160)
                        .PaddingTop(isA5 ? 40 : 60)
                        .PaddingHorizontal(isA5 ? 48 : 68)
                        .Column(meta =>
                        {
                            meta.Spacing(isA5 ? 4 : 6);
                            meta.Item().AlignRight().Text($"شماره: {number}")
                                .FontFamily("Vazirmatn-Bold").FontSize(smallFontSize)
                                .FontColor(Colors.Black).LineHeight(1.3f)
                                .DirectionFromRightToLeft();
                            meta.Item().AlignRight().Text($"تاریخ: {date}")
                                .FontFamily("Vazirmatn-Bold").FontSize(smallFontSize)
                                .FontColor(Colors.Black).LineHeight(1.3f)
                                .DirectionFromRightToLeft();
                            meta.Item().AlignRight().Text($"پیوست: {(hasAttachment ? "دارد" : "ندارد")}")
                                .FontFamily("Vazirmatn-Bold").FontSize(smallFontSize)
                                .FontColor(Colors.Black).LineHeight(1.3f)
                                .DirectionFromRightToLeft();
                        });
                }

                // One shared padded body column keeps receiver, subject, paragraphs,
                // signature block on identical left/right margins.
                page.Content().PaddingTop(contentTopPadding)
                    .PaddingHorizontal(isA5 ? 48 : 68).Column(col =>
                {
                    col.Spacing(isA5 ? 4 : 6);

                    col.Item().Text($"به: {receiverLine}")
                        .FontFamily("Vazirmatn").FontSize(fontSize).LineHeight(lineHeight)
                        .AlignRight().DirectionFromRightToLeft();

                    col.Item().Text($"موضوع: {title}")
                        .FontFamily("Vazirmatn-Bold").FontSize(titleFontSize).LineHeight(lineHeight)
                        .AlignRight().DirectionFromRightToLeft();

                    if (!string.IsNullOrWhiteSpace(externalReference))
                    {
                        col.Item().Text($"عطف به شماره: {externalReference}")
                            .FontSize(smallFontSize).LineHeight(lineHeight)
                            .AlignRight().DirectionFromRightToLeft();
                    }

                    col.Item().PaddingVertical(isA5 ? 3 : 5)
                        .LineHorizontal(0.7f).LineColor("#999999");

                    if (!string.IsNullOrWhiteSpace(bodyText))
                    {
                        // Right-align; never justify. Newlines and ZWNJ are preserved,
                        // and normal word wrapping avoids clipping/splitting words.
                        col.Item().Text(bodyText)
                            .FontFamily("Vazirmatn").FontSize(fontSize).LineHeight(lineHeight)
                            .AlignRight().DirectionFromRightToLeft();
                    }

                    if (signerRows.Count > 0)
                    {
                        // Keep the heading and first signer row together; do not add a closing or stamp box.
                        col.Item().PaddingTop(isA5 ? 8 : 12).ShowEntire().Column(signOff =>
                        {
                            signOff.Item().Text("امضاکنندگان")
                                .FontFamily("Vazirmatn-Bold").FontSize(smallFontSize)
                                .AlignRight().DirectionFromRightToLeft();
                            AddSignerRow(signOff.Item().PaddingTop(isA5 ? 4 : 6), signerRows[0]);
                        });

                        for (var rowIndex = 1; rowIndex < signerRows.Count; rowIndex++)
                            AddSignerRow(col.Item().PaddingTop(isA5 ? 8 : 12), signerRows[rowIndex]);
                    }

                    if (withCopy && ((copyTos?.Count ?? 0) > 0 || !string.IsNullOrWhiteSpace(letter.CopyTo)))
                    {
                        col.Item().PaddingTop(isA5 ? 8 : 12)
                            .LineHorizontal(0.5f).LineColor("#cbd5e1");
                        col.Item().Text("رونوشت:")
                            .FontFamily("Vazirmatn-Bold").FontSize(smallFontSize)
                            .LineHeight(lineHeight).AlignRight().DirectionFromRightToLeft();

                        if (copyTos is { Count: > 0 })
                        {
                            foreach (var copyTo in copyTos)
                            {
                                var details = new[] { copyTo.Desc, copyTo.RefNo }
                                    .Where(value => !string.IsNullOrWhiteSpace(value))
                                    .Select(ToPersianDigits)
                                    .ToArray();
                                var suffix = details.Length == 0 ? string.Empty : $" — {string.Join("، ", details)}";
                                col.Item().Text($"• {ToPersianDigits(copyTo.Name)}{suffix}")
                                    .FontSize(smallFontSize).LineHeight(lineHeight)
                                    .AlignRight().DirectionFromRightToLeft();
                            }
                        }
                        else
                        {
                            col.Item().Text(ToPersianDigits(letter.CopyTo))
                                .FontSize(smallFontSize).LineHeight(lineHeight)
                                .AlignRight().DirectionFromRightToLeft();
                        }
                    }
                });
            });
        });

        return document.GeneratePdf();
    }

    private static string FullName(User? user)
    {
        if (user == null) return string.Empty;
        var value = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(value) ? user.Username : value;
    }

    /// <summary>قرار دادن صفحات نامه روی سربرگ یا لوگوی شرکت (PDF یا تصویر)</summary>
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

            // 1) سربرگ یا لوگوی شرکت به‌عنوان پس‌زمینه
            if (isPdf && letterheadForm != null)
            {
                letterheadForm.PageNumber = 1;
                gfx.DrawImage(letterheadForm, rect);
            }
            else if (letterheadImg != null)
            {
                gfx.DrawImage(letterheadImg, rect);
            }

            // 2) متن نامه روی سربرگ
            contentForm.PageNumber = i + 1;
            gfx.DrawImage(contentForm, rect);
        }

        using var ms = new MemoryStream();
        output.Save(ms);
        return ms.ToArray();
    }
}
