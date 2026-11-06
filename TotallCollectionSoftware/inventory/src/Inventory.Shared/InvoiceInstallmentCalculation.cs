using Inventory.Shared.Dtos;

namespace Inventory.Shared;

/// <summary>
/// نتیجهٔ همان ماشین‌حساب ماهانهٔ پرداخت؛ سود و ردیف‌های پرداخت از یک محاسبه ساخته می‌شوند.
/// تمام مبالغ ریالی‌اند و سود مطابق محاسبهٔ مشترک سرور به ریال کامل گرد می‌شود.
/// </summary>
public readonly record struct InvoiceInstallmentCalculation(
    decimal BaseAmount, decimal MonthlyRatePercent, int Months,
    decimal Interest, decimal Total, decimal RegularAmount, decimal LastAmount)
{
    public static InvoiceInstallmentCalculation Create(decimal baseAmount, decimal monthlyRatePercent, int months)
    {
        var interest = SaleChequeInitialCalculation.Calculate(baseAmount, monthlyRatePercent, months);
        var total = baseAmount + interest;

        // The payment-method classifier ignores components at or below half its
        // tolerance. Do not create a cheque component it would mistake for cash.
        if (total <= PaymentMix.Tolerance / 2m)
            throw new ArgumentOutOfRangeException(nameof(baseAmount), "مبلغ چک برای ثبت روش پرداخت کافی نیست.");
        // Preserve the existing whole-rial split; retain cents in the last payment.
        // Reject zero-value rows rather than silently creating an invalid schedule.
        var regular = months == 1 ? total : decimal.Floor(total / months);
        var last = total - regular * (months - 1);
        if (regular <= 0 || last <= 0)
            throw new ArgumentOutOfRangeException(nameof(months), "مبلغ برای تعداد ردیف‌های انتخاب‌شده کافی نیست.");
        return new InvoiceInstallmentCalculation(baseAmount, monthlyRatePercent, months, interest, total, regular, last);
    }

    /// <summary>
    /// ورودی ردیف مالی فروش از همین نتیجه ساخته می‌شود؛ سرور سود را دوباره از ورودی‌ها
    /// کنترل می‌کند. نرخ صفر هیچ گردش درآمدی تازه‌ای تولید نمی‌کند.
    /// </summary>
    public SaleChequeChargeInput? CreateSaleChequeCharge(decimal goodsTotal, decimal adjustmentRatePercent, int adjustmentPeriodDays)
    {
        if (BaseAmount > goodsTotal)
            throw new ArgumentOutOfRangeException(nameof(goodsTotal), "مبنای محاسبه بیشتر از مبلغ کالا و ارزش افزوده است.");
        if (adjustmentRatePercent < 0 || adjustmentRatePercent > 999999999999.999999m ||
            decimal.Round(adjustmentRatePercent, 6) != adjustmentRatePercent || adjustmentPeriodDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(adjustmentRatePercent), "نرخ یا بازه تعدیل سررسید معتبر نیست.");
        if (Interest == 0) return null;
        // Check the full goods + income amount, not only the portion split into cheques.
        _ = SaleChequeVoucherAmounts.Create(goodsTotal, 0m, Interest);
        return new SaleChequeChargeInput
        {
            CalculatorBaseAmount = BaseAmount,
            CalculatorRatePercent = MonthlyRatePercent,
            CalculatorMonths = Months,
            AdjustmentRatePercent = adjustmentRatePercent,
            AdjustmentPeriodDays = adjustmentPeriodDays
        };
    }
}
