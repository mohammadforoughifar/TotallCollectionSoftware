using Inventory.Api.Data;
using Inventory.Client.Pages;
using Inventory.Shared.Dtos;

var checks = 0;
void Check(bool ok, string message)
{
    if (!ok) throw new InvalidOperationException("FAIL: " + message);
    checks++;
    Console.WriteLine("PASS: " + message);
}
WorkOrderSmsEventDto Event(int id, WorkOrderSmsStatus status, int? code = null) =>
    new() { Id = id, Status = status, FailureCode = code, RecipientName = $"کاربر {id}" };

// The first read can be pending. Only status GETs follow, until the actual result arrives.
using (var ui = new StatusHarness())
{
    var open = ui.Open(10);
    var first = await ui.Requests.At(0);
    Check(first.Path == "/api/workorders/10/sms-events", "owner reads only the opened order's status endpoint");
    first.Complete(Event(1, WorkOrderSmsStatus.Pending), Event(2, WorkOrderSmsStatus.Accepted));
    await open.WaitAsync(TimeSpan.FromSeconds(5));
    var automatic = await ui.Requests.At(1);
    Check(automatic.Path == first.Path, "a pending event triggers a status read, not a send");
    automatic.Complete(Event(1, WorkOrderSmsStatus.Failed, 427), Event(2, WorkOrderSmsStatus.Accepted));
    await ui.LastWatch.WaitAsync(TimeSpan.FromSeconds(5));
    Check(!ui.Loading && !ui.Failed && ui.Rows.Count == 2 &&
          WorkOrderSmsAppearance.Text(ui.Rows[0].Status) == "ناموفق" &&
          WorkOrderSmsAppearance.ErrorCode(ui.Rows[0]) == 427 &&
          WorkOrderSmsAppearance.IsSuccess(ui.Rows[1].Status),
        "the eventual 427 is shown as a failure without changing another recipient's success");
    Check(ui.Requests.Count == 2, "polling stops after all returned rows are terminal");
}

using (var ui = new StatusHarness())
{
    var open = ui.Open(20, owner: 9);
    await open;
    await ui.Refresh();
    Check(ui.Requests.Count == 0 && ui.Rows.Count == 0,
        "a non-owner neither starts nor manually refreshes the owner-only history");
}

// Old in-flight responses must be ignored even if a transport ignores cancellation.
using (var ui = new StatusHarness())
{
    var old = ui.Open(30);
    var oldRead = await ui.Requests.At(0);
    var current = ui.Open(31);
    var currentRead = await ui.Requests.At(1);
    Check(oldRead.Cancellation.IsCancellationRequested, "switching orders cancels the old initial read");
    currentRead.Complete(Event(31, WorkOrderSmsStatus.Accepted));
    await current.WaitAsync(TimeSpan.FromSeconds(5));
    oldRead.Complete(Event(30, WorkOrderSmsStatus.Failed, 427));
    await old.WaitAsync(TimeSpan.FromSeconds(5));
    Check(ui.Rows.Count == 1 && ui.Rows[0].Id == 31 && WorkOrderSmsAppearance.IsSuccess(ui.Rows[0].Status),
        "a late response from another order cannot overwrite the current status");
}
using (var ui = new StatusHarness())
{
    var old = ui.Open(32);
    var oldRead = await ui.Requests.At(0);
    ui.Close();
    Check(oldRead.Cancellation.IsCancellationRequested, "closing details cancels the initial read");
    var current = ui.Open(32);
    var currentRead = await ui.Requests.At(1);
    currentRead.Complete(Event(2, WorkOrderSmsStatus.Failed, 427));
    await current.WaitAsync(TimeSpan.FromSeconds(5));
    oldRead.Complete(Event(1, WorkOrderSmsStatus.Accepted));
    await old.WaitAsync(TimeSpan.FromSeconds(5));
    Check(ui.Rows.Count == 1 && ui.Rows[0].Id == 2 && WorkOrderSmsAppearance.ErrorCode(ui.Rows[0]) == 427,
        "reopening the same order cannot resurrect its previous session's response");
}

using (var ui = new StatusHarness())
{
    var open = ui.Open(40);
    (await ui.Requests.At(0)).Complete(Event(1, WorkOrderSmsStatus.Sending));
    await open.WaitAsync(TimeSpan.FromSeconds(5));
    var oldAuto = await ui.Requests.At(1);
    var watch = ui.LastWatch;
    var refresh = ui.Refresh();
    var manual = await ui.Requests.At(2);
    Check(oldAuto.Cancellation.IsCancellationRequested && !manual.Cancellation.IsCancellationRequested,
        "manual refresh cancels an in-flight automatic status read");
    manual.Complete(Event(1, WorkOrderSmsStatus.Accepted));
    await refresh.WaitAsync(TimeSpan.FromSeconds(5));
    oldAuto.Complete(Event(1, WorkOrderSmsStatus.Failed, 427));
    await watch.WaitAsync(TimeSpan.FromSeconds(5));
    Check(ui.Rows.Count == 1 && ui.Rows[0].Status == WorkOrderSmsStatus.Accepted && !ui.Failed && ui.Requests.Count == 3,
        "a cancelled automatic response cannot overwrite the newer manual refresh or restart polling");
}

using (var ui = new StatusHarness())
{
    var open = ui.Open(50);
    (await ui.Requests.At(0)).Complete(Event(1, WorkOrderSmsStatus.Pending));
    await open.WaitAsync(TimeSpan.FromSeconds(5));
    var read = await ui.Requests.At(1);
    read.Fail();
    await ui.LastWatch.WaitAsync(TimeSpan.FromSeconds(5));
    Check(ui.Failed && !ui.Loading && ui.Requests.Count == 2,
        "a failed status read is indicated and automatic polling stops");
    var refresh = ui.Refresh();
    (await ui.Requests.At(2)).Complete(Event(1, WorkOrderSmsStatus.Unknown));
    await refresh.WaitAsync(TimeSpan.FromSeconds(5));
    Check(!ui.Failed && ui.Rows[0].Status == WorkOrderSmsStatus.Unknown &&
          !WorkOrderSmsAppearance.IsSuccess(ui.Rows[0].Status) && ui.Requests.Count == 3,
        "manual refresh works after a read error and unknown does not claim delivery or trigger another read");
}

using (var ui = new StatusHarness())
{
    var open = ui.Open(60);
    (await ui.Requests.At(0)).Complete(Event(1, WorkOrderSmsStatus.Accepted));
    await open.WaitAsync(TimeSpan.FromSeconds(5));
    var refresh = ui.Refresh();
    var staleManual = await ui.Requests.At(1);
    ui.Close();
    var reopened = ui.Open(60);
    var current = await ui.Requests.At(2);
    Check(staleManual.Cancellation.IsCancellationRequested, "closing also cancels a manual read");
    current.Complete(Event(2, WorkOrderSmsStatus.Failed, 427));
    await reopened.WaitAsync(TimeSpan.FromSeconds(5));
    staleManual.Complete(Event(1, WorkOrderSmsStatus.Accepted));
    await refresh.WaitAsync(TimeSpan.FromSeconds(5));
    Check(ui.Rows.Count == 1 && ui.Rows[0].Id == 2 && WorkOrderSmsAppearance.ErrorCode(ui.Rows[0]) == 427,
        "a late manual refresh cannot overwrite a reopened detail session");
}

{
    var ui = new StatusHarness();
    var open = ui.Open(70);
    var pendingRead = await ui.Requests.At(0);
    ui.Dispose();
    Check(pendingRead.Cancellation.IsCancellationRequested, "disposing the page cancels an in-flight initial read");
    pendingRead.Complete(Event(1, WorkOrderSmsStatus.Pending));
    await open.WaitAsync(TimeSpan.FromSeconds(5));
    Check(ui.Rows.Count == 0 && ui.Requests.Count == 1,
        "a disposed page ignores a late response and does not start polling");
}

// Exercise the actual endpoint projection and owner/company guards against fake EF.
var db = new FakeDb();
db.Orders.Add(new FakeOrder { Id = 90, CompanyId = 1, OwnerUserId = 1 });
db.People.Add(new FakeUser { Id = 2, Name = "گیرنده" });
void Add(int id, int company, int order, WorkOrderSmsStatus status, int? provider, int? message)
{
    db.Notifications.Add(new WorkOrderSmsNotification {
        Id = id, CompanyId = company, WorkOrderId = order, RecipientUserId = 2,
        Status = status, ProviderCode = provider, ProviderMessageStatus = message,
        Recipient = "09120000000", EventType = WorkOrderSmsEventType.Registration
    });
}
Add(1, 1, 90, WorkOrderSmsStatus.Failed, 427, null);
Add(2, 1, 90, WorkOrderSmsStatus.Failed, 200, 6);
Add(3, 1, 90, WorkOrderSmsStatus.Accepted, 427, 1);
Add(4, 2, 90, WorkOrderSmsStatus.Failed, 431, null);
Add(5, 1, 91, WorkOrderSmsStatus.Failed, 431, null);
var api = new EndpointHarness(db);
var ok = (FakeResult)await api.SmsEvents(90);
var rows = (List<WorkOrderSmsEventDto>)ok.Value!;
Check(ok.Kind == "ok" && rows.Count == 3 && rows.All(r => r.RecipientName == "گیرنده"),
    "owner endpoint returns only the requested order and active company's events");
Check(rows.Single(r => r.Id == 1).FailureCode == 427 &&
      rows.Single(r => r.Id == 2).FailureCode == 6 && rows.Single(r => r.Id == 3).FailureCode is null,
    "endpoint projects the actual 427, message failure status and no success failure code");
Check(typeof(WorkOrderSmsEventDto).GetProperty("Recipient") is null &&
      typeof(WorkOrderSmsEventDto).GetProperty("ProviderMessageId") is null,
    "owner status DTO does not expose the stored mobile or provider tracking identifier");
api.MyUserId = 2;
Check(((FakeResult)await api.SmsEvents(90)).Kind == "forbid", "another user cannot read the owner's SMS history");
api.MyUserId = 1; api.CompanyId = 2;
Check(((FakeResult)await api.SmsEvents(90)).Kind == "not-found", "another active company cannot read the history");
api.CompanyId = null;
Check(((FakeResult)await api.SmsEvents(90)).Kind == "bad-request", "an active company is required");

Console.WriteLine($"Work-order SMS extracted UI/API: {checks} offline checks passed.");
