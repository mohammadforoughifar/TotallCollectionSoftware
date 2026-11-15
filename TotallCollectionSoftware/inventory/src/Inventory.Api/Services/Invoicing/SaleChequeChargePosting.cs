using System.Data;
using Inventory.Api.Data;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Api.Services.Invoicing;

/// <summary>
/// ثبت رویداد اولیه درآمد در دفتر گردش فروشگاهی. این رویداد بخشی از تراکنش
/// فاکتور است و به قاعده، حساب، سال مالی یا سند حسابداری ERP نیازی ندارد.
/// </summary>
public static class SaleChequeChargePosting
{
    public static async Task<SaleChequeChargeEvent> PostAsync(AppDbContext db, int saleId, string? user)
    {
        _ = user;
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("ثبت گردش چک باید در تراکنش فاکتور انجام شود.");
        if (transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("تراکنش گردش چک باید سطح Serializable داشته باشد.");

        var sale = await db.Transactions.Include(t => t.Lines).Include(t => t.Cheques).Include(t => t.Installments)
            .FirstOrDefaultAsync(t => t.Id == saleId)
            ?? throw new InvalidOperationException("فاکتور فروش یافت نشد.");
        var charge = await db.SaleChequeCharges.FirstOrDefaultAsync(x => x.TransactionId == sale.Id)
            ?? throw new InvalidOperationException("ردیف تاخیر تادیه چک یافت نشد.");
        if (sale.Type != TransactionType.Sale || sale.PartyId is not > 0 || sale.CompanyId != charge.CompanyId ||
            charge.Amount <= 0 || charge.CustomerCredit != 0 || charge.InitialAmount != charge.Amount ||
            charge.RatePercent < 0 || charge.RatePercent > 999999999999.999999m ||
            decimal.Round(charge.RatePercent, 6) != charge.RatePercent || charge.PeriodDays <= 0)
            throw new InvalidOperationException("اطلاعات اولیه فاکتور یا تاخیر تادیه چک معتبر نیست.");
        if (await db.SaleChequeChargeEvents.AnyAsync(e => e.SaleChequeChargeId == charge.Id))
            throw new InvalidOperationException("گردش اولیه تاخیر تادیه چک برای این فاکتور قبلاً ثبت شده است.");

        var goods = sale.Lines.Sum(l => Math.Max(0m, (l.GrossAmount ?? OrderLineAmounts.Gross(l.Quantity, l.Price)) - l.DiscountAmount));
        var vat = sale.Lines.Sum(l => l.VatAmount);
        if (charge.BaseAmount != goods + vat || sale.Amount != charge.BaseAmount + charge.Amount || sale.Amount <= 0 ||
            !charge.CalculatorBaseAmount.HasValue || !charge.CalculatorRatePercent.HasValue || !charge.CalculatorMonths.HasValue)
            throw new InvalidOperationException("مبنای کالا، مالیات یا مبلغ فاکتور با تاخیر تادیه چک مطابقت ندارد.");
        decimal initial;
        try
        {
            initial = SaleChequeInitialCalculation.Calculate(charge.CalculatorBaseAmount.Value,
                charge.CalculatorRatePercent.Value, charge.CalculatorMonths.Value);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new InvalidOperationException("ورودی‌های ماشین‌حساب اولیه تاخیر تادیه چک معتبر نیست.");
        }
        if (charge.CalculatorBaseAmount.Value > charge.BaseAmount || initial != charge.InitialAmount)
            throw new InvalidOperationException("مبلغ اولیه با نتیجه ماشین‌حساب مطابقت ندارد.");

        var cash = sale.PaymentMethod == PaymentMethod.Cash ? sale.Amount : sale.CashAmount;
        if (cash < 0 || cash > sale.Amount || decimal.Round(cash, 2) != cash || sale.SettledAmount != cash ||
            sale.Cheques.Count == 0 || sale.Cheques.Any(c => c.IsCleared || c.Status != ChequeStatus.InHand ||
                c.Amount <= 0 || decimal.Round(c.Amount, 2) != c.Amount ||
                !c.OriginalDueDate.HasValue || c.OriginalDueDate.Value.Date != c.DueDate.Date) ||
            sale.Installments.Any(i => i.IsPaid || i.Amount <= 0 || decimal.Round(i.Amount, 2) != i.Amount))
            throw new InvalidOperationException("اجزای پرداخت، چک‌ها یا اقساط اولیه معتبر نیستند.");
        var parts = cash + sale.Cheques.Sum(c => c.Amount) + sale.Installments.Sum(i => i.Amount);
        if (parts > sale.Amount || (parts < sale.Amount && !sale.DueDate.HasValue))
            throw new InvalidOperationException("جمع اجزای پرداخت با مبلغ فاکتور مطابقت ندارد؛ اختلاف را تعیین‌تکلیف کنید.");

        var entry = new SaleChequeChargeEvent
        {
            CompanyId = charge.CompanyId, OperationId = Guid.NewGuid(), SaleChequeChargeId = charge.Id,
            Kind = SaleChequeChargeEventKind.Initial, Date = sale.Date.Date, IncomeDelta = charge.Amount
        };
        db.SaleChequeChargeEvents.Add(entry);
        await db.SaveChangesAsync();
        return entry;
    }
}
