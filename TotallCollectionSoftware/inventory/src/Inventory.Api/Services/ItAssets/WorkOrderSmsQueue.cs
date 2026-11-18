using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Services.ItAssets;

/// <summary>Builds events inside the work-order transaction; never calls the provider.</summary>
public static class WorkOrderSmsQueue
{
    private sealed record Candidate(User User, string? Mobile, bool HasAccess, WorkOrderSmsStatus Status, string Key);

    internal static async Task StageAsync(AppDbContext db, IKavenegarSmsGateway gateway, KavenegarSmsOptions options,
        WorkOrder order, WorkOrderLog log, IReadOnlyCollection<int> recipientIds, WorkOrderSmsEventType kind,
        CancellationToken ct = default)
    {
        if (order.CompanyId is not > 0 || log.Id <= 0 || log.OrderId != order.Id ||
            !WorkOrderSmsPolicy.MatchesLog(kind, log.Action))
            throw new InvalidOperationException("Work-order SMS requires its own company and business event.");

        var ids = recipientIds.Where(id => id > 0).Distinct().ToArray();
        if (kind == WorkOrderSmsEventType.Reply)
            ids = ids.Where(id => id == order.OwnerUserId).ToArray();
        else
        {
            var assigned = await db.WorkOrderAssignees.AsNoTracking()
                .Where(a => a.OrderId == order.Id && ids.Contains(a.UserId)).Select(a => a.UserId).ToListAsync(ct);
            ids = ids.Where(assigned.Contains).ToArray();
        }
        if (ids.Length == 0) return;

        // This read is inside the serializable business transaction: revoking a permission
        // cannot miss a newly committed Pending event and resurrect it on re-enable.
        // WorkOrderSmsService checked the resolved active company against the order;
        // the usual tenant query filter remains in force for this permission read.
        var revision = await CompanySmsServicePolicy.GetEnabledRevisionAsync(db, order.CompanyId, SmsModuleCatalog.WorkOrders, ct);
        var companyName = await db.SystemCompanies.AsNoTracking().Where(c => c.Id == order.CompanyId && c.IsActive)
            .Select(c => c.Name).SingleOrDefaultAsync(ct);
        var template = WorkOrderSmsPolicy.TemplateName(kind, options);
        var configured = gateway.IsLookupConfigured && KavenegarSmsValidation.TemplateName(template);
        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).OrderBy(u => u.Id).ToListAsync(ct);
        var candidates = new List<Candidate>();
        foreach (var user in users)
        {
            var mobile = KavenegarSmsValidation.NormalizeMobile(user.Mobile);
            var access = user.IsActive && await WorkOrderSmsCompanyAccess.HasAccessAsync(db, user, order.CompanyId.Value, ct);
            var status = WorkOrderSmsPolicy.InitialStatus(user.Mobile, access, revision.HasValue, gateway.IsEnabled,
                configured, companyName, order.Number, order.Title, order.DueAt, kind);
            // The key is stable after the phone is erased on completion. Different users
            // with the same normalized phone get at most one message for this operation.
            var key = WorkOrderRequestIdentity.Hash(mobile is null ? $"user:{user.Id}" : $"phone:{mobile}");
            candidates.Add(new Candidate(user, mobile, access, status, key));
        }
        var existing = (await db.WorkOrderSmsNotifications.AsNoTracking()
            .Where(n => n.CompanyId == order.CompanyId && n.EventLogId == log.Id)
            .Select(n => n.RecipientKey).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        foreach (var row in db.WorkOrderSmsNotifications.Local.Where(n => n.EventLogId == log.Id))
            existing.Add(row.RecipientKey);
        var now = DateTime.UtcNow;
        foreach (var group in candidates.GroupBy(c => c.Key, StringComparer.Ordinal))
        {
            // If two selected users share a phone, prefer one who can access the company;
            // selecting an ineligible user first must not suppress an eligible recipient.
            var chosen = group.OrderByDescending(c => c.HasAccess).ThenBy(c => c.User.Id).First();
            if (!existing.Add(chosen.Key)) continue;
            db.WorkOrderSmsNotifications.Add(new WorkOrderSmsNotification
            {
                CompanyId = order.CompanyId.Value, WorkOrderId = order.Id, EventLogId = log.Id,
                RecipientUserId = chosen.User.Id, RecipientKey = chosen.Key, EventType = kind,
                Status = chosen.Status, PermissionRevision = revision,
                Recipient = chosen.Status == WorkOrderSmsStatus.Pending ? chosen.Mobile : null,
                CreatedAtUtc = now, CompletedAtUtc = chosen.Status == WorkOrderSmsStatus.Pending ? null : now
            });
        }
    }
}

/// <summary>Match active-company middleware rules without trusting a client-selected company.</summary>
public static class WorkOrderSmsCompanyAccess
{
    // SQL-filterable so recipient paging is applied after company access, not before it.
    public static IQueryable<User> EligibleUsers(AppDbContext db, int companyId)
    {
        if (companyId <= 0) return db.Users.AsNoTracking().Where(u => false);
        return db.Users.AsNoTracking().Where(u => u.IsActive &&
            db.SystemCompanies.Any(c => c.Id == companyId && c.IsActive) &&
            (db.UserCompanyAccesses.Any(a => a.UserId == u.Id && a.CompanyId == companyId) ||
             (!db.UserCompanyAccesses.Any(a => a.UserId == u.Id &&
                  db.SystemCompanies.Any(c => c.Id == a.CompanyId && c.IsActive)) &&
              ((db.SystemUsers.Where(s => s.Username == u.Username && s.CompanyId != null &&
                    db.SystemCompanies.Any(c => c.Id == s.CompanyId && c.IsActive))
                    .Select(s => s.CompanyId).Distinct().Count() == 1 &&
                db.SystemUsers.Any(s => s.Username == u.Username && s.CompanyId == companyId)) ||
               (!db.SystemUsers.Any(s => s.Username == u.Username && s.CompanyId != null &&
                    db.SystemCompanies.Any(c => c.Id == s.CompanyId && c.IsActive)) && u.Role == "Admin")))));
    }

    public static Task<bool> HasAccessAsync(AppDbContext db, User user, int companyId, CancellationToken ct = default)
        => EligibleUsers(db, companyId).AnyAsync(u => u.Id == user.Id, ct);

}

/// <summary>One atomic claim, then at most one SDK request; uncertain results are terminal.</summary>
public sealed class WorkOrderSmsDispatcher(AppDbContext db, IKavenegarSmsGateway gateway, IOptions<KavenegarSmsOptions> options)
{
    public async Task MarkAbandonedUnknownAsync(DateTime now, CancellationToken ct = default)
    {
        var cutoff = now.AddMinutes(-10);
        await db.WorkOrderSmsNotifications.Where(n => n.Status == WorkOrderSmsStatus.Sending && n.AttemptStartedAtUtc < cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, WorkOrderSmsStatus.Unknown)
                .SetProperty(n => n.Recipient, (string?)null)
                .SetProperty(n => n.CompletedAtUtc, (DateTime?)now), ct);
    }

    public async Task<bool> ProcessOneAsync(CancellationToken ct = default)
    {
        var id = await db.WorkOrderSmsNotifications.AsNoTracking().Where(n => n.Status == WorkOrderSmsStatus.Pending)
            .OrderBy(n => n.Id).Select(n => (int?)n.Id).FirstOrDefaultAsync(ct);
        if (id is null) return false;
        var started = DateTime.UtcNow;
        var claimed = await db.WorkOrderSmsNotifications.Where(n => n.Id == id && n.Status == WorkOrderSmsStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, WorkOrderSmsStatus.Sending)
                .SetProperty(n => n.AttemptStartedAtUtc, (DateTime?)started), ct);
        if (claimed != 1) return true;
        var job = await db.WorkOrderSmsNotifications.AsNoTracking()
            .SingleOrDefaultAsync(n => n.Id == id && n.Status == WorkOrderSmsStatus.Sending, ct);
        if (job is null) return true;

        var state = WorkOrderSmsStatus.Cancelled;
        KavenegarSmsSendResult? result = null;
        // Worker contexts see every company. Check the original order, operation, recipient,
        // company and unchanged mobile explicitly; never infer a legacy order's company.
        var order = await db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == job.WorkOrderId && w.CompanyId == job.CompanyId)
            .Select(w => new { w.Number, w.Title, w.DueAt, w.OwnerUserId, w.Recurrence, w.RecurrenceSeriesId }).SingleOrDefaultAsync(ct);
        var eventLog = await db.WorkOrderLogs.AsNoTracking()
            .Where(l => l.Id == job.EventLogId && l.OrderId == job.WorkOrderId)
            .Select(l => l.Action).SingleOrDefaultAsync(ct);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == job.RecipientUserId, ct);
        var companyName = await db.SystemCompanies.AsNoTracking()
            .Where(c => c.Id == job.CompanyId && c.IsActive).Select(c => c.Name).SingleOrDefaultAsync(ct);
        var template = WorkOrderSmsPolicy.TemplateName(job.EventType, options.Value);
        var number = WorkOrderSmsPolicy.NumberTokens(order?.Number);
        var title = WorkOrderSmsPolicy.TitleToken(order?.Title);
        var seriesCount = 0;
        if (order is not null && job.EventType == WorkOrderSmsEventType.Registration && eventLog == "Created" &&
            !string.IsNullOrEmpty(order.RecurrenceSeriesId))
        {
            // Only the series' original Created event produces a summary; Scheduled
            // occurrences have no SMS event. Count current, non-deleted occurrences for
            // this recipient, so a removed occurrence/person is not falsely promised.
            seriesCount = await db.WorkOrders.AsNoTracking().Where(w => w.CompanyId == job.CompanyId &&
                w.RecurrenceSeriesId == order.RecurrenceSeriesId && db.WorkOrderAssignees.Any(a =>
                    a.OrderId == w.Id && a.UserId == job.RecipientUserId)).CountAsync(ct);
        }
        var deadline = order is not null && WorkOrderSmsPolicy.RequiresDeadline(job.EventType)
            ? WorkOrderSmsPolicy.DeadlineToken(order.DueAt, order.Recurrence, seriesCount) : null;
        var eligibleRecipient = order is not null && (job.EventType == WorkOrderSmsEventType.Reply
            ? order.OwnerUserId == job.RecipientUserId
            : WorkOrderSmsPolicy.RequiresDeadline(job.EventType) && await db.WorkOrderAssignees.AnyAsync(a =>
                a.OrderId == job.WorkOrderId && a.UserId == job.RecipientUserId, ct));
        if (order is null || user is null || !user.IsActive || !eligibleRecipient || job.Recipient is null ||
            KavenegarSmsValidation.NormalizeMobile(user.Mobile) != job.Recipient ||
            !WorkOrderSmsPolicy.MatchesLog(job.EventType, eventLog))
            state = WorkOrderSmsStatus.Cancelled;
        else if (!await WorkOrderSmsCompanyAccess.HasAccessAsync(db, user, job.CompanyId, ct))
            state = WorkOrderSmsStatus.NoCompanyAccess;
        else if (!await CompanySmsServicePolicy.IsEnabledForRevisionAsync(db, job.CompanyId, SmsModuleCatalog.WorkOrders, job.PermissionRevision, ct))
            state = WorkOrderSmsStatus.ModuleDisabled;
        else if (!gateway.IsEnabled) state = WorkOrderSmsStatus.Disabled;
        else if (!gateway.IsLookupConfigured || !KavenegarSmsValidation.TemplateName(template)) state = WorkOrderSmsStatus.NotConfigured;
        else if (!KavenegarSmsValidation.CompanyToken(companyName)) state = WorkOrderSmsStatus.InvalidCompanyName;
        else if (number is null) state = WorkOrderSmsStatus.InvalidOrderNumber;
        else if (title is null) state = WorkOrderSmsStatus.InvalidTitle;
        else if (WorkOrderSmsPolicy.RequiresDeadline(job.EventType) && deadline is null) state = WorkOrderSmsStatus.InvalidDeadline;
        else if (await db.WorkOrderSmsNotifications.AnyAsync(n => n.Id == job.Id && n.Status == WorkOrderSmsStatus.Sending, ct))
        {
            try
            {
                // Recheck immediately before the network call. A request already handed
                // to the provider cannot be recalled by a concurrent revocation.
                if (!await CompanySmsServicePolicy.IsEnabledForRevisionAsync(db, job.CompanyId, SmsModuleCatalog.WorkOrders, job.PermissionRevision, ct))
                    state = WorkOrderSmsStatus.ModuleDisabled;
                else
                {
                    result = await gateway.SendWorkOrderLookupAsync(job.Recipient, number.Value.Year, number.Value.Serial,
                        companyName!, title, deadline, template!);
                    state = WorkOrderSmsPolicy.Result(result.State);
                }
            }
            catch { state = WorkOrderSmsStatus.Unknown; }
        }
        var done = DateTime.UtcNow;
        var messageId = result?.MessageId;
        var providerCode = result?.ProviderCode;
        var messageStatus = result?.MessageStatus;
        await db.WorkOrderSmsNotifications.Where(n => n.Id == job.Id && n.Status == WorkOrderSmsStatus.Sending)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, state)
                .SetProperty(n => n.Recipient, (string?)null)
                .SetProperty(n => n.ProviderMessageId, messageId)
                .SetProperty(n => n.ProviderCode, providerCode)
                .SetProperty(n => n.ProviderMessageStatus, messageStatus)
                .SetProperty(n => n.CompletedAtUtc, (DateTime?)done), ct);
        return true;
    }
}

public sealed class WorkOrderSmsWorker(IServiceScopeFactory scopes, ILogger<WorkOrderSmsWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<WorkOrderSmsDispatcher>();
                await dispatcher.MarkAbandonedUnknownAsync(DateTime.UtcNow, stoppingToken);
                for (var i = 0; i < 25; i++)
                    if (!await dispatcher.ProcessOneAsync(stoppingToken)) break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogWarning("Work-order SMS queue could not be processed ({ErrorType}).", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
