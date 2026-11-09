using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Repairs;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

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
    await setup.SaveChangesAsync();
}
var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
http.HttpContext.Items["ActiveCompanyId"] = 1;
await using var db = new AppDbContext(options, http);
await using var workerDb = new AppDbContext(options); // no HTTP/tenant filter, like the real worker
var gateway = new FakeSmsGateway();
var repairs = new RepairService(db, new InventoryService(db, NullLogger<InventoryService>.Instance), gateway);
var worker = new RepairSmsDispatcher(workerDb, gateway);
var customer = new Party { Name = "مشتری", Type = PartyType.Customer, Mobile = "۰۹۱۲-۳۴۵-۶۷۸۹", Phone = "02111111111" };
var noMobile = new Party { Name = "بدون موبایل", Type = PartyType.Customer, Phone = "02122222222" };
db.Parties.AddRange(customer, noMobile);
await db.SaveChangesAsync();
RepairOrderDto NewOrder(Party party) => new() { PartyId = party.Id, DeviceType = "لپ‌تاپ", ReceivedAt = DateTime.Now };

var order = await repairs.SaveRepairAsync(NewOrder(customer));
Check(order.SmsNotifications.Single().Status == RepairSmsStatus.Pending && gateway.Sent.Count == 0,
    "new acceptance and its event commit before any network call");
await worker.ProcessOneAsync();
order = (await repairs.GetRepairAsync(order.Id))!;
Check(order.SmsNotifications.Single().Status == RepairSmsStatus.Accepted && gateway.Sent.Count == 1 &&
    gateway.Sent[0].Recipient == "09123456789" && gateway.Sent[0].Message.Contains(order.Number),
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
Check(gateway.Sent.Count == 2 && gateway.Sent[1].Message.Contains(order.Number) && gateway.Sent[1].Message.Contains("آماده تحویل"),
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
var second = new RepairSmsDispatcher(secondDb, gateway);
Check(!await second.ProcessOneAsync(), "another worker cannot claim an already-sending event");
gateway.Hold.SetResult(true);
await first;
Check(gateway.Sent.Count == before + 1 &&
    (await repairs.GetRepairAsync(simultaneous.Id))!.SmsNotifications.Single().Status == RepairSmsStatus.Accepted,
    "one concurrent claim sends exactly once");

Check(RepairSmsPolicy.NormalizeMobile("02112345678") is null &&
    RepairSmsPolicy.NormalizeMobile("0912,0913") is null &&
    RepairSmsPolicy.NormalizeMobile("+98 912 345 6789") == "09123456789", "mobile normalization rejects landlines and lists");
Console.WriteLine("All offline repair SMS checks passed.");

sealed class FakeSmsGateway : IKavenegarSmsGateway
{
    public bool IsEnabled { get; set; } = true;
    public bool IsConfigured { get; set; } = true;
    public KavenegarSmsSendResult Result { get; set; } = new(KavenegarSmsSendState.Accepted, 42, 1, 200);
    public List<(string Recipient, string Message)> Sent { get; } = new();
    public TaskCompletionSource<bool>? Hold { get; set; }
    public TaskCompletionSource<bool>? Entered { get; set; }
    public async Task<KavenegarSmsSendResult> SendAsync(string recipient, string message)
    {
        Sent.Add((recipient, message));
        Entered?.TrySetResult(true);
        if (Hold is not null) await Hold.Task;
        return Result;
    }
}
