using System.Data;
using System.Drawing;
using System.Globalization;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
using Stimulsoft.Report;
using Stimulsoft.Report.Dictionary;

namespace Inventory.Api.Services.Office.Outgoing;

/// <summary>
/// رندر سروری قالب‌های قدیمی Stimulsoft برای نامه صادره.
/// قالب هر شرکت از مسیر Reports/OutgoingLetters/Companies/{CompanyId} خوانده می‌شود
/// و هیچ اتصال مستقیمی از MRT به SQL Server انجام نمی‌شود.
/// </summary>
public interface IStimulsoftOutgoingLetterPrintService
{
    /// <returns>PDF یا null اگر موتور غیرفعال/قالب ناموجود/رندر ناموفق باشد.</returns>
    Task<byte[]?> TryGeneratePdfAsync(int letterId, string size, CancellationToken cancellationToken = default);
}

public sealed class StimulsoftOutgoingLetterPrintService : IStimulsoftOutgoingLetterPrintService
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StimulsoftOutgoingLetterPrintService> _logger;

    public StimulsoftOutgoingLetterPrintService(
        AppDbContext db,
        IWebHostEnvironment env,
        IConfiguration configuration,
        ILogger<StimulsoftOutgoingLetterPrintService> logger)
    {
        _db = db;
        _env = env;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<byte[]?> TryGeneratePdfAsync(
        int letterId,
        string size,
        CancellationToken cancellationToken = default)
    {
        if (!_configuration.GetValue("Reporting:Stimulsoft:Enabled", true))
            return null;

        size = string.Equals(size, "A5", StringComparison.OrdinalIgnoreCase) ? "A5" : "A4";

        var letter = await _db.OutgoingLetters.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == letterId && !x.IsDelete, cancellationToken);
        if (letter == null) return null;

        // قالب دارای سربرگ اختصاصی است؛ بدون شرکت نباید قالب شرکت دیگری انتخاب شود.
        if (letter.CompanyId is not > 0)
        {
            _logger.LogWarning("چاپ MRT انجام نشد: نامه {LetterId} شرکت صادرکننده ندارد.", letterId);
            return null;
        }

        var company = await _db.SystemCompanies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == letter.CompanyId && x.IsActive, cancellationToken);
        if (company == null)
        {
            _logger.LogWarning("چاپ MRT انجام نشد: شرکت {CompanyId} نامه {LetterId} پیدا نشد یا غیرفعال است.", letter.CompanyId, letterId);
            return null;
        }

        var templatePath = ResolveTemplatePath(company.Id, size);
        if (templatePath == null)
        {
            _logger.LogWarning(
                "قالب MRT پیدا نشد. LetterId={LetterId}, CompanyId={CompanyId}, Size={Size}. مسیر مورد انتظار: Reports/OutgoingLetters/Companies/{CompanyId}/Outgoing-{Size}.mrt",
                letterId, company.Id, size, company.Id, size);
            return null;
        }

        var signers = await _db.OutgoingLetterSigners.AsNoTracking()
            .Include(x => x.User)
            .Where(x => x.SourceId == letterId && !x.IsDelete)
            .OrderBy(x => x.Order).ThenBy(x => x.Id)
            .Take(3)
            .ToListAsync(cancellationToken);

        var sematIds = signers.Where(x => x.SematId.HasValue)
            .Select(x => x.SematId!.Value).Distinct().ToList();
        var sematTitles = await _db.Semats.AsNoTracking()
            .Where(x => sematIds.Contains(x.SematId) && !x.IsDelete)
            .ToDictionaryAsync(
                x => x.SematId,
                x => string.IsNullOrWhiteSpace(x.OnvanMokatebati) ? x.Title : x.OnvanMokatebati!,
                cancellationToken);

        var hasAttachment = await _db.AppAttachments.AsNoTracking()
            .AnyAsync(x => x.Module == "OutgoingLetters" && x.RefId == letterId, cancellationToken);

        try
        {
            using var report = new StiReport();
            report.Load(templatePath);

            // اتصال رمزگذاری‌شده قدیمی A5 هرگز نباید در نسخه وب استفاده شود.
            report.Dictionary.Databases.Clear();

            RegisterLetterData(report, letter);
            SetVariable(report, "dateshamsi", ToPersianDate(letter.DateSadere ?? letter.DateSabt));
            SetVariable(report, "peyvast_String", hasAttachment ? "دارد" : "ندارد");
            // در ساختار فعلی نزدیک‌ترین مفهوم به هامش، رونوشت نامه است.
            SetVariable(report, "hamesh", letter.CopyTo ?? string.Empty);

            for (var index = 0; index < 3; index++)
            {
                var signer = index < signers.Count ? signers[index] : null;
                var suffix = index == 0 ? string.Empty : (index + 1).ToString(CultureInfo.InvariantCulture);
                SetVariable(report, $"Name{index + 1}", signer == null ? string.Empty : FullName(signer.User));
                SetVariable(report, $"SematTitle{index + 1}",
                    signer?.SematId is int sematId && sematTitles.TryGetValue(sematId, out var title)
                        ? title
                        : string.Empty);
                SetVariable(report, $"ImageSignature{suffix}", null);
            }

            // امضای آزمایشی فقط برای امضاکنندگان تاییدشده و فقط با تنظیم صریح فعال می‌شود.
            if (_configuration.GetValue("Reporting:Stimulsoft:UseTestSignature", false))
            {
                var signaturePath = ResolveFile("Resources/reports/test-signature.png");
                if (signaturePath != null)
                {
                    for (var index = 0; index < signers.Count; index++)
                    {
                        if (!signers[index].IsSigned) continue;
                        var suffix = index == 0 ? string.Empty : (index + 1).ToString(CultureInfo.InvariantCulture);
                        // Image از Stream جدا می‌شود تا تا پایان Render معتبر بماند.
                        using var source = Image.FromFile(signaturePath);
                        var signature = new Bitmap(source);
                        SetVariable(report, $"ImageSignature{suffix}", signature);
                    }
                }
            }

            report.Render(false);
            using var output = new MemoryStream();
            report.ExportDocument(StiExportFormat.Pdf, output);

            _logger.LogInformation(
                "نامه {LetterId} با Stimulsoft و قالب {Template} رندر شد.", letterId, templatePath);
            return output.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "رندر Stimulsoft ناموفق بود. LetterId={LetterId}, CompanyId={CompanyId}, Size={Size}, Template={Template}",
                letterId, company.Id, size, templatePath);
            return null;
        }
    }

    private void RegisterLetterData(StiReport report, OutgoingLetter letter)
    {
        var table = new DataTable("DataSource1");
        table.Columns.Add("LetterNumber", typeof(string));
        table.Columns.Add("Date_Letter", typeof(DateTime));
        table.Columns.Add("TextLetter", typeof(string));
        table.Columns.Add("TitleLetter", typeof(string));
        table.Columns.Add("GirandeAsli", typeof(string));

        var number = string.IsNullOrWhiteSpace(letter.SadereNumber)
            ? letter.LetterNumber ?? "—"
            : letter.SadereNumber;

        table.Rows.Add(
            number,
            letter.DateSadere ?? letter.DateSabt,
            letter.Text ?? string.Empty,
            letter.Title,
            BuildReceiver(letter));

        var dataSet = new DataSet("LetterData");
        dataSet.Tables.Add(table);

        // نام NameInSource در دو MRT قدیمی متفاوت بود؛ در زمان اجرا یکسان‌سازی می‌شود.
        // کالکشن DataSources نوع پایه StiDataSource برمی‌گرداند؛ NameInSource فقط
        // روی StiDataTableSource وجود دارد، بنابراین cast صریح لازم است.
        if (report.Dictionary.DataSources["DataSource1"] is StiDataTableSource source)
            source.NameInSource = "LetterData.DataSource1";

        report.RegData("LetterData", dataSet);
    }

    private string? ResolveTemplatePath(int companyId, string size)
    {
        var root = _configuration["Reporting:Stimulsoft:TemplateRoot"]
                   ?? "Reports/OutgoingLetters/Companies";
        return ResolveFile(Path.Combine(root, companyId.ToString(CultureInfo.InvariantCulture), $"Outgoing-{size}.mrt"));
    }

    private string? ResolveFile(string relativePath)
    {
        var clean = relativePath.Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        if (clean.Split(Path.DirectorySeparatorChar).Contains("..")) return null;

        var candidates = new[]
        {
            Path.Combine(_env.ContentRootPath, clean),
            Path.Combine(AppContext.BaseDirectory, clean)
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static void SetVariable(StiReport report, string name, object? value)
    {
        if (report.Dictionary.Variables[name] != null)
            report[name] = value;
    }

    private static string BuildReceiver(OutgoingLetter letter)
    {
        var parts = new[] { letter.ReceiverOrganization, letter.ReceiverTitle, letter.ReceiverName }
            .Where(x => !string.IsNullOrWhiteSpace(x));
        return string.Join(" - ", parts);
    }

    private static string FullName(User? user)
    {
        if (user == null) return string.Empty;
        var value = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(value) ? user.Username : value;
    }

    private static string ToPersianDate(DateTime value)
    {
        var calendar = new PersianCalendar();
        return $"{calendar.GetYear(value):0000}/{calendar.GetMonth(value):00}/{calendar.GetDayOfMonth(value):00}";
    }
}
