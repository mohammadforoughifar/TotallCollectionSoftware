using Inventory.Licensing.Model;
using Inventory.Licensing.Modules;

namespace Inventory.Licensing.Entitlement;

/// <summary>
/// تفکیک لایسنس به «به‌ازای هر شرکت».
///
/// <para>
/// <b>سناریوی واقعی (جریان کارفرما):</b> یک نصب، چند شرکت فعال دارد و هر شرکت ادمین خودش را دارد.
/// ادمین شرکت درخواست لایسنس می‌فرستد، پشتیبان سند را امضا می‌کند، و همان ادمین فایل را در
/// نرم‌افزار بارگذاری می‌کند؛ بعد نقش و کاربر می‌سازد و کاربرانش به‌اندازهٔ ماژول‌های همان سند
/// دسترسی می‌گیرند.
/// </para>
///
/// <para>
/// پیامد مهندسی: نتیجهٔ ادغام «یک شیء برای کل نصب» کافی نیست. اگر دو شرکت هرکدام سند جدا داشته
/// باشند، شرکت الف نباید ماژول‌های سند شرکت ب را ببیند. این کلاس اسناد را بر اساس
/// <c>companyId</c> گروه می‌کند و برای هر شرکت یک <see cref="EffectiveEntitlement"/> مستقل می‌سازد:
/// </para>
/// <list type="bullet">
/// <item>فقط اسنادی که همان شرکت را نام برده‌اند در نظر گرفته می‌شوند.</item>
/// <item>ماژول‌های هر سند با فهرست ماژول همان شرکت <b>اشتراک</b> گرفته می‌شود.</item>
/// <item>سقف کاربر همان شرکت، سقف صندلی سند را محدود می‌کند (اگر شرکت سقف نداده باشد، سقف سند می‌ماند).</item>
/// </list>
///
/// <para>
/// اسنادی که اصلاً فهرست شرکت ندارند (اسناد قدیمی) در <see cref="Unscoped"/> می‌آیند تا گزارش
/// روشن باشد و با یافتهٔ <see cref="UnscopedIssueCode"/> هشدار داده شود؛ این اسناد عمداً به
/// هیچ شرکتی «نسبت داده نمی‌شوند» (نسبت دادنشان یعنی بخشیدن ماژول به همهٔ شرکت‌ها = دور زدن).
/// </para>
/// </summary>
public sealed class CompanyLicenseResolver
{
    /// <summary>کد یافته‌ای که می‌گوید سندی بدون فهرست شرکت روی این نصب هست.</summary>
    public const string UnscopedIssueCode = "LICENSE_WITHOUT_COMPANY";

    private readonly Dictionary<string, EffectiveEntitlement> _byCompany;

    private CompanyLicenseResolver(Dictionary<string, EffectiveEntitlement> byCompany, EffectiveEntitlement? unscoped)
    {
        _byCompany = byCompany;
        Unscoped = unscoped;
    }

    /// <summary>لایسنس مؤثر به‌ازای هر شرکت (کلید = شناسهٔ شرکت، بدون حساسیت به بزرگی/کوچکی حروف).</summary>
    public IReadOnlyDictionary<string, EffectiveEntitlement> ByCompany => _byCompany;

    /// <summary>ادغام اسنادِ بدون فهرست شرکت (سازگاری عقب‌رو)؛ برای گزارش.</summary>
    public EffectiveEntitlement? Unscoped { get; }

    /// <summary>شناسهٔ شرکت‌هایی که دست‌کم یک سند معتبر دارند.</summary>
    public IEnumerable<string> CompanyIds => _byCompany.Keys;

    /// <summary>
    /// لایسنس مؤثر یک شرکت. اگر شرکتی هیچ سندی نداشته باشد، یک Entitlement «بدون لایسنس»
    /// برمی‌گردد (نه null) تا فراخوان مجبور به بررسی null نباشد و پیام یکنواخت باشد.
    /// </summary>
    public EffectiveEntitlement For(string? companyId)
    {
        var id = companyId?.Trim();
        if (!string.IsNullOrEmpty(id) && _byCompany.TryGetValue(id, out var found)) return found;
        return Empty(id);
    }

    /// <summary>آیا این شرکت لایسنسی دارد که حداقل یک ماژول فعال به او بدهد؟</summary>
    public bool HasLicense(string? companyId) => For(companyId).Status == LicenseOverallStatus.Valid;

    /// <summary>میان‌بر تصمیم‌گیری: آیا ماژول برای این شرکت مجاز است؟</summary>
    public bool IsModuleAllowed(string? companyId, string module)
    {
        var ent = For(companyId);
        if (ent.Status != LicenseOverallStatus.Valid) return false;
        return ent.IsModuleAllowedForCompany(companyId, module) && ent.IsReadable(module);
    }

    /// <summary>سقف کاربر مؤثر این شرکت (اگر لایسنس سیاست کاربری داشته باشد).</summary>
    public int? SeatCap(string? companyId)
    {
        var ent = For(companyId);
        if (ent.Status != LicenseOverallStatus.Valid) return 0;
        return ent.Companies.FirstOrDefault(c => string.Equals(c.CompanyId, companyId?.Trim(), StringComparison.OrdinalIgnoreCase))?.MaxUsers
               ?? ent.Seats.MaxForModules(Array.Empty<string>());
    }

    /// <summary>ساخت تفکیک شرکت‌ها از مجموعهٔ اسناد تأییدشده.</summary>
    /// <param name="documents">اسناد تأییدشده (امضا + ساختار).</param>
    /// <param name="now">تاریخ مرجع؛ پیش‌فرض «امروز تهران».</param>
    public static CompanyLicenseResolver Resolve(IReadOnlyCollection<LicenseDocument>? documents, DateTime? now = null)
    {
        var byCompany = new Dictionary<string, EffectiveEntitlement>(StringComparer.OrdinalIgnoreCase);
        if (documents is null || documents.Count == 0)
            return new CompanyLicenseResolver(byCompany, null);

        var at = now?.Date;

        // ---- ۱) گروه‌بندی اسناد بر اساس شرکت ----
        var grouped = new Dictionary<string, List<(LicenseDocument Source, LicenseCompany Company)>>(StringComparer.OrdinalIgnoreCase);
        var unscopedDocs = new List<LicenseDocument>();

        foreach (var doc in documents)
        {
            if (doc.Companies.Count == 0)
            {
                unscopedDocs.Add(doc);
                continue;
            }

            foreach (var company in doc.Companies)
            {
                var id = company.CompanyId?.Trim();
                if (string.IsNullOrEmpty(id)) continue;
                if (!grouped.TryGetValue(id, out var list))
                    grouped[id] = list = new List<(LicenseDocument, LicenseCompany)>();
                list.Add((doc, company));
            }
        }

        // ---- ۲) برای هر شرکت، اسناد را به دید همان شرکت «تصویر» کن ----
        foreach (var (companyId, entries) in grouped)
        {
            var projected = new List<LicenseDocument>(entries.Count);
            var seenSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (source, company) in entries)
            {
                if (!string.IsNullOrWhiteSpace(source.Serial) && !seenSerials.Add(source.Serial))
                    continue;               // همان سند در این گروه دوبار نیاید
                projected.Add(ProjectForCompany(source, company));
            }

            var entitlement = EntitlementMerger.Merge(projected, at);

            // هشدار روشن اگر سندهای بی‌شرکت هم روی نصب باشند: این‌ها به هیچ شرکتی داده نشده‌اند.
            if (unscopedDocs.Count > 0)
            {
                var issues = entitlement.Issues.ToList();
                issues.Add(new LicenseIssue(UnscopedIssueCode, LicenseIssueSeverity.Warning,
                    "روی این نصب سندی هست که فهرست شرکت ندارد؛ آن سند به هیچ شرکتی نسبت داده نمی‌شود " +
                    "(برای استفادهٔ درست، لایسنس باید شرکت را نام ببرد)."));
                entitlement = RebuildWithIssues(entitlement, issues);
            }

            byCompany[companyId] = entitlement;
        }

        var unscoped = unscopedDocs.Count == 0 ? null : EntitlementMerger.Merge(unscopedDocs, at);
        return new CompanyLicenseResolver(byCompany, unscoped);
    }

    /// <summary>
    /// یک سند را «از دید یک شرکت» بازنویسی می‌کند:
    /// ماژول‌ها = اشتراک scope آیتم با ماژول‌های شرکت؛ سقف کاربر = کمینهٔ سقف سند و سقف شرکت.
    /// سند اصلی دست‌نخورده می‌ماند (کپی عمیق دستی — سبک و بدون وابستگی).
    /// </summary>
    private static LicenseDocument ProjectForCompany(LicenseDocument source, LicenseCompany company)
    {
        var companyModules = company.Modules
            .Select(ModuleCodes.Normalize)
            .Where(ModuleCodes.IsKnown)
            .Where(m => !ModuleCodes.IsAlwaysOn(m))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var projected = new LicenseDocument
        {
            Version = source.Version,
            KeyId = source.KeyId,
            Serial = source.Serial,
            CustomerId = source.CustomerId,
            CustomerName = source.CustomerName,
            Deployment = source.Deployment,
            InstallationId = source.InstallationId,
            IssuedAt = source.IssuedAt,
            Note = source.Note,
            Companies = new List<LicenseCompany>
            {
                new()
                {
                    CompanyId = company.CompanyId.Trim(),
                    Name = company.Name,
                    MaxUsers = company.MaxUsers,
                    Modules = companyModules.ToList(),
                    Note = company.Note
                }
            }
        };

        foreach (var item in source.Items)
        {
            var scope = item.Scope
                .Select(ModuleCodes.Normalize)
                .Where(ModuleCodes.IsKnown)
                .Where(m => companyModules.Count == 0 || companyModules.Contains(m) || ModuleCodes.IsAlwaysOn(m))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // ماژول همیشه‌فعال به scope اضافه نمی‌شود؛ خودش بدون لایسنس معتبر است.
            scope.RemoveAll(ModuleCodes.IsAlwaysOn);

            var max = item.Users.Max;
            if (company.MaxUsers is { } companySeats)
                max = max <= 0 ? companySeats : Math.Min(max, companySeats);

            var exempt = Math.Min(item.Users.Exempt, max <= 0 ? 0 : max);
            var exemptUsers = item.Users.ExemptUsers.Take(Math.Max(0, exempt)).ToList();

            projected.Items.Add(new LicenseItem
            {
                Id = item.Id,
                Scope = scope,
                Term = new LicenseTerm
                {
                    Type = item.Term.Type,
                    ValidFrom = item.Term.ValidFrom,
                    ValidUntil = item.Term.ValidUntil,
                    UpdatesUntil = item.Term.UpdatesUntil
                },
                Users = new LicenseUsers
                {
                    Mode = item.Users.Mode,
                    Max = max,
                    AppliesTo = item.Users.AppliesTo,
                    Exempt = exempt,
                    ExemptUsers = exemptUsers
                },
                Quotas = item.Quotas.Select(q => new LicenseQuota
                {
                    Metric = q.Metric,
                    Limit = q.Limit,
                    Period = q.Period,
                    Stackable = q.Stackable,
                    PackId = q.PackId,
                    Grandfather = q.Grandfather
                }).ToList(),
                Note = item.Note,
                Serial = item.Serial
            });
        }

        // آیتمی که بعد از اشتراک هیچ ماژولی ندارد حذف می‌شود؛ ولی اگر همه حذف شوند،
        // یک آیتم خالی می‌گذاریم تا ادغام پیام «بدون ماژول» بدهد نه خطای ساختاری گمراه‌کننده.
        var nonEmpty = projected.Items.Where(i => i.Scope.Count > 0).ToList();
        if (nonEmpty.Count > 0) projected.Items = nonEmpty;

        return projected;
    }

    private static EffectiveEntitlement RebuildWithIssues(EffectiveEntitlement source, List<LicenseIssue> issues) =>
        new()
        {
            Status = source.Status,
            CustomerId = source.CustomerId,
            CustomerName = source.CustomerName,
            Deployment = source.Deployment,
            ComputedAt = source.ComputedAt,
            Items = source.Items,
            Modules = source.Modules,
            Limits = source.Limits,
            Seats = source.Seats,
            Companies = source.Companies,
            CompaniesRestricted = source.CompaniesRestricted,
            Issues = issues,
            UpdatesUntil = source.UpdatesUntil,
            HasPerpetualItem = source.HasPerpetualItem,
            AppliedSerials = source.AppliedSerials
        };

    private static EffectiveEntitlement Empty(string? companyId) => new()
    {
        Status = LicenseOverallStatus.Missing,
        Companies = companyId is null
            ? Array.Empty<CompanyEntitlement>()
            : new[]
            {
                new CompanyEntitlement
                {
                    CompanyId = companyId,
                    Modules = Array.Empty<string>(),
                    MaxUsers = 0
                }
            },
        CompaniesRestricted = companyId is not null,
        Issues = new[]
        {
            new LicenseIssue("COMPANY_LICENSE_MISSING", LicenseIssueSeverity.Error,
                companyId is null
                    ? "هیچ شرکتی مشخص نشده و لایسنس شرکتی هم روی این نصب نیست."
                    : $"برای شرکت «{companyId}» هیچ لایسنس معتبری روی این نصب وجود ندارد؛ ادمین همان شرکت باید فایل .lic خودش را بارگذاری کند.")
        }
    };
}
