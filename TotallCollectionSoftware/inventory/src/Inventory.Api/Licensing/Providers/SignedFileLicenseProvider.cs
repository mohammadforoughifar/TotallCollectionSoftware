using Inventory.Api.Data;
using Inventory.Licensing.Abstractions;
using Inventory.Licensing.Crypto;
using Inventory.Licensing.Entitlement;
using Inventory.Licensing.Model;
using Inventory.Licensing.Validation;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Licensing.Providers;

/// <summary>
/// منبع لایسنس برای نصب داخلی (OnPrem): <b>فایل امضاشده</b>.
///
/// <para>
/// مسیر کار: مدیر فایل درخواست را از برنامه می‌گیرد → ما فایل امضاشده را برمی‌گردانیم →
/// مدیر آپلود می‌کند → payload و امضا عیناً در <c>LicenseGrants</c> ذخیره می‌شوند.
/// </para>
///
/// <para>
/// نکتهٔ مهم: امضا در <b>هر بار خواندن</b> دوباره بررسی می‌شود، نه فقط هنگام آپلود.
/// بنابراین اگر کسی مستقیم در دیتابیس payload را عوض کند، امضا نمی‌خواند و سند کنار
/// گذاشته می‌شود. هیچ بررسی آنلاینی وجود ندارد.
/// </para>
/// </summary>
public sealed class SignedFileLicenseProvider : ILicenseProvider
{
    private readonly AppDbContext _db;
    private readonly PublicKeyRingProvider _keys;
    private readonly IInstallationIdentity _identity;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SignedFileLicenseProvider> _log;

    public SignedFileLicenseProvider(AppDbContext db, PublicKeyRingProvider keys,
        IInstallationIdentity identity, IConfiguration configuration, ILogger<SignedFileLicenseProvider> log)
    {
        _db = db;
        _keys = keys;
        _identity = identity;
        _configuration = configuration;
        _log = log;
    }

    public string Name => "SignedFile";

    public async Task<IReadOnlyList<LicenseDocument>> LoadDocumentsAsync(CancellationToken cancellationToken = default)
    {
        var grants = await _db.LicenseGrants.AsNoTracking()
            .Where(g => g.Status == "Active")
            .OrderBy(g => g.AppliedAt)
            .ToListAsync(cancellationToken);

        if (grants.Count == 0) return Array.Empty<LicenseDocument>();

        var installationId = await _identity.GetInstallationIdAsync(cancellationToken);
        var context = new LicenseValidationContext
        {
            ExpectedInstallationId = installationId,
            ExpectedDeployment = LicenseDeployment.FromConfiguration(_configuration),
            Now = DateTime.Now
        };

        var documents = new List<LicenseDocument>();
        foreach (var grant in grants)
        {
            var document = ReadVerified(grant);
            if (document is null) continue;

            var issues = LicenseValidator.Validate(document, context);
            var errors = issues.Where(i => i.Severity == LicenseIssueSeverity.Error).ToList();
            if (errors.Count > 0)
            {
                // سند معتبرِ امضا اما نامعتبر از نظر ساختار/نصب — اعمال نمی‌شود.
                _log.LogWarning("[License] سند {Serial} اعمال نشد: {Errors}", grant.Serial,
                    string.Join(" | ", errors.Select(e => e.Message)));
                continue;
            }

            documents.Add(document);
        }

        return documents;
    }

    /// <summary>خواندن یک grant با بررسی مجدد امضا؛ در صورت خرابی null.</summary>
    private LicenseDocument? ReadVerified(LicenseGrant grant)
    {
        try
        {
            var payload = Convert.FromBase64String(grant.Payload);
            var signature = Convert.FromBase64String(grant.Signature);

            if (!_keys.Ring.Verify(grant.KeyId, payload, signature, grant.Algorithm, out var error))
            {
                _log.LogError("[License] امضای سند {Serial} معتبر نیست و کنار گذاشته شد: {Error}", grant.Serial, error);
                return null;
            }

            var document = LicenseJson.ReadDocument(payload);
            if (document is null)
            {
                _log.LogError("[License] محتوای سند {Serial} خوانده نشد.", grant.Serial);
                return null;
            }

            // شناسهٔ سند و آیتم‌ها از ردیف دیتابیس تثبیت می‌شود تا گزارش‌ها قابل ردیابی باشند.
            document.Serial = grant.Serial;
            foreach (var item in document.Items) item.Serial = grant.Serial;
            return document;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[License] خواندن سند {Serial} با خطا مواجه شد.", grant.Serial);
            return null;
        }
    }
}
