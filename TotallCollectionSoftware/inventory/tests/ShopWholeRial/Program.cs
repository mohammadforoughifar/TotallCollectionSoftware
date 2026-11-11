using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Export;
using Inventory.Client.Pages.Orders;
using ClosedXML.Excel;
using System.Reflection;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Db = Inventory.Api.Data;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}
static async Task Rejected(Func<Task> operation, string message)
{
    try { await operation(); }
    catch (InvalidOperationException) { Console.WriteLine("PASS: " + message); return; }
    throw new Exception("FAIL: " + message);
}

Check(ShopInvoiceAmounts.Gross(1.5m, 1001m) == 1502m, "half rial rounds up");
Check(ShopInvoiceAmounts.Gross(1.25m, 1001m) == 1251m, "below half rounds down");
Check(ShopInvoiceAmounts.Gross(.5m, 1001m) == 501m, "quantity remains fractional");
Check(!ShopInvoiceAmounts.TryGross(999999999999999.99m, ShopInvoiceAmounts.MaximumPrice, out _),
    "oversized fractional quantities do not crash rendering");
Check(ShopQuantityRules.DefaultAllowsFraction("عدد") == false && ShopQuantityRules.DefaultAllowsFraction("کیلوگرم") == true &&
      ShopQuantityRules.DefaultAllowsFraction("واحد اختصاصی") is null, "standard and unknown units are distinguished");
Check(PurchaseSalePriceInput.TryParse("۱٬۰۰۱", out var parsed) && parsed == 1001m, "Persian whole price");
Check(!PurchaseSalePriceInput.TryParse("100,50", out _) && !PurchaseSalePriceInput.TryParse("1.5", out _) &&
      !PurchaseSalePriceInput.TryParse("-100", out _), "no silently changed decimal, malformed grouping or negative price");
Check(PurchaseSalePriceInput.TryParseLegacy("10.5", out var oldPrice) && oldPrice == 10.5m,
    "legacy fractional price can be preserved without accepting it as a new price");
Check(!ShopMinimumProfit.MeetsPurchase(76_000_000m, 1m, 65_000_000m, 1m, 20m, 0m, 0m), "76m below floor is rejected");
Check(ShopMinimumProfit.MeetsPurchase(78_000_000m, 1m, 65_000_000m, 1m, 20m, 0m, 0m), "78m meets floor");
Check(ShopMinimumProfit.MeetsPurchase(240m, 3m, 200m, 3m, 20m, 0m, 0m), "recurring cost remains exact");
Check(!ShopMinimumProfit.MeetsPurchase(239m, 3m, 200m, 3m, 20m, 0m, 0m), "recurring cost never undercuts the floor");

// Ordinary exports keep their previous formatting; only explicitly marked
// stock/legacy-price columns display fractional values.
var pdfFormat = typeof(PdfWriter).GetMethod("Format", BindingFlags.NonPublic | BindingFlags.Static)!;
var exportSpec = new ExportSpec { PersianDigits = true, ShowRowNumbers = false };
string FormatPdf(decimal value, ExportValueKind kind, bool fractional) =>
    (string)pdfFormat.Invoke(null, new object[] { value, kind, exportSpec, fractional })!;
Check(FormatPdf(1.5m, ExportValueKind.Number, false) == Fa.Number(1.5m), "ordinary PDF numbers stay unchanged");
Check(FormatPdf(1.5m, ExportValueKind.Number, true) == Fa.Digits("1.5"), "fractional shop quantity is visible in PDF");
Check(FormatPdf(10.5m, ExportValueKind.Money, true) == Fa.Digits("10.5"), "legacy shop price is visible in PDF");
exportSpec.Columns.Add(new ExportColumn("legacy price", ExportValueKind.Money) { PreserveFractionalDigits = true });
exportSpec.Columns.Add(new ExportColumn("ordinary amount", ExportValueKind.Money));
exportSpec.Rows.Add(new ExportRow(10.5m, 10.5m));
using (var workbook = new XLWorkbook(new MemoryStream(ExcelWriter.Build(exportSpec))))
{
    var sheet = workbook.Worksheet(1);
    var header = sheet.RowsUsed().Single(row => row.Cell(1).GetString() == "legacy price");
    var data = header.RowBelow();
    Check(data.Cell(1).GetDouble() == 10.5 && data.Cell(1).Style.NumberFormat.Format.Contains(".###"),
        "legacy price remains numeric and displays decimals in Excel");
    Check(data.Cell(2).GetDouble() == 10.5 && !data.Cell(2).Style.NumberFormat.Format.Contains(".###"),
        "ordinary Excel money format stays unchanged");
}

// The API's actual line calculator must preserve old snapshots and use the same
// newly rounded gross for payment, discounts, VAT and persistence.
var original = new Db.TransactionLine { ProductId = 1, Quantity = 1.5m, Price = 10.5m,
    GrossAmount = 15.75m, DiscountAmount = 0m, VatValue = 10m, VatAmount = 1.58m };
var unchanged = new OrderLineInput { ProductId = 1, Quantity = 1.5m, Price = 10.5m, VatValue = 10m };
var oldAmounts = ShopInvoiceLineMath.Calculate(unchanged, true, original);
Check(oldAmounts.Gross == 15.75m && oldAmounts.VatAmount == 1.58m && oldAmounts.Total == 17.33m,
    "unchanged historical line preserves gross, VAT and total");
var newer = ShopInvoiceLineMath.Calculate(new OrderLineInput { ProductId = 1, Quantity = 1.5m, Price = 1001m, VatValue = 10m }, true);
Check(newer.Gross == 1502m && newer.VatAmount == 150.2m && newer.Total == 1652.2m,
    "new gross is whole; VAT follows existing contract without changing percentage precision");
try
{
    ShopInvoiceLineMath.Total(new[] {
        new OrderLineInput { Quantity = 1m, Price = ShopInvoiceAmounts.MaximumPrice },
        new OrderLineInput { Quantity = 1m, Price = ShopInvoiceAmounts.MaximumPrice }
    }, true);
    throw new Exception("FAIL: oversized invoice must be rejected");
}
catch (InvalidOperationException) { Console.WriteLine("PASS: oversized invoice is rejected before persistence"); }

await using (var sql = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer("Server=localhost;Database=not-used;Integrated Security=True;TrustServerCertificate=True").Options))
{
    const string migration = "20261106110000_AddShopQuantityPrecision";
    Check(sql.Database.GetMigrations().Contains(migration), "quantity migration discoverable");
    var script = sql.GetService<IMigrator>().GenerateScript("20261106100000_AddShopPricing", migration);
    Check(script.Contains("AllowsFractionalQuantity") && script.Contains("COL_LENGTH"), "additive SQL Server migration");
}

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
await using var db = new AppDbContext(options);
await db.Database.EnsureCreatedAsync();
var warehouse = new Db.Warehouse { Name = "انبار" };
var supplier = new Db.Party { Name = "تأمین", Type = PartyType.Supplier };
var customer = new Db.Party { Name = "مشتری", Type = PartyType.Customer };
var whole = new Db.Product { Name = "عدد", Code = "W-1", Unit = "عدد" };
var weight = new Db.Product { Name = "وزنی", Code = "W-2", Unit = "کیلوگرم" };
var custom = new Db.Product { Name = "اختصاصی", Code = "W-3", Unit = "واحد اختصاصی" };
var service = new Db.Product { Name = "خدمت", Code = "W-4", Unit = "خدمت", IsService = true };
db.AddRange(warehouse, supplier, customer, whole, weight, custom, service,
    new Db.MeasureUnit { Name = "عدد", AllowsFractionalQuantity = false },
    new Db.MeasureUnit { Name = "کیلوگرم", AllowsFractionalQuantity = true },
    new Db.MeasureUnit { Name = "واحد اختصاصی", AllowsFractionalQuantity = null },
    new Db.AppSetting { AllowNegativeStock = true });
await db.SaveChangesAsync();
var inventory = new InventoryService(db, NullLogger<InventoryService>.Instance);
var pricing = new ShopPricingService(db);
var day = DateTime.Today;
OrderCommand Invoice(TransactionType type, int productId, decimal qty, decimal price, decimal vat = 0m) => new()
{
    Type = type, Date = day, WarehouseId = warehouse.Id, PartyId = type == TransactionType.Purchase ? supplier.Id : customer.Id,
    PaymentMethod = PaymentMethod.Credit, DueDate = day.AddDays(30),
    Lines = new() { new() { ProductId = productId, Quantity = qty, Price = price, VatValue = vat } }
};

await Rejected(async () => { await inventory.CreateOrderAsync(Invoice(TransactionType.Purchase, whole.Id, 1.5m, 1000m)); }, "fractional pieces rejected in purchase");
await Rejected(async () => { await inventory.CreateOrderAsync(Invoice(TransactionType.Sale, whole.Id, .5m, 1000m)); }, "fractional pieces rejected in sale");
await Rejected(async () => { await inventory.CreateOrderAsync(Invoice(TransactionType.Purchase, weight.Id, 1m, 1000.5m)); }, "fractional purchase price rejected");
await Rejected(async () => { await inventory.CreateOrderAsync(Invoice(TransactionType.Sale, weight.Id, 1m, 1000.5m)); }, "fractional sale price rejected");
await Rejected(async () => { await inventory.CreateOrderAsync(Invoice(TransactionType.Purchase, custom.Id, 1m, 1000m)); }, "unknown custom unit needs classification");
var wPurchase = await inventory.CreateOrderAsync(Invoice(TransactionType.Purchase, weight.Id, 1.5m, 1001m));
Check(wPurchase.Lines.Single().Gross == 1502m && wPurchase.TotalAmount == 1502m, "purchase stores rounded gross and total");
var wSale = await inventory.CreateOrderAsync(Invoice(TransactionType.Sale, weight.Id, 1.5m, 2001m));
Check(wSale.Lines.Single().Gross == 3002m && wSale.TotalAmount == 3002m, "sale stores rounded gross and total");
var customUnit = (await inventory.GetUnitsAsync()).Single(u => u.Name == "واحد اختصاصی");
customUnit.AllowsFractionalQuantity = true;
await inventory.SaveUnitAsync(customUnit);
var customPurchase = await inventory.CreateOrderAsync(Invoice(TransactionType.Purchase, custom.Id, .5m, 1001m));
Check(customPurchase.TotalAmount == 501m, "explicit custom-unit decision is honored");

// An old invoice with an integer-only unit, fractional price and fractional gross
// must remain byte-for-byte numerically stable on an unrelated edit.
var old = new Db.Transaction { Type = TransactionType.Purchase, Date = day.AddDays(-2), Number = "LEGACY-1",
    WarehouseId = warehouse.Id, PartyId = supplier.Id, PaymentMethod = PaymentMethod.Credit, DueDate = day.AddDays(30), Amount = 17.33m,
    Lines = new() { new Db.TransactionLine { ProductId = whole.Id, Quantity = 1.5m, Price = 10.5m,
        GrossAmount = 15.75m, DiscountAmount = 0, VatValue = 10, VatAmount = 1.58m } } };
db.Transactions.Add(old);
await db.SaveChangesAsync();
var oldCommand = Invoice(TransactionType.Purchase, whole.Id, 1.5m, 10.5m, 10m);
oldCommand.Date = old.Date;
oldCommand.Lines[0].Id = old.Lines.Single().Id;
oldCommand.Description = "ویرایش توضیح";
var preserved = await inventory.UpdateOrderAsync(old.Id, oldCommand);
Check(preserved.TotalAmount == 17.33m && preserved.Lines.Single().Gross == 15.75m &&
      preserved.Lines.Single().VatAmount == 1.58m && preserved.Lines.Single().Price == 10.5m,
    "unrelated historical invoice edit retains saved amounts");

await inventory.CreateOrderAsync(Invoice(TransactionType.Purchase, whole.Id, 1m, 65_000_000m));
await pricing.SavePolicyAsync(new ShopPricingPolicyDto { MinimumProfitEnabled = true, MinimumMarkupPercent = 20m }, "test");
var basis = await pricing.GetMinimumProfitBasisAsync(whole.Id, day, null);
Check(basis.MinimumProfitEnabled && basis.NetUnitCost == 65_000_000m &&
      basis.MinimumUnitRevenueBeforeSaleDiscountAndCommission == 78_000_000m, "diagnostic and guard share actual 65m purchase");
var before = await db.Transactions.CountAsync();
await Rejected(async () => { await inventory.CreateOrderAsync(Invoice(TransactionType.Sale, whole.Id, 1m, 76_000_000m)); }, "76m sale rejected by enabled policy");
Check(await db.Transactions.CountAsync() == before, "failed sale rolls back");
var exactFloor = await inventory.CreateOrderAsync(Invoice(TransactionType.Sale, whole.Id, 1m, 78_000_000m));
Check(exactFloor.TotalAmount == 78_000_000m, "78m sale accepted at floor");

var futurePurchase = Invoice(TransactionType.Purchase, whole.Id, 1m, 90_000_000m);
futurePurchase.Date = day.AddDays(1);
await inventory.CreateOrderAsync(futurePurchase);
Check((await pricing.GetMinimumProfitBasisAsync(whole.Id, day, exactFloor.Id)).NetUnitCost == 65_000_000m,
    "future purchase cannot affect an earlier sale");
await inventory.CreateOrderAsync(Invoice(TransactionType.Purchase, whole.Id, 1m, 70_000_000m));
Check((await pricing.GetMinimumProfitBasisAsync(whole.Id, day, exactFloor.Id)).NetUnitCost == 65_000_000m,
    "later same-day purchase cannot change an existing sale's basis");
Check((await pricing.GetMinimumProfitBasisAsync(whole.Id, day, null)).NetUnitCost == 70_000_000m,
    "new sale uses the most recently registered same-day purchase");
await Rejected(async () => { await inventory.CreateOrderAsync(Invoice(TransactionType.Sale, whole.Id, 1m, 83_000_000m)); },
    "new 70m basis raises the floor to 84m");
var discountPurchase = Invoice(TransactionType.Purchase, weight.Id, 1m, 1000m);
discountPurchase.Lines[0].DiscountPercent = 10m;
await inventory.CreateOrderAsync(discountPurchase);
var discountBasis = await pricing.GetMinimumProfitBasisAsync(weight.Id, day, null);
Check(discountBasis.NetUnitCost == 900m && discountBasis.MinimumUnitRevenueBeforeSaleDiscountAndCommission == 1080m,
    "purchase discount is reflected in the actual basis");
await Rejected(async () => { await inventory.CreateOrderAsync(Invoice(TransactionType.Sale, whole.Id, 1m, 76_000_000m, 10m)); }, "VAT does not mask a below-floor sale");
var serviceSale = await inventory.CreateOrderAsync(Invoice(TransactionType.Sale, service.Id, 1.5m, 100m));
Check(serviceSale.TotalAmount == 150m, "services remain outside goods quantity and profit policy");

// Same custom name may have different classifications in two companies.
var oneHttp = new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };
oneHttp.HttpContext.Items["ActiveCompanyId"] = 11;
var twoHttp = new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };
twoHttp.HttpContext.Items["ActiveCompanyId"] = 22;
await using var one = new AppDbContext(options, oneHttp);
await using var two = new AppDbContext(options, twoHttp);
var oneProduct = new Db.Product { Name = "شرکت اول", Code = "COMP-1", Unit = "واحد مشترک" };
var twoProduct = new Db.Product { Name = "شرکت دوم", Code = "COMP-2", Unit = "واحد مشترک" };
one.AddRange(oneProduct, new Db.MeasureUnit { Name = "واحد مشترک", AllowsFractionalQuantity = false });
two.AddRange(twoProduct, new Db.MeasureUnit { Name = "واحد مشترک", AllowsFractionalQuantity = true },
    new Db.MeasureUnit { Name = "کیلوگرم", AllowsFractionalQuantity = false });
await one.SaveChangesAsync();
await two.SaveChangesAsync();
// New/activated company seeding uses the caller's transaction even when the
// active request belongs to a different company, and never changes a choice.
await Seeder.EnsureUnitsForCompanyAsync(one, 22);
await Seeder.EnsureUnitsForCompanyAsync(one, 22);
Check((await two.MeasureUnits.SingleAsync(u => u.Name == "کیلوگرم")).AllowsFractionalQuantity == false &&
      (await two.MeasureUnits.SingleAsync(u => u.Name == "عدد")).AllowsFractionalQuantity == false &&
      (await two.MeasureUnits.SingleAsync(u => u.Name == "گالن")).AllowsFractionalQuantity is null &&
      !await one.MeasureUnits.AnyAsync(u => u.Name == "عدد"),
    "new-company defaults are scoped, idempotent and preserve explicit choices");
var oneInventory = new InventoryService(one, NullLogger<InventoryService>.Instance);
var twoInventory = new InventoryService(two, NullLogger<InventoryService>.Instance);
Check((await oneInventory.GetProductAsync(oneProduct.Id))!.AllowsFractionalQuantity == false &&
      (await twoInventory.GetProductAsync(twoProduct.Id))!.AllowsFractionalQuantity == true,
    "same-named unit classifications remain company scoped");
Check((await inventory.GetProductAsync(oneProduct.Id))!.AllowsFractionalQuantity == false &&
      (await inventory.GetProductAsync(twoProduct.Id))!.AllowsFractionalQuantity == true,
    "background context never borrows a different company's unit decision");
var unitOne = (await oneInventory.GetUnitsAsync()).Single(u => u.Name == "واحد مشترک");
unitOne.Name = "واحد تغییر یافته";
await oneInventory.SaveUnitAsync(unitOne);
Check((await twoInventory.GetProductAsync(twoProduct.Id))!.Unit == "واحد مشترک",
    "renaming a unit does not rewrite another company's product");

Console.WriteLine("PASS: whole-rial shop integration probe");
