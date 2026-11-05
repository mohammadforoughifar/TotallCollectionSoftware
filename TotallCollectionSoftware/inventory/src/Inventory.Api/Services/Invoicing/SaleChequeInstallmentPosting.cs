using System.Data;
using Inventory.Api.Data;
using Inventory.Api.Services.Treasury;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Api.Services.Invoicing;

/// <summary>
/// دریافت و لغو دریافت قسط در فاکتورهای خرید و فروش فروشگاهی،
/// با انتخاب صریح حساب شرکت و ثبت گردش عملیاتی و معکوس.
/// </summary>
public static class SaleChequeInstallmentPosting
{
    private const decimal MaxMoney = 9999999999999999.99m;

    public static async Task<SaleChequeInstallmentReceipt> ReceiveAsync(
        AppDbContext db, int installmentId, Guid operationId, int trsAccountId, DateTime date, string? user)
    {
        var tx = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("دریافت قسط باید در تراکنش فاکتور انجام شود.");
        try
        {
            if (tx.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                throw new InvalidOperationException("تراکنش دریافت قسط باید سطح Serializable داشته باشد.");
            return await ReceiveInTransactionAsync(db, installmentId, operationId, trsAccountId, date, user);
        }
        catch
        {
            try { await tx.RollbackAsync(); }
            finally { db.ChangeTracker.Clear(); }
            throw;
        }
    }

    public static async Task ReverseAsync(
        AppDbContext db, int installmentId, Guid operationId, DateTime date, string? user)
    {
        var tx = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("لغو دریافت قسط باید در تراکنش فاکتور انجام شود.");
        try
        {
            if (tx.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                throw new InvalidOperationException("تراکنش لغو دریافت قسط باید سطح Serializable داشته باشد.");
            await ReverseInTransactionAsync(db, installmentId, operationId, date, user);
        }
        catch
        {
            try { await tx.RollbackAsync(); }
            finally { db.ChangeTracker.Clear(); }
            throw;
        }
    }

    private static async Task<SaleChequeInstallmentReceipt> ReceiveInTransactionAsync(
        AppDbContext db, int installmentId, Guid operationId, int trsAccountId, DateTime date, string? user)
    {
        if (operationId == Guid.Empty || trsAccountId <= 0 || date == default || user?.Length > 120)
            throw new InvalidOperationException("شناسه عملیات، تاریخ و حساب مقصد دریافت قسط را مشخص کنید.");
        date = date.Date;

        var prior = await db.SaleChequeInstallmentReceipts.FirstOrDefaultAsync(r => r.OperationId == operationId);
        if (prior is not null)
        {
            if (prior.InstallmentId != installmentId || prior.TrsAccountId != trsAccountId || prior.Date.Date != date)
                throw new InvalidOperationException("شناسه عملیات قبلاً برای دریافت قسط دیگری استفاده شده است.");
            return prior;
        }

        var inst = await db.Installments.FirstOrDefaultAsync(i => i.Id == installmentId)
            ?? throw new InvalidOperationException("قسط یافت نشد.");
        if (inst.IsPaid || await db.SaleChequeInstallmentReceipts.AnyAsync(r => r.InstallmentId == installmentId && r.ReversalDate == null))
            throw new InvalidOperationException("این قسط قبلاً تسویه شده است.");
        if (inst.Amount <= 0 || inst.Amount > MaxMoney || decimal.Round(inst.Amount, 2) != inst.Amount)
            throw new InvalidOperationException("مبلغ قسط معتبر نیست.");

        var sale = await db.Transactions.FirstOrDefaultAsync(t => t.Id == inst.TransactionId)
            ?? throw new InvalidOperationException("فاکتور یافت نشد.");
        var charge = await db.SaleChequeCharges.AsNoTracking().FirstOrDefaultAsync(c => c.TransactionId == sale.Id);
        if (sale.Type is not (TransactionType.Sale or TransactionType.Purchase) || sale.PartyId is not > 0 || date < sale.Date.Date ||
            inst.CompanyId != sale.CompanyId || (charge is not null && (sale.Type != TransactionType.Sale || charge.CompanyId != sale.CompanyId)))
            throw new InvalidOperationException("فاکتور، حساب مشتری یا تاریخ دریافت قسط معتبر نیست.");
        if (await db.SaleChequeCancellations.AnyAsync(c => c.SaleTransactionId == sale.Id))
            throw new InvalidOperationException("فاکتور ابطال شده است و دریافت قسط مجاز نیست.");

        if ((charge is not null && await db.SaleChequeReschedules.AnyAsync(x => x.SaleChequeChargeId == charge.Id && x.Date > date)) ||
            await db.SaleChequeReturns.AnyAsync(x => x.SaleTransactionId == sale.Id && x.Date > date) ||
            await db.SaleChequeCreditSettlements.AnyAsync(x => x.SaleTransactionId == sale.Id && x.Date > date) ||
            await db.SaleChequeInstallmentReceipts.AnyAsync(x => x.TransactionId == sale.Id && (x.Date > date || x.ReversalDate > date)) ||
            await db.SaleChequeReceipts.AnyAsync(x => x.TransactionId == sale.Id && (x.Date > date || x.ReversalDate > date)))
            throw new InvalidOperationException("تاریخ دریافت قسط نمی‌تواند پیش از آخرین گردش ثبت‌شده فاکتور باشد.");

        if (charge is not null)
        {
            var initial = await db.SaleChequeChargeEvents.AsNoTracking()
                .Where(e => e.SaleChequeChargeId == charge.Id && e.Kind == SaleChequeChargeEventKind.Initial).ToListAsync();
            if (initial.Count != 1 || initial[0].IncomeDelta != charge.InitialAmount || initial[0].Date.Date != sale.Date.Date)
                throw new InvalidOperationException("گردش اولیه فاکتور معتبر نیست.");
        }

        var trsAccount = await db.TrsAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == trsAccountId && a.IsActive)
            ?? throw new InvalidOperationException("حساب مقصد فعال نیست یا یافت نشد.");
        await SaleChequeTreasuryGuard.VerifyCompanyOwnershipAsync(db, trsAccount, sale.CompanyId);
        if (charge is null && sale.ShopCashLedgerEnabled) await ShopOrderCashLedger.VerifyCurrentAsync(db, sale);
        if (charge is null && !sale.ShopCashLedgerEnabled && sale.CashAmount > 0)
            throw new InvalidOperationException("نقد فاکتور قدیمی ابتدا باید به‌صورت صریح تطبیق شود.");
        await SaleChequeSettlementLedger.EnsureSettledAmountConsistentAsync(db, sale);
        if (sale.Type == TransactionType.Purchase)
        {
            var balance = await TreasuryBalanceCalculator.GetAmountAsync(db, trsAccount.Id);
            if (!balance.HasValue || balance.Value < inst.Amount)
                throw new InvalidOperationException("مانده صندوق/بانک برای پرداخت قسط خرید کافی نیست.");
        }
        if (sale.SettledAmount + inst.Amount > MaxMoney)
            throw new InvalidOperationException("جمع دریافت از محدوده مجاز بیشتر است.");

        var receipt = new SaleChequeInstallmentReceipt
        {
            CompanyId = sale.CompanyId,
            OperationId = operationId,
            InstallmentId = inst.Id,
            TransactionId = sale.Id,
            TrsAccountId = trsAccount.Id,
            Date = date,
            Amount = inst.Amount
        };

        inst.IsPaid = true;
        inst.PaidAt = date;
        sale.SettledAmount += inst.Amount;
        db.SaleChequeInstallmentReceipts.Add(receipt);
        await db.SaveChangesAsync();
        return receipt;
    }

    private static async Task ReverseInTransactionAsync(
        AppDbContext db, int installmentId, Guid operationId, DateTime date, string? user)
    {
        if (operationId == Guid.Empty || date == default || user?.Length > 120)
            throw new InvalidOperationException("شناسه، تاریخ یا نام کاربر عملیات معتبر نیست.");
        date = date.Date;

        var prior = await db.SaleChequeInstallmentReceipts.FirstOrDefaultAsync(r => r.ReversalOperationId == operationId);
        if (prior is not null)
        {
            if (prior.InstallmentId != installmentId || prior.ReversalDate != date)
                throw new InvalidOperationException("شناسه عملیات قبلاً برای لغو دریافت قسط دیگری استفاده شده است.");
            return;
        }

        var receipt = await db.SaleChequeInstallmentReceipts
            .Include(r => r.Installment).Include(r => r.Transaction)
            .FirstOrDefaultAsync(r => r.InstallmentId == installmentId && r.ReversalDate == null)
            ?? throw new InvalidOperationException("دریافت فعال قسط یافت نشد.");
        var sale = receipt.Transaction;
        var inst = receipt.Installment;
        if (sale.Type is not (TransactionType.Sale or TransactionType.Purchase) || sale.CompanyId != receipt.CompanyId ||
            inst.CompanyId != receipt.CompanyId || inst.TransactionId != sale.Id || sale.PartyId is not > 0 ||
            !inst.IsPaid || inst.Amount != receipt.Amount || date < receipt.Date.Date)
            throw new InvalidOperationException("وضعیت قسط یا تاریخ لغو دریافت معتبر نیست.");
        if (await db.SaleChequeCancellations.AnyAsync(c => c.SaleTransactionId == sale.Id))
            throw new InvalidOperationException("فاکتور ابطال شده است.");

        if (await db.SaleChequeChargeEvents.AnyAsync(e => e.SaleChequeCharge.TransactionId == sale.Id && e.Date > date) ||
            await db.SaleChequeReturns.AnyAsync(r => r.SaleTransactionId == sale.Id && r.Date > date) ||
            await db.SaleChequeCreditSettlements.AnyAsync(s => s.SaleTransactionId == sale.Id && s.Date > date) ||
            await db.SaleChequeInstallmentReceipts.AnyAsync(r => r.TransactionId == sale.Id && (r.Date > date || r.ReversalDate > date)) ||
            await db.SaleChequeReceipts.AnyAsync(r => r.TransactionId == sale.Id && (r.Date > date || r.ReversalDate > date)))
            throw new InvalidOperationException("تاریخ لغو دریافت قسط نمی‌تواند پیش از آخرین گردش ثبت‌شده فاکتور باشد.");

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
                throw new InvalidOperationException("از محل دریافت این قسط قبلاً بستانکاری به مشتری پرداخت یا مصرف شده است؛ ابتدا آن را تعیین‌تکلیف کنید.");
        }

        var trsAccount = await db.TrsAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == receipt.TrsAccountId && a.IsActive)
            ?? throw new InvalidOperationException("حساب مقصد اولیه قسط فعال نیست یا تغییر کرده است.");
        await SaleChequeTreasuryGuard.VerifyCompanyOwnershipAsync(db, trsAccount, sale.CompanyId);
        if (sale.Type == TransactionType.Sale)
        {
            var balance = await TreasuryBalanceCalculator.GetAmountAsync(db, trsAccount.Id);
            if (!balance.HasValue || balance.Value < receipt.Amount)
                throw new InvalidOperationException("مانده حساب مقصد برای لغو دریافت قسط کافی نیست.");
        }
        receipt.ReversalOperationId = operationId;
        receipt.ReversalDate = date;

        sale.SettledAmount -= receipt.Amount;
        inst.IsPaid = false;
        inst.PaidAt = null;
        await db.SaveChangesAsync();
    }
}
