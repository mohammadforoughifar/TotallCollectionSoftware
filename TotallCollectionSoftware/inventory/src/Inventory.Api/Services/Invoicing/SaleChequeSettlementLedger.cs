using Inventory.Api.Data;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Invoicing;

/// <summary>
/// منبع واحد محاسبه و راستی‌آزمایی تسویه، مانده بدهی و بستانکاری قابل مصرف فاکتور فروش.
/// </summary>
public static class SaleChequeSettlementLedger
{
    public static async Task<decimal> ExpectedSettledAmountAsync(AppDbContext db, Transaction sale)
    {
        var activeCheques = (await db.SaleChequeReceipts.AsNoTracking()
            .Where(r => r.TransactionId == sale.Id && r.ReversalVoucherId == null)
            .Select(r => r.Amount).ToListAsync()).Sum();
        var activeInstallments = (await db.SaleChequeInstallmentReceipts.AsNoTracking()
            .Where(r => r.TransactionId == sale.Id && r.ReversalVoucherId == null)
            .Select(r => r.Amount).ToListAsync()).Sum();
        var inboundAllocations = (await db.SaleChequeCreditSettlements.AsNoTracking()
            .Where(s => s.Kind == SaleChequeCreditSettlementKind.ApplyToSale && s.TargetSaleTransactionId == sale.Id)
            .Select(s => s.Amount).ToListAsync()).Sum();
        var refundedInitialCash = (await db.SaleChequeCancellations.AsNoTracking()
            .Where(c => c.SaleTransactionId == sale.Id)
            .Select(c => c.RefundedCashAmount).ToListAsync()).Sum();

        return sale.CashAmount - refundedInitialCash + activeCheques + activeInstallments + inboundAllocations;
    }

    public static async Task EnsureSettledAmountConsistentAsync(AppDbContext db, Transaction sale)
    {
        var expected = await ExpectedSettledAmountAsync(db, sale);
        if (sale.SettledAmount != expected)
            throw new InvalidOperationException("مبلغ تسویه فاکتور با تاریخچه دریافت‌ها و تخصیص‌ها مطابقت ندارد.");
    }

    /// <summary>
    /// مانده علامت‌دار فاکتور سوددار: مثبت = بدهی باقی‌مانده مشتری، منفی = بستانکاری باقی‌مانده مشتری.
    /// </summary>
    public static async Task<decimal> SignedChargedSaleBalanceAsync(AppDbContext db, Transaction sale, SaleChequeCharge charge)
    {
        await EnsureSettledAmountConsistentAsync(db, sale);
        var returns = await db.SaleChequeReturns.AsNoTracking()
            .Where(r => r.SaleTransactionId == sale.Id)
            .Select(r => new { r.GoodsAmount, r.VatAmount }).ToListAsync();
        var cancellations = await db.SaleChequeCancellations.AsNoTracking()
            .Where(c => c.SaleTransactionId == sale.Id)
            .Select(c => new { c.ReversedGoodsAmount, c.ReversedVatAmount }).ToListAsync();
        var outboundSettlements = (await db.SaleChequeCreditSettlements.AsNoTracking()
            .Where(s => s.SaleTransactionId == sale.Id)
            .Select(s => s.Amount).ToListAsync()).Sum();

        var remainingBase = charge.BaseAmount
                          - returns.Sum(r => r.GoodsAmount + r.VatAmount)
                          - cancellations.Sum(c => c.ReversedGoodsAmount + c.ReversedVatAmount);
        return remainingBase + charge.Amount - charge.CustomerCredit - sale.SettledAmount + outboundSettlements;
    }

    public static async Task<decimal> UnpaidSaleBalanceAsync(AppDbContext db, Transaction sale)
    {
        var charge = await db.SaleChequeCharges.AsNoTracking().FirstOrDefaultAsync(c => c.TransactionId == sale.Id);
        if (charge is not null)
        {
            var signed = await SignedChargedSaleBalanceAsync(db, sale, charge);
            return Math.Max(0m, signed);
        }
        return Math.Max(0m, sale.Amount - Math.Max(0m, sale.SettledAmount));
    }
}
