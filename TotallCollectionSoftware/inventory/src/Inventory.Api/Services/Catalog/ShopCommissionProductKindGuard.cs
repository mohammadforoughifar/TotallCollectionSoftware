using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
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
        // The warranty source and its linked stock/sale classify the row as goods
        // or service. Changing that classification would rewrite its history.
        var inWarranty = await db.ShopWarrantyItems.AnyAsync(i => i.ProductId == product.Id) ||
            await db.RepairItems.AnyAsync(i => i.ProductId == product.Id && i.WarrantyDecision != WarrantyDecision.Unspecified) ||
            await db.ShopWarranties.AnyAsync(w => w.ChargeTransactionId.HasValue &&
                db.TransactionLines.Any(l => l.TransactionId == w.ChargeTransactionId && l.ProductId == product.Id)) ||
            await db.RepairOrders.AnyAsync(r => r.WarrantyProcessed && r.InvoiceTransactionId.HasValue &&
                db.TransactionLines.Any(l => l.TransactionId == r.InvoiceTransactionId && l.ProductId == product.Id));
        if (inWarranty)
            throw new InvalidOperationException("نوع این کالا/خدمت در پذیرش گارانتی ثبت شده و برای حفظ سابقه قابل تغییر نیست.");
        var hasRecordedCommissionPolicy = await db.Transactions.AsNoTracking().AnyAsync(t =>
            t.Type == TransactionType.Sale && (t.ReferrerGoodsPercentSnapshot.HasValue ||
            t.ReferrerServicePercentSnapshot.HasValue) && t.Lines.Any(l => l.ProductId == product.Id));
        if (hasRecordedCommissionPolicy)
            throw new InvalidOperationException("نوع این کالا/خدمت در فروش دارای پورسانت ثبت شده و قابل تغییر نیست؛ برای نوع جدید کالای دیگری تعریف کنید.");
    }
}
