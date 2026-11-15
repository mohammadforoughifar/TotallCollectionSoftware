using System.Security.Claims;
using Inventory.Api.Controllers;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Core;
using Inventory.Client.Pages.Orders;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

static void Check(bool ok, string message)
{
    if (!ok) throw new Exception("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}

Check(OrderLineAmounts.Gross(1.25m, 1.25m) == 1.56m, "gross rounds to database precision per line");
Check(OrderLineAmounts.Vat(900, 0) == 0, "zero VAT");
Check(OrderLineAmounts.Vat(900, 10) == 90, "percentage after discount");
Check(OrderLineAmounts.Vat(900, 100) == 900, "100 is 100 percent");
Check(OrderLineAmounts.Vat(900, 101) == 101, "above 100 is a fixed rial amount");
Check(OrderLineAmounts.Vat(900, 100.01m) == 100.01m, "mode boundary is based on the entered number");
Check(OrderLineAmounts.Total(2, 1000, 10, 0, false, 10) == 1980, "discount before VAT");
Check(OrderLineAmounts.Total(2, 1000, 0, 200, true, 101) == 1901, "fixed discount and fixed VAT");
Check(!OrderLineAmounts.IsValidVatValue(-1) && !OrderLineAmounts.IsValidVatValue(1.001m), "invalid VAT rejected");

// Product menus use a complete snapshot of the selected warehouse, not the previous
// selection or an empty dictionary standing in for a failed request.
var stock = new InvoiceWarehouseStock();
var noWarehouse = InvoiceProductOption.Create(1, "goods", "عدد", false, stock);
Check(noWarehouse.StockQuantity is null && !noWarehouse.IsStockWarning,
    "unselected warehouse does not claim zero stock");
var oldPage = new TaskCompletionSource<PagedResult<InvoiceStockItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
var oldLoad = stock.LoadAsync(1, (_, _, _) => oldPage.Task);
Check(stock.IsLoading && !stock.IsLoaded &&
      InvoiceProductOption.Create(1, "goods", "عدد", false, stock).StockQuantity is null,
    "stock is unknown while loading");
var loadedPages = new List<int>();
var newLoad = stock.LoadAsync(2, (id, page, size) =>
{
    loadedPages.Add(page);
    Check(id == 2 && size == 10000, "stock request uses the selected warehouse and page size");
    return Task.FromResult(new PagedResult<InvoiceStockItem>
    {
        TotalCount = 3,
        Items = page == 1
            ? new() { new() { WarehouseId = 2, ProductId = 1, Quantity = 12.345m }, new() { WarehouseId = 2, ProductId = 2, Quantity = 0m } }
            : new() { new() { WarehouseId = 2, ProductId = 3, Quantity = -1m } }
    });
});
Check(await newLoad && loadedPages.SequenceEqual(new[] { 1, 2 }) && stock.IsLoaded,
    "every stock page is loaded before treating missing products as zero");
oldPage.SetResult(new PagedResult<InvoiceStockItem>
{
    TotalCount = 1, Items = new() { new() { WarehouseId = 1, ProductId = 1, Quantity = 99m } }
});
Check(!await oldLoad && stock.WarehouseId == 2 && stock.Quantities[1] == 12.345m,
    "late results cannot replace the latest warehouse snapshot");
var positiveOption = InvoiceProductOption.Create(1, "goods", "عدد", false, stock);
var zeroOption = InvoiceProductOption.Create(2, "goods", "کیلو", false, stock);
var negativeOption = InvoiceProductOption.Create(3, "goods", "عدد", false, stock);
var missingOption = InvoiceProductOption.Create(4, "goods", "عدد", false, stock);
var serviceOption = InvoiceProductOption.Create(4, "service", "", true, stock);
Check(positiveOption.StockQuantity == 12.345m && !positiveOption.IsStockWarning && positiveOption.StockUnit == "عدد",
    "positive stock and unit belong to the selected warehouse");
Check(zeroOption.StockQuantity == 0m && zeroOption.IsStockWarning && negativeOption.IsStockWarning &&
      missingOption.StockQuantity == 0m && missingOption.IsStockWarning,
    "zero, negative and absent stock are warnings after a complete load");
Check(serviceOption.StockQuantity is null && !serviceOption.IsStockWarning && serviceOption.InfoText == "خدمات",
    "services do not acquire fictional inventory");
Check(InvoiceProductOption.FormatStock(0.001m) == "۰.۰۰۱" && InvoiceProductOption.FormatStock(1234.5m) == "۱,۲۳۴.۵",
    "stock formatting preserves fractional quantities and Persian digits");

var pendingOld = new TaskCompletionSource<PagedResult<InvoiceStockItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
var pendingNew = new TaskCompletionSource<PagedResult<InvoiceStockItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
var staleFailure = stock.LoadAsync(3, (_, _, _) => pendingOld.Task);
var pendingLoad = stock.LoadAsync(4, (_, _, _) => pendingNew.Task);
pendingOld.SetException(new InvalidOperationException("old request failed"));
Check(!await staleFailure && stock.IsLoading && stock.WarehouseId == 4,
    "an old failure cannot stop a newer loading request");
pendingNew.SetException(new InvalidOperationException("current request failed"));
Check(await pendingLoad && !stock.IsLoaded && !stock.IsLoading &&
      InvoiceProductOption.Create(1, "goods", "عدد", false, stock) is { StockQuantity: null, IsStockWarning: false, InfoText: "موجودی انبار نامشخص است" },
    "a failed request is unknown, never a red zero");
await stock.LoadAsync(5, (id, page, size) => Task.FromResult(new PagedResult<InvoiceStockItem>
{
    TotalCount = 2,
    Items = page == 1 ? new() { new() { WarehouseId = id, ProductId = 1, Quantity = 5m } } : new()
}));
Check(!stock.IsLoaded && stock.Quantities.Count == 0, "an incomplete page sequence is not mistaken for a complete snapshot");
await stock.LoadAsync(5, (id, page, size) => Task.FromResult(new PagedResult<InvoiceStockItem>
{
    TotalCount = page == 1 ? 2 : 1,
    Items = new() { new() { WarehouseId = id, ProductId = page, Quantity = 5m } }
}));
Check(!stock.IsLoaded && stock.Quantities.Count == 0, "a changing total between pages cannot produce false zero stock");
await stock.LoadAsync(0, (_, _, _) => throw new Exception("no warehouse must not query stock"));
Check(stock.WarehouseId == 0 && !stock.IsLoaded && !stock.IsLoading, "clearing the warehouse also clears its snapshot");

foreach (var section in ShopSections.All)
{
    Check(!ShopSections.AliasAllows("Orders.ViewProfit", section.ViewPermission) &&
          !ShopSections.AliasAllows("orders.viewprofit", section.ManagePermission),
        $"{section.Key} section permissions never imply sale-profit access");
}
Check(ShopSections.AliasAllows("Orders.Create", "Shop.Sale.Manage") &&
      ShopSections.AliasAllows("Orders.Read", "Shop.Sale.View"),
    "ordinary shop sales aliases remain available");

// Upgrade an existing minimal SQLite schema twice; snapshots on old lines remain zero.
await using (var connection = new SqliteConnection("Data Source=:memory:"))
{
    await connection.OpenAsync();
    await using var cmd = connection.CreateCommand();
    cmd.CommandText = "CREATE TABLE Products (Id INTEGER PRIMARY KEY); INSERT INTO Products VALUES(1); " +
        "CREATE TABLE TransactionLines (Id INTEGER PRIMARY KEY); INSERT INTO TransactionLines VALUES(1);";
    await cmd.ExecuteNonQueryAsync();
    var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
    await using var db = new AppDbContext(options);
    await OrderVatSchemaV1.EnsureAsync(db);
    await OrderVatSchemaV1.EnsureAsync(db);
    cmd.CommandText = "SELECT VatValue + VatAmount FROM TransactionLines WHERE Id = 1";
    Check(Convert.ToDecimal(await cmd.ExecuteScalarAsync()) == 0m, "legacy line stays zero after repeat upgrade");
    cmd.CommandText = "SELECT GrossAmount FROM TransactionLines WHERE Id = 1";
    Check(await cmd.ExecuteScalarAsync() is DBNull, "legacy gross is not rewritten");
    cmd.CommandText = "SELECT InvoiceVatValue FROM Products WHERE Id = 1";
    Check(await cmd.ExecuteScalarAsync() is DBNull, "legacy product retains fallback to prior percentage rate");
}

// Verify EF discovers the SQL Server migration without needing a server connection.
await using (var sql = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer("Server=localhost;Database=not-used;Integrated Security=True;TrustServerCertificate=True").Options))
{
    const string migration = "20261104120000_AddOrderVat";
    Check(sql.Database.GetMigrations().Contains(migration), "SQL Server migration is discoverable");
    var script = sql.GetService<IMigrator>().GenerateScript("20261021120000_AddOrganizationCompanyAndUserOrganizationId", migration);
    Check(script.Contains("[InvoiceVatValue]") && script.Contains("[VatAmount]"), "SQL Server migration includes VAT columns");
}

// Actual service + SQLite persistence for purchase, sale, edit, old clients, payment and profit.
await using (var connection = new SqliteConnection("Data Source=:memory:"))
{
    await connection.OpenAsync();
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    await db.Database.EnsureCreatedAsync();
    var wh = new Inventory.Api.Data.Warehouse { Name = "test warehouse" };
    var party = new Inventory.Api.Data.Party { Name = "test party" };
    var product = new Inventory.Api.Data.Product { Name = "test product", Code = "VAT-1", Unit = "عدد", PurchasePrice = 1000, IsVatIncluded = true, VatRate = 10 };
    db.AddRange(wh, party, product);
    await db.SaveChangesAsync();
    var service = new InventoryService(db, NullLogger<InventoryService>.Instance);
    var productDto = await service.GetProductAsync(product.Id) ?? throw new Exception("missing product");
    Check(productDto.InvoiceVatValue == 10, "legacy enabled percentage used as product default");
    productDto.InvoiceVatValue = 5000;
    await service.SaveProductAsync(productDto);
    Check(product.VatRate == 10 && product.InvoiceVatValue == 5000, "fixed invoice VAT does not modify other VAT modules");

    var purchase = new OrderCommand { WarehouseId = wh.Id, PartyId = party.Id, Type = TransactionType.Purchase,
        Date = DateTime.Today, PaymentMethod = PaymentMethod.Cash,
        Lines = new() { new() { ProductId = product.Id, Quantity = 2, Price = 1000, DiscountPercent = 10, VatValue = 100 } } };
    var created = await service.CreateOrderAsync(purchase);
    Check(created.TotalAmount == 3600 && created.CashAmount == 3600 && created.Lines.Single().VatAmount == 1800,
        "purchase total, cash and stored VAT agree");
    var line = created.Lines.Single();
    purchase.Lines[0].Id = line.Id;
    purchase.Lines[0].VatValue = 1250;
    var updated = await service.UpdateOrderAsync(created.Id, purchase);
    Check(updated.TotalAmount == 3050 && updated.CashAmount == 3050 && updated.Lines.Single().VatValue == 1250,
        "update snapshots fixed VAT and payment");
    product.InvoiceVatValue = 0;
    await db.SaveChangesAsync();
    Check((await service.GetOrderAsync(created.Id))!.Lines.Single().VatValue == 1250, "product changes do not alter old invoices");
    purchase.Lines[0].Id = updated.Lines.Single().Id;
    purchase.Lines[0].VatValue = null;
    var oldClientUpdate = await service.UpdateOrderAsync(created.Id, purchase);
    Check(oldClientUpdate.TotalAmount == 3050 && oldClientUpdate.Lines.Single().VatValue == 1250, "old clients preserve stored VAT on edit");

    var sale = new OrderCommand { WarehouseId = wh.Id, PartyId = party.Id, Type = TransactionType.Sale,
        Date = DateTime.Today, PaymentMethod = PaymentMethod.Cheque, CashAmount = 200,
        Cheques = new() { new() { Number = "123", Amount = 790, DueDate = DateTime.Today.AddDays(1) } },
        Lines = new() { new() { ProductId = product.Id, Quantity = 1, Price = 1000, DiscountPercent = 10, VatValue = 10 } } };
    var sold = await service.CreateOrderAsync(sale);
    Check(sold.TotalAmount == 990 && sold.CashAmount == 200 && sold.Cheques.Sum(c => c.Amount) == 790 && sold.DueDate == null,
        "mixed payment uses the discounted total including VAT");
    Check(sold.Lines.Single().Profit == -100, "VAT is excluded from sales profit");
    var html = SaleInvoicePrint.Build(sold, null);
    Check(html.Contains("ارزش افزوده") && html.Contains("پس از تخفیف"), "print shows VAT and discounted subtotal");

    // A copy is a detached whitelist, never an edit model or the original payment history.
    var copy = InvoiceCopyTemplate.From(sold);
    Check(copy.Type == TransactionType.Sale && copy.WarehouseId == wh.Id && copy.PartyId == party.Id &&
          copy.Lines.Single().Price == 1000 && copy.Lines.Single().VatValue == 10,
        "sale copy retains commercial details and VAT");
    Check(InvoiceCopyTemplate.From(created).Type == TransactionType.Purchase, "purchase is also copyable");
    foreach (var name in new[] { "Id", "Number", "Date", "Cheques", "Installments", "CashTrackingNumber", "ChequeChargeSnapshot", "SettledAmount" })
        Check(typeof(InvoiceCopyTemplate).GetProperty(name) is null, $"copy cannot carry old {name}");
    Check(typeof(InvoiceCopyLine).GetProperty("Id") is null, "copy lines cannot carry old line IDs");
    var serviceCopy = InvoiceCopyTemplate.From(new Order { Lines = new() { new() { IsService = true } } });
    Check(serviceCopy.Lines.Single().IsService, "an unavailable copied service is not displayed as stockable goods");
    var legacyCopy = InvoiceCopyTemplate.From(new Order { Type = TransactionType.Purchase, Lines = new()
    {
        new() { ProductId = product.Id, Quantity = 1.2345m, Price = 123.456m, DiscountPercent = 1.234m, VatValue = 0.123m }
    }}).Lines.Single();
    Check(legacyCopy.Quantity == 1.2345m && legacyCopy.Price == 123.456m &&
          legacyCopy.DiscountPercent == 1.234m && legacyCopy.VatValue == 0.123m,
        "legacy values are not rounded by the copy template");
    sold.Lines.Single().Price = 5;
    Check(copy.Lines.Single().Price == 1000, "copy lines are detached from the source");

    // Exercise actual controller responses against current effective permissions.
    var permissions = new TestEffectivePermissions();
    var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, "123"), new Claim(ClaimTypes.Role, "Admin")
    }, "test"));
    var controller = new OrdersController(service, permissions)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } }
    };
    Check(!(bool)((OkObjectResult)(await controller.ProfitAccess()).Result!).Value!,
        "stale admin claims and shop manage do not grant current profit access");
    var invoiceStockPage = (PagedResult<InvoiceStockItem>)((OkObjectResult)(await controller.WarehouseStock(wh.Id, 1, 1)).Result!).Value!;
    Check(invoiceStockPage.Items.Single() is { Quantity: 1m } && invoiceStockPage.Items.Single().ProductId == product.Id &&
          invoiceStockPage.Items.Single().WarehouseId == wh.Id && invoiceStockPage.TotalCount == 1,
        "invoice stock endpoint returns the selected warehouse quantity with paging");
    var noStockPage = (PagedResult<InvoiceStockItem>)((OkObjectResult)(await controller.WarehouseStock(wh.Id + 1000)).Result!).Value!;
    Check(noStockPage.TotalCount == 0 && noStockPage.Items.Count == 0 &&
          (await controller.WarehouseStock(0)).Result is BadRequestObjectResult,
        "invoice stock endpoint never returns all warehouses for an invalid or empty selection");
    Check(typeof(InvoiceStockItem).GetProperties().Select(p => p.Name).ToHashSet()
          .SetEquals(new[] { "WarehouseId", "ProductId", "Quantity" }),
        "invoice stock endpoint cannot expose cost, price or profit fields");
    var hidden = (Order)((OkObjectResult)(await controller.GetById(sold.Id)).Result!).Value!;
    Check(hidden.TotalProfit is null && hidden.NetProfit is null && hidden.Lines.Single().Profit is null &&
          hidden.Lines.Single().UnitCost is null, "sale detail redacts profit and cost without explicit access");
    var hiddenPage = (PagedResult<Order>)((OkObjectResult)(await controller.GetAll(TransactionType.Sale, null, null, null, null)).Result!).Value!;
    Check(hiddenPage.Items.Single().Lines.Single().Profit is null && hiddenPage.Items.Single().TotalProfit is null,
        "sales list also redacts profit");
    permissions.AllowProfit = true;
    Check((bool)((OkObjectResult)(await controller.ProfitAccess()).Result!).Value!,
        "current explicit profit access is reported to the form");
    var shown = (Order)((OkObjectResult)(await controller.GetById(sold.Id)).Result!).Value!;
    Check(shown.Lines.Single().Profit == -100 && shown.Lines.Single().UnitCost.HasValue,
        "current explicit profit permission restores detail values");
    permissions.AllowProfit = false;
    permissions.LegacyAdmin = true;
    Check((bool)((OkObjectResult)(await controller.ProfitAccess()).Result!).Value!,
        "current legacy admin retains profit access");
    permissions.LegacyAdmin = false;
    var savedWithoutProfit = (Order)((OkObjectResult)(await controller.Create(sale)).Result!).Value!;
    Check(savedWithoutProfit.Lines.Single().Profit is null && savedWithoutProfit.Lines.Single().UnitCost is null,
        "successful creation returns no profit without permission");
    sale.Lines[0].Id = savedWithoutProfit.Lines.Single().Id;
    var updatedWithoutProfit = (Order)((OkObjectResult)(await controller.Update(savedWithoutProfit.Id, sale)).Result!).Value!;
    Check(updatedWithoutProfit.Lines.Single().Profit is null && updatedWithoutProfit.TotalProfit is null,
        "successful edit also returns no profit without permission");
    var beforeFailedCheck = await db.Transactions.CountAsync();
    permissions.ThrowOnRead = true;
    try { await controller.Create(sale); throw new Exception("permission check failure was ignored"); }
    catch (InvalidOperationException ex) when (ex.Message == "permission read failed") { }
    Check(await db.Transactions.CountAsync() == beforeFailedCheck,
        "failed permission read happens before saving, avoiding an ambiguous committed invoice");
    permissions.ThrowOnRead = false;

    var fractional = new OrderCommand { WarehouseId = wh.Id, PartyId = party.Id, Type = TransactionType.Purchase,
        Date = DateTime.Today, PaymentMethod = PaymentMethod.Cash,
        Lines = new() { new() { ProductId = product.Id, Quantity = 1.25m, Price = 1.25m, VatValue = 100 } } };
    var fractionalOrder = await service.CreateOrderAsync(fractional);
    Check(fractionalOrder.Lines.Single().Gross == 1.56m && fractionalOrder.TotalAmount == 3.12m &&
        fractionalOrder.Lines.Single().Total == 3.12m && fractionalOrder.CashAmount == 3.12m,
        "fractional quantities keep line, invoice and payment precision consistent");
    fractional.Lines[0].Price = 1.234m;
    try { await service.CreateOrderAsync(fractional); throw new Exception("taxed price precision was accepted"); }
    catch (InvalidOperationException) { Console.WriteLine("PASS: taxed price beyond storage precision rejected"); }
}

public sealed class TestEffectivePermissions : IEffectivePermissions
{
    public bool AllowProfit { get; set; }
    public bool LegacyAdmin { get; set; }
    public bool ThrowOnRead { get; set; }
    public Task<HashSet<string>> GetAsync(ClaimsPrincipal user)
    {
        if (ThrowOnRead) throw new InvalidOperationException("permission read failed");
        return Task.FromResult(new HashSet<string>(AllowProfit ? new[] { "Orders.ViewProfit" } : new[] { "Shop.Sale.Manage" }, StringComparer.OrdinalIgnoreCase));
    }
    public Task<bool> IsLegacyAdminAsync(ClaimsPrincipal user) => Task.FromResult(LegacyAdmin);
    public Task<bool> CanSeeModuleAsync(ClaimsPrincipal user, string module) => Task.FromResult(true);
    public Task<List<int>> MyRoleIdsAsync(int userId) => Task.FromResult(new List<int>());
}
