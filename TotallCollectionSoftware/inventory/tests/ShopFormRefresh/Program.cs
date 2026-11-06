using Inventory.Client.Pages.Orders;
using Inventory.Shared;

internal static class ShopFormRefreshTests
{
    private static int checks;

    private static void Check(bool condition, string description)
    {
        checks++;
        if (!condition) throw new Exception("FAIL: " + description);
    }

    private static void Throws<T>(Action action, string description) where T : Exception
    {
        checks++;
        try { action(); }
        catch (T) { return; }
        throw new Exception("FAIL: " + description);
    }

    private static decimal Sum(decimal[] values)
    {
        decimal sum = 0;
        foreach (var value in values) sum += value;
        return sum;
    }

    private static bool Same(decimal[] a, decimal[] b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static void CheckRows(InvoiceInstallmentCalculation calculation)
    {
        var amounts = calculation.Amounts;
        Check(amounts.Length == calculation.Months, "number of cheques remains unchanged");
        Check(Sum(amounts) == calculation.Total, "cheque sum equals principal plus interest exactly");
        var min = amounts[0]; var max = amounts[0];
        for (var i = 0; i < amounts.Length; i++)
        {
            Check(amounts[i] > 0, "each cheque amount remains positive");
            if (i < amounts.Length - 1)
                Check(amounts[i] % RoundedInstallmentDistribution.RoundingUnit == 0, "non-final cheque is rounded");
            if (amounts[i] < min) min = amounts[i];
            if (amounts[i] > max) max = amounts[i];
        }
        Check(amounts[amounts.Length - 1] % RoundedInstallmentDistribution.RoundingUnit ==
              calculation.Total % RoundedInstallmentDistribution.RoundingUnit, "exact residual is on last cheque");
        Check(max - min <= RoundedInstallmentDistribution.RoundingUnit, "rounding is spread evenly");
    }

    private static void Price(string input, string expected, decimal value)
    {
        var formatted = PurchaseSalePriceInput.Format(input);
        Check(formatted == expected, "format " + input + " as " + expected + ", got " + formatted);
        Check(PurchaseSalePriceInput.TryParse(formatted, out var parsed) && parsed == value,
            "parse formatted price " + input);
    }

    private static void InvalidPrice(string input)
    {
        var formatted = PurchaseSalePriceInput.Format(input);
        Check(!string.IsNullOrWhiteSpace(formatted), "invalid text must not become an optional blank: " + input);
        Check(!PurchaseSalePriceInput.TryParse(formatted, out _), "reject invalid price " + input);
    }

    private static void Quantity(string input, string expected, decimal value)
    {
        var output = ShopQuantityInput.Format(input);
        Check(output == expected && ShopQuantityInput.TryParse(output, out var parsed) && parsed == value,
            "quantity " + input + " -> " + output);
    }

    private static void InvalidQuantity(string input)
    {
        var output = ShopQuantityInput.Format(input);
        Check(!ShopQuantityInput.TryParse(output, out _), "invalid quantity " + input + " -> " + output);
    }

    private static void Main()
    {
        var original = InvoiceInstallmentCalculation.Create(2_000_000m, 5m, 5);
        var schedule = new CalculatedChequeSchedule(original, 2_000_000m, 0m, 0m, 5m, 30);
        var rows = original.Amounts;
        var larger = schedule.Recalculate(2_500_000m, 0m, 0m, 5, rows);
        Check(larger.Calculation.BaseAmount == 2_500_000m && larger.Calculation.Interest == 625_000m &&
              larger.Calculation.Total == 3_125_000m, "goods increase recomputes interest on principal, not old interest");
        Check(larger.Calculation.MonthlyRatePercent == 5m && larger.Calculation.Months == 5, "rate and duration preserved");
        CheckRows(larger.Calculation);
        Check(Same(rows, original.Amounts), "recalculation does not mutate original payment amounts");
        var back = larger.Recalculate(2_000_000m, 0m, 0m, 5, larger.Calculation.Amounts);
        Check(back.Calculation == original && Same(back.Calculation.Amounts, original.Amounts),
            "repeated increase/decrease is reversible and does not compound interest");

        // Keep manually agreed cash, installments and existing credit unchanged;
        // the goods-total delta is absorbed only by the old calculator principal.
        var mixedCalc = InvoiceInstallmentCalculation.Create(1_000_000m, 5m, 2);
        var mixed = new CalculatedChequeSchedule(mixedCalc, 2_000_000m, 500_000m, 200_000m, 2m, 45);
        var mixedNext = mixed.Recalculate(2_400_000m, 500_000m, 200_000m, 2, mixedCalc.Amounts);
        var mixedCharge = mixedNext.Calculation.CreateSaleChequeCharge(2_400_000m, mixedNext.AdjustmentRatePercent, mixedNext.AdjustmentPeriodDays);
        Check(mixedNext.Calculation.BaseAmount == 1_400_000m && mixedNext.Calculation.Interest == 140_000m,
            "mixed-payment base follows only the goods delta");
        Check(mixedNext.Calculation.Total + mixedNext.CashAmount + mixedNext.InstallmentAmount + 300_000m ==
              mixedNext.GoodsTotal + mixedNext.Calculation.Interest, "cash, installment and existing credit portions are preserved");
        Check(mixedCharge is not null && mixedCharge.CalculatorBaseAmount == 1_400_000m &&
              mixedCharge.CalculatorRatePercent == 5m && mixedCharge.CalculatorMonths == 2 &&
              mixedCharge.AdjustmentRatePercent == 2m && mixedCharge.AdjustmentPeriodDays == 45,
            "sale interest article and separate due-date terms match the refreshed cheques");
        CheckRows(mixedNext.Calculation);

        var zeroCalc = InvoiceInstallmentCalculation.Create(1_000_001.23m, 0m, 3);
        var zero = new CalculatedChequeSchedule(zeroCalc, 1_000_001.23m, 0m, 0m, 0m, 30);
        var zeroNext = zero.Recalculate(1_000_101.23m, 0m, 0m, 3, zeroCalc.Amounts);
        Check(zeroNext.Calculation.Interest == 0m &&
              zeroNext.Calculation.CreateSaleChequeCharge(1_000_101.23m, 0m, 30) is null,
            "zero interest does not invent a sale charge");
        CheckRows(zeroNext.Calculation);

        var tinyInterest = InvoiceInstallmentCalculation.Create(300_000m, 0.0002m, 1);
        var tinySchedule = new CalculatedChequeSchedule(tinyInterest, 300_000m, 0m, 0m, 0.0002m, 30);
        var tinyNext = tinySchedule.Recalculate(200_000m, 0m, 0m, 1, tinyInterest.Amounts);
        Check(tinyInterest.Interest == 1m && tinyNext.Calculation.Interest == 0m &&
              tinyNext.Calculation.CreateSaleChequeCharge(200_000m, 0.0002m, 30) is null,
            "a small previously nonzero interest can legitimately round to zero after a decrease");
        var tinyAgain = tinyNext.Recalculate(300_000m, 0m, 0m, 1, tinyNext.Calculation.Amounts);
        Check(tinyAgain.Calculation.Interest == 1m &&
              tinyAgain.Calculation.CreateSaleChequeCharge(300_000m, 0.0002m, 30) is not null,
            "the same rate may create nonzero interest again when goods increase");

        var edited = original.Amounts;
        edited[0] += 10_000m; edited[1] -= 10_000m;
        Throws<InvalidOperationException>(() => schedule.Recalculate(2_500_000m, 0m, 0m, 5, edited),
            "manual changes are not overwritten even when total is unchanged");
        Throws<InvalidOperationException>(() => schedule.Recalculate(2_500_000m, 1m, 0m, 5, original.Amounts),
            "changed cash invalidates the snapshot");
        Throws<InvalidOperationException>(() => schedule.Recalculate(2_500_000m, 0m, 1m, 5, original.Amounts),
            "changed installments invalidate the snapshot");
        Throws<InvalidOperationException>(() => schedule.Recalculate(2_500_000m, 0m, 0m, 4, original.Amounts),
            "changed cheque count invalidates the snapshot");
        Throws<InvalidOperationException>(() => schedule.Recalculate(2_500_000m, 0m, 0m, 5, new decimal[] { 1m }),
            "partial or missing rows cannot be treated as generated");
        Throws<ArgumentOutOfRangeException>(() => schedule.Recalculate(0m, 0m, 0m, 5, original.Amounts),
            "invalid or too-small principal cannot produce zero rows");
        Throws<ArgumentOutOfRangeException>(() => schedule.Recalculate(9_999_999_999_999_999.99m, 0m, 0m, 5, original.Amounts),
            "overflow or excessive principal is rejected");
        Check(schedule.GoodsTotal == 2_000_000m && schedule.Calculation == original,
            "a failed refresh never mutates the previous schedule");

        // Many counts, totals and rates exercise the exact same production splitter.
        for (var count = 1; count <= 60; count++)
        {
            for (var i = 0; i < 15; i++)
            {
                var baseAmount = count * 50_000m + i * 23_457m + 0.23m;
                var plan = InvoiceInstallmentCalculation.Create(baseAmount, i % 2 == 0 ? 0m : 2.75m, count);
                var test = new CalculatedChequeSchedule(plan, baseAmount + 400_000m, 100_000m, 100_000m, 2.75m, 30);
                var result = test.Recalculate(test.GoodsTotal + 123_456.54m, 100_000m, 100_000m, count, plan.Amounts);
                Check(result.Calculation.BaseAmount == baseAmount + 123_456.54m, "goods delta applied to original principal");
                Check(result.Calculation.Interest == SaleChequeInitialCalculation.Calculate(
                    result.Calculation.BaseAmount, plan.MonthlyRatePercent, count), "interest recomputed with original terms");
                CheckRows(result.Calculation);
            }
        }

        Price("1234567", "1,234,567", 1234567m);
        Price("۱۲۳۴۵۶۷", "1,234,567", 1234567m);
        Price("١٬٢٣٤٬٥٦٧", "1,234,567", 1234567m);
        Price("001234", "1,234", 1234m);
        Price("1,0001", "10,001", 10001m); // typing after the live thousands separator
        Price("9999999999999999", "9,999,999,999,999,999", 9999999999999999m);
        var legacy = PurchaseSalePriceInput.Format("۱۲۳۴۵۶۷٫۸۹");
        Check(legacy == "1,234,567.89" && PurchaseSalePriceInput.TryParseLegacy(legacy, out var legacyPrice) && legacyPrice == 1234567.89m,
            "an unchanged historical fractional price is preserved exactly");
        Check(!PurchaseSalePriceInput.TryParse(legacy, out _), "historical fractional price cannot be registered as a new price");
        Check(PurchaseSalePriceInput.Format(" ") == "" && !PurchaseSalePriceInput.TryParse("", out _),
            "blank remains optional and is not parsed as zero");
        InvalidPrice(","); InvalidPrice("٬"); InvalidPrice("-1,234"); InvalidPrice("+1");
        InvalidPrice("1a234"); InvalidPrice("1.2.3"); InvalidPrice(".");
        InvalidPrice("12.345"); InvalidPrice("1.00"); InvalidPrice(".5"); InvalidPrice("1234567.");
        InvalidPrice("100,50"); InvalidPrice("10000000000000000"); InvalidPrice("1e3");
        Quantity("1,5", "1.5", 1.5m);
        Quantity("1.5", "1.5", 1.5m);
        Quantity("۱٫۵", "1.5", 1.5m);
        Quantity("1,234.50", "1,234.50", 1234.5m);
        Quantity("1.234,50", "1,234.50", 1234.5m);
        Quantity("1,000,50", "1,000.50", 1000.5m);
        Quantity("1,0001", "10,001", 10001m);
        Quantity(".5", "0.5", .5m);
        InvalidQuantity("1,,2"); InvalidQuantity("1,23,456"); InvalidQuantity("1.2.3");
        InvalidQuantity("-1"); InvalidQuantity("1x2"); InvalidQuantity("1.2,3"); InvalidQuantity("1.");

        Console.WriteLine("PASS: " + checks + " cheque refresh, payment, rounding and price input checks");
    }
}
