using System.Data;
using Inventory.Api.Data;
using Inventory.Api.Services.Treasury;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Api.Services.Invoicing;

/// <summary>لغو صریح وصول با گردش معکوس فروشگاهی؛ هیچ سابقه اولیه‌ای حذف نمی‌شود.</summary>
public static class SaleChequeReceiptReversal
{
    public static async Task ReverseAsync(AppDbContext db, int chequeId, Guid operationId, DateTime date, string? user)
    {
        var tx = db.Database.CurrentTransaction ?? throw new InvalidOperationException("لغو وصول باید در تراکنش انجام شود.");
        try
        {
            if (tx.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                throw new InvalidOperationException("تراکنش لغو وصول باید Serializable باشد.");
            await ReverseInTransactionAsync(db, chequeId, operationId, date, user);
        }
        catch
        {
            try { await tx.RollbackAsync(); }
            finally { db.ChangeTracker.Clear(); }
            throw;
        }
    }

    private static async Task ReverseInTransactionAsync(AppDbContext db, int chequeId, Guid operationId, DateTime date, string? user)
    {
        if (operationId == Guid.Empty || date == default || user?.Length > 120)
            throw new InvalidOperationException("شناسه، تاریخ یا نام کاربر عملیات معتبر نیست.");
        date = date.Date;
        var prior = await db.SaleChequeReceipts.FirstOrDefaultAsync(r => r.ReversalOperationId == operationId);
        if (prior is not null)
        {
            if (prior.ChequeId != chequeId || prior.ReversalDate != date)
                throw new InvalidOperationException("شناسه عملیات قبلاً برای لغو وصول دیگری استفاده شده است.");
            return;
        }
        var receipt = await db.SaleChequeReceipts.Include(r => r.Cheque).Include(r => r.Transaction)
            .FirstOrDefaultAsync(r => r.ChequeId == chequeId && r.ReversalDate == null)
            ?? throw new InvalidOperationException("وصول فعال چک یافت نشد.");
        var sale = receipt.Transaction;
        var cheque = receipt.Cheque;
        if (sale.CompanyId != receipt.CompanyId || cheque.CompanyId != receipt.CompanyId || sale.PartyId is not > 0 ||
            !cheque.IsCleared || cheque.Status != ChequeStatus.Cleared || cheque.Amount != receipt.Amount ||
            cheque.TrsAccountId != receipt.TrsAccountId || date < receipt.Date.Date || !receipt.PreviousAccountKnown)
            throw new InvalidOperationException("وضعیت چک یا اطلاعات اولیه وصول برای لغو معتبر نیست.");
        if (receipt.PreviousTrsAccountId.HasValue && !await db.TrsAccounts.AnyAsync(a => a.Id == receipt.PreviousTrsAccountId))
            throw new InvalidOperationException("حساب قبلی چک یافت نشد؛ بازگردانی خودکار ممکن نیست.");
        var originalStatus = await db.SaleChequeStatusEvents.AsNoTracking().FirstOrDefaultAsync(e => e.OperationId == receipt.OperationId &&
            e.ChequeId == chequeId && e.NewStatus == ChequeStatus.Cleared);
        if (originalStatus is null || originalStatus.PreviousStatus is not (ChequeStatus.InHand or ChequeStatus.Bounced))
            throw new InvalidOperationException("وضعیت پیش از وصول ثبت نشده است؛ لغو خودکار ممکن نیست.");
        if (await db.SaleChequeStatusEvents.AnyAsync(e => e.OperationId == operationId))
            throw new InvalidOperationException("شناسه عملیات قبلاً استفاده شده است.");
        if (await db.SaleChequeCancellations.AnyAsync(c => c.SaleTransactionId == sale.Id))
            throw new InvalidOperationException("فاکتور ابطال شده است.");
        if (await db.SaleChequeChargeEvents.AnyAsync(e => e.SaleChequeCharge.TransactionId == sale.Id && e.Date > date) ||
            await db.SaleChequeReturns.AnyAsync(r => r.SaleTransactionId == sale.Id && r.Date > date) ||
            await db.SaleChequeCreditSettlements.AnyAsync(s => s.SaleTransactionId == sale.Id && s.Date > date) ||
            await db.ShopTradeInUses.AnyAsync(u => u.TargetSaleId == sale.Id && u.Date > date) ||
            await db.SaleChequeInstallmentReceipts.AnyAsync(r => r.TransactionId == sale.Id && (r.Date > date || r.ReversalDate > date)) ||
            await db.ShopOrderCreditReceipts.AnyAsync(r => r.TransactionId == sale.Id && (r.Date > date || r.ReversalDate > date)) ||
            await db.SaleChequeReceipts.AnyAsync(r => r.TransactionId == sale.Id && (r.Date > date || r.ReversalDate > date)))
            throw new InvalidOperationException("تاریخ لغو وصول نمی‌تواند پیش از آخرین گردش ثبت‌شده فاکتور باشد.");
        if (sale.ShopCashLedgerEnabled) await ShopOrderCashLedger.VerifyCurrentAsync(db, sale);
        await SaleChequeSettlementLedger.EnsureSettledAmountConsistentAsync(db, sale);
        if (sale.SettledAmount < receipt.Amount)
            throw new InvalidOperationException("تاریخچه تسویه فاکتور ناسازگار است.");
        var charge = await db.SaleChequeCharges.AsNoTracking().FirstOrDefaultAsync(c => c.TransactionId == sale.Id);
        var outboundCredits = (await db.SaleChequeCreditSettlements.AsNoTracking()
            .Where(s => s.SaleTransactionId == sale.Id).Select(s => s.Amount).ToListAsync()).Sum();
        if (outboundCredits > 0)
        {
            if (charge is null) throw new InvalidOperationException("گردش بستانکاری فاکتور نامعتبر است.");
            var signedNow = await SaleChequeSettlementLedger.SignedChargedSaleBalanceAsync(db, sale, charge);
            var rawCreditAfterReversal = Math.Max(0m, -(signedNow - outboundCredits + receipt.Amount));
            if (outboundCredits > rawCreditAfterReversal)
                throw new InvalidOperationException("از محل وصول این چک قبلاً بستانکاری به مشتری پرداخت یا مصرف شده است؛ ابتدا بستانکاری تسویه‌شده را تعیین‌تکلیف کنید.");
        }
        var bank = await db.TrsAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == receipt.TrsAccountId && a.IsActive &&
            a.Kind == TreasuryAccountKind.Bank)
            ?? throw new InvalidOperationException("حساب بانکی اولیه فعال نیست یا تغییر کرده است.");
        await SaleChequeTreasuryGuard.VerifyCompanyOwnershipAsync(db, bank, sale.CompanyId);
        if (sale.Type == TransactionType.Sale)
        {
            var balance = await TreasuryBalanceCalculator.GetAmountAsync(db, bank.Id);
            if (!balance.HasValue || balance.Value < receipt.Amount)
                throw new InvalidOperationException("مانده حساب بانکی برای لغو وصول کافی نیست.");
            await TreasuryBalanceCalculator.EnsureAvailableOnAndAfterAsync(db, bank.Id, date, receipt.Amount);
        }
        else if (sale.Type != TransactionType.Purchase)
            throw new InvalidOperationException("نوع فاکتور برای لغو وصول معتبر نیست.");
        receipt.ReversalOperationId = operationId;
        receipt.ReversalDate = date;
        sale.SettledAmount -= receipt.Amount;
        cheque.IsCleared = false;
        cheque.Status = originalStatus.PreviousStatus;
        cheque.ClearedAt = null;
        cheque.TrsAccountId = receipt.PreviousTrsAccountId;
        db.SaleChequeStatusEvents.Add(new SaleChequeStatusEvent
        {
            CompanyId = receipt.CompanyId, OperationId = operationId, ChequeId = cheque.Id,
            PreviousStatus = ChequeStatus.Cleared, NewStatus = cheque.Status, CreatedAt = DateTime.Now, CreatedBy = user,
            Reason = "لغو وصول فروشگاهی"
        });
        await db.SaveChangesAsync();
    }
}
