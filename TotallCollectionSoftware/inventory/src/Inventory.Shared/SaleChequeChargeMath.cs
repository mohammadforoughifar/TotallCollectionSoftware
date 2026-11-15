using System.Numerics;

namespace Inventory.Shared;

/// <summary>محاسبات مشترک ردیف مالی چک؛ کلیه مبالغ به ریال هستند.</summary>
public static class SaleChequeChargeMath
{
    /// <summary>مبلغ کالایی از جمع فاکتور؛ ردیف مالی چک جزو فروش کالا نیست.</summary>
    public static decimal GoodsTotal(decimal invoiceTotal, decimal chequeIncome)
    {
        if (invoiceTotal < 0 || chequeIncome < 0 || chequeIncome > invoiceTotal)
            throw new InvalidOperationException("مبلغ تاخیر تادیه چک با مبلغ فاکتور مطابقت ندارد.");
        return invoiceTotal - chequeIncome;
    }

    /// <summary>
    /// اختلاف سود پس از تغییر سررسید. مبنا، مبلغ کل فاکتور شامل ارزش افزوده است؛
    /// نرخ برای بازهٔ دلخواه تعیین می‌شود. نتیجه در انتهای محاسبه به ریال گرد می‌شود.
    /// </summary>
    public static decimal DateAdjustment(decimal invoiceBase, decimal ratePercent, int periodDays,
        DateTime previousDueDate, DateTime newDueDate)
    {
        if (invoiceBase < 0) throw new ArgumentOutOfRangeException(nameof(invoiceBase));
        if (ratePercent < 0) throw new ArgumentOutOfRangeException(nameof(ratePercent));
        if (periodDays <= 0) throw new ArgumentOutOfRangeException(nameof(periodDays));
        var days = (newDueDate.Date - previousDueDate.Date).Days;
        // Matches the existing calculator's rounding to whole rials (MidpointRounding.ToEven).
        return decimal.Round(invoiceBase * ratePercent / 100m * days / periodDays, 0, MidpointRounding.ToEven);
    }

    /// <summary>
    /// تغییر مبلغ هنگام جابه‌جایی مجدد یک سررسید، نسبت به سررسید اولیهٔ همان چک.
    /// دو مقدار تجمعی جداگانه گرد می‌شوند؛ بنابراین چند تغییر پشت سر هم با یک تغییر
    /// مستقیم به همان تاریخ نتیجهٔ یکسان می‌دهد. مبنا، نرخ و بازه باید همان snapshot اولیه باشند.
    /// </summary>
    public static decimal IncrementalDateAdjustment(decimal invoiceBase, decimal ratePercent, int periodDays,
        DateTime originalDueDate, DateTime previousDueDate, DateTime newDueDate)
    {
        var before = DateAdjustment(invoiceBase, ratePercent, periodDays, originalDueDate, previousDueDate);
        var after = DateAdjustment(invoiceBase, ratePercent, periodDays, originalDueDate, newDueDate);
        return after - before;
    }

    /// <summary>
    /// سهم سود برگشتی برای این نوبت، بر اساس پایهٔ اولیهٔ کالا با VAT و سود snapshot.
    /// تفاضل جمع تجمعی گرد‌شده از جمع قبلی، مانع اختلاف در برگشت‌های متعدد می‌شود.
    /// </summary>
    public static decimal ProportionalReturn(decimal invoiceBase, decimal income,
        decimal previouslyReturnedBase, decimal currentReturnedBase)
    {
        if (invoiceBase <= 0) throw new ArgumentOutOfRangeException(nameof(invoiceBase));
        if (income < 0 || decimal.Round(income, 0) != income)
            throw new ArgumentOutOfRangeException(nameof(income));
        if (previouslyReturnedBase < 0 || previouslyReturnedBase > invoiceBase)
            throw new ArgumentOutOfRangeException(nameof(previouslyReturnedBase));
        if (currentReturnedBase < 0 || currentReturnedBase > invoiceBase - previouslyReturnedBase)
            throw new ArgumentOutOfRangeException(nameof(currentReturnedBase));

        decimal Cumulative(decimal amount) => amount == invoiceBase
            ? income
            : RoundWholeRialRatio(income, amount, invoiceBase);
        return Cumulative(previouslyReturnedBase + currentReturnedBase) - Cumulative(previouslyReturnedBase);
    }

    /// <summary>
    /// سهم برگشتی از سود جاری برای کالایی که هنوز برگشت نشده است. مبنا شامل ارزش افزودهٔ
    /// کالا است. وقتی بین دو برگشت سررسید و سود عوض شده باشد نباید سود جدید را با سهم
    /// برگشت‌های قبلی از مبنای اولیه ترکیب کرد؛ هر دو ورودی، ماندهٔ پیش از همین برگشت‌اند.
    /// این محاسبه به‌تنهایی وصول چک، پرداخت به مشتری یا تهاتر با فروش دیگری را ثبت نمی‌کند.
    /// </summary>
    public static decimal ProportionalRemainingReturn(decimal remainingBase, decimal remainingIncome, decimal returnedBase)
    {
        if (remainingBase <= 0) throw new ArgumentOutOfRangeException(nameof(remainingBase));
        if (remainingIncome < 0 || decimal.Round(remainingIncome, 0) != remainingIncome)
            throw new ArgumentOutOfRangeException(nameof(remainingIncome));
        if (returnedBase < 0 || returnedBase > remainingBase)
            throw new ArgumentOutOfRangeException(nameof(returnedBase));
        if (returnedBase == remainingBase) return remainingIncome;
        return RoundWholeRialRatio(remainingIncome, returnedBase, remainingBase);
    }

    // Exact rational arithmetic avoids decimal overflow and premature rounding at
    // the half-rial boundary, including for large decimal(18,2) invoice values.
    private static decimal RoundWholeRialRatio(decimal wholeIncome, decimal part, decimal total)
    {
        static (BigInteger Value, int Scale) Parts(decimal value)
        {
            var bits = decimal.GetBits(value);
            var integer = ((BigInteger)(uint)bits[2] << 64) |
                          ((BigInteger)(uint)bits[1] << 32) | (uint)bits[0];
            return (integer, (bits[3] >> 16) & 0xff);
        }
        var (partValue, partScale) = Parts(part);
        var (totalValue, totalScale) = Parts(total);
        var numerator = (BigInteger)wholeIncome * partValue * BigInteger.Pow(10, totalScale);
        var denominator = totalValue * BigInteger.Pow(10, partScale);
        var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
        var comparison = (remainder * 2).CompareTo(denominator);
        if (comparison > 0 || (comparison == 0 && !quotient.IsEven)) quotient++;
        return (decimal)quotient;
    }

    /// <summary>
    /// سود خالص مثبت، ردیف درآمد است؛ مازاد کاهش زیر صفر به نفع مشتری است.
    /// accumulatedAdjustment باید مجموع تغییرات تأییدشده نسبت به سود اولیه باشد.
    /// </summary>
    public static (decimal Income, decimal CustomerCredit) Split(decimal initialAmount, decimal accumulatedAdjustment)
    {
        if (initialAmount < 0) throw new ArgumentOutOfRangeException(nameof(initialAmount));
        var net = initialAmount + accumulatedAdjustment;
        return net >= 0 ? (net, 0m) : (0m, -net);
    }
}
