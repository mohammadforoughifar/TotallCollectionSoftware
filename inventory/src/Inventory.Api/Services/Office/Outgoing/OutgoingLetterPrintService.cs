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
    private readonly ILogger<OutgoingLetterPrintService> _logger;

    public OutgoingLetterPrintService(
        AppDbContext db,
        IWebHostEnvironment env,
        ILogger<OutgoingLetterPrintService> logger)
    {
        _db = db;
        _env = env;
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
            "مسیر سربرگ نامه از اطلاعات شرکت خوانده شد. LetterId={LetterId}, LetterCompanyId={LetterCompanyId}, SelectedCompanyId={SelectedCompanyId}, ConfiguredPath={ConfiguredPath}, ResolvedPath={ResolvedPath}, WebRoot={WebRoot}, ContentRoot={ContentRoot}",
            letterId, letterCompanyId, company?.Id, company?.LetterheadFileName, letterheadPath, _env.WebRootPath, _env.ContentRootPath);

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

        bool isA5 = string.Equals(size, "A5", StringComparison.OrdinalIgnoreCase);

        // Keep the content PDF transparent; the exact company-registered file is
        // placed underneath it by OverlayOnLetterhead below.
        var contentPdf = BuildContentPdf(letter, signers, sematTitles, hasAttachment, isA5, withCopy, copyTos, letterheadPath != null);

        // Use the same company record and letterhead path for all rendered pages.
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

    /// <summary>ساخت محتوای نامه با RTL صریح، فونت وزیرمتن و سربرگ ثبت‌شده.</summary>
    private byte[] BuildContentPdf(
        OutgoingLetter letter,
        List<OutgoingLetterSigner> signers,
        IReadOnlyDictionary<int, string> sematTitles,
        bool hasAttachment,
        bool isA5,
        bool withCopy,
        List<OutgoingLetterCopyTo>? copyTos,
        bool hasLetterhead)
    {
        EnsureFonts();

        var fontSize = isA5 ? 8f : 10.5f;
        var titleFontSize = isA5 ? 9.5f : 12f;
        var smallFontSize = isA5 ? 7f : 9f;
        var lineHeight = isA5 ? 1.5f : 1.55f;

        var plainText = HtmlToPlainText(letter.Text);
        var closingMatch = Regex.Match(
            plainText,
            @"(?:^|\n)[ \t]*(?<closing>با\s*تشکر[^\n]*)\s*$",
            RegexOptions.Multiline);
        var bodyText = closingMatch.Success
            ? plainText[..closingMatch.Index].TrimEnd()
            : plainText;
        var closingText = closingMatch.Success
            ? ToPersianDigits(closingMatch.Groups["closing"].Value.Trim())
            : "با تشکر";

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
        var stampHeight = isA5 ? 24f : 32f;

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
                        card.Item().PaddingTop(5).Height(stampHeight)
                            .Border(0.5f).BorderColor("#9ca3af").AlignCenter().AlignMiddle()
                            .Text("مهر").FontFamily("Vazirmatn").FontSize(smallFontSize)
                            .DirectionFromRightToLeft();
                    });
                }

                // Keep a partial final row aligned to the right on RTL pages.
                row.RelativeItem();
            });
        }

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(isA5 ? PageSizes.A5 : PageSizes.A4);
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

                // The Homepa letterhead already prints these three labels in its
                // blue header. Place values only in the matching blank field to
                // the left of each label; do not overprint duplicate captions.
                if (hasLetterhead)
                {
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
                // closing and signature block on identical left/right margins.
                page.Content().PaddingTop(isA5 ? 40 : 30)
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

                    if (signerRows.Count == 0)
                    {
                        col.Item().PaddingTop(isA5 ? 8 : 12).ShowEntire()
                            .Text(closingText).FontFamily("Vazirmatn-Bold").FontSize(fontSize)
                            .LineHeight(lineHeight).AlignRight().DirectionFromRightToLeft();
                    }
                    else
                    {
                        // Keep the closing and the first signer row together; later rows
                        // also move intact if there is not enough room above the footer.
                        col.Item().PaddingTop(isA5 ? 8 : 12).ShowEntire().Column(signOff =>
                        {
                            signOff.Item().Text(closingText)
                                .FontFamily("Vazirmatn-Bold").FontSize(fontSize)
                                .LineHeight(lineHeight).AlignRight().DirectionFromRightToLeft();
                            signOff.Item().PaddingTop(isA5 ? 8 : 12)
                                .Text("امضاکنندگان")
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
