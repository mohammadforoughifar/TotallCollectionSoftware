using System.Security.Claims;
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
using Db = Inventory.Api.Data;

static void Check(bool ok, string message)
{
    if (!ok) throw new Exception("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}
static async Task Rejected(Func<Task> action, string reason)
{
    try { await action(); }
    catch (InvalidOperationException) { Console.WriteLine("PASS: " + reason); return; }
    throw new Exception("FAIL: " + reason);
}

await using (var sql = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer("Server=localhost;Database=not-used;Integrated Security=True;TrustServerCertificate=True").Options))
{
    const string migration = "20261106100000_AddShopPricing";
    Check(sql.Database.GetMigrations().Contains(migration), "pricing migration is discoverable");
    var script = sql.GetService<IMigrator>().GenerateScript("20261105100000_AddShopReferrerCommissionRates", migration);
    Check(script.Contains("ShopSalePrices") && script.Contains("ShopPricingPolicies") && script.Contains("OBJECT_ID"),
        "pricing SQL Server script is additive and idempotent");
}

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
await using var db = new AppDbContext(options);
await db.Database.EnsureCreatedAsync();
var category = new Db.ProductCategory { Name = "آزمایش" };
var wh = new Db.Warehouse { Name = "انبار" };
var supplier = new Db.Party { Name = "تأمین", Type = PartyType.Supplier };
var customer = new Db.Party { Name = "مشتری", Type = PartyType.Customer };
var referrer = new Db.Referrer { Name = "معرف", GoodsCommissionPercent = 10m };
db.AddRange(category, wh, supplier, customer, referrer);
await db.SaveChangesAsync();
var referred = new Db.Party { Name = "مشتری معرف‌دار", Type = PartyType.Customer, ReferrerId = referrer.Id };
var goods = new Db.Product { Name = "کالای اصلی", Code = "PRICE-G", Unit = "عدد", CategoryId = category.Id, Category = category.Name };
var missing = new Db.Product { Name = "بدون خرید", Code = "PRICE-M", Unit = "عدد", CategoryId = category.Id, Category = category.Name };
var labor = new Db.Product { Name = "خدمت", Code = "PRICE-S", Unit = "خدمت", IsService = true };
db.AddRange(referred, goods, missing, labor);
await db.SaveChangesAsync();
var settings = new Db.AppSetting { AllowNegativeStock = true };
db.AppSettings.Add(settings);
await db.SaveChangesAsync();
var inventory = new InventoryService(db, NullLogger<InventoryService>.Instance);
var pricing = new ShopPricingService(db);
var purchaseDate = DateTime.Today.AddDays(-10);
var saleDate = purchaseDate.AddDays(1);
OrderCommand Purchase(decimal? newSalePrice = null) => new()
{
    Type = TransactionType.Purchase, Date = purchaseDate, WarehouseId = wh.Id, PartyId = supplier.Id,
    PaymentMethod = PaymentMethod.Credit, DueDate = purchaseDate.AddDays(30),
    Lines = new() { new() { ProductId = goods.Id, Quantity = 10, Price = 100, NewSalePrice = newSalePrice } }
};
OrderCommand Sale(int productId, decimal price, int partyId, decimal discount = 0, decimal vat = 0) => new()
{
    Type = TransactionType.Sale, Date = saleDate, WarehouseId = wh.Id, PartyId = partyId,
    PaymentMethod = PaymentMethod.Credit, DueDate = saleDate.AddDays(30),
    Lines = new() { new() { ProductId = productId, Quantity = 1, Price = price, DiscountPercent = discount, VatValue = vat } }
};

var purchase = await inventory.CreateOrderAsync(Purchase(150), "pricing-test");
Check((await pricing.GetPriceAsync(goods.Id, purchaseDate.AddDays(-1))).Price is null, "no invented early selling price");
Check((await pricing.GetPriceAsync(goods.Id, purchaseDate)).Price == 150, "purchase price takes effect on purchase date");
var oldSale = await inventory.CreateOrderAsync(Sale(goods.Id, 110, customer.Id), "pricing-test");
var adjustment = new ShopPriceAdjustmentRequest { CategoryId = category.Id, Percent = 10, Increase = true, EffectiveDate = saleDate.AddDays(1) };
var preview = await pricing.PreviewAsync(adjustment);
Check(preview.Rows.Single(r => r.ProductId == goods.Id).NewPrice == 165, "group preview uses price as of the effective date");
Check(preview.Rows.Single(r => r.ProductId == missing.Id).NewPrice is null, "unknown baseline is not fabricated");
var apply = new ShopPriceAdjustmentCommand { CategoryId = category.Id, Percent = 10, Increase = true,
    EffectiveDate = adjustment.EffectiveDate, PreviewHash = preview.PreviewHash, OperationId = Guid.NewGuid() };
await pricing.ApplyAsync(apply, "pricing-test");
await pricing.ApplyAsync(apply, "pricing-test");
Check(await db.ShopSalePrices.CountAsync(h => h.OperationId == apply.OperationId) == 1, "retries are idempotent");
Check((await inventory.GetOrderAsync(oldSale.Id))!.Lines.Single().Price == 110, "existing invoice price never changes");

var roundingGroup = new Db.ProductCategory { Name = "گروه گرد کردن" };
db.ProductCategories.Add(roundingGroup);
await db.SaveChangesAsync();
var roundedProduct = new Db.Product { Name = "کالای گرد کردن", Code = "PRICE-R", Unit = "عدد", CategoryId = roundingGroup.Id };
db.Products.Add(roundedProduct);
await db.SaveChangesAsync();
db.ShopSalePrices.Add(new Db.ShopSalePrice { ProductId = roundedProduct.Id, EffectiveDate = purchaseDate, Price = 1005m, Source = "test" });
await db.SaveChangesAsync();
var roundingPreview = await pricing.PreviewAsync(new ShopPriceAdjustmentRequest { CategoryId = roundingGroup.Id,
    Percent = 10m, Increase = true, EffectiveDate = saleDate });
Check(roundingPreview.Rows.Single().NewPrice == 1106m, "automatic percentage price rounds half rial upward");

await pricing.SavePolicyAsync(new ShopPricingPolicyDto { MinimumProfitEnabled = true, MinimumMarkupPercent = 20 }, "manager");
var count = await db.Transactions.CountAsync();
await Rejected(async () => { await inventory.CreateOrderAsync(Sale(goods.Id, 119m, customer.Id), "clerk"); }, "markup below last purchase floor");
Check(await db.Transactions.CountAsync() == count, "rejected sale rolls back atomically");
await Rejected(async () => { await inventory.CreateOrderAsync(Sale(goods.Id, 120, customer.Id, discount: 1), "clerk"); }, "line discount is considered");
var vatSale = await inventory.CreateOrderAsync(Sale(goods.Id, 120, customer.Id, vat: 10), "clerk");
Check(vatSale.Lines.Single().Price == 120 && vatSale.TotalAmount == 132, "VAT is excluded from the minimum profit");
await Rejected(async () => { await inventory.CreateOrderAsync(Sale(goods.Id, 122m, referred.Id), "clerk"); }, "referrer commission cannot consume minimum markup");
var commissionSale = await inventory.CreateOrderAsync(Sale(goods.Id, 123m, referred.Id), "clerk");
Check(commissionSale.Lines.Single().Price == 123m, "commission-adjusted boundary passes");
await Rejected(async () => { await inventory.CreateOrderAsync(Sale(missing.Id, 999, customer.Id), "clerk"); }, "missing last purchase blocks goods sale");
var serviceSale = await inventory.CreateOrderAsync(Sale(labor.Id, 50, customer.Id), "clerk");
Check(serviceSale.Lines.Single().Price == 50, "services are excluded");

var edit = Sale(goods.Id, 110, customer.Id);
edit.Lines[0].Id = oldSale.Lines.Single().Id;
var kept = await inventory.UpdateOrderAsync(oldSale.Id, edit, "manager");
Check(kept.Lines.Single().Price == 110, "unchanged historical price remains editable without repricing");
edit.Lines[0].Id = kept.Lines.Single().Id;
edit.Lines[0].Price = 109;
await Rejected(async () => { await inventory.UpdateOrderAsync(oldSale.Id, edit, "manager"); }, "changed old line must meet current rule");
await Rejected(async () => { await inventory.DeleteOrderAsync(purchase.Id, "manager"); }, "purchase source of price history cannot be deleted");

// A shop clerk cannot submit a backdated invoice or set sale price on purchase through HTTP.
var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "123") }, "test"));
var controller = new OrdersController(inventory, new ClerkPermissions())
{ ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } } };
Check((await controller.Create(Purchase(160))).Result is ObjectResult { StatusCode: 403 }, "purchase price update requires pricing management permission");
Check((await controller.Create(Sale(goods.Id, 200, customer.Id))).Result is ObjectResult { StatusCode: 403 }, "backdated sale requires pricing management permission");

// Same connection, distinct active companies and group names.
var accessOne = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
accessOne.HttpContext.Items["ActiveCompanyId"] = 11;
var accessTwo = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
accessTwo.HttpContext.Items["ActiveCompanyId"] = 22;
await using var dbOne = new AppDbContext(options, accessOne);
await using var dbTwo = new AppDbContext(options, accessTwo);
var policyOne = new ShopPricingService(dbOne);
var policyTwo = new ShopPricingService(dbTwo);
await policyOne.SavePolicyAsync(new ShopPricingPolicyDto { MinimumProfitEnabled = true, MinimumMarkupPercent = 35 }, "one");
Check((await policyOne.GetPolicyAsync()).MinimumMarkupPercent == 35 && !(await policyTwo.GetPolicyAsync()).MinimumProfitEnabled,
    "pricing policy is isolated per company");
Console.WriteLine("PASS: shop pricing integration probe");

public sealed class ClerkPermissions : IEffectivePermissions
{
    public Task<HashSet<string>> GetAsync(ClaimsPrincipal user) => Task.FromResult(new HashSet<string> { "Shop.Sale.Manage", "Shop.Purchase.Manage" });
    public Task<bool> IsLegacyAdminAsync(ClaimsPrincipal user) => Task.FromResult(false);
    public Task<bool> CanSeeModuleAsync(ClaimsPrincipal user, string module) => Task.FromResult(true);
    public Task<List<int>> MyRoleIdsAsync(int userId) => Task.FromResult(new List<int>());
}
