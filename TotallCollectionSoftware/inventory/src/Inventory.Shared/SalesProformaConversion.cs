using Inventory.Shared.Dtos;

namespace Inventory.Shared;

/// <summary>
/// Builds a sale request from the persisted quotation and separately supplied payment.
/// This does not persist anything. The caller must check company, status and permissions,
/// and atomically mark the quotation converted in the same transaction as the sale.
/// </summary>
public static class SalesProformaConversion
{
    public static OrderCommand CreateSale(SalesProformaCommand quote, OrderCommand payment)
    {
        ArgumentNullException.ThrowIfNull(quote);
        ArgumentNullException.ThrowIfNull(payment);
        if (quote.WarehouseId <= 0 || quote.PartyId <= 0 || quote.Date == default)
            throw new ArgumentException("اطلاعات پیش‌فاکتور کامل نیست.");
        if (quote.Lines is null || quote.Lines.Count == 0 || quote.Lines.Count > 500)
            throw new ArgumentException("تعداد اقلام پیش‌فاکتور معتبر نیست.");
        if (quote.Lines.Any(line => line is null || line.ProductId <= 0 || line.Quantity <= 0 ||
            line.Quantity != decimal.Round(line.Quantity, 2) || line.Quantity > ShopInvoiceAmounts.MaximumQuantity ||
            !ShopInvoiceAmounts.IsWholePrice(line.Price) || !ShopInvoiceAmounts.TryGross(line.Quantity, line.Price, out _) ||
            line.DiscountPercent < 0 || line.DiscountPercent > 100 || line.DiscountPercent != decimal.Round(line.DiscountPercent, 2) || line.DiscountAmount < 0 ||
            !OrderLineAmounts.IsValidVatValue(line.VatValue)))
            throw new ArgumentException("اطلاعات یکی از اقلام پیش‌فاکتور معتبر نیست.");

        try
        {
            if (quote.Lines.Sum(line => line.Total) > OrderLineAmounts.MaxVatValue ||
                quote.Lines.Any(line => line.Total > OrderLineAmounts.MaxVatValue))
                throw new ArgumentException("جمع پیش‌فاکتور خارج از محدوده قابل ثبت است.");
        }
        catch (OverflowException)
        {
            throw new ArgumentException("جمع پیش‌فاکتور خارج از محدوده قابل ثبت است.");
        }

        // Never take sale lines, customer, warehouse or prices from the payment request.
        // The normal sale service must revalidate catalog, stock, pricing and payments.
        return new OrderCommand
        {
            Type = TransactionType.Sale,
            WarehouseId = quote.WarehouseId,
            PartyId = quote.PartyId,
            Date = quote.Date,
            Description = quote.Description,
            ReferrerId = quote.ReferrerId,
            PaymentMethod = payment.PaymentMethod,
            CashType = payment.CashType,
            CashTrackingNumber = payment.CashTrackingNumber,
            CashAmount = payment.CashAmount,
            TrsAccountId = payment.TrsAccountId,
            DueDate = payment.DueDate,
            ChequeCharge = payment.ChequeCharge,
            Cheques = payment.Cheques ?? new(),
            Installments = payment.Installments ?? new(),
            Lines = quote.Lines.Select(line => new OrderLineInput
            {
                ProductId = line.ProductId,
                Quantity = line.Quantity,
                Price = line.Price,
                DiscountPercent = line.DiscountPercent,
                DiscountAmount = line.DiscountAmount,
                DiscountIsAmount = line.DiscountIsAmount,
                VatValue = line.VatValue
            }).ToList()
        };
    }
}
