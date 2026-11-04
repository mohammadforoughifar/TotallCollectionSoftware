using System.Data;
using Inventory.Api.Data;
using Inventory.Api.Services.Accounting;
using Inventory.Api.Services.Treasury;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Api.Services.Invoicing;

/// <summary>
/// تعیین‌تکلیف اتمیک بستانکاری مشتری از دو مسیر مجاز:
/// ۱) پرداخت وجه بستانکاری از صندوق/بانک شرکت با سند حسابداری و خزانه
/// ۲) مصرف بستانکاری برای تسویه فاکتور فروش دیگر همان مشتری بدون دوباره‌شماری
/// </summary>
public static class SaleChequeCreditSettlementService
{
    private const decimal MaxMoney = 9999999999999999.99m;

    public static async Task<SaleChequeCreditSettlement> RefundAsync(
        AppDbContext db,
        int saleId,
        Guid operationId,
        int trsAccountId,
        decimal amount,
        DateTime date,
        string? note,
        string? user)
    {
        var tx = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("پرداخت بستانکاری مشتری باید در تراکنش حسابداری انجام شود.");
        try
        {
            if (tx.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                throw new InvalidOperationException("تراکنش پرداخت بستانکاری باید سطح Serializable داشته باشد.");
            return await RefundInTransactionAsync(db, saleId, operationId, trsAccountId, amount, date, note, user);
        }
        catch
        {
            try { await tx.RollbackAsync(); }
            finally { db.ChangeTracker.Clear(); }
            throw;
        }
    }

    public static async Task<SaleChequeCreditSettlement> ApplyToSaleAsync(
        AppDbContext db,
        int sourceSaleId,
        int targetSaleId,
        Guid operationId,
        decimal amount,
        DateTime date,
        string? note,
        string? user)
    {
        var tx = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("مصرف بستانکاری مشتری باید در تراکنش حسابداری انجام شود.");
        try
        {
            if (tx.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                throw new InvalidOperationException("تراکنش مصرف بستانکاری باید سطح Serializable داشته باشد.");
            return await ApplyToSaleInTransactionAsync(db, sourceSaleId, targetSaleId, operationId, amount, date, note, user);
        }
        catch
        {
            try { await tx.RollbackAsync(); }
            finally { db.ChangeTracker.Clear(); }
            throw;
        }
    }

    private static async Task<SaleChequeCreditSettlement> RefundInTransactionAsync(
        AppDbContext db,
        int saleId,
        Guid operationId,
        int trsAccountId,
        decimal amount,
        DateTime date,
        string? note,
        string? user)
    {
        if (operationId == Guid.Empty || trsAccountId <= 0 || date == default ||
            amount <= 0 || amount > MaxMoney || decimal.Round(amount, 2) != amount ||
            note?.Length > 200 || user?.Length > 120)
            throw new InvalidOperationException("مشخصات پرداخت بستانکاری مشتری معتبر نیست.");
        date = date.Date;

        var prior = await db.SaleChequeCreditSettlements.FirstOrDefaultAsync(s => s.OperationId == operationId);
        if (prior is not null)
        {
            if (prior.Kind != SaleChequeCreditSettlementKind.RefundPayment ||
                prior.SaleTransactionId != saleId || prior.TrsAccountId != trsAccountId ||
                prior.Amount != amount || prior.Date.Date != date)
                throw new InvalidOperationException("شناسه عملیات قبلاً برای تسویه دیگری استفاده شده است.");
            return prior;
        }

        var (sale, charge) = await LoadVerifiedSourceSaleAsync(db, saleId, date);
        var signedBalance = await SaleChequeSettlementLedger.SignedChargedSaleBalanceAsync(db, sale, charge);
        var availableCredit = Math.Max(0m, -signedBalance);
        if (amount > availableCredit)
            throw new InvalidOperationException("مبلغ پرداختی بیشتر از بستانکاری قابل پرداخت مشتری در این فاکتور است.");

        var trsAccount = await db.TrsAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == trsAccountId && a.IsActive && a.AccountId > 0)
            ?? throw new InvalidOperationException("حساب صندوق یا بانک فعال و متصل به حسابداری یافت نشد.");
        await SaleChequeTreasuryGuard.VerifyCompanyOwnershipAsync(db, trsAccount, sale.CompanyId);
        var ledgerAccountId = trsAccount.AccountId!.Value;
        var partyAccountId = charge.PartyAccountId!.Value;
        if (ledgerAccountId == partyAccountId)
            throw new InvalidOperationException("حساب صندوق/بانک و حساب مشتری باید مستقل باشند.");

        var accounts = await db.AccAccounts.AsNoTracking()
            .Where(a => a.Id == ledgerAccountId || a.Id == partyAccountId).ToListAsync();
        if (accounts.Count != 2 || accounts.Any(a => !a.IsActive || !a.IsPostable || a.Type != AccountType.Asset))
            throw new InvalidOperationException("حساب‌های مشتری و صندوق/بانک باید دارایی، فعال و قابل ثبت باشند.");

        var trsBalance = await new TreasuryService(db, new AccountingService(db)).GetAccountAsync(trsAccount.Id);
        if (trsBalance is null || trsBalance.Balance < amount)
            throw new InvalidOperationException("مانده حساب صندوق/بانک برای پرداخت بستانکاری مشتری کافی نیست.");

        var year = await RequireOpenFiscalYearAsync(db, date);
        var voucherNo = (await db.AccVouchers.Where(v => v.FiscalYearId == year.Id).Select(v => (int?)v.Number).MaxAsync() ?? 0) + 1;
        var title = $"پرداخت بستانکاری مشتری — فاکتور {sale.Number}";
        var voucher = new AccVoucher
        {
            FiscalYearId = year.Id,
            Number = voucherNo,
            Date = date,
            Status = VoucherStatus.Confirmed,
            Source = VoucherSource.StoreSaleCreditRefund,
            SourceId = sale.Id,
            SourceTitle = title,
            Description = string.IsNullOrWhiteSpace(note) ? title : $"{title} ({note.Trim()})",
            RefNumber = sale.Number,
            CreatedBy = user,
            ConfirmedBy = user,
            CreatedAt = DateTime.Now,
            ConfirmedAt = DateTime.Now,
            TotalDebit = amount,
            TotalCredit = amount,
            Lines = new()
            {
                new AccVoucherLine
                {
                    RowNo = 1, AccountId = partyAccountId, PartyId = sale.PartyId,
                    Debit = amount, Description = title, RefNumber = sale.Number
                },
                new AccVoucherLine
                {
                    RowNo = 2, AccountId = ledgerAccountId, PartyId = sale.PartyId,
                    Credit = amount, Description = title, RefNumber = sale.Number
                }
            }
        };
        db.AccVouchers.Add(voucher);
        await db.SaveChangesAsync();

        var trsNo = (await db.TrsVouchers.Where(v => v.Kind == TreasuryKind.Payment)
            .Select(v => (int?)v.Number).MaxAsync() ?? 0) + 1;
        var trsVoucher = new TrsVoucher
        {
            Number = trsNo,
            Kind = TreasuryKind.Payment,
            Date = date,
            PartyId = sale.PartyId,
            Description = title,
            RefNumber = sale.Number,
            Status = TreasuryStatus.Confirmed,
            TotalAmount = amount,
            CashAmount = amount,
            VoucherId = voucher.Id,
            CreatedBy = user,
            ConfirmedBy = user,
            CreatedAt = DateTime.Now,
            ConfirmedAt = DateTime.Now,
            Lines = new()
            {
                new TrsVoucherLine
                {
                    RowNo = 1,
                    TrsAccountId = trsAccount.Id,
                    Amount = amount,
                    Method = trsAccount.Kind is TreasuryAccountKind.Cash or TreasuryAccountKind.PettyCash ? PayMethod.Cash : PayMethod.Card,
                    RefNumber = sale.Number,
                    Description = title
                }
            }
        };
        db.TrsVouchers.Add(trsVoucher);

        var settlement = new SaleChequeCreditSettlement
        {
            CompanyId = sale.CompanyId,
            OperationId = operationId,
            Kind = SaleChequeCreditSettlementKind.RefundPayment,
            SaleTransactionId = sale.Id,
            SaleChequeChargeId = charge.Id,
            TrsAccountId = trsAccount.Id,
            TreasuryLedgerAccountId = ledgerAccountId,
            PartyAccountId = partyAccountId,
            AccVoucher = voucher,
            TrsVoucher = trsVoucher,
            Date = date,
            Amount = amount,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedBy = user,
            CreatedAt = DateTime.Now
        };
        db.SaleChequeCreditSettlements.Add(settlement);
        await db.SaveChangesAsync();
        return settlement;
    }

    private static async Task<SaleChequeCreditSettlement> ApplyToSaleInTransactionAsync(
        AppDbContext db,
        int sourceSaleId,
        int targetSaleId,
        Guid operationId,
        decimal amount,
        DateTime date,
        string? note,
        string? user)
    {
        if (operationId == Guid.Empty || sourceSaleId <= 0 || targetSaleId <= 0 || sourceSaleId == targetSaleId ||
            date == default || amount <= 0 || amount > MaxMoney || decimal.Round(amount, 2) != amount ||
            note?.Length > 200 || user?.Length > 120)
            throw new InvalidOperationException("مشخصات انتقال بستانکاری به فاکتور دیگر معتبر نیست.");
        date = date.Date;

        var prior = await db.SaleChequeCreditSettlements.FirstOrDefaultAsync(s => s.OperationId == operationId);
        if (prior is not null)
        {
            if (prior.Kind != SaleChequeCreditSettlementKind.ApplyToSale ||
                prior.SaleTransactionId != sourceSaleId || prior.TargetSaleTransactionId != targetSaleId ||
                prior.Amount != amount || prior.Date.Date != date)
                throw new InvalidOperationException("شناسه عملیات قبلاً برای انتقال بستانکاری دیگری استفاده شده است.");
            return prior;
        }

        var (sourceSale, sourceCharge) = await LoadVerifiedSourceSaleAsync(db, sourceSaleId, date);
        var targetSale = await db.Transactions.FirstOrDefaultAsync(t => t.Id == targetSaleId)
            ?? throw new InvalidOperationException("فاکتور مقصد یافت نشد.");
        if (targetSale.Type != TransactionType.Sale || targetSale.PartyId != sourceSale.PartyId ||
            targetSale.CompanyId != sourceSale.CompanyId || date < targetSale.Date.Date)
            throw new InvalidOperationException("فاکتور مقصد باید فروشِ همان مشتری و همان شرکت باشد و تاریخ انتقال پیش از آن نباشد.");
        if (await db.SaleChequeCancellations.AnyAsync(c => c.SaleTransactionId == targetSale.Id))
            throw new InvalidOperationException("فاکتور مقصد ابطال شده است.");

        var signedSource = await SaleChequeSettlementLedger.SignedChargedSaleBalanceAsync(db, sourceSale, sourceCharge);
        var availableCredit = Math.Max(0m, -signedSource);
        if (amount > availableCredit)
            throw new InvalidOperationException("مبلغ انتخاب‌شده بیشتر از بستانکاری قابل مصرف مشتری در فاکتور مبدأ است.");

        var unpaidTarget = await SaleChequeSettlementLedger.UnpaidSaleBalanceAsync(db, targetSale);
        if (amount > unpaidTarget)
            throw new InvalidOperationException("مبلغ انتخاب‌شده بیشتر از مانده پرداخت‌نشده فاکتور مقصد است.");
        if (targetSale.SettledAmount + amount > MaxMoney)
            throw new InvalidOperationException("جمع تسویه فاکتور مقصد از محدوده حسابداری بیشتر است.");

        var targetCharge = await db.SaleChequeCharges.AsNoTracking().FirstOrDefaultAsync(c => c.TransactionId == targetSale.Id);
        var sourcePartyAccountId = sourceCharge.PartyAccountId!.Value;
        var targetPartyAccountId = targetCharge?.PartyAccountId ?? sourcePartyAccountId;
        if (targetPartyAccountId <= 0)
            throw new InvalidOperationException("حساب دریافتنی مشتری در فاکتور مقصد معتبر نیست.");

        var neededIds = new[] { sourcePartyAccountId, targetPartyAccountId }.Distinct().ToList();
        var accounts = await db.AccAccounts.AsNoTracking().Where(a => neededIds.Contains(a.Id)).ToListAsync();
        if (accounts.Count != neededIds.Count || accounts.Any(a => !a.IsActive || !a.IsPostable || a.Type != AccountType.Asset))
            throw new InvalidOperationException("حساب دریافتنی مشتری باید فعال و قابل ثبت باشد.");

        var year = await RequireOpenFiscalYearAsync(db, date);
        var voucherNo = (await db.AccVouchers.Where(v => v.FiscalYearId == year.Id).Select(v => (int?)v.Number).MaxAsync() ?? 0) + 1;
        var title = $"مصرف بستانکاری فاکتور {sourceSale.Number} در فاکتور {targetSale.Number}";
        var voucher = new AccVoucher
        {
            FiscalYearId = year.Id,
            Number = voucherNo,
            Date = date,
            Status = VoucherStatus.Confirmed,
            Source = VoucherSource.StoreSaleCreditAllocation,
            SourceId = sourceSale.Id,
            SourceTitle = title,
            Description = string.IsNullOrWhiteSpace(note) ? title : $"{title} ({note.Trim()})",
            RefNumber = targetSale.Number,
            CreatedBy = user,
            ConfirmedBy = user,
            CreatedAt = DateTime.Now,
            ConfirmedAt = DateTime.Now,
            TotalDebit = amount,
            TotalCredit = amount,
            Lines = new()
            {
                new AccVoucherLine
                {
                    RowNo = 1, AccountId = sourcePartyAccountId, PartyId = sourceSale.PartyId,
                    Debit = amount, Description = $"انتقال بستانکاری از فاکتور {sourceSale.Number}", RefNumber = sourceSale.Number
                },
                new AccVoucherLine
                {
                    RowNo = 2, AccountId = targetPartyAccountId, PartyId = targetSale.PartyId,
                    Credit = amount, Description = $"تسویه فاکتور {targetSale.Number} از بستانکاری {sourceSale.Number}", RefNumber = targetSale.Number
                }
            }
        };
        db.AccVouchers.Add(voucher);

        targetSale.SettledAmount += amount;

        var settlement = new SaleChequeCreditSettlement
        {
            CompanyId = sourceSale.CompanyId,
            OperationId = operationId,
            Kind = SaleChequeCreditSettlementKind.ApplyToSale,
            SaleTransactionId = sourceSale.Id,
            TargetSaleTransactionId = targetSale.Id,
            SaleChequeChargeId = sourceCharge.Id,
            PartyAccountId = sourcePartyAccountId,
            TargetPartyAccountId = targetPartyAccountId,
            AccVoucher = voucher,
            Date = date,
            Amount = amount,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedBy = user,
            CreatedAt = DateTime.Now
        };
        db.SaleChequeCreditSettlements.Add(settlement);
        await db.SaveChangesAsync();
        return settlement;
    }

    private static async Task<(Transaction Sale, SaleChequeCharge Charge)> LoadVerifiedSourceSaleAsync(
        AppDbContext db, int saleId, DateTime date)
    {
        var sale = await db.Transactions.FirstOrDefaultAsync(t => t.Id == saleId)
            ?? throw new InvalidOperationException("فاکتور مبدأ یافت نشد.");
        if (sale.Type != TransactionType.Sale || sale.PartyId is not > 0 || date < sale.Date.Date)
            throw new InvalidOperationException("فاکتور مبدأ یا تاریخ تسویه معتبر نیست.");
        if (await db.SaleChequeCancellations.AnyAsync(c => c.SaleTransactionId == sale.Id))
            throw new InvalidOperationException("فاکتور مبدأ ابطال شده است.");
        var charge = await db.SaleChequeCharges.AsNoTracking().FirstOrDefaultAsync(c => c.TransactionId == sale.Id)
            ?? throw new InvalidOperationException("ردیف تاخیر تادیه چک در فاکتور مبدأ یافت نشد.");
        if (charge.CompanyId != sale.CompanyId || charge.PartyAccountId is not > 0 || charge.IncomeAccountId is not > 0)
            throw new InvalidOperationException("حساب‌های ثبت‌شده فاکتور مبدأ معتبر نیستند.");

        if (await db.SaleChequeChargeEvents.AnyAsync(e => e.SaleChequeChargeId == charge.Id && e.Date > date) ||
            await db.SaleChequeReturns.AnyAsync(r => r.SaleTransactionId == sale.Id && r.Date > date) ||
            await db.SaleChequeReschedules.AnyAsync(r => r.SaleChequeChargeId == charge.Id && r.Date > date) ||
            await db.SaleChequeCreditSettlements.AnyAsync(s => s.SaleTransactionId == sale.Id && s.Date > date) ||
            await db.SaleChequeInstallmentReceipts.AnyAsync(r => r.TransactionId == sale.Id && (r.Date > date || r.ReversalDate > date)) ||
            await db.SaleChequeReceipts.AnyAsync(r => r.TransactionId == sale.Id && (r.Date > date || r.ReversalDate > date)))
            throw new InvalidOperationException("تاریخ تسویه بستانکاری نمی‌تواند پیش از عملیات قبلی فاکتور باشد.");

        return (sale, charge);
    }

    private static async Task<AccFiscalYear> RequireOpenFiscalYearAsync(AppDbContext db, DateTime date)
    {
        var years = await db.AccFiscalYears.Where(y => y.StartDate <= date && y.EndDate >= date).ToListAsync();
        if (years.Count != 1 || years[0].IsClosed)
            throw new InvalidOperationException("برای تاریخ عملیات باید دقیقاً یک سال مالی باز وجود داشته باشد.");
        return years[0];
    }
}
