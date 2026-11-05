using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Services.Moadian;
using Inventory.Shared.Dtos.Moadian;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static Inventory.Shared.Dtos.Moadian.VmInvoiceItems;

// Runs the real service/EF against an isolated in-memory SQLite database.
// No destination SQL, TaxCollect SDK network calls, or production data involved.
await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var reject = new RejectInvoiceLines();
await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite(connection).AddInterceptors(reject)
    .LogTo(Console.WriteLine, Microsoft.Extensions.Logging.LogLevel.Error).Options);
await db.Database.EnsureCreatedAsync();
db.SystemCompanies.AddRange(new SystemCompany { Id = 1, Name = "Test company", IsActive = true },
    new SystemCompany { Id = 2, Name = "Other company", IsActive = true });
db.CPInfos.AddRange(new CpInfo { Id = 1, CompanyId = 1, PersianName = "Provider 1" },
    new CpInfo { Id = 2, CompanyId = 2, PersianName = "Foreign provider" },
    new CpInfo { Id = 3, CompanyId = 1, PersianName = "Provider 3" });
db.FiscalYears.Add(new FiscalYear { Id = 1, CpInfo = 1, Title = "Test fiscal year", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), CreatedDate = new DateTime(2026, 1, 1) });
const string duplicate = "2720000005833";
db.GoodsOrServices.AddRange(
    Good(10, 1, duplicate), Good(11, 1, duplicate), Good(12, 2, duplicate), Good(13, 1, duplicate, true),
    Good(20, 1, "UNIQUE-LOCAL"), Good(21, 2, "UNIQUE-LOCAL"), Good(22, 1, "UNIQUE-LOCAL", true),
    Good(30, 2, "FOREIGN-ONLY"), Good(31, 1, "DELETED-ONLY", true), Good(40, 1, null));
db.Customers.AddRange(new Customer { Id = 1, CpInfoId = 1, Name = "Local customer" },
    new Customer { Id = 2, CpInfoId = 2, Name = "Foreign customer" },
    new Customer { Id = 3, CpInfoId = 1, Name = "Deleted customer", IsDelete = true });
await db.SaveChangesAsync();
var http = new DefaultHttpContext();
http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "1"),
    new Claim(ClaimTypes.Name, "local-test"), new Claim(ClaimTypes.Role, "Admin") }, "test"));
http.Request.Headers["X-Company-Id"] = "1";
var service = new ConnectService(db, new HttpContextAccessor { HttpContext = http });

// The exact reported code can occur on multiple products, providers and deleted rows.
var repeated = Form(Row(10, duplicate), Row(11, duplicate), Row(10, duplicate));
var created = await service.SabtInvoiceAsync(repeated);
Check(created.Success && created.Status == 201, "reported duplicate tax code no longer crashes invoice creation");
var saved = await db.InvoiceItems.AsNoTracking().Where(i => i.InvoiceId == repeated.Id).OrderBy(i => i.Id).ToListAsync();
Check(saved.Select(i => i.GoodsOrServicesId).SequenceEqual(new[] { 10, 11, 10 }), "selected product IDs and repeated invoice rows remain distinct");
Check(saved.All(i => i.UniqueIdentifier == duplicate), "repeated tax identifiers are preserved, not merged or rewritten");
var originalCount = await db.Invoices.CountAsync();
await Bad(Form(Row(0, duplicate)), "legacy code-only request rejects ambiguous active products");
await Bad(Form(Row(999, duplicate)), "invalid explicit ID never falls back to a matching tax code");
await Bad(Form(Row(12, duplicate)), "foreign-provider ID is rejected even with a matching local code");
await Bad(Form(Row(13, duplicate)), "soft-deleted explicit ID is rejected even with a matching active code");
await Bad(Form(Row(10, "WRONG-CODE")), "ID/code mismatch is rejected");
await Bad(Form(Row(0, "FOREIGN-ONLY")), "legacy fallback cannot resolve a foreign-only code");
await Bad(Form(Row(0, "DELETED-ONLY")), "legacy fallback cannot resolve a deleted-only code");
await Bad(Form(Row(0, "MISSING")), "missing tax code receives a validation error");
await Bad(Form(Row(-10, duplicate)), "negative IDs are invalid, not legacy fallback");
await Bad(Form(Row(40, null)), "selected catalog row without a tax identifier is rejected");
await Bad(Form(), "empty invoice lines are rejected");
var badProvider = Form(Row(10, duplicate)); badProvider.CPInfoId = 0;
await Bad(badProvider, "missing provider is rejected");
Check(await db.Invoices.CountAsync() == originalCount, "invalid goods never write an invoice header");

var legacy = Form(Row(0, "UNIQUE-LOCAL"));
Check((await service.SabtInvoiceAsync(legacy)).Success, "legacy code-only caller resolves a unique active product in its provider");
Check((await db.InvoiceItems.AsNoTracking().SingleAsync(i => i.InvoiceId == legacy.Id)).GoodsOrServicesId == 20,
    "foreign/deleted rows do not make a unique local fallback ambiguous");
var noCode = Form(Row(10, null));
Check((await service.SabtInvoiceAsync(noCode)).Success && noCode.InvoiceItems[0].UniqueIdentifier == duplicate,
    "explicit valid ID supplies its canonical tax code when omitted");

var disclosed = Form(Row(10, duplicate)); disclosed.TypePattern = "نوع اول"; disclosed.CustomerId = 1;
Check((await service.SabtInvoiceAsync(disclosed)).Success, "type-1 invoice accepts its provider's active customer");
disclosed = Form(Row(10, duplicate)); disclosed.TypePattern = "نوع اول"; disclosed.CustomerId = 2;
await Bad(disclosed, "type-1 invoice rejects a foreign-provider customer");
disclosed = Form(Row(10, duplicate)); disclosed.TypePattern = "نوع اول"; disclosed.CustomerId = 3;
await Bad(disclosed, "type-1 invoice rejects a deleted customer");

// Invalid edits must not delete old lines or mutate the existing header.
var update = Form(Row(12, duplicate)); update.IsNew = false; update.Id = repeated.Id; update.Description = "must not save";
await Bad(update, "update applies the same company/active-product checks");
Check((await db.InvoiceItems.AsNoTracking().Where(i => i.InvoiceId == repeated.Id).ToListAsync()).Count == 3
    && (await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == repeated.Id)).Description == "original",
    "invalid update preserves old header and all old lines");
var foreignInvoice = new Invoice { CpInfo = 3, DateOrder = new DateOnly(2026, 11, 5), Description = "provider 3" };
db.Invoices.Add(foreignInvoice); await db.SaveChangesAsync();
update.Id = foreignInvoice.Id; update.InvoiceItems = new() { Row(10, duplicate) };
var forbiddenEdit = await service.SabtInvoiceAsync(update);
Check(!forbiddenEdit.Success && forbiddenEdit.Status == 404, "valid provider header cannot edit another provider's invoice");

update.Id = repeated.Id; update.Description = "updated";
update.InvoiceItems = new() { Row(11, duplicate), Row(10, duplicate) };
Check((await service.SabtInvoiceAsync(update)).Success, "valid edit accepts two products with the same tax code");
saved = await db.InvoiceItems.AsNoTracking().Where(i => i.InvoiceId == repeated.Id).OrderBy(i => i.Id).ToListAsync();
Check(saved.Select(i => i.GoodsOrServicesId).SequenceEqual(new[] { 11, 10 }), "valid edit keeps exact selected IDs");

// Simulate failure on line persistence after a header has been inserted.
db.ChangeTracker.Clear();
var beforeHeaders = await db.Invoices.CountAsync(); var beforeLines = await db.InvoiceItems.CountAsync();
reject.Enabled = true;
var failed = await service.SabtInvoiceAsync(Form(Row(10, duplicate)));
reject.Enabled = false; db.ChangeTracker.Clear();
Check(!failed.Success && failed.Status == 500 && await db.Invoices.CountAsync() == beforeHeaders
    && await db.InvoiceItems.CountAsync() == beforeLines, "failed create rolls back both header and lines");
reject.Enabled = true;
update.Description = "must roll back";
failed = await service.SabtInvoiceAsync(update);
reject.Enabled = false; db.ChangeTracker.Clear();
Check(!failed.Success && failed.Status == 500
    && (await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == repeated.Id)).Description == "updated"
    && await db.InvoiceItems.CountAsync() == beforeLines, "failed edit reports failure and preserves persisted data");
Console.WriteLine("PASS: all Moadian invoice-goods regression checks completed; SQLite only, no real SQL Server/tax submission.");

GoodsOrService Good(int id, int cpInfo, string? code, bool deleted = false) => new()
    { Id = id, CpInfo = cpInfo, Name = "Test product " + id, UniqueIdentifier = code, IsDelete = deleted };
VmInvoiceItems Row(int id, string? code) => new()
    { GoodsOrServicesId = id, UniqueIdentifier = code, Count = 1, PriceUnit = 100, ValueAddedPercentage = 10, Discount = 0 };
InvoiceVM Form(params VmInvoiceItems[] rows) => new()
{
    IsNew = true, CPInfoId = 1, TypePattern = "نوع دوم", PaymentType = "نقدی", StatusSentToSystem = "0",
    DateOrder = new DateOnly(2026, 11, 5), FiscalYearId = "1", Description = "original", InvoiceItems = new(rows)
};
async Task Bad(InvoiceVM form, string description)
{
    var result = await service.SabtInvoiceAsync(form);
    Check(!result.Success && result.Status == 400 && !string.IsNullOrWhiteSpace(result.Message), description);
    db.ChangeTracker.Clear();
}
void Check(bool ok, string description)
{
    if (!ok) throw new Exception(description);
    Console.WriteLine("PASS: " + description);
}
sealed class RejectInvoiceLines : SaveChangesInterceptor
{
    public bool Enabled { get; set; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Enabled && eventData.Context!.ChangeTracker.Entries<InvoiceItem>().Any(e =>
            e.State is EntityState.Added or EntityState.Deleted or EntityState.Modified))
            throw new InvalidOperationException("Simulated invoice line persistence failure");
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
