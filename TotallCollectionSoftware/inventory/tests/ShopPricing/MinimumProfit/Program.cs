using Inventory.Shared;

static void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL: " + message); }
Check(ShopMinimumProfit.Meets(120, 1, 100, 20, 0, 0), "markup on cost: 100 + 20% = 120");
Check(!ShopMinimumProfit.Meets(119.99m, 1, 100, 20, 0, 0), "below markup rejected");
Check(ShopMinimumProfit.Meets(122.22m, 1, 100, 20, 10, 100), "commission deducted after gross profit");
Check(!ShopMinimumProfit.Meets(122.21m, 1, 100, 20, 10, 100), "commission cannot eat into minimum");
Check(ShopMinimumProfit.Meets(124.44m, 1, 100, 20, 10, 80), "actual commission cost differs from last-purchase basis");
Check(!ShopMinimumProfit.Meets(124.43m, 1, 100, 20, 10, 80), "actual commission cost cannot be ignored");
Check(ShopMinimumProfit.Meets(300m, 2.5m, 100, 20, 0, 0), "fractional quantity");
Check(ShopMinimumProfit.Meets(240m, 3m, 200m / 3m, 20, 0, 0), "recurring purchase unit cost does not add a phantom cent");
Check(!ShopMinimumProfit.Meets(239.99m, 3m, 200m / 3m, 20, 0, 0), "recurring purchase cost cannot fall below the floor");
Check(!ShopMinimumProfit.Meets(120m, 1.01m, 54.11m / 1.01m, 0, 55, .19m), "commission matches rounded report profit");
Check(!ShopMinimumProfit.Meets(500, 1, 0, 20, 0, 0), "missing or zero cost is not a valid basis");
Check(!ShopMinimumProfit.Meets(120, 0, 100, 20, 0, 0), "zero quantity rejected");
Check(!ShopMinimumProfit.Meets(200, 1, 100, 20, 101, 100), "invalid commission rejected");
Check(!ShopMinimumProfit.Meets(200, 1, 100, 20, 100, 100), "100 percent commission cannot meet positive markup");
var random = new Random(29041);
for (var i = 0; i < 10000; i++)
{
    var qty = random.Next(1, 1000) / 100m;
    var purchase = random.Next(1, 100000) / 100m;
    var actual = random.Next(0, 100000) / 100m;
    var revenue = random.Next(0, 10000000) / 100m;
    var markup = random.Next(0, 501) / 10m;
    var rate = random.Next(0, 1001) / 10m;
    var commission = decimal.Round(Math.Max(0, decimal.Round(revenue - qty * actual, 2)) * rate / 100m, 2);
    var basis = decimal.Round(qty * purchase, 6);
    var target = decimal.Ceiling((basis + basis * markup / 100m) * 100m) / 100m;
    var expected = revenue - commission >= target;
    Check(ShopMinimumProfit.Meets(revenue, qty, purchase, markup, rate, actual) == expected, "random exact decimal contract");
}
Console.WriteLine("PASS: minimum-profit boundaries and 10,000 randomized cases");
