namespace Inventory.Shared;

/// <summary>
/// محاسبهٔ اولیه همان ماشین‌حساب ماهانه موجود است؛ مبنای تعدیل سررسید جداگانه
/// از کل کالا و خدمات همراه با ارزش افزوده در ردیف مالی ثبت می‌شود.
/// </summary>
public static class SaleChequeInitialCalculation
{
    private const decimal MaxAmount = 9999999999999999.99m; // decimal(18,2)
    private const decimal MaxRate = 999999999999.999999m; // decimal(18,6)

    public static decimal Calculate(decimal calculatorBase, decimal monthlyRatePercent, int months)
    {
        if (calculatorBase <= 0 || calculatorBase > MaxAmount || decimal.Round(calculatorBase, 2) != calculatorBase)
            throw new ArgumentOutOfRangeException(nameof(calculatorBase));
        if (monthlyRatePercent < 0 || monthlyRatePercent > MaxRate || decimal.Round(monthlyRatePercent, 6) != monthlyRatePercent)
            throw new ArgumentOutOfRangeException(nameof(monthlyRatePercent));
        if (months <= 0 || months > 60) throw new ArgumentOutOfRangeException(nameof(months));
        try
        {
            // Preserve the existing calculator's operation order and midpoint rounding.
            var interest = Math.Round(calculatorBase * monthlyRatePercent / 100m * months, 0);
            if (interest > MaxAmount || calculatorBase + interest > MaxAmount)
                throw new ArgumentOutOfRangeException(nameof(calculatorBase), "مبلغ از محدودهٔ حسابداری بیشتر است.");
            return interest;
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(calculatorBase), "مبلغ از محدودهٔ حسابداری بیشتر است.");
        }
    }
}
