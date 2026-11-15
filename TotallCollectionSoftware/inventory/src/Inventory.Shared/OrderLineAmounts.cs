namespace Inventory.Shared;

/// <summary>Shared calculation contract for purchase/sale line discounts and VAT, in rials.</summary>
public static class OrderLineAmounts
{
    // Matches decimal(18,2) in SQL Server. Values above 100 are fixed rials.
    public const decimal MaxVatValue = 9999999999999999.99m;
    public static bool IsValidVatValue(decimal value) =>
        value >= 0 && value <= MaxVatValue && decimal.Round(value, 2) == value;

    public static decimal Gross(decimal quantity, decimal price) => Math.Round(quantity * price, 2);

    public static decimal Discount(decimal quantity, decimal price, decimal percent, decimal amount, bool isAmount)
    {
        var gross = Gross(quantity, price);
        if (isAmount) return Math.Clamp(Math.Round(amount, 0), 0, Math.Max(0, gross));
        return percent > 0 ? Math.Round(gross * Math.Clamp(percent, 0, 100) / 100m, 0) : 0;
    }

    public static decimal Vat(decimal afterDiscount, decimal value) => value <= 100m
        ? Math.Round(Math.Max(0, afterDiscount) * value / 100m, 2)
        : value;

    public static decimal Total(decimal quantity, decimal price, decimal percent, decimal amount, bool isAmount, decimal vatValue)
    {
        var afterDiscount = Math.Max(0, Gross(quantity, price) - Discount(quantity, price, percent, amount, isAmount));
        return afterDiscount + Vat(afterDiscount, vatValue);
    }
}
