namespace Inventory.Shared;

/// <summary>Whole-rial prices and line gross for newly recorded shop invoices.</summary>
public static class ShopInvoiceAmounts
{
    // SQL Server's decimal(18,2) can hold at most 16 integral digits.
    public const decimal MaximumPrice = 9999999999999999m;
    // SQL Server stores transaction quantities in decimal(18,3).
    public const decimal MaximumQuantity = 999999999999999.999m;

    public static bool IsWholePrice(decimal value) =>
        value >= 0m && value <= MaximumPrice && value == decimal.Truncate(value);

    // A half rial goes away from zero, rather than banker's rounding.
    public static decimal Gross(decimal quantity, decimal price) =>
        decimal.Round(quantity * price, 0, MidpointRounding.AwayFromZero);

    public static bool TryGross(decimal quantity, decimal price, out decimal gross)
    {
        try
        {
            gross = Gross(quantity, price);
            return gross >= 0m && gross <= OrderLineAmounts.MaxVatValue;
        }
        catch (OverflowException) { gross = 0m; return false; }
    }

    public static decimal Discount(decimal gross, decimal percent, decimal amount, bool isAmount)
    {
        if (isAmount) return Math.Clamp(decimal.Round(amount, 0), 0, Math.Max(0, gross));
        return percent > 0 ? decimal.Round(gross * Math.Clamp(percent, 0, 100) / 100m, 0) : 0m;
    }

    public static decimal Total(decimal quantity, decimal price, decimal percent, decimal amount, bool isAmount, decimal vatValue)
    {
        var gross = Gross(quantity, price);
        var afterDiscount = Math.Max(0m, gross - Discount(gross, percent, amount, isAmount));
        return afterDiscount + OrderLineAmounts.Vat(afterDiscount, vatValue);
    }
}
