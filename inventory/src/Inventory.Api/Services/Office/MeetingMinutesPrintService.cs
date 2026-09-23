using System.Globalization;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Inventory.Api.Services.Office;

// ============================================================
//  قالب چاپی صورتجلسه با سربرگ شرکت — A4
//  • سربرگ: فایل PDF/تصویر که نامش در SystemCompanies.LetterheadFileName
//    (تنظیمات ← شرکت) ثبت شده — مثل نامه صادره، محتوا روی سربرگ Overlay می‌شود.
//  • امضای الکترونیکی حاضرین به‌صورت تصویر در صفحهٔ چاپ درج می‌شود.
// ============================================================

public interface IMeetingMinutesPrintService
{
    /// <summary>تولید PDF صورتجلسه روی سربرگ شرکت — اگر سربرگ نباشد بدون پس‌زمینه برمی‌گردد.</summary>
    Task<byte[]?> GeneratePdfAsync(int minutesId);
}

public class MeetingMinutesPrintService : IMeetingMinutesPrintService
{
    private static bool _fontsRegistered;
    private static readonly object FontLock = new();

    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;

    public MeetingMinutesPrintService(AppDbContext db, IWebHostEnvironment env)
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
        var pc = new PersianCalendar();
        var v = d.Value;
        return $"{pc.GetYear(v)}/{pc.GetMonth(v):00}/{pc.GetDayOfMonth(v):00}";
    }

    // ==================== تولید PDF ====================

    public async Task<byte[]?> GeneratePdfAsync(int minutesId)
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

        var contentPdf = BuildContentPdf(m, parts, items);

        // سربرگ شرکت — تنظیمات ← شرکت (SystemCompanies.LetterheadFileName)
        SystemCompany? company = await _db.SystemCompanies.AsNoTracking()
            .Where(c => c.IsActive && c.LetterheadFileName != null && c.LetterheadFileName != "")
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync();

        var letterheadPath = ResolveLetterheadPath(company?.LetterheadFileName);
        if (letterheadPath == null) return contentPdf; // سربرگ موجود نیست

        try
        {
            return OverlayOnLetterhead(contentPdf, letterheadPath);
        }
        catch
        {
            return contentPdf; // فایل سربرگ خراب — چاپ بدون سربرگ
        }
    }

    private static byte[] BuildContentPdf(MeetingMinutes m, List<MeetingMinutesParticipant> parts, List<MeetingMinutesItem> items)
    {
        EnsureFonts();

        float fs = 10f;
        float fsSmall = 8.5f;
        var number = m.Id.ToString();

        var attendees = parts.Where(p => p.Kind == MinutesParticipantKind.Attendee).ToList();
        var absent = parts.Where(p => p.Kind == MinutesParticipantKind.Absent).ToList();

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                // حاشیه‌ها طوری که سربرگ (بالا) و پاصفحه آزاد بماند
                page.MarginTop(120);
                page.MarginBottom(60);
                page.MarginHorizontal(46);

                page.ContentFromRightToLeft();
                page.DefaultTextStyle(x => x.FontFamily("Vazirmatn").FontSize(fs).LineHeight(1.7f));

                page.Header().Column(col =>
                {
                    col.Item().AlignLeft().Column(meta =>
                    {
                        meta.Item().Text(t =>
                        {
                            t.Span("شماره: ").FontSize(fsSmall);
                            t.Span(number).FontFamily("Vazirmatn-Bold").FontSize(fsSmall);
                        });
                        meta.Item().Text(t =>
                        {
                            t.Span("تاریخ جلسه: ").FontSize(fsSmall);
                            t.Span(FaDate(m.MeetingDate)).FontFamily("Vazirmatn-Bold").FontSize(fsSmall);
                        });
                        meta.Item().Text(t =>
                        {
                            t.Span("تاریخ ثبت: ").FontSize(fsSmall);
                            t.Span(FaDate(m.DateRegistered)).FontFamily("Vazirmatn-Bold").FontSize(fsSmall);
                        });
                        meta.Item().Text(t =>
                        {
                            t.Span("وضعیت: ").FontSize(fsSmall);
                            t.Span(MeetingMinutesStatus.ToFa(m.Status)).FontFamily("Vazirmatn-Bold").FontSize(fsSmall);
                        });
                    });
                    col.Item().PaddingTop(10);
                });

                page.Content().Column(col =>
                {
                    // عنوان
                    col.Item().AlignCenter().Text($"صورتجلسه «{m.Title}»")
                        .FontFamily("Vazirmatn-Bold").FontSize(15);
                    col.Item().AlignCenter().Text($"ثبت‌کننده: {m.CreatedByName}")
                        .FontSize(fsSmall).FontColor("#555555");

                    col.Item().PaddingVertical(8).LineHorizontal(0.7f).LineColor("#999999");

                    // حاضرین / غایبین
                    col.Item().Text(t =>
                    {
                        t.Span("حاضرین: ").FontFamily("Vazirmatn-Bold");
                        t.Span(attendees.Count > 0 ? string.Join("، ", attendees.Select(p => p.Name)) : "—");
                    });
                    if (absent.Count > 0)
                        col.Item().Text(t =>
                        {
                            t.Span("غایبین: ").FontFamily("Vazirmatn-Bold");
                            t.Span(string.Join("، ", absent.Select(p => p.Name)));
                        });

                    col.Item().PaddingTop(10).LineHorizontal(0.5f).LineColor("#cbd5e1");

                    // جدول بندها
                    if (items.Count > 0)
                    {
                        col.Item().PaddingTop(6).Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(24);   // ردیف
                                c.RelativeColumn(4);    // شرح
                                c.ConstantColumn(66);   // تاریخ انجام
                                c.RelativeColumn(1.3f);  // مسئول
                                c.ConstantColumn(66);   // تاریخ پیگیری
                                c.RelativeColumn(1.3f);  // مسئول پیگیری
                                c.ConstantColumn(62);   // وضعیت
                            });

                            table.Header(h =>
                            {
                                void Cell(string txt) => h.Cell().Border(0.5f).BorderColor("#999999")
                                    .Background("#eef2f7").PaddingVertical(4).PaddingHorizontal(4)
                                    .AlignCenter().Text(txt).FontFamily("Vazirmatn-Bold").FontSize(fsSmall);
                                Cell("ردیف"); Cell("شرح بند / تصمیم"); Cell("تاریخ انجام");
                                Cell("مسئول"); Cell("تاریخ پیگیری"); Cell("مسئول پیگیری"); Cell("وضعیت");
                            });

                            foreach (var it in items)
                            {
                                table.Cell().Border(0.5f).BorderColor("#cbd5e1").Padding(4).AlignCenter()
                                    .Text(it.RowNo.ToString()).FontSize(fsSmall);
                                table.Cell().Border(0.5f).BorderColor("#cbd5e1").Padding(4)
                                    .Text(it.Description).FontSize(fsSmall);
                                table.Cell().Border(0.5f).BorderColor("#cbd5e1").Padding(4).AlignCenter()
                                    .Text(FaDate(it.DueDate)).FontSize(fsSmall);
                                table.Cell().Border(0.5f).BorderColor("#cbd5e1").Padding(4).AlignCenter()
                                    .Text(it.ResponsibleName ?? "—").FontSize(fsSmall);
                                table.Cell().Border(0.5f).BorderColor("#cbd5e1").Padding(4).AlignCenter()
                                    .Text(FaDate(it.FollowUpDate)).FontSize(fsSmall);
                                table.Cell().Border(0.5f).BorderColor("#cbd5e1").Padding(4).AlignCenter()
                                    .Text(it.FollowUpName ?? "—").FontSize(fsSmall);
                                table.Cell().Border(0.5f).BorderColor("#cbd5e1").Padding(4).AlignCenter()
                                    .Text(MinutesItemStatus.ToFa(it.ItemStatus)).FontSize(fsSmall)
                                    .FontColor(it.ItemStatus == MinutesItemStatus.Done ? "#15803d"
                                        : it.ItemStatus == MinutesItemStatus.Rejected ? "#b91c1c" : "#374151");
                            }
                        });
                    }
                    else
                    {
                        col.Item().PaddingTop(8).Text("— بندی ثبت نشده است —").FontSize(fsSmall).FontColor("#888888");
                    }

                    // ==================== امضای حاضرین ====================
                    if (attendees.Count > 0)
                    {
                        col.Item().PaddingTop(24).LineHorizontal(0.5f).LineColor("#cbd5e1");
                        col.Item().PaddingTop(6).AlignCenter().Text("امضای حاضرین جلسه")
                            .FontFamily("Vazirmatn-Bold").FontSize(11);

                        // هر ردیف تا ۴ امضا
                        var rows = new List<List<MeetingMinutesParticipant>>();
                        for (int i = 0; i < attendees.Count; i += 4)
                            rows.Add(attendees.Skip(i).Take(4).ToList());

                        foreach (var row in rows)
                        {
                            col.Item().PaddingTop(14).Row(r =>
                            {
                                for (var i = 0; i < 4; i++)
                                {
                                    r.RelativeItem().Column(sc =>
                                    {
                                        if (i < row.Count)
                                        {
                                            var p = row[i];
                                            // تصویر امضا
                                            if (!string.IsNullOrWhiteSpace(p.SignatureData))
                                            {
                                                sc.Item().AlignCenter()
                                                .Width(130).Height(55)
                                                .Image(p.SignatureData!, QuestPDF.Infrastructure.ImageScaling.FitArea);
                                            }
                                            else
                                            {
                                                // خط امضای خالی
                                                sc.Item().AlignCenter().Text("________________").FontSize(10).FontColor("#999999");
                                            }
                                            sc.Item().AlignCenter().Text(p.Name).FontFamily("Vazirmatn-Bold").FontSize(fsSmall);
                                            sc.Item().AlignCenter().Text(
                                                    p.SignedAt != null ? $"امضا: {FaDate(p.SignedAt)}" : "امضا")
                                                .FontSize(fsSmall - 1.5f).FontColor("#666666");
                                        }
                                    });
                                }
                            });
                        }
                    }
                });

                page.Footer().Column(f =>
                {
                    f.Item().AlignCenter().Text(t =>
                    {
                        t.Span($"صورتجلسه #{number}   ").FontSize(fsSmall - 1.5f).FontColor("#777777");
                        t.CurrentPageNumber().FontSize(fsSmall - 1.5f).FontColor("#777777");
                        t.Span(" از ").FontSize(fsSmall - 1.5f).FontColor("#777777");
                        t.TotalPages().FontSize(fsSmall - 1.5f).FontColor("#777777");
                    });
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
