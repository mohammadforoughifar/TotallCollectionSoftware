using Inventory.Api.Data;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>
/// Reusable company AND module permission for integrated SMS senders. Background workers
/// supply the company persisted on the business event, never a user or default company.
/// A durable outbox captures the returned revision and requires it to match at dispatch;
/// events created before opt-in or before an off/on cycle cannot become eligible later.
/// </summary>
public static class CompanySmsServicePolicy
{
    public static async Task<long?> GetEnabledRevisionAsync(AppDbContext db, int? companyId, string moduleKey,
        CancellationToken ct = default)
    {
        if (companyId is not > 0 || !SmsModuleCatalog.Contains(moduleKey)) return null;
        var revision = await db.CompanySmsSettings.AsNoTracking()
            .Where(s => s.CompanyId == companyId.Value && s.ModuleKey == moduleKey && s.Enabled &&
                db.SystemCompanies.Any(c => c.Id == companyId.Value && c.IsActive))
            .Select(s => (long?)s.Revision).SingleOrDefaultAsync(ct);
        return revision is > 0 ? revision : null;
    }

    public static async Task<bool> IsEnabledForRevisionAsync(AppDbContext db, int? companyId, string moduleKey,
        long? expectedRevision, CancellationToken ct = default) =>
        expectedRevision is > 0 && await GetEnabledRevisionAsync(db, companyId, moduleKey, ct) == expectedRevision;
}
