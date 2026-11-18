using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Inventory.Api.Services.ItAssets;

public interface IWorkOrderSmsService
{
    // False is only a staging warning; it must not turn a saved work order into a failure.
    Task<bool> TryStageAsync(IDbContextTransaction transaction, WorkOrder order, WorkOrderLog log,
        IReadOnlyCollection<int> recipientIds, WorkOrderSmsEventType kind, int? authorizedCompanyId);
}

/// <summary>
/// Isolates the optional outbox insert with a savepoint, after business changes have been
/// saved and before commit. There is no direct HTTP or legacy gateway sending path here.
/// </summary>
public sealed class WorkOrderSmsService(
    AppDbContext db, IKavenegarSmsGatewayFactory gateways, IOptions<KavenegarSmsOptions> options,
    ILogger<WorkOrderSmsService> logger) : IWorkOrderSmsService
{
    public async Task<bool> TryStageAsync(IDbContextTransaction transaction, WorkOrder order, WorkOrderLog log,
        IReadOnlyCollection<int> recipientIds, WorkOrderSmsEventType kind, int? authorizedCompanyId)
    {
        if (recipientIds.Count == 0) return true;
        // Require the middleware-validated active company as well as the persisted order
        // company. Missing context, old unowned orders and mismatches all fail closed.
        if (authorizedCompanyId is not > 0 || order.CompanyId != authorizedCompanyId ||
            db.ActiveCompanyId != authorizedCompanyId) return false;
        if (!transaction.SupportsSavepoints)
        {
            logger.LogWarning("Work-order SMS staging skipped because transaction savepoints are unavailable.");
            return false;
        }
        const string savepoint = "WorkOrderSmsStage";
        var created = false;
        try
        {
            await transaction.CreateSavepointAsync(savepoint);
            created = true;
            await WorkOrderSmsQueue.StageAsync(db, gateways, options.Value, order, log, recipientIds, kind);
            await db.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            if (created)
            {
                try { await transaction.RollbackToSavepointAsync(savepoint); }
                catch (Exception rollback)
                {
                    // A partial outbox insert must never be committed. If the database cannot
                    // restore the savepoint, the caller must abort the whole transaction.
                    logger.LogWarning("Work-order SMS savepoint rollback failed ({ErrorType}); transaction must be aborted.", rollback.GetType().Name);
                    throw new InvalidOperationException("Work-order SMS staging could not be safely rolled back.", rollback);
                }
            }
            // A failed insert must not be retried accidentally by a later SaveChanges.
            foreach (var entry in db.ChangeTracker.Entries<WorkOrderSmsNotification>()
                .Where(e => e.Entity.EventLogId == log.Id).ToList())
                entry.State = EntityState.Detached;
            logger.LogWarning("Work-order SMS staging was skipped ({ErrorType}).", ex.GetType().Name);
            return false;
        }
    }
}
