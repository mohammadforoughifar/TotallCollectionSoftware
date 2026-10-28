using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;              // Encoding.UTF8 (هشِ مادهٔ چاپ) — بدون این using: خطای CS0103
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Http;
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

    /// <summary>
    /// تشخیص مسیر سربرگ/طرح یک نامه (فقط خواندن) — برای پاسخ به «چرا طرح شرکت خوانده نمی‌شود؟».
    /// </summary>
    Task<OutgoingLetterPrintDiagnosticsDto?> DiagnoseAsync(int letterId, string size);
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

    // The same company can have a different id in another installation (Homepa is 1003 in the
    // main database and 3 in the sample one; Companies/3 and Companies/1003 embed the same
    // artwork). Without the id, letters fall back to the generic layout and print incorrectly.
    // Override without a rebuild: Reporting:OutgoingLetters:HomepaCompanyIds = "3,1003".
    private static readonly int[] DefaultHomepaCompanyIds = { 3, HomepaCompanyId };
    private static readonly int[] DefaultAriaCompanyIds = { AriaCompanyId };

    // کش کوتاه‌مدت خروجی چاپ: ساخت PDF روی سربرگ چند ثانیه طول می‌کشد و اگر مرورگر
    // درخواست را قطع کند یا کاربر «تلاش مجدد» بزند، رندر تکرار می‌شد و چاپ آماده نمی‌شد.
    private static readonly TimeSpan RenderedPdfLifetime = TimeSpan.FromSeconds(90);

    /// <summary>
    /// رندرِ مشترکِ درخواست‌های هم‌زمان. عمداً به‌جای <c>Lazy&lt;Task&lt;T&gt;&gt;</c> نوشته شده است:
    /// خواندن <c>Lazy&lt;Task&lt;T&gt;&gt;.Value</c> قاعدهٔ تحلیل‌گر VSTHRD011 را می‌شکند (خطر deadlock
    /// در زمینهٔ همگام) و ساخت پروژه را با «Error» متوقف می‌کند. اینجا Task یک‌بار ساخته می‌شود و
    /// همهٔ فراخوان‌ها فقط آن را await می‌کنند.
    /// </summary>
    private sealed class CachedRender
    {
        private readonly TaskCompletionSource<byte[]?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CachedRender(DateTime createdUtc) => CreatedUtc = createdUtc;

        public DateTime CreatedUtc { get; }

        public Task<byte[]?> Render => _completion.Task;

        public void Complete(byte[]? pdf) => _completion.TrySetResult(pdf);

        public void Fail(Exception error) => _completion.TrySetException(error);
    }

    private static readonly ConcurrentDictionary<string, CachedRender> RenderedPdfs = new(StringComparer.Ordinal);

    private static bool _fontsRegistered;
    private static readonly object FontLock = new();

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<OutgoingLetterPrintService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor? _http;
    private readonly int[] _homepaCompanyIds;
    private readonly int[] _ariaCompanyIds;
    private readonly Dictionary<int, int> _companyTemplateMap;

    public OutgoingLetterPrintService(
        AppDbContext db,
        IWebHostEnvironment env,
        ILogger<OutgoingLetterPrintService> logger,
        IConfiguration? configuration = null,
        IHttpContextAccessor? http = null)
    {
        _db = db;
        _env = env;
        _logger = logger;
        _configuration = configuration ?? new ConfigurationBuilder().Build();
        _http = http;
        _homepaCompanyIds = CompanyIdsFor("Reporting:OutgoingLetters:HomepaCompanyIds", DefaultHomepaCompanyIds);
        _ariaCompanyIds = CompanyIdsFor("Reporting:OutgoingLetters:AriaCompanyIds", DefaultAriaCompanyIds);
        _companyTemplateMap = ReadCompanyTemplateMap();
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>
    /// شناسهٔ شرکت‌هایی که طرح سربرگ هومپا/آریا دارند — با
    /// <c>Reporting:OutgoingLetters:HomepaCompanyIds</c> بدون تغییر کد قابل تنظیم است.
    /// </summary>
    private int[] CompanyIdsFor(string key, int[] fallback)
    {
        var raw = _configuration[key];
        if (string.IsNullOrWhiteSpace(raw)) return fallback;

        var ids = raw.Split(new[] { ',', ';', ' ', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToArray();
        return ids.Length == 0 ? fallback : ids;
    }

    /// <summary>
    /// نگاشت «شناسهٔ شرکتِ پایگاه داده ← شناسهٔ پوشهٔ طرح» از
    /// <c>Reporting:OutgoingLetters:CompanyTemplateMap</c>؛ تصویر سربرگ و مختصات برچسب‌ها
    /// هر دو از همان پوشه خوانده می‌شوند.
    /// </summary>
    private Dictionary<int, int> ReadCompanyTemplateMap()
    {
        var map = new Dictionary<int, int>();
        foreach (var entry in _configuration.GetSection("Reporting:OutgoingLetters:CompanyTemplateMap").GetChildren())
        {
            if (!int.TryParse(entry.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var companyId) || companyId <= 0) continue;
            if (!int.TryParse((entry.Value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var designId) || designId <= 0) continue;
            map[companyId] = designId;
        }
        return map;
    }

    /// <summary>
    /// شناسهٔ «شرکت فعال» همین درخواست (هدر X-Company-Id که میان‌افزار اعتبارسنجی کرده است).
    /// برای نامه‌هایی که CompanyId ندارند، این نزدیک‌ترین حدس درست است.
    /// </summary>
    private int ActiveCompanyIdFromContext()
    {
        var ctx = _http?.HttpContext;
        if (ctx == null) return 0;
        return ctx.Items.TryGetValue(Inventory.Api.Infrastructure.ActiveCompanyMiddleware.ItemKey, out var value)
               && value is int id ? id : 0;
    }

    /// <summary>
    /// آیا طرح جاسازی‌شده در قالب MRT شرکت (پوشهٔ Reports/OutgoingLetters/Companies/{id})
    /// بر فایل ثبت‌شده روی رکورد شرکت اولویت دارد؟ پیش‌فرض: نه (رفتار قبلی).
    /// تنظیم: Reporting:OutgoingLetters:PreferTemplateLetterhead = true
    /// </summary>
    private bool PreferTemplateLetterhead
    {
        get
        {
            var raw = _configuration["Reporting:OutgoingLetters:PreferTemplateLetterhead"];
            return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>شناسهٔ طرحی که این شرکت باید با آن چاپ شود (نگاشت صریح، وگرنه خودش).</summary>
    private int TemplateDesignId(int companyId) => _companyTemplateMap.TryGetValue(companyId, out var mapped) ? mapped : companyId;

    /// <summary>ترتیب جست‌وجوی پوشهٔ قالب: طرح نگاشت‌شده، سپس پوشهٔ هم‌نام خود شرکت.</summary>
    private IEnumerable<int> DesignCompanyIds(int companyId)
    {
        var designId = TemplateDesignId(companyId);
        if (designId != companyId) yield return designId;
        yield return companyId;
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

            // فونت غایب قبلاً بی‌صدا نادیده گرفته می‌شد و PDF با فونت جانشین ناخوانا چاپ می‌شد.
            if (!File.Exists(reg))
                throw new InvalidOperationException(
                    $"فونت چاپ فارسی پیدا نشد: {reg}. پوشهٔ Resources/fonts را همراه خروجی API منتشر کنید.");

            using (var regularStream = File.OpenRead(reg))
                FontManager.RegisterFontWithCustomName("Vazirmatn", regularStream);
            if (File.Exists(bold))
            {
                using var boldStream = File.OpenRead(bold);
                FontManager.RegisterFontWithCustomName("Vazirmatn-Bold", boldStream);
            }
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

    /// <summary>
    /// تشخیص مسیر سربرگ یک نامه بدون تولید PDF؛ پاسخ روشن به این پرسش که
    /// «چرا طرح شرکت در Reports/OutgoingLetters/Companies خوانده نمی‌شود؟»
    /// </summary>
    public async Task<OutgoingLetterPrintDiagnosticsDto?> DiagnoseAsync(int letterId, string size)
    {
        var letter = await _db.OutgoingLetters.AsNoTracking()
            .Where(l => l.Id == letterId && !l.IsDelete)
            .Select(l => new { l.Id, l.CompanyId, l.CreatorUserId, l.SadereNumber, l.LetterNumber, l.Title })
            .FirstOrDefaultAsync();

        if (letter == null) return null;

        var normalizedSize = string.Equals(size, "A5", StringComparison.OrdinalIgnoreCase) ? "A5" : "A4";
        var isA5 = normalizedSize == "A5";

        var activeCompanyIds = await _db.SystemCompanies.AsNoTracking()
            .Where(c => c.IsActive).Select(c => c.Id).ToListAsync();

        var letterCompanyId = letter.CompanyId is > 0 ? letter.CompanyId : null;
        var requestCompanyId = ActiveCompanyIdFromContext();
        var effectiveCompanyId = letterCompanyId;
        var companySource = letterCompanyId != null ? "Letter" : "None";

        if (effectiveCompanyId == null)
        {
            var pickId = activeCompanyIds.Count == 1
                ? activeCompanyIds[0]
                : requestCompanyId > 0 && activeCompanyIds.Contains(requestCompanyId) ? requestCompanyId : 0;
            if (pickId > 0)
            {
                effectiveCompanyId = pickId;
                companySource = activeCompanyIds.Count == 1 ? "OnlyActiveCompany" : "RequestActiveCompany";
            }
        }

        var company = effectiveCompanyId is int companyKey
            ? await _db.SystemCompanies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyKey)
            : null;

        var designId = effectiveCompanyId is int rawDesignId ? TemplateDesignId(rawDesignId) : (int?)null;
        var templatePath = effectiveCompanyId is int designCandidate
            ? FindCompanyTemplate(designCandidate, normalizedSize)
            : null;

        int? templateImageBytes = null;
        string? templateError = null;
        if (templatePath != null)
        {
            try
            {
                templateImageBytes = ReadTemplateWatermark(templatePath)?.Length ?? 0;
            }
            catch (Exception ex)
            {
                templateError = $"{ex.GetType().Name}: {ex.Message}";
            }
        }

        var registeredPath = company != null ? ResolveRegisteredLetterheadFile(company) : null;
        var preferTemplate = PreferTemplateLetterhead;

        string letterheadSource;
        if (preferTemplate && templatePath != null) letterheadSource = "Template";
        else if (registeredPath != null) letterheadSource = "Registered";
        else if (templatePath != null) letterheadSource = "Template";
        else letterheadSource = "None";

        var configuredRoot = _configuration["Reporting:Stimulsoft:TemplateRoot"];
        var templateRoot = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine("Reports", "OutgoingLetters", "Companies")
            : configuredRoot.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var roots = new[] { _env.ContentRootPath, AppContext.BaseDirectory, Directory.GetCurrentDirectory() }
            .Distinct().ToList();

        var companyRecordMissing = effectiveCompanyId != null && company == null;
        var hint = BuildDiagnosticsHint(
            letterCompanyId, companySource, effectiveCompanyId, companyRecordMissing,
            templatePath, templateError, registeredPath, preferTemplate, letterheadSource);

        return new OutgoingLetterPrintDiagnosticsDto
        {
            LetterId = letter.Id,
            LetterNumber = letter.SadereNumber ?? letter.LetterNumber,
            Title = letter.Title,
            LetterCompanyId = letterCompanyId,
            RequestActiveCompanyId = requestCompanyId > 0 ? requestCompanyId : null,
            EffectiveCompanyId = effectiveCompanyId,
            CompanySource = companySource,
            CompanyName = company?.Name,
            CompanyRecordMissing = companyRecordMissing,
            LetterheadFileName = company?.LetterheadFileName,
            TemplateDesignId = designId,
            CompanyTemplateMapApplied = designId != null && effectiveCompanyId != null && designId != effectiveCompanyId,
            TemplatePath = templatePath,
            TemplateImageBytes = templateImageBytes,
            TemplateError = templateError,
            RegisteredLetterheadPath = registeredPath,
            PreferTemplateLetterhead = preferTemplate,
            LetterheadSource = letterheadSource,
            TemplateRoot = templateRoot,
            SearchRoots = roots,
            AvailableDesigns = DescribeAvailableDesigns(templateRoot, roots.ToArray()),
            Hint = hint
        };
    }

    /// <summary>ساخت جملهٔ علت به فارسی — همان چیزی که در لاگ/پاسخ تشخیص دیده می‌شود.</summary>
    private static string BuildDiagnosticsHint(
        int? letterCompanyId,
        string companySource,
        int? effectiveCompanyId,
        bool companyRecordMissing,
        string? templatePath,
        string? templateError,
        string? registeredPath,
        bool preferTemplate,
        string letterheadSource)
    {
        if (letterCompanyId == null)
        {
            var how = companySource switch
            {
                "OnlyActiveCompany" => "چون تنها یک شرکت فعال وجود دارد",
                "RequestActiveCompany" => "بر اساس شرکت فعال انتخاب‌شده در بالای صفحه",
                _ => "و شرکتی هم قابل انتخاب نبود"
            };
            return effectiveCompanyId == null
                ? "این نامه شرکت صادرکننده (CompanyId) ندارد و هیچ شرکت فعالی هم پیدا نشد؛ طرح شرکت قابل انتخاب نیست."
                : $"این نامه شرکت صادرکننده (CompanyId) ندارد؛ شرکت {effectiveCompanyId} {how} انتخاب شد. " +
                  "اگر طرح چاپی متعلق به شرکت دیگری است، نامه را ویرایش کنید و «شرکت صادرکننده» را انتخاب کنید.";
        }

        if (companyRecordMissing)
            return $"شناسهٔ شرکت این نامه ({effectiveCompanyId}) در جدول SystemCompanies نیست یا غیرفعال است؛ " +
                   "رکورد شرکت را درست کنید یا شناسه را با CompanyTemplateMap نگاشت کنید.";

        if (templatePath == null)
            return $"هیچ قالب چاپی برای شرکت {effectiveCompanyId} پیدا نشد؛ پوشهٔ " +
                   $"Reports/OutgoingLetters/Companies/{effectiveCompanyId} یا فایل Outgoing-A4/A5.mrt را بررسی کنید. " +
                   "فهرست پوشه‌های موجود در AvailableDesigns آمده است.";

        if (templateError != null)
            return $"فایل قالب {templatePath} خوانده شد اما تصویر سربرگ آن قابل استخراج نیست ({templateError}).";

        if (registeredPath != null && !preferTemplate)
            return $"سربرگ چاپ از فایل ثبت‌شدهٔ شرکت استفاده می‌کند ({registeredPath}) و طرح داخل قالب نادیده گرفته می‌شود. " +
                   "برای چاپ با طرح پوشهٔ Companies یا مقدار Reporting:OutgoingLetters:PreferTemplateLetterhead=true را تنظیم کنید " +
                   "یا فایل ثبت‌شدهٔ شرکت (LetterheadFileName) را خالی کنید.";

        return letterheadSource == "Template"
            ? $"سربرگ چاپ از طرح داخل قالب شرکت خوانده می‌شود: {templatePath}"
            : "سربرگی برای این نامه پیدا نشد؛ چاپ بدون سربرگ خواهد بود.";
    }

    private string? FindCompanyTemplate(int companyId, string size)
    {
        // The active QuestPDF pipeline must honor the same template-root setting as the legacy renderer.
        var configuredRoot = _configuration["Reporting:Stimulsoft:TemplateRoot"];
        var templateRoot = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine("Reports", "OutgoingLetters", "Companies")
            : configuredRoot.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var roots = new[] { _env.ContentRootPath, AppContext.BaseDirectory, Directory.GetCurrentDirectory() };

        // شناسهٔ شرکت بین نصب‌ها یکسان نیست (هومپا در یکی 1003 و در دیگری 3)؛ نگاشت صریح
        // جلوی چاپ روی طرح شرکت دیگر را می‌گیرد.
        foreach (var designId in DesignCompanyIds(companyId))
        {
            var folder = Path.Combine(templateRoot, designId.ToString(CultureInfo.InvariantCulture));
            var folders = Path.IsPathRooted(templateRoot)
                ? new[] { folder }
                : roots.Select(root => Path.Combine(root, folder)).Distinct().ToArray();

            foreach (var candidateFolder in folders)
            {
                var found = FindTemplateFile(candidateFolder, size, out var exactName);
                if (found == null) continue;

                if (designId != companyId)
                    _logger.LogInformation(
                        "قالب چاپ شرکت {CompanyId} از طرح شرکت {DesignCompanyId} استفاده می‌کند (Reporting:OutgoingLetters:CompanyTemplateMap). Path={Path}, Size={Size}",
                        companyId, designId, found, size);
                if (!exactName)
                    _logger.LogInformation(
                        "نام فایل قالب شرکت {CompanyId} مطابق استاندارد نیست؛ انتظار «{Expected}» بود و همین فایل استفاده شد: {Path}",
                        companyId, $"Outgoing-{size}.mrt", found);
                return found;
            }
        }

        // قبلاً این حالت فقط LogDebug بود و در سطح پیش‌فرض Information هیچ اثری در لاگ نداشت؛
        // نتیجه: «طرح خوانده نمی‌شود» بدون هیچ سرنخی. اکنون پوشه‌های موجود هم فهرست می‌شوند.
        _logger.LogWarning(
            "هیچ قالب چاپی برای شرکت پیدا نشد. CompanyId={CompanyId}, DesignIds={DesignIds}, Size={Size}, " +
            "TemplateRoot={TemplateRoot}, SearchedRoots={SearchedRoots}, AvailableDesigns={AvailableDesigns}",
            companyId, string.Join(",", DesignCompanyIds(companyId)), size, templateRoot,
            string.Join(" | ", roots.Distinct()), DescribeAvailableDesigns(templateRoot, roots));
        return null;
    }

    /// <summary>
    /// یافتن فایل قالب داخل پوشهٔ طرح یک شرکت. اگر نام فایل مطابق استاندارد
    /// (Outgoing-A4.mrt / Outgoing-A5.mrt) نباشد، همان پوشه بررسی می‌شود تا فایلی که
    /// طراح با نام دیگری ذخیره کرده «بی‌صدا نادیده» گرفته نشود.
    /// </summary>
    private static string? FindTemplateFile(string folder, string size, out bool exactName)
    {
        exactName = true;
        if (!Directory.Exists(folder)) return null;

        var expectedName = $"Outgoing-{size}.mrt";
        var expected = Path.Combine(folder, expectedName);
        if (File.Exists(expected)) return expected;

        var files = Directory.EnumerateFiles(folder, "*.mrt")
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0) return null;

        var byName = files.FirstOrDefault(file =>
            string.Equals(Path.GetFileName(file), expectedName, StringComparison.OrdinalIgnoreCase));
        if (byName != null) { exactName = false; return byName; }

        var bySize = files.FirstOrDefault(file =>
            Path.GetFileNameWithoutExtension(file).Contains(size, StringComparison.OrdinalIgnoreCase));
        if (bySize != null) { exactName = false; return bySize; }

        if (files.Length == 1) { exactName = false; return files[0]; }
        return null;
    }

    /// <summary>فهرست پوشه‌های طرح موجود (برای پیام خطای قابل‌فهم در لاگ).</summary>
    private static string DescribeAvailableDesigns(string templateRoot, string[] roots)
    {
        var designs = new List<string>();
        foreach (var root in roots.Distinct())
        {
            var directory = Path.IsPathRooted(templateRoot) ? templateRoot : Path.Combine(root, templateRoot);
            if (!Directory.Exists(directory)) continue;
            foreach (var companyDirectory in Directory.EnumerateDirectories(directory)
                         .OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase))
            {
                var files = Directory.EnumerateFiles(companyDirectory, "*.mrt")
                    .Select(Path.GetFileName);
                designs.Add($"{Path.GetFileName(companyDirectory)}:[{string.Join(",", files)}] @ {companyDirectory}");
            }
        }
        return designs.Count == 0 ? "هیچ پوشهٔ طرحی پیدا نشد" : string.Join(" | ", designs);
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
        var cacheKey = await BuildPrintCacheKeyAsync(letterId, size, withCopy);
        if (cacheKey == null) return null;

        // رکورد پیش از شروع رندر در فرهنگ لغت منتشر می‌شود تا درخواست‌های هم‌زمان
        // (دوبار کلیک، تلاش مجدد پس از قطع مرورگر) همان رندر را await کنند.
        var (cached, startedHere) = RentRenderedPdf(cacheKey);
        if (!startedHere)
        {
            _logger.LogInformation(
                "چاپ نامه {LetterId}: خروجی آمادهٔ همین درخواست استفاده شد (کش {AgeMs}ms).",
                letterId, (int)(DateTime.UtcNow - cached.CreatedUtc).TotalMilliseconds);
            return await cached.Render;
        }

        try
        {
            var pdf = await GeneratePdfCoreAsync(letterId, size, withCopy);
            if (pdf == null) RenderedPdfs.TryRemove(new KeyValuePair<string, CachedRender>(cacheKey, cached));
            cached.Complete(pdf);
            return pdf;
        }
        catch (Exception ex)
        {
            // خطا هرگز کش نمی‌شود؛ تلاش بعدی باید واقعاً دوباره رندر کند.
            RenderedPdfs.TryRemove(new KeyValuePair<string, CachedRender>(cacheKey, cached));
            cached.Fail(ex);
            throw;
        }
    }

    /// <summary>
    /// رکورد رندرِ این کلید را برمی‌گرداند؛ اگر تازه ساخته شد <c>StartedHere=true</c> است و فقط
    /// همان فراخوان مسئول رندر است. رکورد منقضی با مقایسهٔ خودِ نمونه حذف می‌شود تا رکورد تازهٔ
    /// درخواست دیگری پاک نشود.
    /// </summary>
    private static (CachedRender Render, bool StartedHere) RentRenderedPdf(string cacheKey)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (RenderedPdfs.TryGetValue(cacheKey, out var existing))
            {
                if (DateTime.UtcNow - existing.CreatedUtc < RenderedPdfLifetime)
                    return (existing, false);

                RenderedPdfs.TryRemove(new KeyValuePair<string, CachedRender>(cacheKey, existing));
            }

            PruneRenderedPdfs();
            var fresh = new CachedRender(DateTime.UtcNow);
            if (RenderedPdfs.TryAdd(cacheKey, fresh))
                return (fresh, true);
        }

        // رقابت شدید و غیرمنتظره: بدون کش رندر می‌کنیم تا درخواست کاربر معلق نماند.
        return (new CachedRender(DateTime.UtcNow), true);
    }

    /// <summary>پاک‌سازی دوره‌ای انقضاشده‌ها تا حافظه با هر چاپ جدید رشد نکند.</summary>
    private static void PruneRenderedPdfs()
    {
        if (RenderedPdfs.Count < 24) return;
        var now = DateTime.UtcNow;
        foreach (var entry in RenderedPdfs)
        {
            if (now - entry.Value.CreatedUtc >= RenderedPdfLifetime)
                RenderedPdfs.TryRemove(entry.Key, out _);
        }
    }

    /// <summary>اثر انگشت دادهٔ مؤثر در چاپ؛ با هر ویرایش نامه کلید عوض می‌شود.</summary>
    private async Task<string?> BuildPrintCacheKeyAsync(int letterId, string size, bool withCopy)
    {
        var row = await _db.OutgoingLetters.AsNoTracking()
            .Where(l => l.Id == letterId && !l.IsDelete)
            .Select(l => new
            {
                l.CompanyId, l.SadereNumber, l.LetterNumber, l.DateSadere, l.DateSabt, l.Title, l.Text,
                l.ReceiverOrganization, l.ReceiverName, l.ReceiverTitle, l.CopyTo, l.ExternalRefNumber,
                SignerCount = _db.OutgoingLetterSigners.Count(s => s.SourceId == l.Id && !s.IsDelete),
                SignedCount = _db.OutgoingLetterSigners.Count(s => s.SourceId == l.Id && !s.IsDelete && s.IsSigned),
                CopyToCount = _db.OutgoingLetterCopyToes.Count(c => c.OutgoingLetterId == l.Id && !c.IsDelete),
                AttachmentCount = _db.AppAttachments.Count(a => a.Module == "OutgoingLetters" && a.RefId == l.Id)
            })
            .FirstOrDefaultAsync();

        if (row == null) return null;

        var material = string.Join('\u001f',
            letterId.ToString(CultureInfo.InvariantCulture),
            size.ToUpperInvariant(),
            withCopy ? "1" : "0",
            row.CompanyId?.ToString(CultureInfo.InvariantCulture) ?? "-",
            row.SadereNumber, row.LetterNumber,
            row.DateSadere?.Ticks.ToString(CultureInfo.InvariantCulture) ?? "-",
            row.DateSabt.Ticks.ToString(CultureInfo.InvariantCulture),
            row.Title, row.Text, row.ReceiverOrganization, row.ReceiverName, row.ReceiverTitle,
            row.CopyTo, row.ExternalRefNumber,
            row.SignerCount.ToString(CultureInfo.InvariantCulture),
            row.SignedCount.ToString(CultureInfo.InvariantCulture),
            row.CopyToCount.ToString(CultureInfo.InvariantCulture),
            row.AttachmentCount.ToString(CultureInfo.InvariantCulture));

        return "outgoing-print-v2:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    /// <summary>رندر واقعی نامه (بدون کش).</summary>
    private async Task<byte[]?> GeneratePdfCoreAsync(int letterId, string size, bool withCopy = true)
    {
        // Resolve the letter and its company-registered letterhead before rendering.
        var letter = await _db.OutgoingLetters.AsNoTracking()
            .Include(l => l.Creator)
            .FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete);
        if (letter == null) return null;

        var letterCompanyId = letter.CompanyId;
        var hasCompanyId = letterCompanyId is > 0;
        var activeCompanies = await _db.SystemCompanies.AsNoTracking().Where(c => c.IsActive)
            .Select(c => new { c.Id, c.LetterheadFileName }).ToListAsync();

        SystemCompany? company;
        if (hasCompanyId)
        {
            company = await _db.SystemCompanies.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == letterCompanyId);
        }
        else
        {
            // نامهٔ بدون شرکت (دادهٔ قدیمی): انتخاب خودکار فقط وقتی مجاز است که «تنها یک شرکت
            // فعال» وجود داشته باشد یا «شرکت فعال همین درخواست» مشخص باشد.
            // قبلاً اولین شرکتِ دارای سربرگ انتخاب می‌شد؛ در نصب چندشرکتی نتیجه این بود که نامه
            // روی سربرگ شرکت دیگری چاپ می‌شد و به‌نظر می‌رسید «فایل‌های طرح خوانده نمی‌شوند».
            var requestCompanyId = ActiveCompanyIdFromContext();
            var pickId = activeCompanies.Count == 1
                ? activeCompanies[0].Id
                : requestCompanyId > 0 && activeCompanies.Any(c => c.Id == requestCompanyId)
                    ? requestCompanyId
                    : 0;
            company = pickId > 0
                ? await _db.SystemCompanies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == pickId)
                : null;

            _logger.LogWarning(
                "نامه {LetterId} شرکت صادرکننده (CompanyId) ندارد؛ شرکت انتخابی برای چاپ: {SelectedCompanyId} " +
                "(شرکت‌های فعال: {ActiveCount}، شرکت فعال درخواست: {RequestCompanyId}). " +
                "برای چاپ روی طرح درست، «شرکت صادرکننده» را در فرم نامه انتخاب کنید.",
                letterId, company?.Id, activeCompanies.Count, requestCompanyId);
        }

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
        // اولویت منبع سربرگ:
        //   • پیش‌فرض همان رفتار قبلی است: «فایل ثبت‌شدهٔ شرکت» (SystemCompanies.LetterheadFileName)،
        //     و اگر نبود، طرح جاسازی‌شده در قالب MRT همان شرکت.
        //   • اگر Reporting:OutgoingLetters:PreferTemplateLetterhead=true باشد، طرح داخل
        //     Reports/OutgoingLetters/Companies/{id} اولویت می‌گیرد — همان جایی که طرح‌ها
        //     طراحی و نگهداری می‌شوند.
        var preferTemplate = PreferTemplateLetterhead;
        var letterheadPath = preferTemplate && templateLetterheadPath != null
            ? templateLetterheadPath
            : registeredPath ?? templateLetterheadPath;
        if (!preferTemplate && registeredPath != null && templateLetterheadPath != null)
            _logger.LogInformation(
                "فایل ثبت‌شدهٔ شرکت بر طرح قالب اولویت دارد. CompanyId={CompanyId}, RegisteredPath={RegisteredPath}, TemplatePath={TemplatePath}. " +
                "برای چاپ با طرح پوشهٔ Companies مقدار Reporting:OutgoingLetters:PreferTemplateLetterhead=true را تنظیم کنید " +
                "یا LetterheadFileName شرکت را خالی کنید.",
                selectedCompanyId, registeredPath, templateLetterheadPath);

        var letterheadFromTemplate = letterheadPath != null && letterheadPath == templateLetterheadPath;

        // ۳) سربرگ فوروث / لوگوی HR — فقط برای نصب تک‌شرکتیِ بدون رکورد شرکت.
        if (letterheadPath == null && !hasCompanyId && activeCompanies.Count == 0)
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

        // در نصب چندشرکتی، نامهٔ بی‌شرکت نباید سربرگ شرکت دیگر (یا فوروث) را بگیرد؛
        // پیام روشن بهتر از چاپ روی سربرگ اشتباه است.
        if (letterheadPath == null && !hasCompanyId && activeCompanies.Count > 0)
        {
            _logger.LogError(
                "چاپ نامه {LetterId} متوقف شد: نامه به هیچ شرکتی متصل نیست و سامانه چند شرکت فعال دارد (تعداد: {ActiveCount}).",
                letterId, activeCompanies.Count);
            throw new RequiredCompanyLetterheadPrintException(
                "این نامه به هیچ شرکتی متصل نیست و سامانه چند شرکت فعال دارد؛ برای جلوگیری از چاپ روی سربرگ شرکت دیگر، " +
                "ابتدا شرکت صادرکننده را در فرم نامه انتخاب کنید (یا از بالای صفحه شرکت درست را برگزینید) و دوباره چاپ کنید.");
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

        // نامه‌های قدیمی ممکن است سمتِ ثبت‌شده نداشته باشند؛ در این حالت
        // سمتِ فعالِ فعلیِ کاربر (سمتِ پیش‌فرض) نمایش داده می‌شود.
        var signerUserIds = signers.Select(s => s.UserId).Distinct().ToList();
        var sematTitlesByUser = signerUserIds.Count == 0
            ? new Dictionary<int, string>()
            : (await _db.Semats.AsNoTracking()
                    .Where(s => s.UserId != null && signerUserIds.Contains(s.UserId.Value) && !s.IsDelete && s.IsActive)
                    .OrderByDescending(s => s.DefaultSemat)
                    .ThenBy(s => s.SematId)
                    .Select(s => new { s.UserId, s.Title, s.OnvanMokatebati })
                    .ToListAsync())
                .GroupBy(s => s.UserId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => string.IsNullOrWhiteSpace(g.First().OnvanMokatebati) ? g.First().Title : g.First().OnvanMokatebati!);

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
            letter, signers, sematTitles, sematTitlesByUser, hasAttachment, isA5, withCopy, copyTos,
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
        IReadOnlyDictionary<int, string> sematTitlesByUser,
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
        var lineHeight = isA5 ? 1.3f : 1.35f;

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
                : (sematTitlesByUser.TryGetValue(s.UserId, out var userSematTitle) ? userSematTitle : "سمت ثبت نشده");
            return (
                Name: ToPersianDigits(FullName(s.User)),
                Role: ToPersianDigits(role),
                Signature: s.IsSigned ? TryReadSignature(s.User?.SignaturePath) : null);
        }).ToList();

        // ── بلوکِ امضاکنندگان ─────────────────────────────────────────────
        // نام، سمت و امضای امضاکننده در سمتِ چپِ انتهای نامه قرار می‌گیرد و
        // تصویرِ امضا دقیقاً روی نام و سمتِ امضاکننده می‌افتد (Overlap).
        var signerBlockWidth = isA5 ? 140f : 200f;
        var signerBlockHeight = isA5 ? 54f : 72f;
        var signatureBoxWidth = isA5 ? 96f : 132f;
        var signatureBoxHeight = isA5 ? 30f : 40f;

        void AddSignerCard(IContainer container, (string Name, string Role, byte[]? Signature) signer)
        {
            container.Height(signerBlockHeight).Layers(layers =>
            {
                // لایهٔ اصلی: نام و سمتِ امضاکننده (چسبیده به پایینِ بلوک).
                // اندازهٔ این لایه، اندازهٔ کلِ بلوک را تعیین می‌کند.
                layers.PrimaryLayer().AlignBottom().Column(textColumn =>
                {
                    textColumn.Item().Text(signer.Name)
                        .FontFamily("Vazirmatn-Bold").FontSize(fontSize)
                        .LineHeight(1.2f).AlignRight().DirectionFromRightToLeft();
                    textColumn.Item().Text(signer.Role)
                        .FontFamily("Vazirmatn").FontSize(smallFontSize)
                        .LineHeight(1.2f).AlignRight().DirectionFromRightToLeft();
                });

                // لایهٔ رویی: تصویرِ امضا که روی نام و سمت قرار می‌گیرد.
                // چون بعد از لایهٔ اصلی تعریف شده، روی متن رسم می‌شود.
                if (signer.Signature is { Length: > 0 })
                {
                    layers.Layer().AlignBottom().AlignRight()
                        .Width(signatureBoxWidth).Height(signatureBoxHeight)
                        .Image(signer.Signature).FitArea();
                }
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

        void AddDeclaredField(IContainer container, LetterheadMetaFields.Field field, string value,
            float pageHeaderOrigin)
        {
            // Values are painted where the company template says: rectangle, colour, font size and
            // alignment all come from the MRT, so a light letterhead no longer gets invisible white
            // values and a dark band keeps its white values. pageHeaderOrigin is the page top margin.
            var left = Math.Max(0f, field.Left);
            var top = Math.Max(0f, field.Top - pageHeaderOrigin);
            var displayValue = field.IsolateLtr ? $"\u2066{value}\u2069" : value;
            var box = container.PaddingLeft(left).PaddingTop(top)
                .Width(field.Width).Height(field.Height).AlignMiddle();
            box = field.Center ? box.AlignCenter() : box.AlignRight();
            var text = box.ScaleToFit().Text(displayValue)
                .FontFamily(field.Bold ? "Vazirmatn-Bold" : "Vazirmatn").FontSize(field.FontSize)
                .FontColor(field.Color).LineHeight(1.05f);
            if (field.IsolateLtr)
                text.DirectionFromLeftToRight();
            else
                text.DirectionFromRightToLeft();
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

                // چیدمان بر اساس «طرح» (پوشهٔ قالب) انتخاب می‌شود، نه شناسهٔ خام پایگاه داده.
                var designCompanyId = letterheadCompanyId is int designSource ? TemplateDesignId(designSource) : 0;
                var isAriaLetterhead = hasLetterhead && _ariaCompanyIds.Contains(designCompanyId);
                var isHomepaLetterhead = hasLetterhead && _homepaCompanyIds.Contains(designCompanyId);
                var contentTopPadding = isAriaLetterhead
                    ? (isA5 ? 26f : 36f)
                    : (isA5 ? 30f : 30f); // در A5 بدنه نامه از بالاتر شروع شود

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
                else if (isHomepaLetterhead)
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
                    // Number/date/attachment are placed where the company template declares them
                    // (rectangle, colour, font size, alignment), so every company — not only the
                    // two calibrated ones — prints on the labels of its own artwork.
                    var declared = letterheadCompanyId is > 0
                        ? LetterheadMetaFields.TryRead(FindCompanyTemplate(letterheadCompanyId.Value, isA5 ? "A5" : "A4"))
                        : null;

                    if (declared != null)
                    {
                        _logger.LogDebug(
                            "مقادیر شماره/تاریخ/پیوست از قالب شرکت خوانده شد. CompanyId={CompanyId}, Size={Size}, Color={Color}",
                            letterheadCompanyId, isA5 ? "A5" : "A4", declared.Number.Color);

                        page.Header().ContentFromLeftToRight()
                            .Height(isA5 ? 150 : 160)
                            .Layers(layers =>
                            {
                                layers.PrimaryLayer().Extend();
                                AddDeclaredField(layers.Layer(), declared.Number, number, 0f);
                                AddDeclaredField(layers.Layer(), declared.Date, date, 0f);
                                AddDeclaredField(layers.Layer(), declared.Attachment,
                                    hasAttachment ? "دارد" : "ندارد", 0f);
                            });
                    }
                    else
                    {
                        // No declared geometry in the company template: preserve the previous layout.
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

                    if (signerCards.Count > 0)
                    {
                        // بلوکِ امضاکنندگان در سمتِ چپِ انتهای نامه قرار می‌گیرد.
                        // در صفحهٔ راست‌به‌چپ، آیتمِ نسبیِ اول در سمتِ راست و
                        // آیتمِ ثابتِ بعدی در سمتِ چپ می‌نشیند.
                        col.Item().PaddingTop(isA5 ? 8 : 12).ShowEntire().Row(signersRow =>
                        {
                            signersRow.RelativeItem();
                            signersRow.ConstantItem(signerBlockWidth).Column(signerColumn =>
                            {
                                signerColumn.Item().Text("امضاکنندگان")
                                    .FontFamily("Vazirmatn-Bold").FontSize(smallFontSize)
                                    .AlignCenter().DirectionFromRightToLeft();

                                foreach (var signer in signerCards)
                                    AddSignerCard(signerColumn.Item().PaddingTop(isA5 ? 4 : 6), signer);
                            });
                        });
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
