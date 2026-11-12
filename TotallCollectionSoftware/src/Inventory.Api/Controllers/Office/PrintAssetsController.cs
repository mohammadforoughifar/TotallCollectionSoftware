using System.Xml.Linq;
using Inventory.Api.Data;
using Inventory.Api.Infrastructure;
using Inventory.Api.Services.Office;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers.Office;

/// <summary>
/// «چرا چاپ روی سربرگ شرکت انجام نمی‌شود؟» — یک پاسخ برای هر سه چاپ.
///
/// <para>سه سرویس چاپ (نامهٔ داخلی، نامهٔ صادره، صورتجلسه) از دو فایل ورودی استفاده می‌کنند:
/// قالب MRT شرکت و فایل سربرگ ثبت‌شده روی رکورد شرکت. وقتی یکی از این دو پیدا نشود، چاپ یا
/// بدون سربرگ انجام می‌شود یا با خطا متوقف می‌شود و در لاگ هیچ سرنخی نیست. این نقطهٔ پایانی
/// همهٔ مسیرهایی را که برنامه واقعاً بررسی می‌کند برمی‌گرداند تا بشود دقیقاً دید کدام فایل
/// «انتظار می‌رفت» و کدام «پیدا شد».</para>
///
/// <code>GET /api/print-assets/diagnose?companyId=1005&amp;size=A4</code>
/// </summary>
[ApiController]
[Route("api/print-assets")]
[Authorize]
public class PrintAssetsController : RbacControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;

    public PrintAssetsController(AppDbContext db, IWebHostEnvironment env, IConfiguration configuration)
        : base(db)
    {
        _env = env;
        _configuration = configuration;
    }

    private string?[] Roots => new[]
    {
        _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"),
        _env.ContentRootPath,
        AppContext.BaseDirectory
    };

    [HttpGet("diagnose")]
    public async Task<ActionResult> Diagnose([FromQuery] int companyId, [FromQuery] string? size = "A4",
        [FromQuery] int? minutesId = null)
    {
        // «صورتجلسهٔ ۱۰۰۵ روی سربرگ ۱۰۰۵ چاپ نمی‌شود» — با minutesId می‌شود دید خودِ
        // صورتجلسه چه شرکتی دارد و چاپ واقعاً از کدام شرکت/سربرگ/قالب استفاده می‌کند.
        object? minutes = null;
        if (minutesId is int id and > 0)
        {
            var record = await Db.MeetingMinutes.AsNoTracking()
                .Where(x => x.Id == id && !x.IsDeleted)
                .Select(x => new { x.Id, x.Title, x.CompanyId })
                .FirstOrDefaultAsync();
            if (record == null) return NotFound(new { message = "صورتجلسه پیدا نشد." });

            var headerCompanyId = int.TryParse(
                Request.Headers[ActiveCompanyMiddleware.HeaderName].FirstOrDefault(), out var headerId)
                ? headerId
                : 0;
            var recordCompanyId = record.CompanyId is > 0 ? record.CompanyId.Value : 0;
            var effective = recordCompanyId > 0 ? recordCompanyId
                : headerCompanyId > 0 ? headerCompanyId
                : 0;
            minutes = new
            {
                record.Id,
                record.Title,
                recordCompanyId,
                headerCompanyId,
                effectiveCompanyId = effective,
                source = record.CompanyId is > 0 ? "MeetingMinutes.CompanyId"
                    : headerCompanyId > 0 ? "هدر X-Company-Id (صورتجلسهٔ قدیمیِ بدون شرکت)"
                    : "هیچ — چاپ بدون سربرگ",
                note = record.CompanyId is > 0
                    ? "چاپ روی سربرگ همین شرکت انجام می‌شود."
                    : "این صورتجلسه شرکت ثبت‌شده ندارد؛ سربرگ از شرکت فعال درخواست گرفته می‌شود. " +
                      "برای چاپ روی سربرگ ۱۰۰۵، فیلد شرکت خود صورتجلسه را ۱۰۰۵ کنید."
            };
            if (companyId <= 0) companyId = effective;
        }

        if (companyId <= 0)
            return BadRequest(new { message = "شناسهٔ شرکت معتبر نیست. companyId یا minutesId لازم است." });

        var isA5 = string.Equals(size, "A5", StringComparison.OrdinalIgnoreCase);
        var company = await Db.SystemCompanies.AsNoTracking()
            .Where(c => c.Id == companyId)
            .Select(c => new { c.Id, c.Name, c.Code, c.IsActive, c.LetterheadFileName })
            .FirstOrDefaultAsync();

        var letterheadCandidates = PrintAssetResolver.LetterheadCandidates(company?.LetterheadFileName, Roots);
        var letterhead = PrintAssetResolver.Letterhead(company?.LetterheadFileName, Roots);

        return Ok(new
        {
            size = isA5 ? "A5" : "A4",
            minutes,
            company,
            companyRecordMissing = company == null,
            roots = new
            {
                contentRoot = _env.ContentRootPath,
                baseDirectory = AppContext.BaseDirectory,
                webRoot = _env.WebRootPath,
                currentDirectory = Directory.GetCurrentDirectory()
            },
            letterhead = new
            {
                registeredValue = company?.LetterheadFileName,
                resolved = letterhead,
                renderable = PrintAssetResolver.IsRenderable(letterhead),
                letterheadFolders = PrintAssetResolver.LetterheadFolders(Roots),
                candidates = Describe(letterheadCandidates)
            },
            templates = new
            {
                outgoing = Template(PrintAssetResolver.OutgoingLetters,
                    _configuration["Reporting:Stimulsoft:TemplateRoot"],
                    companyId, isA5, "Report_{0}_OutgoingLetter.mrt", "outgoingletter"),
                inner = Template(PrintAssetResolver.InnerLetters,
                    _configuration["Reporting:InnerLetters:TemplateRoot"],
                    companyId, isA5, "Report_{0}_InnerLetter.mrt", "innerletters"),
                minutes = Template(PrintAssetResolver.MeetingMinutes,
                    _configuration["Reporting:MeetingMinutes:TemplateRoot"],
                    companyId, isA5, "Report_{0}_MeetingMinutes.mrt", "meetingminutes")
            },
            hint = BuildHint(company?.LetterheadFileName, letterhead, companyId, isA5)
        });
    }

    /// <summary>
    /// مسیرهای واقعی قالب یک چاپ: نام استاندارد (<c>Outgoing-A4.mrt</c>، <c>Inner-A4.mrt</c>،
    /// <c>Minutes-A4.mrt</c>) و نام «پوشهٔ طراحی» (<c>Report/outgoingletter/{id}/Report_A4_OutgoingLetter.mrt</c>)
    /// که آخرین جای جست‌وجوی هر سرویس است.
    /// </summary>
    private object Template(string kind, string? configuredRoot, int companyId, bool isA5,
        string designFileName, string designFolder)
    {
        var size = isA5 ? "A5" : "A4";
        var fileName = kind switch
        {
            PrintAssetResolver.OutgoingLetters => $"Outgoing-{size}.mrt",
            PrintAssetResolver.MeetingMinutes => $"Minutes-{size}.mrt",
            _ => $"Inner-{size}.mrt"
        };

        var candidates = PrintAssetResolver
            .TemplateCandidates(kind, companyId, fileName, configuredRoot, _env.ContentRootPath, AppContext.BaseDirectory)
            .Concat(PrintAssetResolver.DesignFolderCandidates(designFolder, companyId,
                string.Format(System.Globalization.CultureInfo.InvariantCulture, designFileName, size),
                _env.ContentRootPath, AppContext.BaseDirectory))
            .ToArray();

        var resolved = PrintAssetResolver.FirstExisting(candidates);
        return new
        {
            expectedFile = fileName,
            resolved,
            found = resolved != null,
            watermarkBytes = resolved == null ? 0 : WatermarkBytes(resolved),
            candidates = Describe(candidates)
        };
    }

    private static int WatermarkBytes(string path)
    {
        try
        {
            using var stream = System.IO.File.OpenRead(path);
            var document = XDocument.Load(stream, LoadOptions.None);
            var encoded = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Watermark")?
                .Elements().FirstOrDefault(element => element.Name.LocalName == "ImageBytes")?.Value;
            return string.IsNullOrWhiteSpace(encoded) ? 0 : Convert.FromBase64String(encoded).Length;
        }
        catch
        {
            // قالب خراب/ناقص: صفر یعنی «سربرگ جاسازی‌شده ندارد» — همان چیزی که چاپ می‌بیند.
            return 0;
        }
    }

    private static object Describe(IReadOnlyList<string> candidates)
        => candidates.Select(path => new { path, exists = System.IO.File.Exists(path) }).ToArray();

    private string BuildHint(string? registeredValue, string? resolvedLetterhead, int companyId, bool isA5)
    {
        if (string.IsNullOrWhiteSpace(registeredValue))
        {
            var template = PrintAssetResolver.FirstExisting(PrintAssetResolver.TemplateCandidates(
                PrintAssetResolver.OutgoingLetters, companyId, $"Outgoing-{(isA5 ? "A5" : "A4")}.mrt",
                _configuration["Reporting:Stimulsoft:TemplateRoot"], _env.ContentRootPath, AppContext.BaseDirectory));
            return template == null
                ? "روی رکورد شرکت هیچ فایل سربرگی ثبت نشده و قالب MRT شرکت هم پیدا نشد؛ چاپ نامهٔ صادره متوقف می‌شود."
                : $"روی رکورد شرکت فایلی ثبت نشده؛ چاپ از سربرگ جاسازی‌شده در قالب استفاده می‌کند: {template}";
        }

        return resolvedLetterhead == null
            ? $"فایل سربرگ «{registeredValue}» در هیچ‌یک از مسیرهای بالا پیدا نشد. نام فایل باید بدون مسیر هم " +
              "قابل شناسایی باشد؛ فایل را در wwwroot/uploads/letterheads بگذارید یا مقدار " +
              "SystemCompanies.LetterheadFileName را به مسیر نسبی کامل اصلاح کنید."
            : $"سربرگ چاپ از این فایل خوانده می‌شود: {resolvedLetterhead}";
    }
}
