using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Repairs;

public sealed class RepairSmsRevocationHandler(AppDbContext db) : ICompanySmsRevocationHandler
{
    public string ModuleKey => SmsModuleCatalog.ShopRepairs;

    public async Task CancelPendingAsync(int companyId, DateTime nowUtc, CancellationToken ct)
    {
        await db.RepairSmsNotifications
            .Where(n => n.CompanyId == companyId && n.Status == RepairSmsStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, RepairSmsStatus.ModuleDisabled)
                .SetProperty(n => n.Recipient, (string?)null)
                .SetProperty(n => n.CompletedAtUtc, (DateTime?)nowUtc), ct);
    }
}
