using Inventory.Shared;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL: " + message);
}

static void ExpectInvalid(decimal total, int count)
{
    try
    {
        RoundedInstallmentDistribution.Split(total, count);
        throw new Exception($"FAIL: accepted {total} in {count} rows");
    }
    catch (ArgumentOutOfRangeException) { }
}

const decimal unit = RoundedInstallmentDistribution.RoundingUnit;
Check(unit == 10_000m, "rounding unit is 10,000 rials");
Check(RoundedInstallmentDistribution.Split(1_000_000m, 3)
    .SequenceEqual(new[] { 330_000m, 330_000m, 340_000m }), "whole-unit balancing");
Check(RoundedInstallmentDistribution.Split(1_000_001.23m, 3)
    .SequenceEqual(new[] { 330_000m, 340_000m, 330_001.23m }), "remainder in final row");
Check(RoundedInstallmentDistribution.Split(20_000.01m, 3)
    .SequenceEqual(new[] { 10_000m, 10_000m, 0.01m }), "smallest feasible positive remainder");
Check(RoundedInstallmentDistribution.Split(0.51m, 1).Single() == 0.51m, "single row stays exact");
ExpectInvalid(20_000m, 3); // Three strictly positive rows cannot be formed under the rule.
ExpectInvalid(19_999m, 3);
ExpectInvalid(0m, 1);
ExpectInvalid(100_000m, 0);
ExpectInvalid(100_000m, 61);
ExpectInvalid(100_000.001m, 2);
ExpectInvalid(10_000_000_000_000_000m, 2);

static void CheckInvariants(decimal total, int count)
{
    const decimal unit = RoundedInstallmentDistribution.RoundingUnit;
    var rows = RoundedInstallmentDistribution.Split(total, count);
    Check(rows.Length == count, "row count");
    Check(rows.All(value => value > 0m), "positive amounts");
    Check(rows.Sum() == total, "exact total");
    Check(rows.Take(count - 1).All(value => value % unit == 0m), "all earlier rows are rounded");
    Check(rows[^1] % unit == total % unit, "only the last row retains the exact remainder");
    Check(rows.Max() - rows.Min() <= unit, "whole-unit adjustments are spread across rows");
}

// Exhaust the small-value feasibility boundary for every supported row count.
// A remainder can serve as the final positive row; earlier rows need whole units.
var residuals = new[] { 0m, 0.01m, 1m, 9_999.99m };
for (var count = 1; count <= RoundedInstallmentDistribution.MaximumCount; count++)
{
    for (var wholeUnits = 0; wholeUnits <= count + 1; wholeUnits++)
    {
        foreach (var residual in residuals)
        {
            var total = wholeUnits * unit + residual;
            var possible = total > 0m && wholeUnits >= count - (residual > 0m ? 1 : 0);
            if (possible) CheckInvariants(total, count);
            else ExpectInvalid(total, count);
        }
    }
}

var random = new Random(29041);
for (var count = 1; count <= RoundedInstallmentDistribution.MaximumCount; count++)
{
    // Include exact multiples, non-multiples and fractional rials at each count.
    for (var i = 0; i < 300; i++)
    {
        var wholeRials = random.NextInt64((long)count * (long)unit, 10_000_000_000L);
        var cents = random.Next(0, 100);
        CheckInvariants(wholeRials + cents / 100m, count);
    }
    // Exercise the upper end of decimal(18,2), not just small invoice amounts.
    for (var i = 0; i < 50; i++)
    {
        var wholeRials = random.NextInt64(9_000_000_000_000_000L, 10_000_000_000_000_000L);
        CheckInvariants(wholeRials + random.Next(0, 100) / 100m, count);
    }
}
CheckInvariants(9_999_999_999_999_999.99m, 60);
CheckInvariants(600_000m, 60);
Console.WriteLine("PASS: 21,000 randomized schedules and exhaustive small-value boundaries preserve every rial");
