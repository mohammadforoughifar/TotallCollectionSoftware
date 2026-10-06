using Inventory.Api.Services.Invoicing;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// پاک‌سازی یک‌بارهٔ راه‌اندازی برای مدل «یک اتصال به ازای هر خدمات‌دهنده».
/// نسخه‌های قدیمی، برای هر خدمات‌دهنده چند ردیف اتصال (نسخه) نگه می‌داشتند؛
/// اینجا برای هر خدمات‌دهنده فقط یک ردیف نگه داشته می‌شود (فعال اول، سپس تازه‌ترین
/// تست‌شده، سپس جدیدترین) و بقیه همراه با فایل کلید خصوصی‌شان (در صورت مدیریت‌شدن
/// توسط سرور) حذف قطعی می‌شوند. همهٔ خطاها فقط گزارش می‌شوند تا راه‌اندازی شکست نخورد.
/// </summary>
public static class MoadianConnectionConsolidationV1
{
    public static async Task EnsureAsync(AppDbContext db, IMoadianProviderPrivateKeyService privateKeys)
    {
        var connections = await db.MoadianProviderConnections.ToListAsync();
        var extras = new List<MoadianProviderConnectionProfile>();
        foreach (var group in connections.GroupBy(x => x.ServiceProviderId))
        {
            if (group.Count() < 2) continue;
            var keep = group
                .OrderByDescending(x => !x.IsDeleted)
                .ThenByDescending(x => x.LastConnectionTestAt ?? DateTime.MinValue)
                .ThenByDescending(x => x.Id)
                .First();
            extras.AddRange(group.Where(x => x.Id != keep.Id));
        }

        if (extras.Count == 0) return;

        foreach (var extra in extras)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(extra.PrivateKeyPath))
                    await privateKeys.RemoveAsync(extra.Id, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] Moadian: فایل کلید خصوصی اتصال تکراری پاک نشد ({ex.GetType().Name})؛ ردیف پایگاه‌داده حذف می‌شود.");
            }

            db.MoadianProviderConnections.Remove(extra);
        }

        await db.SaveChangesAsync();
        Console.WriteLine($"[DB] Moadian: {extras.Count} اتصال تکراری (نسخه‌های قدیمی) حذف شد؛ حالا هر خدمات‌دهنده فقط یک اتصال دارد.");
    }
}
