#if SAAS
using Inventory.Api.Data;
using Inventory.Licensing.Abstractions;
using Inventory.Licensing.Crypto;
using Inventory.Licensing.Entitlement;
using Inventory.Licensing.Model;
using Inventory.Licensing.Validation;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Licensing.Providers;

/// <summary>
/// منبع لایسنس برای سرویس <b>SaaS</b> — مستأجر (Tenant) مبنای تفکیک است.
///
/// <para>
/// ⚠️ این کلاس فقط در بستهٔ SaaS کامپایل می‌شود (ثابت <c>SAAS</c> در Publish Profile).
/// در بستهٔ نصب داخلی، این فایل کاملاً از اسمبلی حذف می‌شود تا حتی کد «خواندن مجوز از
/// دیتابیس بدون امضا» روی سرور مشتری وجود نداشته باشد.
/// </para>
///
/// <para>
/// تفاوت با <see cref="SignedFileLicenseProvider"/> فقط در <b>دامنه</b> است: این‌جا
/// مجوزها را پنل مدیریت ما در همان دیتابیس مستأجر می‌نویسد و ردیف‌ها با
/// <c>TenantId</c> فیلتر می‌شوند. اعتبارسنجی امضا و ساختار همچنان اجرا می‌شود، چون
/// پنل هم اسناد را با کلید ما امضا می‌کند — «مجوز بی‌امضا» در هیچ مسیری پذیرفته نمی‌شود.
/// </para>
/// </summary>
public sealed class DatabaseLicenseProvider : ILicenseProvider
{
    private readonly AppDbContext _db;
    private readonly PublicKeyRingProvider _keys;
    private readonly IInstallationIdentity _identity;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseLicenseProvider> _log;

    public DatabaseLicenseProvider(AppDbContext db, PublicKeyRingProvider keys,
        IInstallationIdentity identity, IConfiguration configuration, ILogger<DatabaseLicenseProvider> log)
    {
        _db = db;
        _keys = keys;
        _identity = identity;
        _configuration = configuration;
        _log = log;
    }

    public string Name => "Database";

    public async Task<IReadOnlyList<LicenseDocument>> LoadDocumentsAsync(CancellationToken cancellationToken = default)
    {
        // TenantId نصب — در SaaS همیشه مقدار دارد.
        var installationId = await _identity.GetInstallationIdAsync(cancellationToken);
        var tenantId = await _db.LicenseInstallations.AsNoTracking()
            .Where(x => x.InstallationId == installationId)
            .Select(x => x.TenantId)
            .FirstOrDefaultAsync(cancellationToken);

        if (tenantId is null)
        {
            // بدون شناسهٔ مستأجر، «همهٔ» ردیف‌ها خوانده می‌شد که یعنی دیدن لایسنس مستأجرهای
            // دیگر. در این حالت هیچ سندی برنمی‌گردانیم.
            _log.LogError("[License] شناسهٔ مستأجر نصب مشخص نیست؛ هیچ سند SaaS خوانده نشد. (InstallationId={InstallationId})",
                installationId);
            return Array.Empty<LicenseDocument>();
        }

        var grants = await _db.LicenseGrants.AsNoTracking()
            .Where(g => g.Status == "Active" && g.TenantId == tenantId)
            .OrderBy(g => g.AppliedAt)
            .ToListAsync(cancellationToken);

        var context = new LicenseValidationContext
        {
            ExpectedInstallationId = null,          // در SaaS، شناسهٔ نصب ملاک نیست
            ExpectedDeployment = DeploymentKind.Saas,
            Now = DateTime.Now
        };

        var documents = new List<LicenseDocument>();
        foreach (var grant in grants)
        {
            try
            {
                var payload = Convert.FromBase64String(grant.Payload);
                var signature = Convert.FromBase64String(grant.Signature);
                if (!_keys.Ring.Verify(grant.KeyId, payload, signature, grant.Algorithm, out var error))
                {
                    _log.LogError("[License] امضای مجوز {Serial} معتبر نیست: {Error}", grant.Serial, error);
                    continue;
                }

                var document = LicenseJson.ReadDocument(payload);
                if (document is null) continue;
                document.Serial = grant.Serial;
                foreach (var item in document.Items) item.Serial = grant.Serial;

                var errors = LicenseValidator.Validate(document, context)
                    .Where(i => i.Severity == LicenseIssueSeverity.Error).ToList();
                if (errors.Count > 0)
                {
                    _log.LogWarning("[License] مجوز {Serial} اعمال نشد: {Errors}", grant.Serial,
                        string.Join(" | ", errors.Select(e => e.Message)));
                    continue;
                }

                documents.Add(document);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[License] خواندن مجوز {Serial} ناموفق بود.", grant.Serial);
            }
        }

        return documents;
    }
}
#endif
