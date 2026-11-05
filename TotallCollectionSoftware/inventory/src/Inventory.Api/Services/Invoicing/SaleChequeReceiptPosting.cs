using System.Data;
using Inventory.Api.Data;
using Inventory.Api.Services.Treasury;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Api.Services.Invoicing;

/// <summary>وصول چک فروشگاهی و ثبت اتمیک بانک، طرف حساب و تاریخ دریافت.</summary>
public static class SaleChequeReceiptPosting
{
    public static async Task<SaleChequeReceipt> ReceiveAsync(AppDbContext db, int chequeId, Guid operationId,
        int trsAccountId, DateTime date, string? user)
    {
        var tx = db.Database.CurrentTransaction ?? throw new InvalidOperationException("وصول چک باید در تراکنش فاکتور انجام شود.");
        try
        {
            if (tx.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                throw new InvalidOperationException("تراکنش وصول چک باید سطح Serializable داشته باشد.");
            return await ReceiveInTransactionAsync(db, chequeId, operationId, trsAccountId, date, user);
        }
        catch
        {
            try { await tx.RollbackAsync(); }
            finally { db.ChangeTracker.Clear(); }
            throw;
        }
    }

    private static async Task<SaleChequeReceipt> ReceiveInTransactionAsync(AppDbContext db, int chequeId,
        Guid operationId, int trsAccountId, DateTime date, string? user)
    {
        if (operationId == Guid.Empty || trsAccountId <= 0 || date == default)
            throw new InvalidOperationException("شناسه عملیات، تاریخ و حساب بانکی وصول را مشخص کنید.");
        date = date.Date;
        var prior = await db.SaleChequeReceipts.FirstOrDefaultAsync(x => x.OperationId == operationId);
        if (prior is not null)
        {
            if (prior.ChequeId != chequeId || prior.TrsAccountId != trsAccountId || prior.Date != date)
                throw new InvalidOperationException("شناسه عملیات قبلاً برای وصول دیگری استفاده شده است.");
            return prior;
        }
        var cheque = await db.Cheques.FirstOrDefaultAsync(x => x.Id == chequeId)
            ?? throw new InvalidOperationException("چک یافت نشد.");
        if (cheque.IsCleared || cheque.Status is not (ChequeStatus.InHand or ChequeStatus.Bounced) ||
            await db.SaleChequeReceipts.AnyAsync(x => x.ChequeId == chequeId && x.ReversalDate == null))
            throw new InvalidOperationException("چک در وضعیت قابل وصول نیست یا قبلاً وصول شده است.");
        if (cheque.BouncedAt.HasValue && date < cheque.BouncedAt.Value.Date)
            throw new InvalidOperationException("تاریخ وصول نمی‌تواند پیش از آخرین برگشت چک باشد.");
        if (cheque.Amount <= 0 || cheque.Amount > 9999999999999999.99m || decimal.Round(cheque.Amount, 2) != cheque.Amount)
            throw new InvalidOperationException("مبلغ چک معتبر نیست.");
        var sale = await db.Transactions.FirstOrDefaultAsync(x => x.Id == cheque.TransactionId)
            ?? throw new InvalidOperationException("فاکتور یافت نشد.");
        var charge = await db.SaleChequeCharges.FirstOrDefaultAsync(x => x.TransactionId == sale.Id);
        if (sale.Type is not (TransactionType.Sale or TransactionType.Purchase) || sale.PartyId is not > 0 || date < sale.Date.Date ||
            cheque.CompanyId != sale.CompanyId || (charge is not null && (sale.Type != TransactionType.Sale || charge.CompanyId != sale.CompanyId)))
            throw new InvalidOperationException("فاکتور، حساب مشتری یا تاریخ وصول معتبر نیست.");
        if (await db.SaleChequeCancellations.AnyAsync(c => c.SaleTransactionId == sale.Id))
            throw new InvalidOperationException("فاکتور ابطال شده است و امکان وصول چک ندارد.");
        if ((charge is not null && await db.SaleChequeReschedules.AnyAsync(x => x.SaleChequeChargeId == charge.Id && x.Date > date)) ||
            await db.SaleChequeReturns.AnyAsync(x => x.SaleTransactionId == sale.Id && x.Date > date) ||
            await db.SaleChequeCreditSettlements.AnyAsync(x => x.SaleTransactionId == sale.Id && x.Date > date) ||
            await db.SaleChequeInstallmentReceipts.AnyAsync(x => x.TransactionId == sale.Id && (x.Date > date || x.ReversalDate > date)) ||
            await db.SaleChequeReceipts.AnyAsync(x => x.TransactionId == sale.Id && (x.Date > date || x.ReversalDate > date)))
            throw new InvalidOperationException("تاریخ وصول نمی‌تواند پیش از آخرین تغییر سررسید یا گردش وصول فاکتور باشد.");
        if (charge is not null)
        {
            var initial = await db.SaleChequeChargeEvents.AsNoTracking()
                .Where(e => e.SaleChequeChargeId == charge.Id && e.Kind == SaleChequeChargeEventKind.Initial).ToListAsync();
            if (initial.Count != 1 || initial[0].IncomeDelta != charge.InitialAmount || initial[0].Date.Date != sale.Date.Date)
                throw new InvalidOperationException("گردش اولیه فاکتور معتبر نیست.");
        }
        var bank = await db.TrsAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == trsAccountId && x.IsActive && x.Kind == TreasuryAccountKind.Bank)
            ?? throw new InvalidOperationException("حساب بانکی مقصد فعال نیست یا یافت نشد.");
        if (await db.SystemCompanies.CountAsync() > 1 && (sale.CompanyId is not > 0 || bank.CompanyId != sale.CompanyId))
            throw new InvalidOperationException("در محیط چندشرکتی، حساب بانکی مقصد باید صریحاً متعلق به شرکت همین فاکتور باشد.");
        await SaleChequeTreasuryGuard.VerifyCompanyOwnershipAsync(db, bank, sale.CompanyId);
        if (charge is null && sale.ShopCashLedgerEnabled) await ShopOrderCashLedger.VerifyCurrentAsync(db, sale);
        if (charge is null && !sale.ShopCashLedgerEnabled && sale.CashAmount > 0)
            throw new InvalidOperationException("نقد فاکتور قدیمی ابتدا باید به‌صورت صریح تطبیق شود.");
        await SaleChequeSettlementLedger.EnsureSettledAmountConsistentAsync(db, sale);
        if (sale.Type == TransactionType.Purchase)
        {
            if (cheque.TrsAccountId is > 0 && cheque.TrsAccountId != bank.Id)
                throw new InvalidOperationException("حساب پرداخت باید با حساب صادرکننده چک خرید مطابقت داشته باشد.");
            var balance = await TreasuryBalanceCalculator.GetAmountAsync(db, bank.Id);
            if (!balance.HasValue || balance.Value < cheque.Amount)
                throw new InvalidOperationException("مانده بانک برای پرداخت چک خرید کافی نیست.");
        }
        if (sale.SettledAmount + cheque.Amount > 9999999999999999.99m)
            throw new InvalidOperationException("جمع وصول از محدوده مجاز بیشتر است.");
        var receipt = new SaleChequeReceipt
        {
            CompanyId = sale.CompanyId, OperationId = operationId, ChequeId = cheque.Id, TransactionId = sale.Id,
            TrsAccountId = bank.Id, Date = date, Amount = cheque.Amount,
            PreviousAccountKnown = true, PreviousTrsAccountId = cheque.TrsAccountId
        };
        db.SaleChequeStatusEvents.Add(new SaleChequeStatusEvent
        {
            CompanyId = cheque.CompanyId, OperationId = operationId, ChequeId = cheque.Id,
            PreviousStatus = cheque.Status, NewStatus = ChequeStatus.Cleared,
            CreatedAt = DateTime.Now, CreatedBy = user
        });
        cheque.IsCleared = true;
        cheque.Status = ChequeStatus.Cleared;
        cheque.ClearedAt = date;
        cheque.TrsAccountId = bank.Id;
        // آخرین زمان و علت برگشت برای پیگیری حفظ می‌شود؛ وصول سود جدید ایجاد نمی‌کند.
        sale.SettledAmount += cheque.Amount;
        db.SaleChequeReceipts.Add(receipt);
        await db.SaveChangesAsync();
        return receipt;
    }
}
