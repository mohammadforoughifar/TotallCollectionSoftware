global using System;
global using System.Collections.Generic;
global using System.Linq;
using Inventory.Shared.Dtos;
namespace Inventory.Shared.Dtos { public class LookupItem { public int Id {get;set;} public string Name {get;set;} = ""; } }
class Program {
  static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
  static void Main() {
    var items = new System.Collections.Generic.List<Inventory.Shared.Dtos.ShopWarrantyItemDto> {
      new() { Decision = Inventory.Shared.Dtos.WarrantyDecision.Accepted, ProductId = 1, Supplied = true, Quantity = 2, UnitCost = 80, CustomerUnitPrice = 999 },
      new() { Decision = Inventory.Shared.Dtos.WarrantyDecision.Rejected, ProductId = 2, Supplied = true, Quantity = 1, UnitCost = 100, CustomerUnitPrice = 120 },
      new() { Decision = Inventory.Shared.Dtos.WarrantyDecision.Rejected, ProductId = 3, Supplied = false, Quantity = 1, UnitCost = 80, CustomerUnitPrice = 500 },
      new() { Decision = Inventory.Shared.Dtos.WarrantyDecision.Rejected, ProductId = null, Performed = true, Quantity = 1, UnitCost = 50, CustomerUnitPrice = 200 },
      new() { Decision = Inventory.Shared.Dtos.WarrantyDecision.Rejected, ProductId = null, Performed = false, Quantity = 1, UnitCost = 50, CustomerUnitPrice = 700 },
    };
    var warranty = new Inventory.Shared.Dtos.ShopWarrantyDto { Items = items, ChargePolicyVersion = 1, FinalizedAt = DateTime.Today, SettledAmount = 80 };
    Check(warranty.CustomerAmount == 320, "only supplied/performed rejected rows charged");
    Check(warranty.InternalCost == 310, "only supplied/performed rows incur cost");
    Check(warranty.UnpaidAmount == 240, "actual settlement reduces unpaid balance");
    warranty.ChargePolicyVersion = 0;
    Check(warranty.CustomerAmount == 0 && warranty.UnpaidAmount == 0, "legacy finalized rows remain free");
    var unused = new Inventory.Shared.Dtos.RepairItemDto { WarrantyDecision = Inventory.Shared.Dtos.WarrantyDecision.Rejected, ProductId = 1, WarrantySupplied = false, Price = 100, Cost = 40 };
    Check(unused.CustomerUnitPrice == 0 && unused.EffectiveUnitCost == 0, "unused repair part excluded");
    var service = new Inventory.Shared.Dtos.RepairItemDto { WarrantyDecision = Inventory.Shared.Dtos.WarrantyDecision.Rejected, ProductId = 2, ProductIsService = true, WarrantyPerformed = false, Price = 100, Cost = 40 };
    Check(service.CustomerUnitPrice == 0 && service.EffectiveUnitCost == 0, "unperformed repair service excluded");
    var regular = new Inventory.Shared.Dtos.RepairItemDto { WarrantyDecision = Inventory.Shared.Dtos.WarrantyDecision.Unspecified, ProductId = 1, WarrantySupplied = false, Price = 100, Cost = 40 };
    Check(regular.CustomerUnitPrice == 100 && regular.EffectiveUnitCost == 40, "ordinary repair unchanged");
    Check(Inventory.Shared.ShopInvoiceAmounts.TryGross(1.5m,1001m,out var gross) && gross == 1502, "half-rial rounds upward");
    Check(!Inventory.Shared.ShopInvoiceAmounts.TryGross(decimal.MaxValue,decimal.MaxValue,out _), "overflow rejected");
    Check(!Inventory.Shared.ShopInvoiceAmounts.IsWholePrice(2.5m), "fractional-rial prices rejected");
    warranty.ChargePolicyVersion = 1; warranty.SettledAmount = 999;
    Check(warranty.UnpaidAmount == 0, "unpaid amount never becomes negative");
    var accepted = new Inventory.Shared.Dtos.RepairItemDto { WarrantyDecision = Inventory.Shared.Dtos.WarrantyDecision.Accepted, Price = 500, Cost = 80 };
    Check(accepted.CustomerUnitPrice == 0 && accepted.EffectiveUnitCost == 80, "accepted repair work is free but costs the shop");
    var performed = new Inventory.Shared.Dtos.RepairItemDto { WarrantyDecision = Inventory.Shared.Dtos.WarrantyDecision.Rejected, ProductId = 3, ProductIsService = true, WarrantyPerformed = true, Price = 150, Cost = 40 };
    Check(performed.CustomerUnitPrice == 150 && performed.EffectiveUnitCost == 40, "performed rejected service has separate customer price and cost");
    Check(Inventory.Shared.PaymentMix.Derive(0, 0, 0, 1) == Inventory.Shared.PaymentMethod.Credit, "one-rial credit is classified as credit");
    var line = new Inventory.Shared.Dtos.OrderLineInput { ProductId = 2, Quantity = 1.5m, Price = 1001, SourceDescription = "private", UnitCostSnapshot = 123 };
    var amounts = Inventory.Api.Services.ShopInvoiceLineMath.Calculate(line, true);
    Check(amounts.Gross == 1502 && amounts.Total == 1502, "invoice calculation matches warranty price rounding");
    var command = new Inventory.Shared.Dtos.OrderCommand { IsWarrantyInvoice = true, Lines = new() { line } };
    var serialized = System.Text.Json.JsonSerializer.Serialize(command);
    Check(!serialized.Contains("IsWarrantyInvoice") && !serialized.Contains("SourceDescription") && !serialized.Contains("UnitCostSnapshot"),
      "server-only charge metadata cannot be set through JSON");
    var invoice = new Inventory.Shared.Dtos.Order { Type = Inventory.Shared.TransactionType.Sale, Number = "SALE-1", Date = DateTime.Today,
      TotalAmount = 1502, Lines = new() { new Inventory.Shared.Dtos.OrderLine { Id = 1, ProductName = "خدمت", IsService = true,
        Description = "<script>warranty</script>", Quantity = 1.5m, Price = 1001, GrossAmount = 1502 } } };
    var print = Inventory.Client.Pages.Orders.SaleInvoicePrint.Build(invoice, new Inventory.Shared.Dtos.CompanyProfileDto { PrintPaperSize = "A5" });
    Check(print.Contains("paper-a5") && print.Contains("&lt;script&gt;warranty&lt;/script&gt;") && !print.Contains("<script>warranty</script>"),
      "invoice print honors paper setting and escapes the warranty description");
    var freeRepair = new Inventory.Shared.Dtos.RepairOrderDto { Number = "RP-1", DeviceType = "دستگاه", ReceivedAt = DateTime.Today,
      TotalPrice = 0, QuotedPrice = 5000000, Items = new() { accepted } };
    var deliveryPrint = Inventory.Client.Pages.RepairDeliveryReceiptPrint.Build(freeRepair, "فروشگاه");
    var settlement = deliveryPrint.Substring(deliveryPrint.IndexOf("<section class=\"settlement-box\">"));
    Check(!settlement.Contains("۵,۰۰۰,۰۰۰") && settlement.Contains("۰ <small>ریال</small>"),
      "free warranty print does not substitute the initial estimate as a customer charge");
    var repairPrint = Inventory.Client.Pages.RepairInvoicePrint.Build(freeRepair, "فروشگاه");
    Check(repairPrint.Contains("مجموع خدمات و قطعات"), "free warranty repair print uses actual customer amount");
    var reportRepair = new Inventory.Api.Data.RepairOrder { WarrantyProcessed = true, Items = new() {
      new Inventory.Api.Data.RepairItem { ProductId = 1, Quantity = 1, Cost = 80, Price = 900, WarrantyDecision = WarrantyDecision.Accepted, WarrantySupplied = true },
      new Inventory.Api.Data.RepairItem { ProductId = 1, Quantity = 1, Cost = 80, Price = 140, WarrantyDecision = WarrantyDecision.Rejected, WarrantySupplied = true },
      new Inventory.Api.Data.RepairItem { ProductId = 1, Quantity = 1, Cost = 80, Price = 999, WarrantyDecision = WarrantyDecision.Rejected, WarrantySupplied = false },
      new Inventory.Api.Data.RepairItem { Quantity = 1, Cost = 20, Price = 50 },
      new Inventory.Api.Data.RepairItem { Quantity = 1, Cost = 30, Price = 900, WarrantyDecision = WarrantyDecision.Accepted, WarrantyPerformed = true },
      new Inventory.Api.Data.RepairItem { Quantity = 1, Cost = 40, Price = 900, WarrantyDecision = WarrantyDecision.Rejected, WarrantyPerformed = false }
    } };
    var totals = Inventory.Api.Services.Repairs.RepairReportTotals.Calculate(reportRepair, new HashSet<int>());
    Check(totals == (190m, 160m, 50m), "mixed repair report counts only real charges and work/part costs");
    var legacyReport = new Inventory.Api.Data.RepairOrder { WarrantyProcessed = false, Items = new() {
      new Inventory.Api.Data.RepairItem { Quantity = 1.5m, Price = 1001m, Cost = 10m } } };
    var oldTotals = Inventory.Api.Services.Repairs.RepairReportTotals.Calculate(legacyReport, new HashSet<int>());
    Check(oldTotals.Revenue == 1501.5m && oldTotals.LaborCost == 15m, "legacy report calculations remain unchanged");
    reportRepair.Items = new() { new Inventory.Api.Data.RepairItem { ProductId = 2, Quantity = 1, Cost = 7, Price = 500,
      WarrantyDecision = WarrantyDecision.Accepted, WarrantySupplied = false, WarrantyPerformed = true } };
    totals = Inventory.Api.Services.Repairs.RepairReportTotals.Calculate(reportRepair, new HashSet<int> { 2 });
    Check(totals == (0m, 0m, 7m), "catalog services use performed status and labor-cost reporting");
  }
}

// ShopInvoiceLineMath only needs these persisted snapshot fields; this isolated
// contract executable deliberately does not replace EF/provider integration tests.
namespace Inventory.Api.Data {
  public class TransactionLine {
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal? GrossAmount { get; set; }
    public bool DiscountIsAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal VatValue { get; set; }
    public decimal VatAmount { get; set; }
  }
}
