using Inventory.Api.Data;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>
/// نوع کالا/خدمت مبنای متفاوتی برای پورسانت دارد. تا وقتی مدل قدیمی نوع هر سطر را
/// جداگانه نگه نمی‌دارد، تغییر نوع کالای دارای فروش با نرخ ثبت‌شده نباید تاریخچه را بازنویسی کند.
/// </summary>
internal static class ShopCommissionProductKindGuard
{
    public static async Task EnsureChangeAllowedAsync(AppDbContext db, Product product, bool newKind)
    {
        if (product.Id == 0 || product.IsService == newKind) return;
        var hasRecordedCommissionPolicy = await db.Transactions.AsNoTracking().AnyAsync(t =>
            t.Type == TransactionType.Sale && (t.ReferrerGoodsPercentSnapshot.HasValue ||
            t.ReferrerServicePercentSnapshot.HasValue) && t.Lines.Any(l => l.ProductId == product.Id));
        if (hasRecordedCommissionPolicy)
            throw new InvalidOperationException("نوع این کالا/خدمت در فروش دارای پورسانت ثبت شده و قابل تغییر نیست؛ برای نوع جدید کالای دیگری تعریف کنید.");
    }
}
