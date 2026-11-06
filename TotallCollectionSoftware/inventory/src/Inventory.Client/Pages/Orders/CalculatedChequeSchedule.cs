using Inventory.Shared;

namespace Inventory.Client.Pages.Orders;

/// <summary>
/// Form-session-only provenance for cheque rows built by the installment calculator.
/// A saved/loaded invoice has no reliable indication that its rows were generated,
/// so this is deliberately not reconstructed from historical amounts.
/// </summary>
internal sealed record CalculatedChequeSchedule(
    InvoiceInstallmentCalculation Calculation,
    decimal GoodsTotal,
    decimal CashAmount,
    decimal InstallmentAmount,
    decimal AdjustmentRatePercent,
    int AdjustmentPeriodDays)
{
    /// <summary>
    /// Allocate the change in goods to the original calculator base, preserving any
    /// previously agreed non-cheque/credit portion. Other payments and the generated
    /// rows must still match the snapshot; otherwise the seller must review them.
    /// </summary>
    public CalculatedChequeSchedule Recalculate(
        decimal newGoodsTotal, decimal cashAmount, decimal installmentAmount,
        int chequeCount, IReadOnlyList<decimal> currentAmounts)
    {
        if (cashAmount != CashAmount || installmentAmount != InstallmentAmount ||
            chequeCount != Calculation.Months || currentAmounts.Count != chequeCount)
            throw new InvalidOperationException("اجزای پرداخت یا تعداد چک‌ها تغییر کرده است.");

        var expected = Calculation.Amounts;
        for (var i = 0; i < expected.Length; i++)
            if (currentAmounts[i] != expected[i])
                throw new InvalidOperationException("مبلغ چک‌ها دستی تغییر کرده است.");

        var nextBase = Calculation.BaseAmount + (newGoodsTotal - GoodsTotal);
        var next = InvoiceInstallmentCalculation.Create(nextBase, Calculation.MonthlyRatePercent, Calculation.Months);
        return this with { Calculation = next, GoodsTotal = newGoodsTotal };
    }
}
