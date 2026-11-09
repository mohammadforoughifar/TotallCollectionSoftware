using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Repairs;
using Inventory.Api.Services.ItAssets;
using Inventory.Api.Services.Core;
using Inventory.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using DbParty = Inventory.Api.Data.Party;

static void Check(bool ok, string message)
{
    if (!ok) throw new Exception("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
await using (var setup = new AppDbContext(options))
{
    await setup.Database.EnsureCreatedAsync();
    setup.SystemCompanies.AddRange(new SystemCompany { Id = 1, Name = "اول" }, new SystemCompany { Id = 2, Name = "دوم" });
    setup.CompanySmsSettings.Add(new CompanySmsSetting { CompanyId = 1, ModuleKey = SmsModuleCatalog.ShopRepairs,
        Enabled = true, UpdatedAtUtc = DateTime.UtcNow });
    // A different module's permission must never authorize repairs for company 2.
    setup.CompanySmsSettings.Add(new CompanySmsSetting { CompanyId = 2, ModuleKey = "Shop.Future",
        Enabled = true, UpdatedAtUtc = DateTime.UtcNow });
    await setup.SaveChangesAsync();
}
var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
http.HttpContext.Items["ActiveCompanyId"] = 1;
await using var db = new AppDbContext(options, http);
await using var workerDb = new AppDbContext(options); // no HTTP/tenant filter, like the real worker
var gateway = new FakeSmsGateway();
var smsOptions = Options.Create(new KavenegarSmsOptions());
var repairs = new RepairService(db, new InventoryService(db, NullLogger<InventoryService>.Instance), gateway, smsOptions);
var worker = new RepairSmsDispatcher(workerDb, gateway, smsOptions);
Check((await CompanySmsServicePolicy.GetEnabledRevisionAsync(workerDb, 2, SmsModuleCatalog.ShopRepairs)) is null &&
    (await CompanySmsServicePolicy.GetEnabledRevisionAsync(workerDb, 2, "Shop.Future")) is null,
    "a different or unknown module never authorizes repair SMS");
var customer = new DbParty { Name = "مشتری", Type = PartyType.Customer, Mobile = "۰۹۱۲-۳۴۵-۶۷۸۹", Phone = "02111111111" };
var noMobile = new DbParty { Name = "بدون موبایل", Type = PartyType.Customer, Phone = "02122222222" };
db.Parties.AddRange(customer, noMobile);
await db.SaveChangesAsync();
RepairOrderDto NewOrder(DbParty party) => new() { PartyId = party.Id, DeviceType = "لپ‌تاپ", ReceivedAt = DateTime.Now };

var order = await repairs.SaveRepairAsync(NewOrder(customer));
Check(order.SmsNotifications.Single().Status == RepairSmsStatus.Pending && gateway.Sent.Count == 0,
    "new acceptance and its event commit before any network call");
await worker.ProcessOneAsync();
order = (await repairs.GetRepairAsync(order.Id))!;
Check(order.SmsNotifications.Single().Status == RepairSmsStatus.Accepted && gateway.Sent.Count == 1 &&
    gateway.Sent[0].Recipient == "09123456789" && gateway.Sent[0].Token == order.Number[3..] &&
    gateway.Sent[0].CompanyName == "اول" && gateway.Sent[0].Template == "RepairAccept",
    "registration uses the same customer's normalized mobile and acceptance number; accepted is not delivered");
order.Note = "ordinary edit";
order = await repairs.SaveRepairAsync(order);
Check(order.SmsNotifications.Count == 1 && gateway.Sent.Count == 1, "ordinary edit produces no event");
order.Status = RepairStatus.Ready;
order = await repairs.SaveRepairAsync(order);
Check(order.SmsNotifications.Count == 2 && order.SmsNotifications.Last().EventType == RepairSmsEventType.Ready,
    "editing the order to Ready stages the ready event");
await repairs.SetStatusAsync(order.Id, RepairStatus.Ready);
await repairs.SetStatusAsync(order.Id, RepairStatus.InProgress);
await repairs.SetStatusAsync(order.Id, RepairStatus.Ready);
Check(await db.RepairSmsNotifications.CountAsync(n => n.RepairOrderId == order.Id) == 2,
    "repeated status calls and leaving/returning to Ready do not duplicate the event");
await worker.ProcessOneAsync();
Check(gateway.Sent.Count == 2 && gateway.Sent[1].Token == order.Number[3..] && gateway.Sent[1].Template == "RepairReady",
    "ready event sends once and contains the acceptance number");

var viaEndpoint = await repairs.SaveRepairAsync(NewOrder(customer));
await worker.ProcessOneAsync();
viaEndpoint = await repairs.SetStatusAsync(viaEndpoint.Id, RepairStatus.Ready);
Check(viaEndpoint.SmsNotifications.Any(n => n.EventType == RepairSmsEventType.Ready && n.Status == RepairSmsStatus.Pending),
    "dedicated status endpoint stages Ready too");
await worker.ProcessOneAsync();

var missing = await repairs.SaveRepairAsync(NewOrder(noMobile));
Check(missing.Id > 0 && missing.SmsNotifications.Single().Status == RepairSmsStatus.InvalidMobile &&
    await db.RepairOrders.AnyAsync(r => r.Id == missing.Id), "invalid/missing mobile is visible and does not block repair");
Check(!await worker.ProcessOneAsync(), "a landline is not substituted or sent");

gateway.IsEnabled = false;
var disabled = await repairs.SaveRepairAsync(NewOrder(customer));
Check(disabled.SmsNotifications.Single().Status == RepairSmsStatus.Disabled, "disabled event is terminal");
gateway.IsEnabled = true;
Check(!await worker.ProcessOneAsync(), "enabling later does not send an event created while disabled");

gateway.Result = new(KavenegarSmsSendState.Unknown);
var uncertain = await repairs.SaveRepairAsync(NewOrder(customer));
var before = gateway.Sent.Count;
await worker.ProcessOneAsync();
Check((await repairs.GetRepairAsync(uncertain.Id))!.SmsNotifications.Single().Status == RepairSmsStatus.Unknown &&
    !await worker.ProcessOneAsync() && gateway.Sent.Count == before + 1, "unknown outcome is persisted and never retried");

gateway.Result = new(KavenegarSmsSendState.Accepted, 42, 1, 200);
var abandoned = await repairs.SaveRepairAsync(NewOrder(customer));
await workerDb.RepairSmsNotifications.Where(n => n.RepairOrderId == abandoned.Id)
    .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, RepairSmsStatus.Sending)
        .SetProperty(n => n.AttemptStartedAtUtc, (DateTime?)DateTime.UtcNow.AddMinutes(-11)));
await worker.MarkAbandonedUnknownAsync(DateTime.UtcNow);
Check((await repairs.GetRepairAsync(abandoned.Id))!.SmsNotifications.Single().Status == RepairSmsStatus.Unknown &&
    !await worker.ProcessOneAsync(), "crashed in-flight claim becomes unknown, never pending");

var changed = await repairs.SaveRepairAsync(NewOrder(customer));
customer.Mobile = "09120000000";
await db.SaveChangesAsync();
before = gateway.Sent.Count;
await worker.ProcessOneAsync();
Check((await repairs.GetRepairAsync(changed.Id))!.SmsNotifications.Single().Status == RepairSmsStatus.Cancelled &&
    gateway.Sent.Count == before, "changed customer mobile cancels the snapshot rather than messaging a stale number");

var mismatch = await repairs.SaveRepairAsync(NewOrder(customer));
await workerDb.RepairSmsNotifications.Where(n => n.RepairOrderId == mismatch.Id)
    .ExecuteUpdateAsync(s => s.SetProperty(n => n.CompanyId, (int?)2));
before = gateway.Sent.Count;
await worker.ProcessOneAsync();
Check(await workerDb.RepairSmsNotifications.AnyAsync(n => n.RepairOrderId == mismatch.Id && n.Status == RepairSmsStatus.Cancelled) &&
    gateway.Sent.Count == before, "unfiltered worker rejects an event whose company does not match the repair/customer");

var simultaneous = await repairs.SaveRepairAsync(NewOrder(customer));
gateway.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
gateway.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
before = gateway.Sent.Count;
var first = worker.ProcessOneAsync();
await gateway.Entered.Task;
await using var secondDb = new AppDbContext(options);
var second = new RepairSmsDispatcher(secondDb, gateway, smsOptions);
Check(!await second.ProcessOneAsync(), "another worker cannot claim an already-sending event");
gateway.Hold.SetResult(true);
await first;
Check(gateway.Sent.Count == before + 1 &&
    (await repairs.GetRepairAsync(simultaneous.Id))!.SmsNotifications.Single().Status == RepairSmsStatus.Accepted,
    "one concurrent claim sends exactly once");

Check(RepairSmsPolicy.NormalizeMobile("02112345678") is null &&
    RepairSmsPolicy.NormalizeMobile("0912,0913") is null &&
    RepairSmsPolicy.NormalizeMobile("+98 912 345 6789") == "09123456789", "mobile normalization rejects landlines and lists");
// Two companies, scoped settings API, module revocation, and no resurrection.
var http2 = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
http2.HttpContext.Items["ActiveCompanyId"] = 2;
await using var db2 = new AppDbContext(options, http2);
var customer2 = new DbParty { Name = "مشتری دوم", Type = PartyType.Customer, Mobile = "09120000002" };
db2.Parties.Add(customer2); await db2.SaveChangesAsync();
var repairs2 = new RepairService(db2, new InventoryService(db2, NullLogger<InventoryService>.Instance), gateway, smsOptions);
var initiallyDisabled = await repairs2.SaveRepairAsync(NewOrder(customer2));
Check(initiallyDisabled.SmsNotifications.Single().Status == RepairSmsStatus.ModuleDisabled,
    "another module enabled does not queue company 2 repairs");
db2.CompanySmsSettings.Add(new CompanySmsSetting { CompanyId = 2, ModuleKey = SmsModuleCatalog.ShopRepairs,
    Enabled = true, UpdatedAtUtc = DateTime.UtcNow });
await db2.SaveChangesAsync();
Check(!await worker.ProcessOneAsync(), "enabling a company later does not resurrect a disabled event");
var queued1 = await repairs.SaveRepairAsync(NewOrder(customer));
var queued2 = await repairs2.SaveRepairAsync(NewOrder(customer2));
Check(queued1.SmsNotifications.Single().Status == RepairSmsStatus.Pending && queued2.SmsNotifications.Single().Status == RepairSmsStatus.Pending,
    "each enabled company's new event is independently queued");
http.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "10"),
    new Claim(ClaimTypes.Name, "settings-test") }, "test"));
var handler = new RepairSmsRevocationHandler(db);
var settings = new CompanySmsSettingsController(db, new TestPermissions("SmsSettings.Read", "SmsSettings.Update"),
    new ICompanySmsRevocationHandler[] { handler }) { ControllerContext = new ControllerContext { HttpContext = http.HttpContext } };
SaveCompanySmsSettingsDto Choice(int id, bool enabled) => new() { CompanyId = id,
    Modules = new() { new SaveCompanySmsModuleDto { Key = SmsModuleCatalog.ShopRepairs, Enabled = enabled } } };
Check(await settings.Save(Choice(2, false), CancellationToken.None) is ConflictObjectResult,
    "payload company cannot override the authenticated active company");
Check(await workerDb.CompanySmsSettings.AsNoTracking().AnyAsync(x => x.CompanyId == 2 && x.ModuleKey == SmsModuleCatalog.ShopRepairs && x.Enabled),
    "cross-company settings attempt changes nothing");
var readOnly = new CompanySmsSettingsController(db, new TestPermissions("SmsSettings.Read"),
    new ICompanySmsRevocationHandler[] { handler }) { ControllerContext = new ControllerContext { HttpContext = http.HttpContext } };
Check(await readOnly.Get(CancellationToken.None) is OkObjectResult &&
    await readOnly.Save(Choice(1, false), CancellationToken.None) is ForbidResult,
    "read-only access cannot change settings");
Check(await settings.Save(Choice(1, false), CancellationToken.None) is OkObjectResult,
    "authorized shop user can revoke the active company's repair module without ERP");
Check(await workerDb.RepairSmsNotifications.AsNoTracking().AnyAsync(n => n.RepairOrderId == queued1.Id &&
    n.Status == RepairSmsStatus.ModuleDisabled && n.Recipient == null) &&
    await workerDb.RepairSmsNotifications.AsNoTracking().AnyAsync(n => n.RepairOrderId == queued2.Id && n.Status == RepairSmsStatus.Pending),
    "revocation cancels only that company's pending repair messages");
Check(await settings.Save(Choice(1, true), CancellationToken.None) is OkObjectResult,
    "the company can re-enable the module");
before = gateway.Sent.Count;
await worker.ProcessOneAsync();
Check(gateway.Sent.Count == before + 1 && gateway.Sent[^1].CompanyName == "دوم" &&
    gateway.Sent[^1].Token == queued2.Number[3..] && !await worker.ProcessOneAsync(),
    "re-enabling does not resurrect company 1's event; company 2 uses its own name and number");
await workerDb.SystemCompanies.Where(c => c.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(c => c.Name, new string('ا', 101)));
var invalidCompany = await repairs.SaveRepairAsync(NewOrder(customer));
Check(invalidCompany.Id > 0 && invalidCompany.SmsNotifications.Single().Status == RepairSmsStatus.InvalidCompanyName,
    "overlong company name produces a visible warning without blocking repair");
await workerDb.SystemCompanies.Where(c => c.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(c => c.Name, "اول"));
var legacyNumber = await repairs.SaveRepairAsync(NewOrder(customer));
await worker.ProcessOneAsync();
await workerDb.RepairOrders.Where(r => r.Id == legacyNumber.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Number, "OLD-123"));
db.ChangeTracker.Clear();
var legacyReady = await repairs.SetStatusAsync(legacyNumber.Id, RepairStatus.Ready);
Check(legacyReady.SmsNotifications.Any(n => n.EventType == RepairSmsEventType.Ready && n.Status == RepairSmsStatus.InvalidRepairNumber),
    "legacy number incompatible with literal RP prefix is warned, never silently altered");

Console.WriteLine("All offline repair SMS checks passed.");

sealed class FakeSmsGateway : IKavenegarSmsGateway
{
    public bool IsEnabled { get; set; } = true;
    public bool IsConfigured { get; set; } = true;
    public bool IsLookupConfigured { get; set; } = true;
    public KavenegarSmsSendResult Result { get; set; } = new(KavenegarSmsSendState.Accepted, 42, 1, 200);
    public List<(string Recipient, string Token, string CompanyName, string Template)> Sent { get; } = new();
    public TaskCompletionSource<bool>? Hold { get; set; }
    public TaskCompletionSource<bool>? Entered { get; set; }
    public Task<KavenegarSmsSendResult> SendAsync(string recipient, string message) =>
        throw new InvalidOperationException("Repair SMS must use lookup, not ordinary send.");
    public async Task<KavenegarSmsSendResult> SendLookupAsync(string recipient, string token, string companyName, string template)
    {
        Sent.Add((recipient, token, companyName, template));
        Entered?.TrySetResult(true);
        if (Hold is not null) await Hold.Task;
        return Result;
    }
}

sealed class TestPermissions(params string[] permissions) : IEffectivePermissions
{
    public Task<HashSet<string>> GetAsync(ClaimsPrincipal user) => Task.FromResult(permissions.ToHashSet(StringComparer.OrdinalIgnoreCase));
    public Task<bool> IsLegacyAdminAsync(ClaimsPrincipal user) => Task.FromResult(false);
    public Task<bool> CanSeeModuleAsync(ClaimsPrincipal user, string module) => Task.FromResult(false);
    public Task<List<int>> MyRoleIdsAsync(int userId) => Task.FromResult(new List<int>());
}
