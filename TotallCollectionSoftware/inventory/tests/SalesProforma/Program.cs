using Inventory.Shared;
using Inventory.Shared.Dtos;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var quote = new SalesProformaCommand
{
    PartyId = 1, WarehouseId = 2, Date = new DateTime(2026, 1, 1),
    Lines = new() { new() { ProductId = 3, Quantity = 2, Price = 1000, DiscountPercent = 10, VatValue = 10 } }
};
var payment = new OrderCommand
{
    Type = TransactionType.Purchase, PartyId = 999, WarehouseId = 999,
    PaymentMethod = PaymentMethod.Cash, TrsAccountId = 5,
    Lines = new() { new() { ProductId = 999, Quantity = 100, Price = 0 } }
};
var result = SalesProformaConversion.CreateSale(quote, payment);
Check(result.Type == TransactionType.Sale && result.PartyId == 1 && result.WarehouseId == 2, "sale data must come from quote");
Check(result.Lines.Count == 1 && result.Lines[0].ProductId == 3 && result.Lines[0].Price == 1000, "payment must not replace items");
Check(result.TrsAccountId == 5 && quote.Lines[0].Total == 1980, "payment and quote total");
quote.Lines[0].Quantity = 0;
try { SalesProformaConversion.CreateSale(quote, payment); throw new Exception("invalid quantity accepted"); }
catch (ArgumentException) { }
Console.WriteLine("Sales proforma conversion contract passed.");
