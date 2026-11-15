using System.Text.Json;
using System.Text.Json.Serialization;
using LicenseSigner.Data;
using Microsoft.EntityFrameworkCore;

namespace LicenseSigner.Services;

/// <summary>
/// پیش‌نویس امضا: از فرم به ردیف DB و از ردیف DB به پروفایل JSON ابزار.
/// قاعدهٔ سخت: <b>مبنای امضا همان پروفایل ذخیره‌شده است</b>، نه ورودی فرم هنگام امضا.
/// </summary>
public sealed class DraftService(SupportDbContext db, SignerOptions options, ILogger<DraftService> log)
{
    private static readonly JsonSerializerOptions ProfileOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public Task<LicenseDraft?> CurrentAsync(int requestId, CancellationToken ct = default) =>
        db.Drafts.AsNoTracking().OrderByDescending(d => d.Id).FirstOrDefaultAsync(d => d.RequestId == requestId, ct);

    /// <summary>
    /// پیشنهاد ماژول/بسته برای پیش‌نویس تازه، به‌ترتیب اولویت:
    /// ۱) ماژول‌هایی که مشتری در فایل درخواست خواسته است (InterestedModulesCsv)
    /// ۲) ماژول‌ها و بسته‌های آخرین پیش‌نویسِ امضاشدهٔ همین مشتری (از آخرین لایسنس صادرشده)
    /// خروجی: (ماژول‌ها، بسته‌ها، منبع) — منبع: request | last-license | خالی
    /// </summary>
    public async Task<(List<string> Modules, List<string> Presets, string Source)> SuggestModulesAsync(
        int requestId, CancellationToken ct = default)
    {
        var request = await db.Requests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null) return (new List<string>(), new List<string>(), "");

        var fromRequest = SplitCsv(request.InterestedModulesCsv)
            .Select(m => m.Trim().ToLowerInvariant()).Where(m => m.Length > 0).Distinct().ToList();
        if (fromRequest.Count > 0) return (fromRequest, new List<string>(), "request");

        if (request.CustomerId is not int customerId) return (new List<string>(), new List<string>(), "");

        /* آخرین پرونده‌ای که برای این مشتری لایسنس گرفته است */
        var lastRequestId = await db.IssuedLicenses.AsNoTracking()
            .Where(l => l.CustomerId == customerId)
            .OrderByDescending(l => l.Id)
            .Select(l => (int?)l.RequestId)
            .FirstOrDefaultAsync(ct);
        if (lastRequestId is not int sourceRequestId) return (new List<string>(), new List<string>(), "");

        var previous = await db.Drafts.AsNoTracking()
            .Where(d => d.RequestId == sourceRequestId)
            .OrderByDescending(d => d.Id)
            .FirstOrDefaultAsync(ct);
        if (previous is null) return (new List<string>(), new List<string>(), "");

        var modules = SplitCsv(previous.ModulesCsv)
            .Select(m => m.Trim().ToLowerInvariant()).Where(m => m.Length > 0).Distinct().ToList();
        var presets = SplitCsv(previous.PresetsCsv)
            .Select(p => p.Trim()).Where(p => p.Length > 0).Distinct().ToList();
        if (modules.Count == 0 && presets.Count == 0) return (new List<string>(), new List<string>(), "");

        return (modules, presets, "last-license");
    }

    public async Task<(bool Ok, string? Error)> SaveAsync(int requestId, LicenseDraft draft, SupportUser actor,
        RequestService requests, CancellationToken ct = default)
    {
        var request = await db.Requests.Include(r => r.Customer).FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null) return (false, "درخواست پیدا نشد.");
        if (request.Customer is null) return (false, "مشتری این درخواست مشخص نیست؛ ابتدا مشتری را تعیین کنید.");
        if (Workflow.IsClosed(request.Status))
            return (false, $"در وضعیت «{Ui.Status(request.Status)}» پیش‌نویس قابل ویرایش نیست.");

        var existing = await db.Drafts.FirstOrDefaultAsync(d => d.RequestId == requestId, ct);
        var target = existing ?? new LicenseDraft { RequestId = requestId, CreatedAt = DateTime.UtcNow };
        target.Deployment = Normalize(draft.Deployment, "OnPrem");
        target.TermMode = Normalize(draft.TermMode, "months");
        target.Months = draft.Months;
        target.Until = draft.Until;
        target.UpdatesUntil = draft.UpdatesUntil;
        target.UserMode = Normalize(draft.UserMode, "Named");
        target.UserMax = Math.Clamp(draft.UserMax, 0, 100_000);
        target.Exempt = Math.Clamp(draft.Exempt, 0, 10_000);
        target.AppliesTo = Normalize(draft.AppliesTo, "Global");
        target.PresetsCsv = NormalizeCsv(draft.PresetsCsv);
        target.ModulesCsv = NormalizeCsv(draft.ModulesCsv);
        target.Note = string.IsNullOrWhiteSpace(draft.Note) ? null : Clamp(draft.Note!, 400);
        target.ByName = actor.DisplayName;
        target.UpdatedAt = DateTime.UtcNow;
        target.ProfileJson = BuildProfile(request, target);

        if (existing is null) db.Drafts.Add(target);
        await db.SaveChangesAsync(ct);

        await requests.AddEventAsync(requestId, "DRAFT", null, null,
            $"پیش‌نویس امضا ذخیره شد (مدت {target.Months} ماه، کاربر {target.UserMode} {target.UserMax}).", actor, ct);
        await db.SaveChangesAsync(ct);
        log.LogInformation("پیش‌نویس درخواست {Id} ذخیره شد.", requestId);
        return (true, null);
    }

    /// <summary>ساخت پروفایل سازگار با <c>LicenseTool issue --profile</c>.</summary>
    private static string BuildProfile(LicenseRequest request, LicenseDraft draft)
    {
        var termType = draft.TermMode == "perpetual" ? "Perpetual" : "Subscription";
        var usersMode = draft.UserMode is "None" or "Concurrent" or "Named" ? draft.UserMode : "Named";

        var scope = SplitCsv(draft.ModulesCsv).ToList();
        var appliesTo = MapUserScope(draft.AppliesTo);

        var profile = new Dictionary<string, object?>
        {
            ["customer"] = new Dictionary<string, object?>
            {
                ["id"] = request.Customer?.Code ?? "CUST-PANEL",
                ["name"] = request.Customer?.Name
            },
            ["deployment"] = draft.Deployment,
            ["usePresets"] = SplitCsv(draft.PresetsCsv).ToList(),
            ["items"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["id"] = "MANUAL",
                    ["scope"] = scope,
                    ["term"] = new Dictionary<string, object?>
                    {
                        ["type"] = termType,
                        ["months"] = draft.TermMode == "months" ? draft.Months ?? 12 : null,
                        ["from"] = draft.TermMode == "until" ? JalaliDate.TehranNow().Date : null,
                        ["until"] = draft.TermMode == "until" ? draft.Until : null,
                        ["updatesUntil"] = draft.UpdatesUntil
                    },
                    ["users"] = new Dictionary<string, object?>
                    {
                        ["mode"] = usersMode,
                        ["max"] = draft.UserMax,
                        ["appliesTo"] = appliesTo,
                        ["exempt"] = draft.Exempt
                    }
                }
            },
            ["note"] = draft.Note
        };

        return JsonSerializer.Serialize(profile, ProfileOptions);
    }

    /// <summary>
    /// نگاشت «دامنه» پنل به دامنهٔ شناخته‌شدهٔ سند لایسنس (LicenseUserScope).
    /// ابزار امضا فقط Global و Modules را می‌شناسد؛ PerTenant (حالت SaaS) در سند به Global نگاشت
    /// می‌شود، چون هر مستأجر نصب/لایسنس مستقل خودش را دارد و مرز مستأجر جای دیگری کنترل می‌شود.
    /// </summary>
    public static string MapUserScope(string? appliesTo) => appliesTo switch
    {
        "Modules" => "Modules",
        _ => "Global"   // Global و PerTenant
    };

    /// <summary>فایل‌های لایسنس فعال قبلی همین نصب (برای ادغام در پیش‌نمایش ابزار).</summary>
    public async Task<List<(int Id, string Path)>> PreviousLicenseFilesAsync(string installationId,
        CancellationToken ct = default)
    {
        var rows = await db.IssuedLicenses.AsNoTracking()
            .Where(l => l.InstallationId == installationId && l.Status == LicenseSigner.Data.LicenseStatus.Current)
            .OrderBy(l => l.Id)
            .ToListAsync(ct);

        var list = new List<(int, string)>();
        foreach (var row in rows)
        {
            var path = ResolveLicensePath(row);
            if (path is not null && File.Exists(path)) list.Add((row.Id, path));
        }

        return list;
    }

    public string? ResolveLicensePath(IssuedLicense license)
    {
        if (string.IsNullOrWhiteSpace(license.FilePath)) return null;
        return Path.IsPathRooted(license.FilePath)
            ? license.FilePath
            : Path.Combine(options.EffectiveWorkFolder(), license.FilePath);
    }

    public static IEnumerable<string> SplitCsv(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? Array.Empty<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase);

    private static string? NormalizeCsv(string? csv)
    {
        var items = SplitCsv(csv).ToList();
        return items.Count == 0 ? null : string.Join(",", items);
    }

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string Clamp(string value, int max) => value.Length <= max ? value : value[..max];
}
