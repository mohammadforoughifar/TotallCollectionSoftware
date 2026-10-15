using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
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
//  • سربرگ: فایل PDF ثبت‌شده در SystemCompanies.LetterheadFileName که
//    نسبت به wwwroot مسیر‌دهی می‌شود (مثلاً uploads/letterheads/header.pdf).
//  • متن نامه با QuestPDF (فونت فارسی وزیرمتن، راست‌به‌چپ) تولید و سپس
//    با PDFsharp روی صفحه سربرگ قرار می‌گیرد (Overlay).
//  • اگر شرکت/سربرگ نداشته باشد، نامه بدون پس‌زمینه چاپ می‌شود.
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

public class OutgoingLetterPrintService : IOutgoingLetterPrintService
{
    // Used only to prevent Homepa from ever falling back to Forough's letterhead.
    private const int HomepaCompanyId = 1003;

    private static bool _fontsRegistered;
    private static readonly object FontLock = new();

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IStimulsoftOutgoingLetterPrintService _stimulsoft;
    private readonly ILogger<OutgoingLetterPrintService> _logger;

    public OutgoingLetterPrintService(
        AppDbContext db,
        IWebHostEnvironment env,
        IStimulsoftOutgoingLetterPrintService stimulsoft,
        ILogger<OutgoingLetterPrintService> logger)
    {
        _db = db;
        _env = env;
        _stimulsoft = stimulsoft;
        _logger = logger;
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

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Use the exact letterhead path saved on the selected SystemCompany.</summary>
    private string? ResolveCompanyLetterheadPath(SystemCompany? company)
    {
        var configuredFileName = company?.LetterheadFileName;
        var configuredPath = ResolveLetterheadPath(configuredFileName);
        if (configuredPath != null) return configuredPath;

        // Homepa must never silently receive another company's letterhead.
        if (company?.Id == HomepaCompanyId) return null;

        if (string.IsNullOrWhiteSpace(configuredFileName)
            || configuredFileName.EndsWith(".mrt", StringComparison.OrdinalIgnoreCase))
            return ResolveLetterheadPath("uploads/letterheads/forough-letterhead.pdf");

        return null;
    }

    /// <summary>If PDFsharp cannot import the configured PDF, try its same-name raster derivative.</summary>
    private static string? ResolveRasterSibling(string resolvedLetterheadPath)
    {
        if (!resolvedLetterheadPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return null;

        foreach (var extension in new[] { ".png", ".jpg", ".jpeg" })
        {
            var candidate = Path.ChangeExtension(resolvedLetterheadPath, extension);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    // ==================== تبدیل HTML ادیتور به متن ساده ====================

    /// <summary>متن نامه در ادیتور غنی HTML است — برای PDF به متن ساده با حفظ پاراگراف‌ها تبدیل می‌شود</summary>
    private static string HtmlToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var s = html;
        s = Regex.Replace(s, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"</\s*(p|div|li|h[1-6]|tr)\s*>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<\s*li[^>]*>", "• ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<[^>]+>", "");
        s = WebUtility.HtmlDecode(s);
        s = Regex.Replace(s, @"[ \t]+\n", "\n");
        s = Regex.Replace(s, @"\n{3,}", "\n\n");
        return s.Trim();
    }

    private static string FaDate(DateTime? d)
    {
        if (d is null) return "—";
        var pc = new PersianCalendar();
        var v = d.Value;
        return $"{pc.GetYear(v)}/{pc.GetMonth(v):00}/{pc.GetDayOfMonth(v):00}";
    }

    // ==================== تولید PDF ====================

    /// <summary>
    /// تولید PDF نامه صادره — در دو نسخهٔ «با رونوشت» و «بدون رونوشت».
    /// نسخهٔ بدون رونوشت برای تحویل به سازمان مقصد است؛ نسخهٔ با رونوشت
    /// در پروندهٔ دبیرخانه باقی می‌ماند.
    /// </summary>
    public async Task<byte[]?> GeneratePdfAsync(int letterId, string size, bool withCopy = true)
    {
        // Resolve the letter, its company record and the registered letterhead first.
        // The MRT renderer then loads the matching Companies/{CompanyId} template.
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

        var isHomepaLetter = letterCompanyId == HomepaCompanyId || company?.Id == HomepaCompanyId;
        var companyRecordMissing = hasCompanyId && company == null;
        var letterheadPath = companyRecordMissing ? null : ResolveCompanyLetterheadPath(company);
        if (letterheadPath == null && !isHomepaLetter && !companyRecordMissing)
        {
            var hrLogoPath = await _db.HrMainCompanies.AsNoTracking()
                .Select(c => c.LogoPath)
                .FirstOrDefaultAsync();
            letterheadPath = ResolveLetterheadPath(hrLogoPath);
        }

        _logger.LogInformation(
            "مسیر سربرگ نامه از اطلاعات شرکت خوانده شد. LetterId={LetterId}, CompanyId={CompanyId}, ConfiguredPath={ConfiguredPath}, ResolvedPath={ResolvedPath}, WebRoot={WebRoot}, ContentRoot={ContentRoot}",
            letterId, company?.Id ?? letterCompanyId, company?.LetterheadFileName, letterheadPath, _env.WebRootPath, _env.ContentRootPath);

        // متن‌های کوتاه با قالب MRT چاپ می‌شوند؛ متن‌های طولانی مستقیماً به
        // QuestPDF می‌روند تا امکان صفحه‌های بعدی با سربرگ تکرارشونده وجود داشته باشد.
        var isLongLetter = HtmlToPlainText(letter.Text).Length > 2500;
        var stimulsoftPdf = isLongLetter
            ? null
            : await _stimulsoft.TryGeneratePdfAsync(letterId, size, withCopy);
        if (stimulsoftPdf is { Length: > 0 })
        {
            if (letterheadPath == null)
            {
                _logger.LogError(
                    "سربرگ خروجی MRT پیدا نشد؛ PDF بدون overlay برگردانده شد. LetterId={LetterId}, CompanyId={CompanyId}, ConfiguredPath={ConfiguredPath}",
                    letterId, company?.Id ?? letterCompanyId, company?.LetterheadFileName);
                return stimulsoftPdf;
            }

            try
            {
                _logger.LogInformation(
                    "اعمال مسیر سربرگ ثبت‌شده روی خروجی MRT. LetterId={LetterId}, CompanyId={CompanyId}, Path={LetterheadPath}",
                    letterId, company?.Id ?? letterCompanyId, letterheadPath);
                return OverlayOnLetterhead(stimulsoftPdf, letterheadPath);
            }
            catch (Exception ex)
            {
                var rasterFallback = ResolveRasterSibling(letterheadPath);
                if (rasterFallback != null)
                {
                    try
                    {
                        _logger.LogWarning(ex,
                            "ورود PDF سربرگ ناموفق بود؛ از مشتق تصویری همان مسیر استفاده می‌شود. LetterId={LetterId}, Path={RasterPath}",
                            letterId, rasterFallback);
                        return OverlayOnLetterhead(stimulsoftPdf, rasterFallback);
                    }
                    catch (Exception rasterException)
                    {
                        _logger.LogError(rasterException,
                            "اعمال مشتق تصویری سربرگ نیز ناموفق بود. LetterId={LetterId}, Path={RasterPath}",
                            letterId, rasterFallback);
                    }
                }

                _logger.LogError(ex,
                    "قرار دادن سربرگ ثبت‌شده روی خروجی MRT ناموفق بود. LetterId={LetterId}, CompanyId={CompanyId}, Path={LetterheadPath}",
                    letterId, company?.Id ?? letterCompanyId, letterheadPath);
                return stimulsoftPdf;
            }
        }

        _logger.LogWarning(
            "چاپ نامه {LetterId} با MRT انجام نشد؛ QuestPDF به‌عنوان fallback استفاده می‌شود.",
            letterId);

        var signers = await _db.OutgoingLetterSigners.AsNoTracking()
            .Include(s => s.User)
            .Where(s => s.SourceId == letterId && !s.IsDelete)
            .OrderBy(s => s.Order).ThenBy(s => s.Id)
            .ToListAsync();

        // رونوشت‌گیرندگان از جدول مستقل — منبع اصلی برای چاپ «با رونوشت».
        // برای نامه‌های قدیمی که ردیفی در این جدول ندارند، ستون متنی CopyTo به کار می‌رود.
        var copyTos = await _db.OutgoingLetterCopyToes.AsNoTracking()
            .Where(c => c.OutgoingLetterId == letterId && !c.IsDelete)
            .OrderBy(c => c.RowNo).ThenBy(c => c.Id)
            .ToListAsync();

        var hasAttachment = await _db.AppAttachments.AsNoTracking()
            .AnyAsync(a => a.Module == "OutgoingLetters" && a.RefId == letterId);

        bool isA5 = string.Equals(size, "A5", StringComparison.OrdinalIgnoreCase);

        var contentPdf = BuildContentPdf(letter, signers, hasAttachment, isA5, withCopy, copyTos);

        // Use the company record and registered letterhead path resolved before MRT rendering.
        if (letterheadPath == null)
        {
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
                "اعمال سربرگ نامه. LetterId={LetterId}, CompanyId={CompanyId}, Size={Size}, Path={LetterheadPath}",
                letterId, company?.Id ?? letterCompanyId, size, letterheadPath);
            return OverlayOnLetterhead(contentPdf, letterheadPath);
        }
        catch (Exception ex)
        {
            var rasterFallback = ResolveRasterSibling(letterheadPath);
            if (rasterFallback != null)
            {
                try
                {
                    _logger.LogWarning(ex,
                        "ورود PDF سربرگ ناموفق بود؛ از مشتق تصویری همان مسیر استفاده می‌شود. LetterId={LetterId}, Path={RasterPath}",
                        letterId, rasterFallback);
                    return OverlayOnLetterhead(contentPdf, rasterFallback);
                }
                catch (Exception rasterException)
                {
                    _logger.LogError(rasterException,
                        "اعمال مشتق تصویری سربرگ نیز ناموفق بود. LetterId={LetterId}, Path={RasterPath}",
                        letterId, rasterFallback);
                }
            }

            _logger.LogError(ex,
                "قرار دادن سربرگ ثبت‌شده روی نامه ناموفق بود. LetterId={LetterId}, CompanyId={CompanyId}, Path={LetterheadPath}",
                letterId, company?.Id ?? letterCompanyId, letterheadPath);
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

    /// <summary>ساخت محتوای PDF نامه با فونت وزیرمتن؛ سربرگ ثبت‌شده بعداً به‌صورت overlay درج می‌شود.</summary>
    /// <param name="withCopy">false = نسخهٔ بدون رونوشت (بلوک «رونوشت» چاپ نمی‌شود)</param>
    private byte[] BuildContentPdf(OutgoingLetter letter, List<OutgoingLetterSigner> signers, bool hasAttachment, bool isA5, bool withCopy = true, List<OutgoingLetterCopyTo>? copyTos = null)
    {
        EnsureFonts();

        float fs = isA5 ? 8f : 10.5f;      // اندازه فونت متن
        float fsTitle = isA5 ? 9.5f : 12f; // اندازه فونت موضوع
        float fsSmall = isA5 ? 7f : 9f;

        var text = HtmlToPlainText(letter.Text);
        var number = string.IsNullOrWhiteSpace(letter.SadereNumber) ? (letter.LetterNumber ?? "—") : letter.SadereNumber;
        var date = FaDate(letter.DateSadere ?? letter.DateSabt);

        static string FullName(User? u) =>
            u == null ? "" :
            string.IsNullOrWhiteSpace((u.FirstName ?? "") + (u.LastName ?? "")) ? u.Username : $"{u.FirstName} {u.LastName}".Trim();

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(isA5 ? PageSizes.A5 : PageSizes.A4);

                // حاشیه‌ها طوری انتخاب شده که سربرگ (بالای صفحه) و پاصفحه سربرگ آزاد بماند
                page.MarginTop(isA5 ? 88 : 130);
                page.MarginBottom(isA5 ? 42 : 60);
                page.MarginHorizontal(isA5 ? 30 : 46);

                page.ContentFromRightToLeft();
                page.DefaultTextStyle(x => x.FontFamily("Vazirmatn").FontSize(fs).LineHeight(1.7f));

                page.Header().Column(col =>
                {
                    // بلوک شماره / تاریخ / پیوست — سمت چپ سربرگ‌های رسمی ایرانی
                    col.Item().AlignLeft().Column(meta =>
                    {
                        meta.Item().Text(t =>
                        {
                            t.Span("شماره: ").FontSize(fsSmall);
                            t.Span(number).FontFamily("Vazirmatn-Bold").FontSize(fsSmall);
                        });
                        meta.Item().Text(t =>
                        {
                            t.Span("تاریخ: ").FontSize(fsSmall);
                            t.Span(date).FontFamily("Vazirmatn-Bold").FontSize(fsSmall);
                        });
                        meta.Item().Text(t =>
                        {
                            t.Span("پیوست: ").FontSize(fsSmall);
                            t.Span(hasAttachment ? "دارد" : "ندارد").FontFamily("Vazirmatn-Bold").FontSize(fsSmall);
                        });
                    });

                    col.Item().PaddingTop(isA5 ? 4 : 8);
                });

                page.Content().Column(col =>
                {
                    // گیرنده
                    col.Item().Text(t =>
                    {
                        t.Span("به: ").FontFamily("Vazirmatn-Bold");
                        var to = letter.ReceiverOrganization;
                        if (!string.IsNullOrWhiteSpace(letter.ReceiverName))
                            to += $" — {(string.IsNullOrWhiteSpace(letter.ReceiverTitle) ? "" : letter.ReceiverTitle + " ")}{letter.ReceiverName}";
                        else if (!string.IsNullOrWhiteSpace(letter.ReceiverTitle))
                            to += $" — {letter.ReceiverTitle}";
                        t.Span(to);
                    });

                    // موضوع
                    col.Item().PaddingTop(2).Text(t =>
                    {
                        t.Span("موضوع: ").FontFamily("Vazirmatn-Bold").FontSize(fsTitle);
                        t.Span(letter.Title).FontFamily("Vazirmatn-Bold").FontSize(fsTitle);
                    });

                    // عطف به شماره طرف مقابل
                    if (!string.IsNullOrWhiteSpace(letter.ExternalRefNumber))
                        col.Item().PaddingTop(2).Text($"عطف به شماره: {letter.ExternalRefNumber}").FontSize(fsSmall);

                    col.Item().PaddingVertical(isA5 ? 4 : 7).LineHorizontal(0.7f).LineColor("#999999");

                    // با سلام و احترام + متن نامه
                    if (!string.IsNullOrWhiteSpace(text))
                        col.Item().Text(text).FontSize(fs);

                    // امضا کنندگان — پایین متن، سمت چپ
                    if (signers.Count > 0)
                    {
                        col.Item().PaddingTop(isA5 ? 14 : 24).AlignLeft().Row(row =>
                        {
                            foreach (var s in signers)
                            {
                                row.AutoItem().PaddingRight(isA5 ? 12 : 22).Column(sc =>
                                {
                                var signatureBytes = TryReadSignature(s.User?.SignaturePath);
                                if (signatureBytes != null)
                                    sc.Item().Height(isA5 ? 28 : 42).AlignCenter().Image(signatureBytes).FitHeight();
                                sc.Item().AlignCenter().Text(FullName(s.User)).FontFamily("Vazirmatn-Bold").FontSize(fs);
                                sc.Item().AlignCenter().Text(s.IsSigned ? $"امضا شده — {FaDate(s.DateSigned)}" : "")
                                        .FontSize(fsSmall).FontColor("#444444");
                                });
                            }
                        });
                    }

                    // رونوشت — فقط در نسخهٔ «با رونوشت» چاپ می‌شود.
                    // نسخهٔ «بدون رونوشت» مخصوص تحویل به سازمان مقصد است و
                    // اطلاعِ رونوشت‌گیرندگان داخلی در آن درج نمی‌شود.
                    var hasCopyRows = copyTos != null && copyTos.Count > 0;
                    if (withCopy && (hasCopyRows || !string.IsNullOrWhiteSpace(letter.CopyTo)))
                    {
                        col.Item().PaddingTop(isA5 ? 8 : 14).LineHorizontal(0.5f).LineColor("#cbd5e1");
                        col.Item().PaddingTop(isA5 ? 4 : 6).Text(t =>
                        {
                            t.Span("رونوشت: ").FontFamily("Vazirmatn-Bold").FontSize(fsSmall);
                        });

                        if (hasCopyRows)
                        {
                            // هر رونوشت‌گیرنده در یک سطر جداگانه — خواناتر از یک رشتهٔ خام
                            foreach (var ct in copyTos)
                            {
                                var parts = new[] { ct.Desc, ct.RefNo }
                                    .Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
                                var extra = parts.Length == 0 ? "" : string.Join(" — ", parts);

                                col.Item().PaddingTop(2).Text(t =>
                                {
                                    t.Span("• ").FontSize(fsSmall);
                                    t.Span(ct.Name).FontSize(fsSmall);
                                    if (extra != "")
                                        t.Span($"  ({extra})").FontSize(fsSmall).FontColor("#555555");
                                });
                            }
                        }
                        else
                        {
                            // نامهٔ قدیمی: هنوز ردیفی در جدول ندارد — متن آزادِ ستون CopyTo
                            col.Item().PaddingTop(2).Text(t =>
                            {
                                t.Span(letter.CopyTo!).FontSize(fsSmall);
                            });
                        }
                    }
                });

                page.Footer().Column(f =>
                {
                    // نشانهٔ نسخهٔ چاپ — دبیرخانه باید بتواند دو نسخه را از هم تشخیص دهد
                    f.Item().AlignLeft().Text(withCopy ? "نسخه: با رونوشت" : "نسخه: بدون رونوشت")
                        .FontSize(fsSmall - 2).FontColor("#9ca3af");

                    f.Item().AlignCenter().Text(t =>
                    {
                        t.Span($"شماره صادره: {number}   ").FontSize(fsSmall - 1).FontColor("#777777");
                        t.CurrentPageNumber().FontSize(fsSmall - 1).FontColor("#777777");
                        t.Span(" از ").FontSize(fsSmall - 1).FontColor("#777777");
                        t.TotalPages().FontSize(fsSmall - 1).FontColor("#777777");
                    });
                });
            });
        });

        return doc.GeneratePdf();
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
