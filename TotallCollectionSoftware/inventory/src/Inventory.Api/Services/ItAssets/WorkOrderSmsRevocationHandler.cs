using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.ItAssets;

public sealed class WorkOrderSmsRevocationHandler(AppDbContext db) : ICompanySmsRevocationHandler
{
    public string ModuleKey => SmsModuleCatalog.WorkOrders;

    public async Task CancelPendingAsync(int companyId, DateTime nowUtc, CancellationToken ct)
    {
        await db.WorkOrderSmsNotifications
            .Where(n => n.CompanyId == companyId && n.Status == WorkOrderSmsStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, WorkOrderSmsStatus.ModuleDisabled)
                .SetProperty(n => n.Recipient, (string?)null)
                .SetProperty(n => n.CompletedAtUtc, (DateTime?)nowUtc), ct);
    }
}
