using System.Text.Json;
using System.Text.Json.Serialization;
using LicenseSigner.Data;
using Microsoft.EntityFrameworkCore;

namespace LicenseSigner.Services;

/// <summary>
/// پیش‌نویس امضا: از فرم به ردیف DB و از ردیف DB به پروفایل JSON ابزار.
/// قاعدهٔ سخت: <b>مبنای امضا همان پروفایل ذخیره‌شده است</b>، نه ورودی فرم هنگام امضا.
/// </summary>
/// <summary>
/// یک شرکت در پیش‌نویس امضا — پیش از تبدیل به JSON پروفایل.
/// </summary>
/// <param name="CompanyId">شناسهٔ شرکت در نرم‌افزار اصلی.</param>
/// <param name="Name">نام نمایشی (اختیاری).</param>
/// <param name="MaxUsers">سقف کاربر شرکت؛ <c>null</c> = تابع سقف کل سند.</param>
/// <param name="Modules">ماژول‌های مجاز شرکت؛ خالی = همهٔ ماژول‌های سند.</param>
public sealed record CompanyLine(string CompanyId, string? Name, int? MaxUsers, List<string> Modules)
{
    /// <summary>بازگردانی به خط قابل ویرایش؛ فیلدهای خالی انتهایی حذف می‌شوند.</summary>
    public string ToText()
    {
        var parts = new List<string> { CompanyId, Name ?? "", MaxUsers is { } n ? n.ToString() : "", string.Join(",", Modules) };
        while (parts.Count > 1 && parts[^1].Length == 0) parts.RemoveAt(parts.Count - 1);
        // اگر سقف خالی ولی ماژول دارد، جای سقف باید حفظ شود: «شناسه | نام |  | ماژول‌ها»
        if (parts.Count == 3 && parts[2].Length == 0) parts.RemoveAt(2);
        return string.Join(" | ", parts);
    }
}

public sealed class DraftService(SupportDbContext db, SignerOptions options, ILogger<DraftService> log)
{
    /// <summary>سقف تعداد شرکت در یک پیش‌نویس (هم‌راستا با سقف درخواست مشتری).</summary>
    public const int MaxCompanies = 50;

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

        // ---- قابلیت‌های داخلی ماژول ----
        // هر قابلیت باید ماژول والدش در همین پیش‌نویس انتخاب شده باشد؛ وگرنه سند معتبر نیست.
        var selectedModules = SplitCsv(target.ModulesCsv)
            .Select(Inventory.Licensing.Modules.ModuleCodes.Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var features = new List<string>();
        foreach (var rawFeature in SplitCsv(draft.FeaturesCsv))
        {
            var code = Inventory.Licensing.Modules.FeatureCodes.Normalize(rawFeature);
            var feature = Inventory.Licensing.Modules.FeatureCodes.Find(code);
            if (feature is null)
                return (false, $"قابلیت ناشناخته: «{rawFeature}».");

            if (!selectedModules.Contains(Inventory.Licensing.Modules.ModuleCodes.Normalize(feature.Module)))
                return (false, $"قابلیت «{Inventory.Licensing.Modules.FeatureCodes.TitleOf(code)}» بدون ماژول والدش " +
                               $"«{Inventory.Licensing.Modules.ModuleCodes.TitleOf(feature.Module)}» انتخاب شده است.");

            if (!features.Contains(code, StringComparer.OrdinalIgnoreCase)) features.Add(code);
        }
        target.FeaturesCsv = features.Count == 0 ? null : string.Join(",", features);

        // ---- شرکت‌های مجاز ----
        // ورودی فرم یا همین‌جا JSON است (از صفحهٔ پیش‌نویس) یا متن خطی؛ هر دو پشتیبانی می‌شود
        // تا فراخوان‌های برنامه‌ای هم کار کنند.
        List<CompanyLine> companies;
        if (!string.IsNullOrWhiteSpace(draft.CompaniesJson) && draft.CompaniesJson!.TrimStart().StartsWith('['))
        {
            companies = FromCompaniesJson(draft.CompaniesJson);
        }
        else
        {
            var (parsed, parseErrors) = ParseCompanyText(draft.CompaniesJson);
            if (parseErrors.Count > 0)
                return (false, string.Join(" ", parseErrors));
            companies = parsed;
        }

        // ---- قاعدهٔ تک‌شرکتی: هر لایسنس برای یک شرکت ----
        // هویت شرکت (شناسه + نام) از «فایل درخواست» می‌آید؛ اینجا فقط تحمیل و تکمیل می‌شود.
        if (companies.Count > 1)
            return (false, "هر لایسنس فقط برای یک شرکت صادر می‌شود؛ این پیش‌نویس " + companies.Count +
                           " شرکت دارد. برای شرکت‌های دیگر، درخواست/لایسنس جداگانه بسازید.");

        var requestCompany = await RequestCompanyAsync(requestId, ct);
        if (requestCompany is { } required)
        {
            if (companies.Count == 0)
            {
                // شرکت در فرم نیامده ⇒ همان شرکتِ فایل درخواست، بدون پرسش.
                companies.Add(new CompanyLine(required.CompanyId, required.Name, null, new List<string>()));
            }
            else
            {
                var only = companies[0];
                if (!string.Equals(only.CompanyId.Trim(), required.CompanyId.Trim(), StringComparison.OrdinalIgnoreCase))
                    return (false, $"شناسهٔ شرکت پیش‌نویس («{only.CompanyId}») با شناسهٔ شرکت در فایل درخواست " +
                                   $" («{required.CompanyId}») یکی نیست. هر لایسنس فقط برای همان شرکتِ درخواست صادر می‌شود.");

                // نام و شناسه، مرجع‌شان فایل درخواست است؛ نام خالی/متفاوت با نام درخواست جایگزین می‌شود.
                if (string.IsNullOrWhiteSpace(only.Name) && !string.IsNullOrWhiteSpace(required.Name))
                    companies[0] = only with { Name = required.Name };
            }
        }

        // سقف کل کاربران این پیش‌نویس — جمع سقف شرکت‌ها نباید از آن بیشتر شود.
        var seatCap = target.UserMode == "None" ? 0 : Math.Clamp(draft.UserMax, 0, 100_000);
        var allocated = companies.Where(c => c.MaxUsers is > 0).Sum(c => c.MaxUsers!.Value);
        if (seatCap > 0 && allocated > seatCap)
            return (false, $"جمع سقف کاربران شرکت‌ها ({allocated}) از سقف کاربران این پیش‌نویس ({seatCap}) بیشتر است.");
        if (seatCap == 0 && companies.Any(c => c.MaxUsers is > 0))
            return (false, "برای شرکت‌ها سقف کاربر تعیین کرده‌اید ولی سیاست کاربری پیش‌نویس «بدون سقف» است.");

        // ماژول‌های شرکت باید زیرمجموعهٔ ماژول‌های همین پیش‌نویس باشند.
        var draftModules = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var presetCode in SplitCsv(target.PresetsCsv))
        {
            var preset = await db.Presets.AsNoTracking().FirstOrDefaultAsync(p => p.Code == presetCode, ct);
            if (preset?.Json is null) continue;
            foreach (var module in ReadPresetScope(preset.Json)) draftModules[module] = true;
        }
        foreach (var module in SplitCsv(target.ModulesCsv)) draftModules[module.ToLowerInvariant()] = true;

        foreach (var company in companies)
        {
            foreach (var module in company.Modules)
            {
                if (!draftModules.ContainsKey(module))
                    return (false, $"شرکت «{company.CompanyId}» ماژول «{module}» را دارد که در این پیش‌نویس انتخاب نشده است.");
            }
        }

        target.CompaniesJson = ToCompaniesJson(companies);
        target.Note = string.IsNullOrWhiteSpace(draft.Note) ? null : Clamp(draft.Note!, 400);
        target.ByName = actor.DisplayName;
        target.UpdatedAt = DateTime.UtcNow;
        target.ProfileJson = await BuildProfileAsync(request, target, ct);

        if (existing is null) db.Drafts.Add(target);
        await db.SaveChangesAsync(ct);

        await requests.AddEventAsync(requestId, "DRAFT", null, null,
            $"پیش‌نویس امضا ذخیره شد (مدت {target.Months} ماه، کاربر {target.UserMode} {target.UserMax}"
            + (companies.Count > 0 ? $"، شرکت‌های مجاز: {companies.Count}" : "") + ").", actor, ct);
        await db.SaveChangesAsync(ct);
        log.LogInformation("پیش‌نویس درخواست {Id} ذخیره شد.", requestId);
        return (true, null);
    }

    /// <summary>
    /// ساخت پروفایل سازگار با <c>LicenseTool issue --profile</c>.
    ///
    /// نکتهٔ مهم: بسته‌های آماده (presets) به‌جای <c>usePresets</c> به آیتم‌های صریح باز می‌شوند
    /// تا <b>سیاست کاربر یکسان</b> روی همهٔ آیتم‌ها اعمال شود. پیش از این، بسته سیاست خودش را
    /// (مثلاً Named) می‌آورد و آیتم دستی سیاست پنل را؛ نتیجه یک سند با دو سیاست و خطای
    /// USER_POLICY_CONFLICT بود که امضا را متوقف می‌کرد.
    /// </summary>
    private async Task<string> BuildProfileAsync(LicenseRequest request, LicenseDraft draft, CancellationToken ct)
    {
        var termType = draft.TermMode == "perpetual" ? "Perpetual" : "Subscription";
        var usersMode = draft.UserMode is "None" or "Concurrent" or "Named" ? draft.UserMode : "Named";
        var appliesTo = MapUserScope(draft.AppliesTo);

        var term = new Dictionary<string, object?>
        {
            ["type"] = termType,
            ["months"] = draft.TermMode == "months" ? draft.Months ?? 12 : null,
            ["from"] = draft.TermMode == "until" ? JalaliDate.TehranNow().Date : null,
            ["until"] = draft.TermMode == "until" ? draft.Until : null,
            ["updatesUntil"] = draft.UpdatesUntil
        };

        var users = new Dictionary<string, object?>
        {
            ["mode"] = usersMode,
            ["max"] = draft.UserMax,
            ["appliesTo"] = appliesTo,
            ["exempt"] = draft.Exempt
        };

        var items = new List<object>();
        var presetCodes = SplitCsv(draft.PresetsCsv).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        var fallbackPresets = new List<string>();   // بسته‌هایی که JSON‌شان پیدا نشد

        foreach (var code in presetCodes)
        {
            var preset = await db.Presets.AsNoTracking().FirstOrDefaultAsync(p => p.Code == code, ct);
            var scope = preset?.Json is null ? new List<string>() : ReadPresetScope(preset.Json);
            if (scope.Count == 0) { fallbackPresets.Add(code); continue; }

            items.Add(new Dictionary<string, object?>
            {
                ["id"] = code,
                ["scope"] = scope,
                ["term"] = term,
                ["users"] = users
            });
        }

        var explicitScope = SplitCsv(draft.ModulesCsv).ToList();
        var explicitFeatures = SplitCsv(draft.FeaturesCsv)
            .Select(Inventory.Licensing.Modules.FeatureCodes.Normalize)
            .Where(Inventory.Licensing.Modules.FeatureCodes.IsKnown)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (explicitScope.Count > 0 || items.Count == 0)
        {
            items.Add(new Dictionary<string, object?>
            {
                ["id"] = "MANUAL",
                ["scope"] = explicitScope,
                // فهرست خالی نوشته می‌شود، نه null: مقدار صریح null در JSON جای مقدار
                // اولیهٔ مدل ابزار را می‌گیرد و ابزار را با NullReferenceException
                // («ابزار ورودی را نپذیرفت (کد ۲)») متوقف می‌کرد.
                ["features"] = explicitFeatures,
                ["term"] = term,
                ["users"] = users
            });
        }

        var companies = FromCompaniesJson(draft.CompaniesJson)
            .Select(c => new Dictionary<string, object?>
            {
                ["companyId"] = c.CompanyId,
                ["name"] = c.Name,
                ["maxUsers"] = c.MaxUsers,
                ["modules"] = c.Modules
            })
            .ToList();

        var profile = new Dictionary<string, object?>
        {
            ["customer"] = new Dictionary<string, object?>
            {
                ["id"] = request.Customer?.Code ?? "CUST-PANEL",
                ["name"] = request.Customer?.Name
            },
            ["deployment"] = draft.Deployment,
            ["usePresets"] = fallbackPresets,
            ["items"] = items,
            // همان ملاحظهٔ features: فهرست خالی، نه null — وگرنه ابزار امضا هنگام
            // پیمایش شرکت‌ها خطا می‌دهد.
            ["companies"] = companies,
            ["note"] = draft.Note
        };

        return JsonSerializer.Serialize(profile, ProfileOptions);
    }

    /// <summary>کدهای ماژول یک بستهٔ آماده از JSON آن (فیلد scope).</summary>
    private static List<string> ReadPresetScope(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("scope", out var scope) || scope.ValueKind != JsonValueKind.Array)
                return new List<string>();
            return scope.EnumerateArray()
                .Where(m => m.ValueKind == JsonValueKind.String)
                .Select(m => (m.GetString() ?? "").Trim().ToLowerInvariant())
                .Where(m => m.Length > 0)
                .Distinct().ToList();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
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

    // ==================== شرکت‌های مجاز ====================

    /// <summary>
    /// تجزیهٔ متن شرکت‌ها از فرم پیش‌نویس. هر خط یک شرکت:
    /// <c>companyId | نام | سقف‌کاربر | ماژول۱,ماژول۲</c>
    ///
    /// <para>
    /// فقط شناسه اجباری است؛ «|» و فیلدهای بعدی اختیاری‌اند. خط خالی و خطی که با # شروع شود
    /// نادیده گرفته می‌شود. حداکثر <see cref="MaxCompanies"/> شرکت.
    /// </para>
    /// </summary>
    public static (List<CompanyLine> Companies, List<string> Errors) ParseCompanyText(string? text)
    {
        var list = new List<CompanyLine>();
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return (list, errors);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var lineNo = 0;

        foreach (var rawLine in lines)
        {
            lineNo++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var parts = line.Split('|', StringSplitOptions.None);
            var id = parts[0].Trim();
            if (id.Length == 0)
            {
                errors.Add($"خط {lineNo}: شناسهٔ شرکت خالی است.");
                continue;
            }

            if (id.Length > 60)
            {
                errors.Add($"خط {lineNo}: شناسهٔ شرکت بیش از ۶۰ نویسه است.");
                continue;
            }

            if (!seen.Add(id))
            {
                errors.Add($"خط {lineNo}: شرکت «{id}» تکراری است.");
                continue;
            }

            var name = parts.Length > 1 && parts[1].Trim().Length > 0 ? parts[1].Trim() : null;
            if (name is { Length: > 200 }) name = name[..200];

            int? seats = null;
            if (parts.Length > 2 && parts[2].Trim().Length > 0)
            {
                if (!int.TryParse(parts[2].Trim(), out var parsed) || parsed < 0)
                    errors.Add($"خط {lineNo}: سقف کاربر «{parts[2].Trim()}» عدد معتبر نیست (خالی = تابع سقف سند).");
                else seats = parsed;
            }

            var modules = new List<string>();
            if (parts.Length > 3)
            {
                foreach (var module in SplitCsv(parts[3]))
                {
                    var code = Inventory.Licensing.Modules.ModuleCodes.Normalize(module);
                    if (!Inventory.Licensing.Modules.ModuleCodes.IsKnown(code))
                    {
                        errors.Add($"خط {lineNo}: ماژول ناشناخته «{module}» برای شرکت «{id}».");
                        continue;
                    }
                    if (Inventory.Licensing.Modules.ModuleCodes.IsAlwaysOn(code)) continue;
                    if (!modules.Contains(code, StringComparer.OrdinalIgnoreCase)) modules.Add(code);
                }
            }

            list.Add(new CompanyLine(id, name, seats, modules));
            if (list.Count > MaxCompanies)
            {
                errors.Add($"تعداد شرکت‌ها بیش از سقف {MaxCompanies} است.");
                break;
            }
        }

        return (list, errors);
    }

    /// <summary>تبدیل فهرست شرکت‌ها به JSON برای ذخیره در پیش‌نویس.</summary>
    public static string? ToCompaniesJson(IEnumerable<CompanyLine> companies)
    {
        var list = companies.ToList();
        if (list.Count == 0) return null;
        return JsonSerializer.Serialize(list.Select(c => new Dictionary<string, object?>
        {
            ["companyId"] = c.CompanyId,
            ["name"] = c.Name,
            ["maxUsers"] = c.MaxUsers,
            ["modules"] = c.Modules
        }), ProfileOptions);
    }

    /// <summary>خواندن فهرست شرکت‌ها از JSON پیش‌نویس (تحمل دادهٔ خراب: خروجی خالی).</summary>
    public static List<CompanyLine> FromCompaniesJson(string? json)
    {
        var list = new List<CompanyLine>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var id = item.TryGetProperty("companyId", out var idEl) ? idEl.GetString()?.Trim() : null;
                if (string.IsNullOrWhiteSpace(id)) continue;
                var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                int? seats = item.TryGetProperty("maxUsers", out var seatsEl) && seatsEl.ValueKind == JsonValueKind.Number
                             && seatsEl.TryGetInt32(out var parsed) ? parsed : null;
                var modules = new List<string>();
                if (item.TryGetProperty("modules", out var modsEl) && modsEl.ValueKind == JsonValueKind.Array)
                    modules.AddRange(modsEl.EnumerateArray().Where(m => m.ValueKind == JsonValueKind.String)
                        .Select(m => (m.GetString() ?? "").Trim()).Where(m => m.Length > 0));
                list.Add(new CompanyLine(id!, name, seats, modules));
            }
        }
        catch (JsonException)
        {
            return new List<CompanyLine>();
        }
        return list;
    }

    /// <summary>متن قابل‌ویرایش شرکت‌ها برای پرکردن textarea.</summary>
    public static string ToCompanyText(IEnumerable<CompanyLine> companies)
        => string.Join("\n", companies.Select(c => c.ToText()));

    /// <summary>
    /// پیشنهاد شرکت‌ها از فایل درخواست مشتری (اگر همراه درخواست فرستاده باشد).
    /// چیزی که مشتری گفته «فهرست شرکت‌های من» است؛ پشتیبان تأیید/ویرایش می‌کند.
    /// </summary>
    public async Task<(List<CompanyLine> Companies, string Source)> SuggestCompaniesAsync(
        int requestId, CancellationToken ct = default)
    {
        var company = await RequestCompanyAsync(requestId, ct);
        if (company is null) return (new List<CompanyLine>(), "");

        // قاعدهٔ محصول: هر لایسنس برای «یک» شرکت ⇒ فقط همان شرکتِ داخل فایل درخواست.
        var line = new CompanyLine(company.Value.CompanyId, company.Value.Name, null, new List<string>());
        return (new List<CompanyLine> { line }, "request");
    }

    /// <summary>
    /// شرکتِ مرجعِ همین درخواست: اول ستون‌های <see cref="LicenseRequest.CompanyId"/>/<c>CompanyName</c>
    /// (که هنگام ثبت از خود فایل درخواست پر می‌شوند)، بعد اولین شرکتِ
    /// <see cref="LicenseRequest.InterestedCompaniesJson"/> برای درخواست‌های قدیمی.
    /// </summary>
    public async Task<(string CompanyId, string? Name)?> RequestCompanyAsync(int requestId, CancellationToken ct = default)
    {
        var request = await db.Requests.AsNoTracking()
            .Where(r => r.Id == requestId)
            .Select(r => new { r.CompanyId, r.CompanyName, r.InterestedCompaniesJson })
            .FirstOrDefaultAsync(ct);

        if (request is null) return null;

        if (!string.IsNullOrWhiteSpace(request.CompanyId))
            return (request.CompanyId!.Trim(), string.IsNullOrWhiteSpace(request.CompanyName) ? null : request.CompanyName!.Trim());

        if (string.IsNullOrWhiteSpace(request.InterestedCompaniesJson)) return null;

        var first = FromCompaniesJson(request.InterestedCompaniesJson).FirstOrDefault();
        return first is null ? null : (first.CompanyId, first.Name);
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
