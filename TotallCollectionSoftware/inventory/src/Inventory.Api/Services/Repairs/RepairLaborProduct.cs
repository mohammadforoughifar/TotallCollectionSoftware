using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Repairs;

/// <summary>کالای خدماتی داخلی برای نگه‌داشتن ردیف مستقل هر کار در فاکتور فروشگاه.</summary>
public static class RepairLaborProduct
{
    public static async Task<int> GetOrCreateAsync(AppDbContext db, int? companyId)
    {
        const string code = "S-REPAIR";
        var existing = await db.Products.FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Code == code);
        if (existing is not null)
        {
            if (!existing.IsService || !existing.IsActive)
                throw new InvalidOperationException("کالای اجرت تعمیرات باید خدمت فعال متعلق به شرکت جاری باشد.");
            return existing.Id;
        }
        var service = new Product
        {
            CompanyId = companyId, Code = code, Name = "اجرت تعمیرات", Unit = "مورد",
            IsService = true, SalePrice = 0, PurchasePrice = 0, IsActive = true, CreatedAt = DateTime.Now
        };
        db.Products.Add(service);
        await db.SaveChangesAsync();
        return service.Id;
    }
}
