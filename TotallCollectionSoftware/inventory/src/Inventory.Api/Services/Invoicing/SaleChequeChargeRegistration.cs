using System.Data;
using Inventory.Api.Data;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Api.Services.Invoicing;

/// <summary>ورودی‌های مورد تأیید کاربر برای ردیف مالی؛ هیچ مبلغ سودی از کلاینت پذیرفته نمی‌شود.</summary>
public sealed record SaleChequeChargeRegistrationInput(
    decimal CalculatorBaseAmount, decimal CalculatorRatePercent, int CalculatorMonths,
    decimal AdjustmentRatePercent, int AdjustmentPeriodDays, DateTime? ConfirmedCreditDueDate);

/// <summary>
/// ثبت اولیهٔ ردیف مالی و گردش فروشگاهی در تراکنش فروش؛
/// در هر خطا تراکنش فراخواننده rollback می‌شود.
/// </summary>
public static class SaleChequeChargeRegistration
{
    public static async Task<SaleChequeChargeEvent> RegisterAsync(AppDbContext db, int saleId,
        SaleChequeChargeRegistrationInput input, string? user)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("ثبت درآمد چک باید در تراکنش فاکتور انجام شود.");
        try
        {
            ArgumentNullException.ThrowIfNull(input);
            if (transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                throw new InvalidOperationException("تراکنش درآمد چک باید سطح Serializable داشته باشد.");
            return await RegisterInTransactionAsync(db, saleId, input, user);
        }
        catch
        {
            // Abort the entire sale, including any stock changes already saved by the caller.
            // EF does not restore tracked entities when a database transaction rolls back.
            // Detach them even if rollback itself fails, so a reused context cannot expose
            // the failed sale/charge as if it still existed. Callers must reload their state.
            try
            {
                await transaction.RollbackAsync();
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
            throw;
        }
    }

    private static async Task<SaleChequeChargeEvent> RegisterInTransactionAsync(AppDbContext db, int saleId,
        SaleChequeChargeRegistrationInput input, string? user)
    {
        var sale = await db.Transactions.Include(t => t.Lines).Include(t => t.Cheques).Include(t => t.Installments)
            .FirstOrDefaultAsync(t => t.Id == saleId)
            ?? throw new InvalidOperationException("فاکتور فروش یافت نشد.");
        if (sale.Type != TransactionType.Sale || sale.PartyId is not > 0 || sale.Lines.Count == 0)
            throw new InvalidOperationException("ردیف مالی فقط برای فاکتور فروش دارای مشتری و کالا قابل ثبت است.");
        if (await db.SaleChequeCharges.AnyAsync(c => c.TransactionId == sale.Id))
            throw new InvalidOperationException("برای این فاکتور ردیف تاخیر تادیه چک از قبل وجود دارد.");
        if (input.AdjustmentRatePercent < 0 || input.AdjustmentRatePercent > 999999999999.999999m ||
            decimal.Round(input.AdjustmentRatePercent, 6) != input.AdjustmentRatePercent || input.AdjustmentPeriodDays <= 0)
            throw new InvalidOperationException("نرخ و بازه تعدیل سررسید معتبر نیست.");
        var baseAmount = sale.Lines.Sum(l => Math.Max(0m, (l.GrossAmount ?? OrderLineAmounts.Gross(l.Quantity, l.Price)) - l.DiscountAmount) + l.VatAmount);
        if (baseAmount <= 0 || sale.Amount != baseAmount)
            throw new InvalidOperationException("مبلغ پایه فاکتور با اقلام و ارزش افزوده مطابقت ندارد.");
        decimal income;
        try
        {
            income = SaleChequeInitialCalculation.Calculate(input.CalculatorBaseAmount, input.CalculatorRatePercent, input.CalculatorMonths);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new InvalidOperationException("ورودی‌های ماشین‌حساب اولیه تاخیر تادیه چک معتبر نیست.");
        }
        if (input.CalculatorBaseAmount > baseAmount || income <= 0)
            throw new InvalidOperationException("مبنای ماشین‌حساب یا سود اولیه تاخیر تادیه چک معتبر نیست.");
        var amounts = SaleChequeVoucherAmounts.Create(baseAmount, 0, income);
        var cheques = sale.Cheques.Sum(c => c.Amount);
        // A nonzero but tiny cheque can otherwise be classified as a cash-only
        // invoice by PaymentMix, inventing an initial cash receipt. Require an
        // actual cheque component for every cheque-income article.
        if (cheques <= PaymentMix.Tolerance / 2m)
            throw new InvalidOperationException("برای ثبت تاخیر تادیه چک، جمع چک‌های معتبر باید بیش از نیم ریال باشد.");
        var cash = sale.PaymentMethod == PaymentMethod.Cash ? sale.Amount : sale.CashAmount;
        if (cash > 0)
        {
            if (sale.TrsAccountId is not > 0)
                throw new InvalidOperationException("برای دریافت نقدی حساب فعال صندوق یا بانک را انتخاب کنید.");
            var account = await db.TrsAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == sale.TrsAccountId && a.IsActive)
                ?? throw new InvalidOperationException("حساب صندوق یا بانک فعال لازم است.");
            if (await db.SystemCompanies.CountAsync() > 1 && (sale.CompanyId is not > 0 || account.CompanyId != sale.CompanyId))
                throw new InvalidOperationException("در محیط چندشرکتی، حساب صندوق یا بانک باید متعلق به شرکت همین فاکتور باشد.");
            await SaleChequeTreasuryGuard.VerifyCompanyOwnershipAsync(db, account, sale.CompanyId);
            var compatible = sale.CashType switch
            {
                CashType.Cash => account.Kind is TreasuryAccountKind.Cash or TreasuryAccountKind.PettyCash,
                CashType.CardReader => account.Kind is TreasuryAccountKind.Pos or TreasuryAccountKind.Bank,
                CashType.CardTransfer => account.Kind is TreasuryAccountKind.Bank or TreasuryAccountKind.Pos,
                _ => false
            };
            if (!compatible) throw new InvalidOperationException("نوع حساب با روش دریافت نقدی انتخاب‌شده مطابقت ندارد.");
        }
        var installments = sale.Installments.Sum(i => i.Amount);
        var parts = cash + cheques + installments;
        if (parts > amounts.CustomerDebit)
            throw new InvalidOperationException("جمع پرداخت‌ها بیشتر از مبلغ فاکتور است؛ اختلاف را دستی اصلاح کنید.");
        var credit = amounts.CustomerDebit - parts;
        if (credit > 0 && !input.ConfirmedCreditDueDate.HasValue)
            throw new InvalidOperationException("جمع پرداخت‌ها کمتر از مبلغ فاکتور است؛ با تعیین سررسید نسیه اختلاف را تأیید کنید.");
        // Store the user's explicit credit decision; do not modify cheque amounts automatically.
        sale.DueDate = credit > 0 ? input.ConfirmedCreditDueDate!.Value.Date : null;
        sale.PaymentMethod = PaymentMix.Derive(cash, cheques, installments, credit);
        sale.Amount = amounts.CustomerDebit;
        var charge = new SaleChequeCharge
        {
            CompanyId = sale.CompanyId, TransactionId = sale.Id, BaseAmount = baseAmount,
            RatePercent = input.AdjustmentRatePercent, PeriodDays = input.AdjustmentPeriodDays,
            CalculatorBaseAmount = input.CalculatorBaseAmount, CalculatorRatePercent = input.CalculatorRatePercent,
            CalculatorMonths = input.CalculatorMonths, InitialAmount = income, Amount = income, CustomerCredit = 0
        };
        db.SaleChequeCharges.Add(charge);
        // This save is inside the existing transaction, not a commit.
        await db.SaveChangesAsync();
        var entry = await SaleChequeChargePosting.PostAsync(db, sale.Id, user);
        if (cash > 0)
        {
            var number = (await db.TrsVouchers.IgnoreQueryFilters().Where(v => v.Kind == TreasuryKind.Receipt)
                .Select(v => (int?)v.Number).MaxAsync() ?? 0) + 1;
            var title = $"دریافت نقدی فاکتور فروش {sale.Number}";
            var cashReceipt = new TrsVoucher
            {
                CompanyId = sale.CompanyId,
                Number = number, Kind = TreasuryKind.Receipt, Date = sale.Date.Date, PartyId = sale.PartyId,
                Description = title, RefNumber = sale.Number, Status = TreasuryStatus.Confirmed,
                TotalAmount = cash, CashAmount = cash,
                CreatedBy = user, ConfirmedBy = user, CreatedAt = DateTime.Now, ConfirmedAt = DateTime.Now,
                Lines = new()
                {
                    new TrsVoucherLine
                    {
                        RowNo = 1, TrsAccountId = sale.TrsAccountId, Amount = cash,
                        Method = sale.CashType == CashType.Cash ? PayMethod.Cash : PayMethod.Card,
                        // The full tracking number remains on the sale; legacy treasury references are limited to 60 characters.
                        RefNumber = sale.CashTrackingNumber is { Length: <= 60 } ? sale.CashTrackingNumber : null, Description = title
                    }
                }
            };
            db.TrsVouchers.Add(cashReceipt);
            charge.InitialCashTrsVoucher = cashReceipt;
            await db.SaveChangesAsync();
        }
        return entry;
    }
}
