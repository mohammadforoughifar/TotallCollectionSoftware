using System.Data;
using Inventory.Api.Data;
using Inventory.Api.Services.Accounting;
using Inventory.Api.Services.Treasury;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Api.Services.Invoicing;

/// <summary>لغو صریح وصول با سند معکوس؛ هیچ سند اولیه‌ای حذف یا تغییر نمی‌شود.</summary>
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
            .Include(r => r.AccVoucher).ThenInclude(v => v.Lines)
            .FirstOrDefaultAsync(r => r.ChequeId == chequeId && r.ReversalVoucherId == null)
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
            await db.SaleChequeInstallmentReceipts.AnyAsync(r => r.TransactionId == sale.Id && (r.Date > date || r.ReversalDate > date)) ||
            await db.SaleChequeReceipts.AnyAsync(r => r.TransactionId == sale.Id && (r.Date > date || r.ReversalDate > date)))
            throw new InvalidOperationException("تاریخ لغو وصول نمی‌تواند پیش از آخرین گردش ثبت‌شده فاکتور باشد.");
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
                throw new InvalidOperationException("از محل وصول این چک قبلاً بستانکاری به مشتری پرداخت یا مصرف شده است؛ ابتدا بستانکاری تسویه‌شده را تعیین‌تکلیف کنید.");
        }
        var bankLines = receipt.AccVoucher.Lines.Where(l => l.AccountId == receipt.BankAccountId).ToList();
        var partyLines = receipt.AccVoucher.Lines.Where(l => l.AccountId == receipt.PartyAccountId && l.PartyId == sale.PartyId).ToList();
        if (receipt.AccVoucher.Status != VoucherStatus.Confirmed || receipt.AccVoucher.Date.Date != receipt.Date.Date ||
            bankLines.Sum(l => l.Debit - l.Credit) != receipt.Amount || partyLines.Sum(l => l.Credit - l.Debit) != receipt.Amount)
            throw new InvalidOperationException("سند وصول اولیه معتبر نیست.");
        var bank = await db.TrsAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == receipt.TrsAccountId && a.IsActive &&
            a.Kind == TreasuryAccountKind.Bank && a.AccountId == receipt.BankAccountId)
            ?? throw new InvalidOperationException("حساب بانکی اولیه فعال نیست یا تنظیم آن تغییر کرده است.");
        await SaleChequeTreasuryGuard.VerifyCompanyOwnershipAsync(db, bank, sale.CompanyId);
        var accounts = await db.AccAccounts.AsNoTracking().Where(a => a.Id == receipt.BankAccountId || a.Id == receipt.PartyAccountId).ToListAsync();
        if (accounts.Count != 2 || accounts.Any(a => !a.IsActive || !a.IsPostable || a.Type != AccountType.Asset))
            throw new InvalidOperationException("حساب‌های بانک و مشتری باید فعال و قابل ثبت باشند.");
        var balance = await new TreasuryService(db, new AccountingService(db)).GetAccountAsync(bank.Id);
        if (balance is null || balance.Balance < receipt.Amount)
            throw new InvalidOperationException("مانده حساب بانکی برای لغو وصول کافی نیست.");
        var years = await db.AccFiscalYears.Where(y => y.StartDate <= date && y.EndDate >= date).ToListAsync();
        if (years.Count != 1 || years[0].IsClosed)
            throw new InvalidOperationException("برای تاریخ لغو وصول باید دقیقاً یک سال مالی باز وجود داشته باشد.");
        var year = years[0];
        var number = (await db.AccVouchers.Where(v => v.FiscalYearId == year.Id).Select(v => (int?)v.Number).MaxAsync() ?? 0) + 1;
        var title = $"لغو وصول چک {cheque.Number} — فاکتور {sale.Number}";
        receipt.ReversalOperationId = operationId;
        receipt.ReversalDate = date;
        receipt.ReversalVoucher = new AccVoucher
        {
            FiscalYearId = year.Id, Number = number, Date = date, Status = VoucherStatus.Confirmed,
            Source = VoucherSource.StoreSaleChequeReceiptReversal, SourceId = sale.Id, SourceTitle = title,
            Description = title, RefNumber = cheque.Number, CreatedBy = user, ConfirmedBy = user,
            CreatedAt = DateTime.Now, ConfirmedAt = DateTime.Now, TotalDebit = receipt.Amount, TotalCredit = receipt.Amount,
            Lines = new()
            {
                new AccVoucherLine { RowNo = 1, AccountId = receipt.PartyAccountId, PartyId = sale.PartyId, Debit = receipt.Amount, Description = title },
                new AccVoucherLine { RowNo = 2, AccountId = receipt.BankAccountId, PartyId = sale.PartyId, Credit = receipt.Amount, Description = title }
            }
        };
        sale.SettledAmount -= receipt.Amount;
        cheque.IsCleared = false;
        cheque.Status = originalStatus.PreviousStatus;
        cheque.ClearedAt = null;
        cheque.TrsAccountId = receipt.PreviousTrsAccountId;
        db.SaleChequeStatusEvents.Add(new SaleChequeStatusEvent
        {
            CompanyId = receipt.CompanyId, OperationId = operationId, ChequeId = cheque.Id,
            PreviousStatus = ChequeStatus.Cleared, NewStatus = cheque.Status, CreatedAt = DateTime.Now, CreatedBy = user,
            Reason = "لغو وصول با سند معکوس"
        });
        await db.SaveChangesAsync();
    }
}
