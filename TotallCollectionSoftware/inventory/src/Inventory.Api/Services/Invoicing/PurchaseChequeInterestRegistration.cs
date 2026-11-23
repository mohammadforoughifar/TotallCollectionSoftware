using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Invoicing;

/// <summary>ورودی‌های تأییدشدهٔ کاربر برای سود چک خرید؛ مبلغ سود از کلاینت پذیرفته نمی‌شود.</summary>
public sealed record PurchaseChequeInterestRegistrationInput(
    decimal CalculatorBaseAmount, decimal CalculatorRatePercent, int CalculatorMonths);

/// <summary>
/// ثبت ردیف «سود (هزینهٔ مالی) چک» برای فاکتور خرید.
/// سود جدا از مبلغ فاکتور ذخیره می‌شود؛ هیچ شماره/مبلغ/بانک/سررسید چکی تغییر نمی‌کند
/// و موجودی انبار و گردش خزانه دست‌نخورده می‌ماند.
/// </summary>
public static class PurchaseChequeInterestRegistration
{
    public static async Task<PurchaseChequeInterest> RegisterAsync(AppDbContext db, int purchaseId,
        PurchaseChequeInterestRegistrationInput input, string? user)
    {
        ArgumentNullException.ThrowIfNull(input);
        var purchase = await db.Transactions.Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.Id == purchaseId)
            ?? throw new InvalidOperationException("فاکتور خرید یافت نشد.");
        if (purchase.Type != TransactionType.Purchase)
            throw new InvalidOperationException("سود چک دستی فقط برای فاکتور خرید مجاز است.");
        if (await db.PurchaseChequeInterests.AnyAsync(x => x.TransactionId == purchase.Id))
            throw new InvalidOperationException("برای این فاکتور خرید سود چک از قبل ثبت شده است.");
        var goodsTotal = purchase.Lines.Sum(l => Math.Max(0m, (l.GrossAmount ?? OrderLineAmounts.Gross(l.Quantity, l.Price)) - l.DiscountAmount) + l.VatAmount);
        // مبلغ فاکتور باید همان مبلغ کالا بماند؛ سود چک هرگز داخل آن جمع نمی‌شود.
        if (goodsTotal <= 0 || purchase.Amount != goodsTotal)
            throw new InvalidOperationException("مبلغ فاکتور خرید با اقلام و ارزش افزوده مطابقت ندارد.");
        var interest = PurchaseChequeInterestMath.Validate(new PurchaseChequeInterestInput
        {
            CalculatorBaseAmount = input.CalculatorBaseAmount,
            CalculatorRatePercent = input.CalculatorRatePercent,
            CalculatorMonths = input.CalculatorMonths
        }, goodsTotal);
        var row = new PurchaseChequeInterest
        {
            CompanyId = purchase.CompanyId,
            TransactionId = purchase.Id,
            BaseAmount = goodsTotal,
            RatePercent = input.CalculatorRatePercent,
            Months = input.CalculatorMonths,
            CalculatorBaseAmount = input.CalculatorBaseAmount,
            CalculatorRatePercent = input.CalculatorRatePercent,
            CalculatorMonths = input.CalculatorMonths,
            Amount = interest,
            CreatedAt = DateTime.Now,
            CreatedBy = string.IsNullOrWhiteSpace(user) ? null : user.Trim()
        };
        db.PurchaseChequeInterests.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>حذف سود ثبت‌شدهٔ خرید؛ فقط ردیف سود حذف می‌شود و پرداخت‌ها دست‌نخورده می‌مانند.</summary>
    public static async Task ClearAsync(AppDbContext db, int purchaseId)
    {
        var row = await db.PurchaseChequeInterests.FirstOrDefaultAsync(x => x.TransactionId == purchaseId);
        if (row is null) return;
        db.PurchaseChequeInterests.Remove(row);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// هم‌سو کردن ردیف سود در ویرایش فاکتور خرید. ورودی null یعنی «همان مقدار ثبت‌شده
    /// حفظ شود»؛ حذف ردیف فقط با clear=true انجام می‌شود.
    /// سود از ورودی‌های ماشین‌حساب دوباره محاسبه می‌شود و به مبنا/نرخ/تعداد جدید
    /// به‌روز می‌شود؛ هیچ مبلغ یا سررسید چکی تغییر نمی‌کند.
    /// </summary>
    public static async Task SyncAsync(AppDbContext db, Transaction purchase, PurchaseChequeInterestInput? input,
        bool clear = false)
    {
        var row = await db.PurchaseChequeInterests.FirstOrDefaultAsync(x => x.TransactionId == purchase.Id);
        if (input is null)
        {
            // Silence means "keep what is stored"; removing the row must be explicit.
            if (clear && row is not null) db.PurchaseChequeInterests.Remove(row);
            return;
        }
        var goodsTotal = purchase.Lines.Sum(l => Math.Max(0m, (l.GrossAmount ?? OrderLineAmounts.Gross(l.Quantity, l.Price)) - l.DiscountAmount) + l.VatAmount);
        if (purchase.Amount != goodsTotal)
            throw new InvalidOperationException("مبلغ فاکتور خرید با اقلام و ارزش افزوده مطابقت ندارد.");
        var interest = PurchaseChequeInterestMath.Validate(input, goodsTotal);
        if (row is null)
        {
            row = new PurchaseChequeInterest { CompanyId = purchase.CompanyId, TransactionId = purchase.Id };
            db.PurchaseChequeInterests.Add(row);
        }
        row.BaseAmount = goodsTotal;
        row.RatePercent = input.CalculatorRatePercent;
        row.Months = input.CalculatorMonths;
        row.CalculatorBaseAmount = input.CalculatorBaseAmount;
        row.CalculatorRatePercent = input.CalculatorRatePercent;
        row.CalculatorMonths = input.CalculatorMonths;
        row.Amount = interest;
    }
}
