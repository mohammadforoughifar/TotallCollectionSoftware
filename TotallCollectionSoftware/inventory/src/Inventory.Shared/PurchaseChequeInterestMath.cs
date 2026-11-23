using Inventory.Shared.Dtos;

namespace Inventory.Shared;

/// <summary>
/// قواعد مشترک «سود (هزینهٔ مالی) چک در فاکتور خرید».
/// <para>
/// سود چک خرید درآمد نیست و مبلغ فاکتور را بالا نمی‌برد؛ فقط یک هزینهٔ مالی است
/// که باید جدا از «مبلغ کالا» در گزارش خرید دیده شود. به همین دلیل مبلغ آن در
/// <c>Transactions.Amount</c> جمع نمی‌شود و تعهد کل پرداخت به تأمین‌کننده برابر
/// «مبلغ کالا + سود» است.
/// </para>
/// </summary>
public static class PurchaseChequeInterestMath
{
    /// <summary>تعهد کل پرداخت به تأمین‌کننده: مبلغ کالا و ارزش افزوده + سود چک.</summary>
    public static decimal PayableTotal(decimal goodsAmount, decimal interest)
    {
        if (goodsAmount < 0) throw new ArgumentOutOfRangeException(nameof(goodsAmount));
        if (interest < 0) throw new ArgumentOutOfRangeException(nameof(interest));
        return goodsAmount + interest;
    }

    /// <summary>سود از همان ورودی‌های تأییدشدهٔ ماشین‌حساب محاسبه می‌شود، نه از مبلغ کلاینت.</summary>
    public static decimal Calculate(PurchaseChequeInterestInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return SaleChequeInitialCalculation.Calculate(input.CalculatorBaseAmount,
            input.CalculatorRatePercent, input.CalculatorMonths);
    }

    /// <summary>
    /// اعتبارسنجی سود درخواستی برای یک فاکتور خرید. مبنا نباید از مبلغ کالا بیشتر
    /// باشد و سود باید مثبت باشد؛ در غیر این صورت ردیفی ثبت نمی‌شود.
    /// </summary>
    public static decimal Validate(PurchaseChequeInterestInput? input, decimal goodsAmount)
    {
        if (input is null) return 0m;
        if (goodsAmount <= 0)
            throw new InvalidOperationException("ثبت سود چک برای فاکتور بدون کالا مجاز نیست.");
        decimal interest;
        try
        {
            interest = Calculate(input);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new InvalidOperationException("ورودی‌های ماشین‌حساب سود چک خرید معتبر نیست.");
        }
        if (input.CalculatorBaseAmount > goodsAmount)
            throw new InvalidOperationException("مبنای سود چک بیشتر از مبلغ کالا و ارزش افزوده است.");
        if (interest <= 0)
            throw new InvalidOperationException("سود چک خرید باید بیشتر از صفر باشد.");
        return interest;
    }

    /// <summary>
    /// اجزای پرداخت (نقد/چک/اقساط) نباید از تعهد کل بیشتر باشند؛ بیش از آن دیگر
    /// سود نیست، مازاد پرداخت واقعی است.
    /// </summary>
    public static void EnsurePaymentsWithinPayable(decimal goodsAmount, decimal interest, decimal payments)
    {
        var payable = PayableTotal(goodsAmount, interest);
        if (payments > payable + PaymentMix.Tolerance)
            throw new InvalidOperationException(
                $"جمع پرداخت‌ها از مبلغ کالا و سود چک ({payable:#,0}) بیشتر است؛ مبلغ چک‌ها را اصلاح کنید.");
    }
}
