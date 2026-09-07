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
//  • سربرگ: فایل PDF که نامش در جدول کمپانی (SystemCompanies.LetterheadFileName)
//    ثبت شده و خود فایل در «مسیر روت API» قرار دارد.
//  • متن نامه با QuestPDF (فونت فارسی وزیرمتن، راست‌به‌چپ) تولید و سپس
//    با PDFsharp روی صفحه سربرگ قرار می‌گیرد (Overlay).
//  • اگر شرکت/سربرگ نداشته باشد، نامه بدون پس‌زمینه چاپ می‌شود.
// ============================================================

public interface IOutgoingLetterPrintService
{
    /// <summary>تولید PDF نامه صادره روی سربرگ شرکت — size: "A4" یا "A5"</summary>
    Task<byte[]?> GeneratePdfAsync(int letterId, string size);
}

public class OutgoingLetterPrintService : IOutgoingLetterPrintService
{
    private static bool _fontsRegistered;
    private static readonly object FontLock = new();

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;

    public OutgoingLetterPrintService(AppDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
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

    /// <summary>یافتن فایل سربرگ شرکت در مسیر روت API — اول ContentRoot بعد BaseDirectory</summary>
    private string? ResolveLetterheadPath(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        // فقط نام فایل — جلوگیری از Path Traversal
        var safe = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(safe)) return null;

        var candidates = new[]
        {
            Path.Combine(_env.ContentRootPath, safe),          // روت پروژه API (توسعه)
            Path.Combine(AppContext.BaseDirectory, safe)       // کنار فایل اجرایی (انتشار)
        };
        return candidates.FirstOrDefault(File.Exists);
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

    public async Task<byte[]?> GeneratePdfAsync(int letterId, string size)
    {
        var letter = await _db.OutgoingLetters.AsNoTracking()
            .Include(l => l.Creator)
            .FirstOrDefaultAsync(l => l.Id == letterId && !l.IsDelete);
        if (letter == null) return null;

        var signers = await _db.OutgoingLetterSigners.AsNoTracking()
            .Include(s => s.User)
            .Where(s => s.SourceId == letterId && !s.IsDelete)
            .OrderBy(s => s.Order).ThenBy(s => s.Id)
            .ToListAsync();

        var hasAttachment = await _db.AppAttachments.AsNoTracking()
            .AnyAsync(a => a.Module == "OutgoingLetters" && a.RefId == letterId);

        SystemCompany? company = null;
        if (letter.CompanyId is > 0)
            company = await _db.SystemCompanies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == letter.CompanyId);

        // اگر نامه شرکت ندارد، اولین شرکت فعالِ دارای سربرگ به‌عنوان پیش‌فرض
        company ??= await _db.SystemCompanies.AsNoTracking()
            .Where(c => c.IsActive && c.LetterheadFileName != null && c.LetterheadFileName != "")
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync();

        bool isA5 = string.Equals(size, "A5", StringComparison.OrdinalIgnoreCase);

        var contentPdf = BuildContentPdf(letter, signers, hasAttachment, isA5);

        // ==================== قرار دادن روی سربرگ ====================
        var letterheadPath = ResolveLetterheadPath(company?.LetterheadFileName);
        if (letterheadPath == null)
            return contentPdf; // سربرگ موجود نیست — خود نامه برگردانده می‌شود

        try
        {
            return OverlayOnLetterhead(contentPdf, letterheadPath);
        }
        catch
        {
            // اگر فایل سربرگ خراب بود، نامه بدون سربرگ چاپ شود تا کار دبیرخانه متوقف نشود
            return contentPdf;
        }
    }

    /// <summary>ساخت PDF متن نامه (بدون پس‌زمینه) — راست‌به‌چپ با فونت وزیرمتن</summary>
    private static byte[] BuildContentPdf(OutgoingLetter letter, List<OutgoingLetterSigner> signers, bool hasAttachment, bool isA5)
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
                                    sc.Item().AlignCenter().Text(FullName(s.User)).FontFamily("Vazirmatn-Bold").FontSize(fs);
                                    sc.Item().AlignCenter().Text(s.IsSigned ? $"امضا شده — {FaDate(s.DateSigned)}" : "")
                                        .FontSize(fsSmall).FontColor("#444444");
                                });
                            }
                        });
                    }

                    // رونوشت
                    if (!string.IsNullOrWhiteSpace(letter.CopyTo))
                    {
                        col.Item().PaddingTop(isA5 ? 8 : 14).Text(t =>
                        {
                            t.Span("رونوشت: ").FontFamily("Vazirmatn-Bold").FontSize(fsSmall);
                            t.Span(letter.CopyTo!).FontSize(fsSmall);
                        });
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span($"شماره صادره: {number}   ").FontSize(fsSmall - 1).FontColor("#777777");
                    t.CurrentPageNumber().FontSize(fsSmall - 1).FontColor("#777777");
                    t.Span(" از ").FontSize(fsSmall - 1).FontColor("#777777");
                    t.TotalPages().FontSize(fsSmall - 1).FontColor("#777777");
                });
            });
        });

        return doc.GeneratePdf();
    }

    /// <summary>قرار دادن صفحات نامه روی صفحه اول PDF سربرگ (PDFsharp Overlay)</summary>
    private static byte[] OverlayOnLetterhead(byte[] contentPdf, string letterheadPath)
    {
        using var contentStream = new MemoryStream(contentPdf);
        using var contentDoc = PdfReader.Open(contentStream, PdfDocumentOpenMode.Import);

        using var output = new PdfDocument();

        // فرم‌های قابل ترسیم
        using var letterheadForm = XPdfForm.FromFile(letterheadPath);
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

            // 1) سربرگ به‌عنوان پس‌زمینه (کشیده تا کل صفحه)
            letterheadForm.PageNumber = 1;
            gfx.DrawImage(letterheadForm, rect);

            // 2) متن نامه روی سربرگ
            contentForm.PageNumber = i + 1;
            gfx.DrawImage(contentForm, rect);
        }

        using var ms = new MemoryStream();
        output.Save(ms);
        return ms.ToArray();
    }
}
