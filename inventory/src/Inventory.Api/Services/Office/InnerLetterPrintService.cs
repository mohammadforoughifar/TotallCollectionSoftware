using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Inventory.Api.Services.Office;

public interface IInnerLetterPrintService
{
    Task<byte[]?> GeneratePdfAsync(int letterId, string size, int userId, CancellationToken ct = default);
}

public sealed class InnerLetterPrintService : IInnerLetterPrintService
{
    private readonly AppDbContext _db;
    private readonly FileStore _files;
    private readonly IWebHostEnvironment _env;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<InnerLetterPrintService> _log;

    public InnerLetterPrintService(AppDbContext db, FileStore files, IWebHostEnvironment env, IHttpContextAccessor http, ILogger<InnerLetterPrintService> log)
    {
        _db = db; _files = files; _env = env; _http = http; _log = log;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<byte[]?> GeneratePdfAsync(int letterId, string size, int userId, CancellationToken ct = default)
    {
        size = string.Equals(size, "A5", StringComparison.OrdinalIgnoreCase) ? "A5" : "A4";
        var letter = await _db.InnerLetters.AsNoTracking().FirstOrDefaultAsync(x => x.Id == letterId && !x.IsDelete, ct);
        if (letter == null) return null;
        if (!await _db.Erjas.AsNoTracking().AnyAsync(x => x.SourceId == letterId && x.ReciverUserId == userId && !x.IsDelete, ct)
            && letter.CreatorUserId != userId)
            return null;

        var receivers = await _db.Erjas.AsNoTracking().Include(x => x.UserReciver)
            .Where(x => x.SourceId == letterId && !x.IsDelete && (x.Type == "گیرنده" || x.Type == "ارجاع"))
            .OrderBy(x => x.ErjaId).Take(20)
            .Select(x => x.UserReciver == null ? "" : ($"{x.UserReciver.FirstName} {x.UserReciver.LastName}").Trim())
            .ToListAsync(ct);
        var hasAttachment = await _db.AppAttachments.AnyAsync(x => x.Module == "InnerLetters" && x.RefId == letterId, ct);
        var companyId = await ActiveCompanyIdAsync(userId, ct);
        var header = companyId is int cid
            ? await _db.SystemCompanies.AsNoTracking().Where(x => x.Id == cid && x.IsActive).Select(x => x.LetterheadFileName).FirstOrDefaultAsync(ct)
            : null;

        var content = BuildContent(letter, receivers, hasAttachment, size == "A5");
        var headerPath = ResolveLetterheadPath(header);
        if (headerPath == null && (string.IsNullOrWhiteSpace(header) || header.EndsWith(".mrt", StringComparison.OrdinalIgnoreCase)))
            headerPath = ResolveLetterheadPath("uploads/letterheads/forough-letterhead.pdf");
        if (headerPath == null)
        {
            _log.LogWarning("سربرگ نامه داخلی پیدا نشد. LetterId={LetterId}, CompanyId={CompanyId}, File={File}", letterId, companyId, header);
            return content;
        }
        try { return Overlay(content, headerPath); }
        catch (Exception ex) { _log.LogError(ex, "چاپ نامه داخلی روی سربرگ ناموفق بود. LetterId={LetterId}", letterId); return content; }
    }

    private string? ResolveLetterheadPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var clean = value.Replace('\\', '/').TrimStart('/');
        if (clean.Contains("..", StringComparison.Ordinal)) return null;
        var noUploads = clean.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase) ? clean["uploads/".Length..] : clean;
        var candidates = new[]
        {
            _files.ToFull(clean), _files.ToFull(noUploads),
            Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"), clean),
            Path.Combine(_env.ContentRootPath, clean),
            Path.Combine(AppContext.BaseDirectory, clean),
            Path.Combine(AppContext.BaseDirectory, Path.GetFileName(clean))
        };
        return candidates.FirstOrDefault(x => x != null && File.Exists(x));
    }

    private async Task<int?> ActiveCompanyIdAsync(int userId, CancellationToken ct)
    {
        var raw = _http.HttpContext?.Request.Headers["X-Company-Id"].FirstOrDefault();
        if (!int.TryParse(raw, out var id) || id <= 0) return null;
        return await _db.UserCompanyAccesses.AnyAsync(x => x.UserId == userId && x.CompanyId == id && x.Company.IsActive, ct) ? id : null;
    }

    private static byte[] BuildContent(InnerLetter l, List<string> receivers, bool attachment, bool a5)
    {
        var text = Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(l.Text ?? "", "<[^>]+>", " ")), @"\s+", " ").Trim();
        var pc = new PersianCalendar(); var d = l.DateSabt;
        var date = $"{pc.GetYear(d):0000}/{pc.GetMonth(d):00}/{pc.GetDayOfMonth(d):00}";
        var doc = Document.Create(root => root.Page(page =>
        {
            page.Size(a5 ? PageSizes.A5 : PageSizes.A4);
            page.MarginTop(a5 ? 90 : 130); page.MarginBottom(a5 ? 42 : 60); page.MarginHorizontal(a5 ? 30 : 46);
            page.ContentFromRightToLeft(); page.DefaultTextStyle(x => x.FontFamily("Vazirmatn").FontSize(a5 ? 9 : 11).LineHeight(1.7f));
            page.Header().AlignLeft().Column(c => { c.Item().Text($"شماره: {l.LetterNumber ?? "—"}"); c.Item().Text($"تاریخ: {date}"); c.Item().Text($"پیوست: {(attachment ? "دارد" : "ندارد")}"); });
            page.Content().Column(c => { c.Item().Text($"به: {string.Join("، ", receivers.Where(x => !string.IsNullOrWhiteSpace(x)))}").Bold(); c.Item().PaddingTop(6).Text($"موضوع: {l.Title}").Bold(); c.Item().PaddingVertical(8).LineHorizontal(0.7f); c.Item().Text(text); });
            page.Footer().AlignCenter().Text(t => { t.CurrentPageNumber(); t.Span(" از "); t.TotalPages(); });
        }));
        return doc.GeneratePdf();
    }

    private static byte[] Overlay(byte[] content, string headerPath)
    {
        using var input = new MemoryStream(content); using var source = PdfReader.Open(input, PdfDocumentOpenMode.Import); using var output = new PdfDocument();
        var isPdf = headerPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase); using var form = isPdf ? XPdfForm.FromFile(headerPath) : null; using var image = !isPdf ? XImage.FromFile(headerPath) : null; using var cs = new MemoryStream(content); using var contentForm = XPdfForm.FromStream(cs);
        for (var i = 0; i < source.PageCount; i++) { var sp = source.Pages[i]; var p = output.AddPage(); p.Width = sp.Width; p.Height = sp.Height; using var g = XGraphics.FromPdfPage(p); var r = new XRect(0, 0, p.Width.Point, p.Height.Point); if (form != null) { form.PageNumber = 1; g.DrawImage(form, r); } else if (image != null) g.DrawImage(image, r); contentForm.PageNumber = i + 1; g.DrawImage(contentForm, r); }
        using var ms = new MemoryStream(); output.Save(ms); return ms.ToArray();
    }
}
