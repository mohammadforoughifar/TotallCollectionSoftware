using Inventory.Api.Data;
using Inventory.Api.Services.ItAssets;
using Microsoft.Extensions.Options;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using DbParty = Inventory.Api.Data.Party;

namespace Inventory.Api.Services.Repairs;

/// <summary>Outbox is committed with the repair, and is never retried after a send starts.</summary>
public static class RepairSmsQueue
{
    public static async Task StageAsync(AppDbContext db, IKavenegarSmsGateway gateway, KavenegarSmsOptions options,
        RepairOrder repair, DbParty? party, RepairSmsEventType kind)
    {
        if (await db.RepairSmsNotifications.AnyAsync(n => n.RepairOrderId == repair.Id && n.EventType == kind)) return;
        // A party from another company, a landline or a missing number can never be a recipient.
        var mobile = party is not null && party.Id == repair.PartyId && party.CompanyId == repair.CompanyId
            ? party.Mobile : null;
        // This read occurs in the repair's serializable transaction. A concurrent revocation
        // therefore cannot miss a newly committed Pending row and resurrect it on re-enable.
        var revision = await CompanySmsServicePolicy.GetEnabledRevisionAsync(db, repair.CompanyId, SmsModuleCatalog.ShopRepairs);
        var companyName = await db.SystemCompanies.AsNoTracking().Where(c => c.Id == repair.CompanyId && c.IsActive)
            .Select(c => c.Name).SingleOrDefaultAsync();
        var template = RepairSmsPolicy.TemplateName(kind, options);
        var configured = gateway.IsLookupConfigured && KavenegarSmsValidation.TemplateName(template);
        var now = DateTime.UtcNow;
        var state = RepairSmsPolicy.InitialStatus(mobile, revision.HasValue, gateway.IsEnabled, configured, companyName, repair.Number);
        db.RepairSmsNotifications.Add(new RepairSmsNotification
        {
            CompanyId = repair.CompanyId, RepairOrderId = repair.Id, PartyId = repair.PartyId,
            EventType = kind, Status = state, PermissionRevision = revision,
            Recipient = state == RepairSmsStatus.Pending ? RepairSmsPolicy.NormalizeMobile(mobile) : null,
            CreatedAtUtc = now, CompletedAtUtc = state == RepairSmsStatus.Pending ? null : now
        });
    }
}

/// <summary>One atomic claim per event, even across server processes. No network call uses a DB transaction.</summary>
public sealed class RepairSmsDispatcher(AppDbContext db, IKavenegarSmsGateway gateway, IOptions<KavenegarSmsOptions> options)
{
    // Much longer than the gateway's 120-second maximum timeout. A crashed in-flight send
    // becomes Unknown, never Pending: it may already have reached the provider.
    public async Task MarkAbandonedUnknownAsync(DateTime now, CancellationToken ct = default)
    {
        var cutoff = now.AddMinutes(-10);
        await db.RepairSmsNotifications
            .Where(n => n.Status == RepairSmsStatus.Sending && n.AttemptStartedAtUtc < cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, RepairSmsStatus.Unknown)
                .SetProperty(n => n.CompletedAtUtc, (DateTime?)now), ct);
    }

    public async Task<bool> ProcessOneAsync(CancellationToken ct = default)
    {
        var id = await db.RepairSmsNotifications.AsNoTracking()
            .Where(n => n.Status == RepairSmsStatus.Pending)
            .OrderBy(n => n.Id).Select(n => (int?)n.Id).FirstOrDefaultAsync(ct);
        if (id is null) return false;
        var started = DateTime.UtcNow;
        var claimed = await db.RepairSmsNotifications
            .Where(n => n.Id == id && n.Status == RepairSmsStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, RepairSmsStatus.Sending)
                .SetProperty(n => n.AttemptStartedAtUtc, (DateTime?)started), ct);
        if (claimed != 1) return true; // another process won the claim

        var job = await db.RepairSmsNotifications.AsNoTracking()
            .SingleOrDefaultAsync(n => n.Id == id && n.Status == RepairSmsStatus.Sending, ct);
        if (job is null) return true; // repair removed, or conservatively marked Unknown

        var state = RepairSmsStatus.Cancelled;
        KavenegarSmsSendResult? result = null;
        // Background contexts see all companies: require the original company, repair,
        // customer AND unchanged mobile explicitly before a single recipient is contacted.
        var repair = await db.RepairOrders.AsNoTracking().Where(r => r.Id == job.RepairOrderId &&
                r.CompanyId == job.CompanyId && r.PartyId == job.PartyId)
            .Select(r => new { r.Number, r.Status }).SingleOrDefaultAsync(ct);
        var party = await db.Parties.AsNoTracking().Where(p => p.Id == job.PartyId && p.CompanyId == job.CompanyId)
            .Select(p => new { p.Mobile }).SingleOrDefaultAsync(ct);
        var companyName = await db.SystemCompanies.AsNoTracking()
            .Where(c => c.Id == job.CompanyId && c.IsActive).Select(c => c.Name).SingleOrDefaultAsync(ct);
        var template = RepairSmsPolicy.TemplateName(job.EventType, options.Value);
        var token = RepairSmsPolicy.NumberToken(repair?.Number);
        if (repair is null || party is null || job.Recipient is null ||
            RepairSmsPolicy.NormalizeMobile(party.Mobile) != job.Recipient ||
            !(job.EventType == RepairSmsEventType.Registration ||
              job.EventType == RepairSmsEventType.Ready && repair.Status == RepairStatus.Ready))
            state = RepairSmsStatus.Cancelled;
        else if (!await CompanySmsServicePolicy.IsEnabledForRevisionAsync(db, job.CompanyId, SmsModuleCatalog.ShopRepairs, job.PermissionRevision, ct))
            state = RepairSmsStatus.ModuleDisabled;
        else if (!gateway.IsEnabled) state = RepairSmsStatus.Disabled;
        else if (!gateway.IsLookupConfigured || !KavenegarSmsValidation.TemplateName(template))
            state = RepairSmsStatus.NotConfigured;
        else if (!KavenegarSmsValidation.CompanyToken(companyName))
            state = RepairSmsStatus.InvalidCompanyName;
        else if (token is null) state = RepairSmsStatus.InvalidRepairNumber;
        else if (await db.RepairSmsNotifications.AnyAsync(n => n.Id == job.Id && n.Status == RepairSmsStatus.Sending, ct))
        {
            try
            {
                // Recheck the exact company/module immediately before handing the request to
                // the SDK. A request already in flight cannot be recalled by a later revocation.
                if (!await CompanySmsServicePolicy.IsEnabledForRevisionAsync(db, job.CompanyId, SmsModuleCatalog.ShopRepairs, job.PermissionRevision, ct))
                    state = RepairSmsStatus.ModuleDisabled;
                else
                {
                    result = await gateway.SendLookupAsync(job.Recipient, token, companyName!, template!);
                    state = RepairSmsPolicy.Result(result.State);
                }
            }
            catch { state = RepairSmsStatus.Unknown; }
        }
        var done = DateTime.UtcNow;
        var messageId = result?.MessageId;
        var providerCode = result?.ProviderCode;
        var messageStatus = result?.MessageStatus;
        await db.RepairSmsNotifications.Where(n => n.Id == job.Id && n.Status == RepairSmsStatus.Sending)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, state)
                .SetProperty(n => n.ProviderMessageId, messageId)
                .SetProperty(n => n.ProviderCode, providerCode)
                .SetProperty(n => n.ProviderMessageStatus, messageStatus)
                .SetProperty(n => n.CompletedAtUtc, (DateTime?)done), ct);
        return true;
    }
}

public sealed class RepairSmsWorker(IServiceScopeFactory scopes, ILogger<RepairSmsWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<RepairSmsDispatcher>();
                await dispatcher.MarkAbandonedUnknownAsync(DateTime.UtcNow, stoppingToken);
                for (var i = 0; i < 25; i++)
                    if (!await dispatcher.ProcessOneAsync(stoppingToken)) break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogWarning("Repair SMS queue could not be processed ({ErrorType}).", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
