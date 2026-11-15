using System.Security.Claims;
using Db = Inventory.Api.Data;
using Inventory.Api.Controllers;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Core;
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

// Old SQLite databases get nullable columns, not invented historical rates.
await using (var connection = new SqliteConnection("Data Source=:memory:"))
{
    await connection.OpenAsync();
    await using var cmd = connection.CreateCommand();
    cmd.CommandText = "CREATE TABLE Transactions (Id INTEGER PRIMARY KEY); INSERT INTO Transactions VALUES(1);";
    await cmd.ExecuteNonQueryAsync();
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    await ShopReferrerCommissionSchemaV1.EnsureAsync(db);
    await ShopReferrerCommissionSchemaV1.EnsureAsync(db);
    cmd.CommandText = "SELECT ReferrerGoodsPercentSnapshot, ReferrerServicePercentSnapshot FROM Transactions WHERE Id=1";
    await using var reader = await cmd.ExecuteReaderAsync();
    Check(await reader.ReadAsync() && reader.IsDBNull(0) && reader.IsDBNull(1),
        "repeat schema repair preserves unknown legacy rates");
}

await using (var sql = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer("Server=localhost;Database=not-used;Integrated Security=True;TrustServerCertificate=True").Options))
{
    const string migration = "20261105100000_AddShopReferrerCommissionRates";
    Check(sql.Database.GetMigrations().Contains(migration), "commission migration is discoverable");
    var script = sql.GetService<IMigrator>().GenerateScript("20261105090000_AddShopCreditReceipts", migration);
    Check(script.Contains("COL_LENGTH") && script.Contains("ReferrerGoodsPercentSnapshot") &&
          script.Contains("ReferrerServicePercentSnapshot") && script.Contains("decimal(18,6)"),
        "SQL Server script adds nullable rate snapshots idempotently");
}

await using (var connection = new SqliteConnection("Data Source=:memory:"))
{
    await connection.OpenAsync();
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    await db.Database.EnsureCreatedAsync();
    var warehouse = new Db.Warehouse { Name = "انبار آزمون" };
    var supplier = new Db.Party { Name = "تأمین‌کننده", Type = PartyType.Supplier };
    var referrer = new Db.Referrer { Name = "معرف", GoodsCommissionPercent = 10m, ServiceCommissionPercent = 20m };
    var goods = new Db.Product { Name = "کالا", Code = "ENH-G", Unit = "عدد", PurchasePrice = 80m };
    var labor = new Db.Product { Name = "خدمت", Code = "ENH-S", Unit = "خدمت", IsService = true };
    db.AddRange(warehouse, supplier, referrer, goods, labor);
    await db.SaveChangesAsync();
    var customer = new Db.Party { Name = "مشتری", Type = PartyType.Customer, ReferrerId = referrer.Id };
    db.Add(customer);
    await db.SaveChangesAsync();
    var service = new InventoryService(db, NullLogger<InventoryService>.Instance);
    var purchaseDate = new DateTime(2026, 5, 1);
    var saleDate = purchaseDate.AddDays(1);
    await service.CreateOrderAsync(new OrderCommand
    {
        Type = TransactionType.Purchase, WarehouseId = warehouse.Id, PartyId = supplier.Id,
        Date = purchaseDate, PaymentMethod = PaymentMethod.Cash,
        Lines = new() { new() { ProductId = goods.Id, Quantity = 10, Price = 80 } }
    }, trackShopCash: false);

    // Unequal cheques are explicitly entered by the user; the shared interest
    // terms add a separate customer debit and do not alter those amounts.
    var plan = InvoiceInstallmentCalculation.CreateInterestOnly(1386m, 5m, 1);
    var saleCommand = new OrderCommand
    {
        Type = TransactionType.Sale, WarehouseId = warehouse.Id, PartyId = customer.Id, Date = saleDate,
        PaymentMethod = PaymentMethod.Cheque,
        Lines = new()
        {
            new() { ProductId = goods.Id, Quantity = 2, Price = 200, DiscountPercent = 10, VatValue = 10 },
            new() { ProductId = labor.Id, Quantity = 1, Price = 1000, DiscountPercent = 10, VatValue = 10 }
        },
        Cheques = new()
        {
            new() { Number = "A", Amount = 500, DueDate = saleDate.AddMonths(1) },
            new() { Number = "B", Amount = 955, DueDate = saleDate.AddMonths(2) }
        },
        ChequeCharge = plan.CreateSaleChequeCharge(1386m, 5m, 30)
    };
    var sale = await service.CreateOrderAsync(saleCommand, "test");
    Check(sale.TotalAmount == 1455m && sale.GoodsAmount == 1386m && sale.ChequeIncomeAmount == 69m &&
          sale.Cheques.Select(c => c.Amount).SequenceEqual(new[] { 500m, 955m }),
        "manual cheque interest increases only customer debit and preserves unequal amounts");
    Check(sale.ReferrerGoodsCommissionPercent == 10m && sale.ReferrerServiceCommissionPercent == 20m &&
          sale.CommissionRatesAreHistorical && !sale.HasEstimatedSaleCost && sale.TotalProfit == 1100m &&
          sale.CommissionAmount == 200m && sale.NetProfit == 900m,
        "goods commission uses positive margin and service commission uses after-discount revenue, excluding VAT");
    Check(sale.Lines.Single(l => l.ProductId == goods.Id).UnitCost == 80m, "goods cost is frozen from reliable purchase history");
    var statement = await service.GetPartyStatementDetailAsync(customer.Id);
    Check(statement.SaleGoodsTotal == 1386m && statement.SaleIssuedDiscountTotal == 140m &&
          statement.SaleChequeIncomeTotal == 69m && statement.ClosingBalance == 1455m &&
          statement.Rows.Single(r => r.Title == "فاکتور فروش").SaleInvoiceDiscount == 140m,
        "customer statement includes discounts and cheque interest, never shop commission");
    Check(statement.Rows.Any(r => r.Title == "تاخیر تادیه چک") && statement.TotalDebit == 1455m,
        "cheque interest is a distinct customer-debit event");

    var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "123") }, "test"));
    var controller = new OrdersController(service, new NoProfitPermissions())
    { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } } };
    var hidden = (Order)((OkObjectResult)(await controller.GetById(sale.Id)).Result!).Value!;
    Check(hidden.TotalProfit is null && hidden.NetProfit is null && hidden.CommissionAmount is null &&
          hidden.ReferrerGoodsCommissionPercent is null && hidden.ReferrerServiceCommissionPercent is null &&
          hidden.Lines.All(l => l.UnitCost is null && l.Profit is null && l.Commission is null),
        "sale API redacts rate, cost, profit and commission without permission");
    var partiesController = new PartiesController(service, new ReportOnlyPermissions(), db)
    { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } } };
    Check((await partiesController.GetPartyStatement(customer.Id)).Result is OkObjectResult,
        "sales-report-only access can print a customer statement without customer management permission");
    Check((await partiesController.GetPartyStatement(supplier.Id)).Result is ObjectResult { StatusCode: 403 },
        "sales-report-only access does not grant supplier statement access");

    referrer.GoodsCommissionPercent = 50m;
    referrer.ServiceCommissionPercent = 50m;
    await db.SaveChangesAsync();
    var stable = (await service.GetOrderAsync(sale.Id))!;
    Check(stable.ReferrerGoodsCommissionPercent == 10m && stable.ReferrerServiceCommissionPercent == 20m &&
          stable.CommissionAmount == 200m, "later referrer rate changes do not reprice a new invoice");
    var productDto = (await service.GetProductAsync(goods.Id))!;
    productDto.IsService = true;
    try { await service.SaveProductAsync(productDto); throw new Exception("product category change was accepted"); }
    catch (InvalidOperationException ex) when (ex.Message.Contains("نوع این کالا/خدمت"))
    { Console.WriteLine("PASS: changing product kind cannot rewrite a saved commission policy"); }

    // Simulate a service invoice with a known internal labor cost: the new
    // service commission still uses 900 of post-discount revenue, not 500 profit.
    var serviceLine = await db.TransactionLines.SingleAsync(l => l.TransactionId == sale.Id && l.ProductId == labor.Id);
    serviceLine.UnitCostSnapshot = 400m;
    await db.SaveChangesAsync();
    stable = (await service.GetOrderAsync(sale.Id))!;
    Check(stable.TotalProfit == 700m && stable.CommissionAmount == 200m && stable.NetProfit == 500m,
        "service commission remains based on revenue even when labor has a cost");
    Check((await service.GetReferrerWalletsAsync(null, "name", false)).Single(r => r.Id == referrer.Id).TotalCommission == 200m,
        "referrer wallet uses the same snapshotted commission");

    var savedEntity = await db.Transactions.SingleAsync(t => t.Id == sale.Id);
    savedEntity.ReferrerServicePercentSnapshot = null;
    await db.SaveChangesAsync();
    var damaged = (await service.GetOrderAsync(sale.Id))!;
    Check(damaged.CommissionAmount is null && damaged.NetProfit is null && !damaged.UsesLegacyCommissionPolicy,
        "partial historical rates are unknown, not silently replaced by current rates");
    try { _ = await service.GetReferrerWalletAsync(referrer.Id); throw new Exception("unknown commission was silently summed as zero"); }
    catch (InvalidOperationException ex) when (ex.Message.Contains("پورسانت"))
    { Console.WriteLine("PASS: wallet rejects an unknown commission instead of summing zero"); }
    var otherReferrer = new Db.Referrer { Name = "معرف دیگر" };
    db.Referrers.Add(otherReferrer);
    await db.SaveChangesAsync();
    Check((await service.GetReferrerWalletAsync(otherReferrer.Id))?.TotalCommission == 0m,
        "an unresolved sale does not block a different referrer's individual dashboard");
    savedEntity.ReferrerServicePercentSnapshot = 20m;
    await db.SaveChangesAsync();

    var goodsLine = stable.Lines.Single(l => l.ProductId == goods.Id);
    var retRequest = new SaleChequeReturnPreviewRequest
    {
        Date = saleDate.AddDays(1), Lines = new() { new() { OriginalLineId = goodsLine.Id, Quantity = 1 } }
    };
    var preview = await service.PreviewSaleChequeReturnAsync(sale.Id, retRequest);
    Check(preview.GoodsBeforeVat == 180m && preview.Vat == 18m && preview.Income == 10m,
        "partial return allocates goods, VAT and cheque interest");
    await service.ReturnSaleChequeOrderAsync(sale.Id, new SaleChequeReturnCommand
    {
        OperationId = Guid.NewGuid(), Date = retRequest.Date, Lines = retRequest.Lines,
        Decision = SaleChequeReturnDecision.ReplaceCheque, ConfirmedTotalCredit = preview.TotalCredit
    }, "test");
    var afterReturn = (await service.GetOrderAsync(sale.Id))!;
    Check(afterReturn.TotalProfit == 600m && afterReturn.CommissionAmount == 190m && afterReturn.NetProfit == 410m,
        "partial goods return reduces margin-based commission without changing service basis");
    var afterStatement = await service.GetPartyStatementDetailAsync(customer.Id);
    Check(afterStatement.SaleGoodsTotal == 1188m && afterStatement.SaleIssuedDiscountTotal == 140m &&
          afterStatement.SaleChequeIncomeTotal == 59m && afterStatement.ClosingBalance == 1247m,
        "statement nets dated returns and labels original issued-invoice discounts separately");
    var period = await service.GetPartyStatementDetailAsync(customer.Id, saleDate.AddDays(1), saleDate.AddDays(1));
    Check(period.OpeningBalance == 1455m && period.ClosingBalance == 1247m && period.TotalCredit == 208m &&
          period.SaleIssuedDiscountTotal == 0m, "period statement carries opening balance and only includes period events");
    try { _ = await service.GetPartyStatementDetailAsync(customer.Id, saleDate.AddDays(2), saleDate); throw new Exception("reversed dates accepted"); }
    catch (InvalidOperationException) { Console.WriteLine("PASS: invalid statement date order rejected"); }
    try { _ = await service.GetPartyStatementSummaryReportAsync(PartyType.Customer, saleDate.AddDays(2), saleDate); throw new Exception("reversed summary dates accepted"); }
    catch (InvalidOperationException) { Console.WriteLine("PASS: invalid summary date order rejected"); }

    // An ordinary credit sale can be edited before any financial activity;
    // changed quantity must not keep a cost from the old FIFO/LIFO quantity.
    var second = new Db.Party { Name = "مشتری دوم", Type = PartyType.Customer, ReferrerId = referrer.Id };
    db.Parties.Add(second); await db.SaveChangesAsync();
    referrer.GoodsCommissionPercent = 10m;
    referrer.ServiceCommissionPercent = 20m;
    await db.SaveChangesAsync();
    var creditCmd = new OrderCommand { Type = TransactionType.Sale, WarehouseId = warehouse.Id, PartyId = second.Id,
        Date = saleDate.AddDays(2), PaymentMethod = PaymentMethod.Credit, DueDate = saleDate.AddMonths(2),
        Lines = new() { new() { ProductId = goods.Id, Quantity = 1, Price = 200 } } };
    var creditSale = await service.CreateOrderAsync(creditCmd, "test");
    referrer.GoodsCommissionPercent = 50m;
    await db.SaveChangesAsync();
    creditCmd.Lines[0].Id = creditSale.Lines.Single().Id;
    creditCmd.Lines[0].Quantity = 2;
    var edited = await service.UpdateOrderAsync(creditSale.Id, creditCmd, "test");
    Check(edited.ReferrerGoodsCommissionPercent == 10m && edited.Lines.Single().UnitCost == 80m &&
          edited.TotalProfit == 240m && edited.CommissionAmount == 24m,
        "quantity edit recalculates reliable cost and preserves the original commission percentage");

    var legacyEntity = await db.Transactions.SingleAsync(t => t.Id == sale.Id);
    legacyEntity.ReferrerGoodsPercentSnapshot = null;
    legacyEntity.ReferrerServicePercentSnapshot = null;
    await db.SaveChangesAsync();
    var legacy = (await service.GetOrderAsync(sale.Id))!;
    Check(legacy.UsesLegacyCommissionPolicy && !legacy.CommissionRatesAreHistorical &&
          legacy.ReferrerGoodsCommissionPercent == 50m && legacy.ReferrerServiceCommissionPercent == 20m &&
          legacy.CommissionAmount == 150m,
        "legacy documents retain old service-profit policy and are marked as using current rates");

    db.InvStocks.Add(new Db.InvStock { ProductId = goods.Id, WarehouseId = warehouse.Id, Quantity = 1, AvgCost = 300, Value = 300 });
    await db.SaveChangesAsync();
    var mixedCmd = new OrderCommand { Type = TransactionType.Sale, WarehouseId = warehouse.Id, PartyId = second.Id,
        Date = saleDate.AddDays(3), PaymentMethod = PaymentMethod.Credit, DueDate = saleDate.AddMonths(2),
        Lines = new() { new() { ProductId = goods.Id, Quantity = 1, Price = 200 } } };
    var mixedLedgerSale = await service.CreateOrderAsync(mixedCmd, "test");
    Check(mixedLedgerSale.HasEstimatedSaleCost && (await db.TransactionLines.SingleAsync(l =>
          l.TransactionId == mixedLedgerSale.Id)).UnitCostSnapshot is null,
        "mixed warehouse ledgers are labelled estimated rather than freezing a guessed cost");
    try { _ = await service.GetReferrerWalletsAsync(null, "name", false); throw new Exception("estimated commission treated as payable"); }
    catch (InvalidOperationException ex) when (ex.Message.Contains("بهای ثبت‌شده"))
    { Console.WriteLine("PASS: new-policy estimated cost is not presented as a definite wallet liability"); }

    var before = await db.Transactions.CountAsync();
    var tiny = new OrderCommand { Type = TransactionType.Sale, WarehouseId = warehouse.Id, PartyId = second.Id,
        Date = saleDate.AddDays(4), PaymentMethod = PaymentMethod.Cheque, DueDate = saleDate.AddMonths(2),
        Lines = new() { new() { ProductId = goods.Id, Quantity = 1, Price = 200 } },
        Cheques = new() { new() { Number = "TINY", Amount = .5m, DueDate = saleDate.AddMonths(1) } },
        ChequeCharge = new SaleChequeChargeInput { CalculatorBaseAmount = 200m, CalculatorRatePercent = 5m,
            CalculatorMonths = 1, AdjustmentRatePercent = 5m, AdjustmentPeriodDays = 30,
            ConfirmedCreditDueDate = saleDate.AddMonths(2) } };
    try { _ = await service.CreateOrderAsync(tiny, "test"); throw new Exception("unclassifiable cheque accepted"); }
    catch (InvalidOperationException ex) when (ex.Message.Contains("نیم ریال"))
    { Console.WriteLine("PASS: cheque-interest registration rejects an unclassifiable tiny cheque"); }
    Check(await db.Transactions.CountAsync() == before, "failed tiny-cheque registration rolls back the entire invoice");
}

public sealed class NoProfitPermissions : IEffectivePermissions
{
    public Task<HashSet<string>> GetAsync(ClaimsPrincipal user) =>
        Task.FromResult(new HashSet<string>(new[] { "Shop.Sale.Manage" }, StringComparer.OrdinalIgnoreCase));
    public Task<bool> IsLegacyAdminAsync(ClaimsPrincipal user) => Task.FromResult(false);
    public Task<bool> CanSeeModuleAsync(ClaimsPrincipal user, string module) => Task.FromResult(true);
    public Task<List<int>> MyRoleIdsAsync(int userId) => Task.FromResult(new List<int>());
}

public sealed class ReportOnlyPermissions : IEffectivePermissions
{
    public Task<HashSet<string>> GetAsync(ClaimsPrincipal user) =>
        Task.FromResult(new HashSet<string>(new[] { "Shop.OrderReports.View" }, StringComparer.OrdinalIgnoreCase));
    public Task<bool> IsLegacyAdminAsync(ClaimsPrincipal user) => Task.FromResult(false);
    public Task<bool> CanSeeModuleAsync(ClaimsPrincipal user, string module) => Task.FromResult(true);
    public Task<List<int>> MyRoleIdsAsync(int userId) => Task.FromResult(new List<int>());
}
