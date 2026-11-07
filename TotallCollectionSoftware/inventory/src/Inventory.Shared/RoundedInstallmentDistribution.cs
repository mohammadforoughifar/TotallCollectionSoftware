namespace Inventory.Shared;

/// <summary>
/// Splits a payment amount into positive rows rounded to 10,000 rials, without
/// changing the total. Only the final row may contain a sub-unit remainder.
/// </summary>
public static class RoundedInstallmentDistribution
{
    public const decimal RoundingUnit = 10_000m;
    public const int MaximumCount = 60;
    private const decimal MaxAmount = 9999999999999999.99m; // decimal(18,2)

    public static decimal[] Split(decimal total, int count)
    {
        if (total <= 0m || total > MaxAmount || decimal.Round(total, 2) != total)
            throw new ArgumentOutOfRangeException(nameof(total), "مبلغ باید مثبت و حداکثر دارای دو رقم اعشار باشد.");
        if (count < 1 || count > MaximumCount)
            throw new ArgumentOutOfRangeException(nameof(count), "تعداد ردیف‌ها باید بین ۱ و ۶۰ باشد.");

        var wholeUnits = decimal.Floor(total / RoundingUnit);
        var baseUnits = decimal.Floor(wholeUnits / count);
        var extraUnits = (int)(wholeUnits % count);
        var remainder = total - wholeUnits * RoundingUnit;
        var amounts = new decimal[count];
        for (var i = 0; i < count; i++) amounts[i] = baseUnits * RoundingUnit;

        // Spread whole units across rows instead of accumulating them all in the
        // last row. If a sub-unit remainder exists, keep the last row below the
        // other raised rows so the difference between payments is at most one unit.
        var upperEnd = remainder > 0m ? count - 1 : count;
        for (var i = upperEnd - extraUnits; i < upperEnd; i++) amounts[i] += RoundingUnit;
        amounts[count - 1] += remainder;

        // A positive rounded row is impossible for very small totals and/or too
        // many rows. Never silently create zero payments or discard the remainder.
        if (amounts.Any(amount => amount <= 0m))
            throw new ArgumentOutOfRangeException(nameof(count), "مبلغ برای ساخت این تعداد ردیف مثبت با رند ۱۰٬۰۰۰ ریالی کافی نیست.");

        return amounts;
    }
}
