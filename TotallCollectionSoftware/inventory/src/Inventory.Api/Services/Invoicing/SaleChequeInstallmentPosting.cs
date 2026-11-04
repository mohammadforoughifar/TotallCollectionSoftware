using System.Data;
using Inventory.Api.Data;
using Inventory.Api.Services.Accounting;
using Inventory.Api.Services.Treasury;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Api.Services.Invoicing;

/// <summary>
/// دریافت و لغو دریافت قسط در فاکتورهای دارای تاخیر تادیه چک،
/// با انتخاب صریح حساب شرکت و صدور سند حسابداری قطعی و معکوس.
/// </summary>
public static class SaleChequeInstallmentPosting
{
    private const decimal MaxMoney = 9999999999999999.99m;

    public static async Task<SaleChequeInstallmentReceipt> ReceiveAsync(
        AppDbContext db, int installmentId, Guid operationId, int trsAccountId, DateTime date, string? user)
    {
        var tx = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("دریافت قسط باید در تراکنش حسابداری انجام شود.");
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
            ?? throw new InvalidOperationException("لغو دریافت قسط باید در تراکنش حسابداری انجام شود.");
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
        if (inst.IsPaid || await db.SaleChequeInstallmentReceipts.AnyAsync(r => r.InstallmentId == installmentId && r.ReversalVoucherId == null))
            throw new InvalidOperationException("این قسط قبلاً تسویه شده است.");
        if (inst.Amount <= 0 || inst.Amount > MaxMoney || decimal.Round(inst.Amount, 2) != inst.Amount)
            throw new InvalidOperationException("مبلغ قسط معتبر نیست.");

        var sale = await db.Transactions.FirstOrDefaultAsync(t => t.Id == inst.TransactionId)
            ?? throw new InvalidOperationException("فاکتور یافت نشد.");
        var charge = await db.SaleChequeCharges.AsNoTracking().FirstOrDefaultAsync(c => c.TransactionId == sale.Id)
            ?? throw new InvalidOperationException("ردیف تاخیر تادیه چک یافت نشد.");
        if (sale.Type != TransactionType.Sale || sale.PartyId is not > 0 || date < sale.Date.Date ||
            inst.CompanyId != sale.CompanyId || charge.CompanyId != sale.CompanyId || charge.PartyAccountId is not > 0)
            throw new InvalidOperationException("فاکتور، حساب مشتری یا تاریخ دریافت قسط معتبر نیست.");
        if (await db.SaleChequeCancellations.AnyAsync(c => c.SaleTransactionId == sale.Id))
            throw new InvalidOperationException("فاکتور ابطال شده است و دریافت قسط مجاز نیست.");

        if (await db.SaleChequeReschedules.AnyAsync(x => x.SaleChequeChargeId == charge.Id && x.Date > date) ||
            await db.SaleChequeReturns.AnyAsync(x => x.SaleTransactionId == sale.Id && x.Date > date) ||
            await db.SaleChequeCreditSettlements.AnyAsync(x => x.SaleTransactionId == sale.Id && x.Date > date) ||
            await db.SaleChequeInstallmentReceipts.AnyAsync(x => x.TransactionId == sale.Id && (x.Date > date || x.ReversalDate > date)) ||
            await db.SaleChequeReceipts.AnyAsync(x => x.TransactionId == sale.Id && (x.Date > date || x.ReversalDate > date)))
            throw new InvalidOperationException("تاریخ دریافت قسط نمی‌تواند پیش از آخرین گردش ثبت‌شده فاکتور باشد.");

        var initialConfirmed = await db.SaleChequeChargeEvents.AnyAsync(e => e.SaleChequeChargeId == charge.Id &&
            e.Kind == SaleChequeChargeEventKind.Initial && e.AccVoucher.Status == VoucherStatus.Confirmed);
        if (!initialConfirmed) throw new InvalidOperationException("سند قطعی اولیه فاکتور یافت نشد.");

        var trsAccount = await db.TrsAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == trsAccountId && a.IsActive && a.AccountId > 0)
            ?? throw new InvalidOperationException("حساب مقصد فعال یا متصل به حسابداری نیست.");
        await SaleChequeTreasuryGuard.VerifyCompanyOwnershipAsync(db, trsAccount, sale.CompanyId);
        var bankAccountId = trsAccount.AccountId!.Value;
        var partyAccountId = charge.PartyAccountId.Value;
        if (bankAccountId == partyAccountId)
            throw new InvalidOperationException("حساب مقصد و حساب مشتری باید مستقل باشند.");

        var accounts = await db.AccAccounts.AsNoTracking()
            .Where(a => a.Id == bankAccountId || a.Id == partyAccountId).ToListAsync();
        if (accounts.Count != 2 || accounts.Any(a => !a.IsActive || !a.IsPostable || a.Type != AccountType.Asset))
            throw new InvalidOperationException("حساب‌های مشتری و مقصد باید دارایی، فعال و قابل ثبت باشند.");

        await SaleChequeSettlementLedger.EnsureSettledAmountConsistentAsync(db, sale);
        if (sale.SettledAmount + inst.Amount > MaxMoney)
            throw new InvalidOperationException("جمع دریافت از محدوده حسابداری بیشتر است.");

        var years = await db.AccFiscalYears.Where(y => y.StartDate <= date && y.EndDate >= date).ToListAsync();
        if (years.Count != 1 || years[0].IsClosed)
            throw new InvalidOperationException("برای تاریخ دریافت قسط باید دقیقاً یک سال مالی باز وجود داشته باشد.");
        var year = years[0];
        var number = (await db.AccVouchers.Where(v => v.FiscalYearId == year.Id).Select(v => (int?)v.Number).MaxAsync() ?? 0) + 1;
        var title = $"دریافت قسط {inst.No} — فاکتور {sale.Number}";
        var voucher = new AccVoucher
        {
            FiscalYearId = year.Id,
            Number = number,
            Date = date,
            Status = VoucherStatus.Confirmed,
            Source = VoucherSource.StoreSaleInstallmentReceipt,
            SourceId = sale.Id,
            SourceTitle = title,
            Description = title,
            RefNumber = sale.Number,
            CreatedBy = user,
            ConfirmedBy = user,
            CreatedAt = DateTime.Now,
            ConfirmedAt = DateTime.Now,
            TotalDebit = inst.Amount,
            TotalCredit = inst.Amount,
            Lines = new()
            {
                new AccVoucherLine
                {
                    RowNo = 1, AccountId = bankAccountId, PartyId = sale.PartyId,
                    Debit = inst.Amount, Description = title, RefNumber = sale.Number
                },
                new AccVoucherLine
                {
                    RowNo = 2, AccountId = partyAccountId, PartyId = sale.PartyId,
                    Credit = inst.Amount, Description = title, RefNumber = sale.Number
                }
            }
        };

        var receipt = new SaleChequeInstallmentReceipt
        {
            CompanyId = sale.CompanyId,
            OperationId = operationId,
            InstallmentId = inst.Id,
            TransactionId = sale.Id,
            TrsAccountId = trsAccount.Id,
            BankAccountId = bankAccountId,
            PartyAccountId = partyAccountId,
            AccVoucher = voucher,
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
            .Include(r => r.AccVoucher).ThenInclude(v => v.Lines)
            .FirstOrDefaultAsync(r => r.InstallmentId == installmentId && r.ReversalVoucherId == null)
            ?? throw new InvalidOperationException("دریافت فعال قسط یافت نشد.");
        var sale = receipt.Transaction;
        var inst = receipt.Installment;
        if (sale.CompanyId != receipt.CompanyId || inst.CompanyId != receipt.CompanyId || sale.PartyId is not > 0 ||
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

        await SaleChequeSettlementLedger.EnsureSettledAmountConsistentAsync(db, sale);
        if (sale.SettledAmount < receipt.Amount)
            throw new InvalidOperationException("تاریخچه تسویه فاکتور ناسازگار است.");

        var charge = await db.SaleChequeCharges.AsNoTracking().FirstOrDefaultAsync(c => c.TransactionId == sale.Id)
            ?? throw new InvalidOperationException("ردیف تاخیر تادیه چک یافت نشد.");
        var outboundCredits = (await db.SaleChequeCreditSettlements.AsNoTracking()
            .Where(s => s.SaleTransactionId == sale.Id).Select(s => s.Amount).ToListAsync()).Sum();
        if (outboundCredits > 0)
        {
            var signedNow = await SaleChequeSettlementLedger.SignedChargedSaleBalanceAsync(db, sale, charge);
            var rawCreditAfterReversal = Math.Max(0m, -(signedNow - outboundCredits + receipt.Amount));
            if (outboundCredits > rawCreditAfterReversal)
                throw new InvalidOperationException("از محل دریافت این قسط قبلاً بستانکاری به مشتری پرداخت یا مصرف شده است؛ ابتدا آن را تعیین‌تکلیف کنید.");
        }

        var bankLines = receipt.AccVoucher.Lines.Where(l => l.AccountId == receipt.BankAccountId).ToList();
        var partyLines = receipt.AccVoucher.Lines.Where(l => l.AccountId == receipt.PartyAccountId && l.PartyId == sale.PartyId).ToList();
        if (receipt.AccVoucher.Status != VoucherStatus.Confirmed || receipt.AccVoucher.Date.Date != receipt.Date.Date ||
            bankLines.Sum(l => l.Debit - l.Credit) != receipt.Amount || partyLines.Sum(l => l.Credit - l.Debit) != receipt.Amount)
            throw new InvalidOperationException("سند دریافت اولیه قسط معتبر نیست.");

        var trsAccount = await db.TrsAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == receipt.TrsAccountId && a.IsActive && a.AccountId == receipt.BankAccountId)
            ?? throw new InvalidOperationException("حساب مقصد اولیه قسط فعال نیست یا تغییر کرده است.");
        await SaleChequeTreasuryGuard.VerifyCompanyOwnershipAsync(db, trsAccount, sale.CompanyId);

        var accounts = await db.AccAccounts.AsNoTracking()
            .Where(a => a.Id == receipt.BankAccountId || a.Id == receipt.PartyAccountId).ToListAsync();
        if (accounts.Count != 2 || accounts.Any(a => !a.IsActive || !a.IsPostable || a.Type != AccountType.Asset))
            throw new InvalidOperationException("حساب‌های مقصد و مشتری باید فعال و قابل ثبت باشند.");

        var balance = await new TreasuryService(db, new AccountingService(db)).GetAccountAsync(trsAccount.Id);
        if (balance is null || balance.Balance < receipt.Amount)
            throw new InvalidOperationException("مانده حساب مقصد برای لغو دریافت قسط کافی نیست.");

        var years = await db.AccFiscalYears.Where(y => y.StartDate <= date && y.EndDate >= date).ToListAsync();
        if (years.Count != 1 || years[0].IsClosed)
            throw new InvalidOperationException("برای تاریخ لغو دریافت قسط باید دقیقاً یک سال مالی باز وجود داشته باشد.");
        var year = years[0];
        var number = (await db.AccVouchers.Where(v => v.FiscalYearId == year.Id).Select(v => (int?)v.Number).MaxAsync() ?? 0) + 1;
        var title = $"لغو دریافت قسط {inst.No} — فاکتور {sale.Number}";
        receipt.ReversalOperationId = operationId;
        receipt.ReversalDate = date;
        receipt.ReversalVoucher = new AccVoucher
        {
            FiscalYearId = year.Id,
            Number = number,
            Date = date,
            Status = VoucherStatus.Confirmed,
            Source = VoucherSource.StoreSaleInstallmentReceiptReversal,
            SourceId = sale.Id,
            SourceTitle = title,
            Description = title,
            RefNumber = sale.Number,
            CreatedBy = user,
            ConfirmedBy = user,
            CreatedAt = DateTime.Now,
            ConfirmedAt = DateTime.Now,
            TotalDebit = receipt.Amount,
            TotalCredit = receipt.Amount,
            Lines = new()
            {
                new AccVoucherLine
                {
                    RowNo = 1, AccountId = receipt.PartyAccountId, PartyId = sale.PartyId,
                    Debit = receipt.Amount, Description = title, RefNumber = sale.Number
                },
                new AccVoucherLine
                {
                    RowNo = 2, AccountId = receipt.BankAccountId, PartyId = sale.PartyId,
                    Credit = receipt.Amount, Description = title, RefNumber = sale.Number
                }
            }
        };

        sale.SettledAmount -= receipt.Amount;
        inst.IsPaid = false;
        inst.PaidAt = null;
        await db.SaveChangesAsync();
    }
}
