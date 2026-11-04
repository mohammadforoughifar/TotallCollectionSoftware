using System.Data;
using Inventory.Api.Data;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Api.Services.Invoicing;

/// <summary>وصول آزمایشی چک فروش و ثبت اتمیک بانک، طرف حساب و تاریخ دریافت.</summary>
public static class SaleChequeReceiptPosting
{
    public static async Task<SaleChequeReceipt> ReceiveAsync(AppDbContext db, int chequeId, Guid operationId,
        int trsAccountId, DateTime date, string? user)
    {
        var tx = db.Database.CurrentTransaction ?? throw new InvalidOperationException("وصول چک باید در تراکنش حسابداری انجام شود.");
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
            await db.SaleChequeReceipts.AnyAsync(x => x.ChequeId == chequeId && x.ReversalVoucherId == null))
            throw new InvalidOperationException("چک در وضعیت قابل وصول نیست یا قبلاً وصول شده است.");
        if (cheque.BouncedAt.HasValue && date < cheque.BouncedAt.Value.Date)
            throw new InvalidOperationException("تاریخ وصول نمی‌تواند پیش از آخرین برگشت چک باشد.");
        if (cheque.Amount <= 0 || cheque.Amount > 9999999999999999.99m || decimal.Round(cheque.Amount, 2) != cheque.Amount)
            throw new InvalidOperationException("مبلغ چک معتبر نیست.");
        var sale = await db.Transactions.FirstOrDefaultAsync(x => x.Id == cheque.TransactionId)
            ?? throw new InvalidOperationException("فاکتور یافت نشد.");
        var charge = await db.SaleChequeCharges.FirstOrDefaultAsync(x => x.TransactionId == sale.Id)
            ?? throw new InvalidOperationException("ردیف تاخیر تادیه چک یافت نشد.");
        if (sale.Type != TransactionType.Sale || sale.PartyId is not > 0 || date < sale.Date.Date ||
            cheque.CompanyId != sale.CompanyId || charge.CompanyId != sale.CompanyId || charge.PartyAccountId is not > 0)
            throw new InvalidOperationException("فاکتور، حساب مشتری یا تاریخ وصول معتبر نیست.");
        if (await db.SaleChequeReschedules.AnyAsync(x => x.SaleChequeChargeId == charge.Id && x.Date > date) ||
            await db.SaleChequeReceipts.AnyAsync(x => x.TransactionId == sale.Id && (x.Date > date || x.ReversalDate > date)))
            throw new InvalidOperationException("تاریخ وصول نمی‌تواند پیش از آخرین تغییر سررسید یا گردش وصول فاکتور باشد.");
        var initialConfirmed = await db.SaleChequeChargeEvents.AnyAsync(e => e.SaleChequeChargeId == charge.Id &&
            e.Kind == SaleChequeChargeEventKind.Initial && e.AccVoucher.Status == VoucherStatus.Confirmed);
        if (!initialConfirmed) throw new InvalidOperationException("سند قطعی اولیه فاکتور یافت نشد.");
        // Treasury accounts are not company-scoped in the existing model.
        if (await db.SystemCompanies.CountAsync() > 1)
            throw new InvalidOperationException("وصول آزمایشی در محیط چندشرکتی تا تفکیک حساب‌های بانکی هر شرکت غیرفعال است.");
        var bank = await db.TrsAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == trsAccountId && x.IsActive && x.Kind == TreasuryAccountKind.Bank)
            ?? throw new InvalidOperationException("حساب بانکی مقصد فعال نیست یا یافت نشد.");
        if (bank.AccountId is not > 0 || bank.AccountId == charge.PartyAccountId)
            throw new InvalidOperationException("حساب حسابداری مستقل برای بانک و مشتری تنظیم کنید.");
        var accounts = await db.AccAccounts.AsNoTracking().Where(a => a.Id == bank.AccountId || a.Id == charge.PartyAccountId).ToListAsync();
        if (accounts.Count != 2 || accounts.Any(a => !a.IsActive || !a.IsPostable || a.Type != AccountType.Asset))
            throw new InvalidOperationException("حساب‌های مشتری و بانک باید دارایی، فعال و قابل ثبت باشند.");
        var previousReceipts = await db.SaleChequeReceipts.AsNoTracking().Where(x => x.TransactionId == sale.Id).ToListAsync();
        if (sale.SettledAmount != sale.CashAmount + previousReceipts.Where(x => x.ReversalVoucherId == null).Sum(x => x.Amount))
            throw new InvalidOperationException("تاریخچه وصول با مانده تسویه فاکتور مطابقت ندارد.");
        if (sale.SettledAmount + cheque.Amount > 9999999999999999.99m)
            throw new InvalidOperationException("جمع وصول از محدوده حسابداری بیشتر است.");
        var years = await db.AccFiscalYears.Where(y => y.StartDate <= date && y.EndDate >= date).ToListAsync();
        if (years.Count != 1 || years[0].IsClosed)
            throw new InvalidOperationException("برای تاریخ وصول باید دقیقاً یک سال مالی باز وجود داشته باشد.");
        var year = years[0];
        var number = (await db.AccVouchers.Where(v => v.FiscalYearId == year.Id).Select(v => (int?)v.Number).MaxAsync() ?? 0) + 1;
        var title = $"وصول چک {cheque.Number} — فاکتور {sale.Number}";
        var voucher = new AccVoucher
        {
            FiscalYearId = year.Id, Number = number, Date = date, Status = VoucherStatus.Confirmed,
            Source = VoucherSource.StoreSaleChequeReceipt, SourceId = sale.Id, SourceTitle = title,
            Description = title, RefNumber = cheque.Number, CreatedBy = user, ConfirmedBy = user,
            CreatedAt = DateTime.Now, ConfirmedAt = DateTime.Now,
            TotalDebit = cheque.Amount, TotalCredit = cheque.Amount,
            Lines = new()
            {
                new AccVoucherLine { RowNo = 1, AccountId = bank.AccountId.Value, PartyId = sale.PartyId,
                    Debit = cheque.Amount, Description = title, RefNumber = cheque.Number },
                new AccVoucherLine { RowNo = 2, AccountId = charge.PartyAccountId.Value, PartyId = sale.PartyId,
                    Credit = cheque.Amount, Description = title, RefNumber = cheque.Number }
            }
        };
        var receipt = new SaleChequeReceipt
        {
            CompanyId = sale.CompanyId, OperationId = operationId, ChequeId = cheque.Id, TransactionId = sale.Id,
            TrsAccountId = bank.Id, BankAccountId = bank.AccountId.Value, PartyAccountId = charge.PartyAccountId.Value,
            AccVoucher = voucher, Date = date, Amount = cheque.Amount,
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
