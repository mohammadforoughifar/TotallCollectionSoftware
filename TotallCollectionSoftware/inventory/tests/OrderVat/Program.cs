using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Client.Pages.Orders;
using Inventory.Shared;
using Inventory.Shared.Dtos;
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
