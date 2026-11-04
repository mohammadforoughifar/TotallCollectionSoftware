using System.Data;
using Inventory.Api.Data;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Api.Services.Invoicing;

/// <summary>
/// تغییر سررسید و سند تعدیل در تراکنش سریال فراخواننده، با پشتیبانی از تغییر پایه پس از برگشت جزئی.
/// </summary>
public static class SaleChequeRescheduling
{
    public static async Task<decimal> EffectiveBaseAsync(AppDbContext db, SaleChequeCharge charge)
    {
        var returns = await db.SaleChequeReturns.AsNoTracking()
            .Where(r => r.SaleChequeChargeId == charge.Id)
            .Select(r => new { r.GoodsAmount, r.VatAmount }).ToListAsync();
        var returned = returns.Sum(r => r.GoodsAmount + r.VatAmount);
        var effectiveBase = charge.BaseAmount - returned;
        if (effectiveBase <= 0)
            throw new InvalidOperationException("تمام کالاهای این فاکتور برگشت داده شده‌اند؛ تعدیل سررسید مجاز نیست.");
        return effectiveBase;
    }

    /// <summary>گردکردن تجمعی در هر دورهٔ پایهٔ ثابت، با رد تاریخچهٔ ناسازگار.</summary>
    public static async Task<decimal> CalculateDeltaAsync(AppDbContext db, Cheque cheque, SaleChequeCharge charge, DateTime newDueDate)
    {
        var (_, delta) = await CalculatePlanAsync(db, cheque, charge, newDueDate);
        return delta;
    }

    public static async Task<(decimal EffectiveBase, decimal Delta)> CalculatePlanAsync(
        AppDbContext db, Cheque cheque, SaleChequeCharge charge, DateTime newDueDate)
    {
        if (!cheque.OriginalDueDate.HasValue)
            throw new InvalidOperationException("سررسید اولیه چک مشخص نیست؛ تعدیل خودکار مجاز نیست.");
        var effectiveBase = await EffectiveBaseAsync(db, charge);
        var records = await db.SaleChequeReschedules.AsNoTracking().Where(r => r.ChequeId == cheque.Id)
            .OrderBy(r => r.Id).ToListAsync();
        var due = cheque.OriginalDueDate.Value.Date;
        var segStartDue = due;
        decimal? currentSegBase = null;
        decimal segPosted = 0;

        foreach (var record in records)
        {
            if (record.SaleChequeChargeId != charge.Id || record.PreviousDueDate.Date != due ||
                record.BaseAmount <= 0 || record.BaseAmount > charge.BaseAmount || record.BaseAmount < effectiveBase ||
                record.RatePercent != charge.RatePercent || record.PeriodDays != charge.PeriodDays)
                throw new InvalidOperationException("تاریخچه سررسید یا مبنای تعدیل ناسازگار است؛ بررسی دستی لازم است.");

            if (currentSegBase.HasValue && record.BaseAmount != currentSegBase.Value)
            {
                if (record.BaseAmount > currentSegBase.Value ||
                    segPosted != SaleChequeChargeMath.DateAdjustment(currentSegBase.Value, charge.RatePercent, charge.PeriodDays, segStartDue, due))
                    throw new InvalidOperationException("تاریخچه سررسید یا گردکردن تعدیلات ناسازگار است؛ بررسی دستی لازم است.");
                segStartDue = due;
                segPosted = 0;
            }

            currentSegBase = record.BaseAmount;
            due = record.NewDueDate.Date;
            segPosted += record.IncomeDelta;
        }

        if (due != cheque.DueDate.Date || (currentSegBase.HasValue &&
            segPosted != SaleChequeChargeMath.DateAdjustment(currentSegBase.Value, charge.RatePercent, charge.PeriodDays, segStartDue, due)))
            throw new InvalidOperationException("تاریخچه سررسید یا گردکردن تعدیلات ناسازگار است؛ بررسی دستی لازم است.");

        var originDue = currentSegBase.HasValue && currentSegBase.Value == effectiveBase
            ? segStartDue
            : cheque.DueDate.Date;
        var delta = SaleChequeChargeMath.IncrementalDateAdjustment(effectiveBase, charge.RatePercent, charge.PeriodDays,
            originDue, cheque.DueDate, newDueDate);
        return (effectiveBase, delta);
    }

    public static async Task<SaleChequeReschedule> ApplyAsync(AppDbContext db, int chequeId, Guid operationId,
        DateTime expectedDueDate, DateTime newDueDate, DateTime operationDate, string? user, decimal? confirmedIncomeDelta = null)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("تغییر سررسید باید داخل تراکنش حسابداری انجام شود.");
        try
        {
            if (transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                throw new InvalidOperationException("تراکنش تغییر سررسید باید سطح Serializable داشته باشد.");
            return await ApplyInTransactionAsync(db, chequeId, operationId, expectedDueDate, newDueDate, operationDate, user, confirmedIncomeDelta);
        }
        catch
        {
            try { await transaction.RollbackAsync(); }
            finally { db.ChangeTracker.Clear(); }
            throw;
        }
    }

    private static async Task<SaleChequeReschedule> ApplyInTransactionAsync(AppDbContext db, int chequeId,
        Guid operationId, DateTime expectedDueDate, DateTime newDueDate, DateTime operationDate, string? user, decimal? confirmedIncomeDelta)
    {
        if (operationId == Guid.Empty || expectedDueDate == default || newDueDate == default || operationDate == default)
            throw new InvalidOperationException("شناسه و تاریخ‌های تغییر سررسید معتبر نیستند.");
        expectedDueDate = expectedDueDate.Date;
        newDueDate = newDueDate.Date;
        operationDate = operationDate.Date;
        var previous = await db.SaleChequeReschedules.FirstOrDefaultAsync(x => x.OperationId == operationId);
        if (previous is not null)
        {
            if (previous.ChequeId != chequeId || previous.PreviousDueDate != expectedDueDate ||
                previous.NewDueDate != newDueDate || previous.Date != operationDate ||
                (confirmedIncomeDelta.HasValue && previous.IncomeDelta != confirmedIncomeDelta.Value))
                throw new InvalidOperationException("شناسه عملیات قبلاً برای تغییر سررسید دیگری استفاده شده است.");
            return previous;
        }
        if (expectedDueDate == newDueDate) throw new InvalidOperationException("سررسید جدید باید با سررسید قبلی متفاوت باشد.");
        var cheque = await db.Cheques.FirstOrDefaultAsync(c => c.Id == chequeId)
            ?? throw new InvalidOperationException("چک یافت نشد.");
        if (cheque.DueDate.Date != expectedDueDate)
            throw new InvalidOperationException("سررسید چک تغییر کرده است؛ اطلاعات را دوباره بارگذاری کنید.");
        if (cheque.IsCleared || cheque.Status is not (ChequeStatus.InHand or ChequeStatus.Bounced))
            throw new InvalidOperationException("تغییر سررسید فقط برای چک نزد صندوق یا برگشتی مجاز است.");
        var sale = await db.Transactions.FirstOrDefaultAsync(t => t.Id == cheque.TransactionId)
            ?? throw new InvalidOperationException("فاکتور یافت نشد.");
        if (sale.Type != TransactionType.Sale || sale.PartyId is not > 0 || operationDate < sale.Date.Date)
            throw new InvalidOperationException("فاکتور فروش یا تاریخ اصلاح معتبر نیست.");
        var charge = await db.SaleChequeCharges.FirstOrDefaultAsync(c => c.TransactionId == sale.Id)
            ?? throw new InvalidOperationException("ردیف تاخیر تادیه چک یافت نشد.");
        if (charge.CompanyId != sale.CompanyId || cheque.CompanyId != sale.CompanyId ||
            charge.IncomeAccountId is not > 0 || charge.PartyAccountId is not > 0 ||
            charge.IncomeAccountId == charge.PartyAccountId || charge.Amount < 0 || charge.CustomerCredit < 0 ||
            (charge.Amount > 0 && charge.CustomerCredit > 0) || sale.Amount != charge.BaseAmount + charge.Amount)
            throw new InvalidOperationException("اطلاعات مالی فاکتور ناسازگار است؛ تغییر سررسید متوقف شد.");
        if (await db.SaleChequeCancellations.AnyAsync(c => c.SaleTransactionId == sale.Id))
            throw new InvalidOperationException("فاکتور ابطال شده است و تغییر سررسید مجاز نیست.");
        var ledgerEvents = await db.SaleChequeChargeEvents.AsNoTracking().Where(e => e.SaleChequeChargeId == charge.Id)
            .Select(e => new { e.Kind, e.Date, e.IncomeDelta, e.AccVoucher.Status }).ToListAsync();
        if (ledgerEvents.Count(e => e.Kind == SaleChequeChargeEventKind.Initial) != 1 ||
            ledgerEvents.Any(e => e.Status != VoucherStatus.Confirmed || e.Date.Date > operationDate) ||
            ledgerEvents.Sum(e => e.IncomeDelta) != charge.Amount - charge.CustomerCredit ||
            await db.SaleChequeReschedules.AnyAsync(x => x.SaleChequeChargeId == charge.Id && x.Date > operationDate) ||
            await db.SaleChequeReturns.AnyAsync(x => x.SaleTransactionId == sale.Id && x.Date > operationDate) ||
            await db.SaleChequeCreditSettlements.AnyAsync(x => x.SaleTransactionId == sale.Id && x.Date > operationDate) ||
            await db.SaleChequeInstallmentReceipts.AnyAsync(r => r.TransactionId == sale.Id && (r.Date > operationDate || r.ReversalDate > operationDate)) ||
            await db.SaleChequeReceipts.AnyAsync(r => r.TransactionId == sale.Id && (r.Date > operationDate || r.ReversalDate > operationDate)))
            throw new InvalidOperationException("تاریخچه درآمد با فاکتور مطابقت ندارد یا تاریخ اصلاح پیش از عملیات قبلی است.");
        var accounts = await db.AccAccounts.AsNoTracking()
            .Where(a => a.Id == charge.IncomeAccountId || a.Id == charge.PartyAccountId).ToListAsync();
        if (accounts.Count != 2 || accounts.Any(a => !a.IsActive || !a.IsPostable) ||
            accounts.Single(a => a.Id == charge.IncomeAccountId).Type != AccountType.Income ||
            accounts.Single(a => a.Id == charge.PartyAccountId).Type != AccountType.Asset)
            throw new InvalidOperationException("حساب‌های ثبت‌شده مشتری و درآمد باید فعال و قابل ثبت باشند.");
        var years = await db.AccFiscalYears.Where(y => y.StartDate <= operationDate && y.EndDate >= operationDate).ToListAsync();
        if (years.Count != 1 || years[0].IsClosed)
            throw new InvalidOperationException("برای تاریخ اصلاح باید دقیقاً یک سال مالی باز وجود داشته باشد.");
        decimal effectiveBase, delta, newNet;
        try
        {
            effectiveBase = await EffectiveBaseAsync(db, charge);
            delta = await CalculateDeltaAsync(db, cheque, charge, newDueDate);
            newNet = charge.Amount - charge.CustomerCredit + delta;
            if (decimal.Abs(newNet) > 9999999999999999.99m)
                throw new OverflowException();
        }
        catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException)
        {
            throw new InvalidOperationException("مبلغ تعدیل سررسید خارج از محدوده یا شرایط آن نامعتبر است.", ex);
        }
        if (confirmedIncomeDelta.HasValue && confirmedIncomeDelta.Value != delta)
            throw new InvalidOperationException("مبلغ تعدیل نسبت به پیش‌نمایش تغییر کرده است؛ دوباره بررسی و تأیید کنید.");
        var newIncome = Math.Max(0, newNet);
        var newCredit = Math.Max(0, -newNet);
        var invoiceTotal = SaleChequeVoucherAmounts.Create(charge.BaseAmount, 0, newIncome).CustomerDebit;
        SaleChequeChargeEvent? incomeEvent = null;
        if (delta != 0)
        {
            var amounts = SaleChequeCorrectionAmounts.ForDueDate(delta);
            var year = years[0];
            var number = (await db.AccVouchers.Where(v => v.FiscalYearId == year.Id).Select(v => (int?)v.Number).MaxAsync() ?? 0) + 1;
            var title = $"تعدیل سررسید چک — فاکتور {sale.Number}";
            var voucher = new AccVoucher
            {
                FiscalYearId = year.Id, Number = number, Date = operationDate, Status = VoucherStatus.Confirmed,
                Source = VoucherSource.StoreSaleChequeAdjustment, SourceId = sale.Id, SourceTitle = title,
                Description = title, RefNumber = sale.Number, CreatedBy = user, ConfirmedBy = user,
                CreatedAt = DateTime.Now, ConfirmedAt = DateTime.Now,
                TotalDebit = amounts.TotalDebit, TotalCredit = amounts.TotalCredit,
                Lines = new()
                {
                    new AccVoucherLine { RowNo = 1, AccountId = charge.PartyAccountId.Value, PartyId = sale.PartyId,
                        Debit = amounts.PartyDebit, Credit = amounts.PartyCredit, Description = title, RefNumber = sale.Number },
                    new AccVoucherLine { RowNo = 2, AccountId = charge.IncomeAccountId.Value, PartyId = sale.PartyId,
                        Debit = amounts.IncomeDebit, Credit = amounts.IncomeCredit, Description = title, RefNumber = sale.Number }
                }
            };
            incomeEvent = new SaleChequeChargeEvent
            {
                CompanyId = charge.CompanyId, OperationId = operationId, SaleChequeChargeId = charge.Id,
                AccVoucher = voucher, Kind = SaleChequeChargeEventKind.DueDateAdjustment, Date = operationDate, IncomeDelta = delta
            };
            db.SaleChequeChargeEvents.Add(incomeEvent);
        }
        var record = new SaleChequeReschedule
        {
            CompanyId = charge.CompanyId, OperationId = operationId, ChequeId = cheque.Id, SaleChequeChargeId = charge.Id,
            SaleChequeChargeEvent = incomeEvent, PreviousDueDate = expectedDueDate, NewDueDate = newDueDate,
            Date = operationDate, BaseAmount = effectiveBase, RatePercent = charge.RatePercent, PeriodDays = charge.PeriodDays,
            IncomeDelta = delta, CreatedBy = user, CreatedAt = DateTime.Now
        };
        cheque.DueDate = newDueDate;
        charge.Amount = newIncome;
        charge.CustomerCredit = newCredit;
        sale.Amount = invoiceTotal;
        db.SaleChequeReschedules.Add(record);
        await db.SaveChangesAsync();
        return record;
    }
}
