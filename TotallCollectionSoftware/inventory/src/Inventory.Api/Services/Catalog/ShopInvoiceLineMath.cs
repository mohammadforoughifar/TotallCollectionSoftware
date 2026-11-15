using Inventory.Shared;
using Inventory.Shared.Dtos;
using Db = Inventory.Api.Data;

namespace Inventory.Api.Services;

/// <summary>One invoice-line calculation shared by payment validation and persisted snapshots.</summary>
internal static class ShopInvoiceLineMath
{
    internal sealed record Amounts(decimal Gross, decimal DiscountPercent, decimal DiscountAmount,
        bool DiscountIsAmount, decimal VatValue, decimal VatAmount)
    {
        public decimal Total => Math.Max(0m, Gross - DiscountAmount) + VatAmount;
    }

    internal static bool SameBasis(OrderLineInput input, Db.TransactionLine? original) =>
        original is not null && original.ProductId == input.ProductId &&
        original.Quantity == input.Quantity && original.Price == input.Price;

    internal static Amounts Calculate(OrderLineInput input, bool wholeRials, Db.TransactionLine? original = null)
    {
        try
        {
            var amounts = CalculateCore(input, wholeRials, original);
            if (wholeRials && (amounts.Gross > OrderLineAmounts.MaxVatValue ||
                amounts.VatAmount > OrderLineAmounts.MaxVatValue || amounts.Total > OrderLineAmounts.MaxVatValue))
                throw new InvalidOperationException("مبلغ سطر یا ارزش افزوده خارج از محدوده قابل ثبت است.");
            return amounts;
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException("مبلغ یا مقدار سطر خارج از محدوده قابل ثبت است.");
        }
    }

    private static Amounts CalculateCore(OrderLineInput input, bool wholeRials, Db.TransactionLine? original)
    {
        var sameBasis = SameBasis(input, original);
        // A historical row keeps its saved gross even when the new policy differs.
        var gross = sameBasis ? original!.GrossAmount ?? OrderLineAmounts.Gross(original.Quantity, original.Price)
            : wholeRials ? ShopInvoiceAmounts.Gross(input.Quantity, input.Price)
            : OrderLineAmounts.Gross(input.Quantity, input.Price);
        var sameDiscount = sameBasis && original!.DiscountIsAmount == input.DiscountIsAmount &&
            (input.DiscountIsAmount ? original.DiscountAmount == input.DiscountAmount
                                   : original.DiscountPercent == input.DiscountPercent);
        decimal discount, percent;
        if (sameDiscount)
        {
            discount = original!.DiscountAmount;
            percent = original.DiscountPercent;
        }
        else if (input.DiscountIsAmount)
        {
            discount = Math.Clamp(decimal.Round(input.DiscountAmount, 0), 0, Math.Max(0, gross));
            percent = gross > 0 ? decimal.Round(discount * 100m / gross, 2) : 0m;
        }
        else
        {
            percent = input.DiscountPercent > 0 ? Math.Clamp(input.DiscountPercent, 0, 100) : 0m;
            discount = percent > 0 ? decimal.Round(gross * percent / 100m, 0) : 0m;
        }
        var vatValue = input.VatValue ?? 0m;
        var vat = sameDiscount && original!.VatValue == vatValue
            ? original.VatAmount
            : OrderLineAmounts.Vat(Math.Max(0m, gross - discount), vatValue);
        return new Amounts(gross, percent, discount, input.DiscountIsAmount, vatValue, vat);
    }

    internal static decimal Total(IEnumerable<OrderLineInput> lines, bool wholeRials,
        IReadOnlyDictionary<int, Db.TransactionLine>? originals = null)
    {
        try
        {
            decimal total = 0m;
            foreach (var line in lines)
            {
                var old = originals is not null && originals.TryGetValue(line.Id, out var saved) ? saved : null;
                total += Calculate(line, wholeRials, old).Total;
                if (wholeRials && total > OrderLineAmounts.MaxVatValue)
                    throw new InvalidOperationException("جمع فاکتور خارج از محدوده قابل ثبت است.");
            }
            return total;
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException("جمع فاکتور خارج از محدوده قابل ثبت است.");
        }
    }
}
