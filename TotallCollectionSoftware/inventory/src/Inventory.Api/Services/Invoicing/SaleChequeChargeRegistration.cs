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
/// آماده‌سازی ثبت اولیهٔ ردیف مالی و سند در تراکنش فروش. این مسیر هنوز به API فاکتور
/// متصل نیست؛ در صورت هر خطا کل تراکنش فراخواننده rollback می‌شود.
/// </summary>
public static class SaleChequeChargeRegistration
{
    public static async Task<AccVoucher> RegisterAsync(AppDbContext db, int saleId,
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

    private static async Task<AccVoucher> RegisterInTransactionAsync(AppDbContext db, int saleId,
        SaleChequeChargeRegistrationInput input, string? user)
    {
        var sale = await db.Transactions.Include(t => t.Lines).Include(t => t.Cheques).Include(t => t.Installments)
            .FirstOrDefaultAsync(t => t.Id == saleId)
            ?? throw new InvalidOperationException("فاکتور فروش یافت نشد.");
        if (sale.Type != TransactionType.Sale || sale.PartyId is not > 0 || sale.Lines.Count == 0)
            throw new InvalidOperationException("ردیف مالی فقط برای فاکتور فروش دارای مشتری و کالا قابل ثبت است.");
        if (await db.SaleChequeCharges.AnyAsync(c => c.TransactionId == sale.Id) ||
            await db.AccVouchers.AnyAsync(v => v.Source == VoucherSource.StoreSale && v.SourceId == sale.Id))
            throw new InvalidOperationException("برای این فاکتور ردیف مالی یا سند حسابداری از قبل وجود دارد.");
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
        var cash = sale.PaymentMethod == PaymentMethod.Cash ? sale.Amount : sale.CashAmount;
        var cheques = sale.Cheques.Sum(c => c.Amount);
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
        db.SaleChequeCharges.Add(new SaleChequeCharge
        {
            CompanyId = sale.CompanyId, TransactionId = sale.Id, BaseAmount = baseAmount,
            RatePercent = input.AdjustmentRatePercent, PeriodDays = input.AdjustmentPeriodDays,
            CalculatorBaseAmount = input.CalculatorBaseAmount, CalculatorRatePercent = input.CalculatorRatePercent,
            CalculatorMonths = input.CalculatorMonths, InitialAmount = income, Amount = income, CustomerCredit = 0
        });
        // This save is inside the existing transaction, not a commit.
        await db.SaveChangesAsync();
        return await SaleChequeChargePosting.PostAsync(db, sale.Id, user);
    }
}
