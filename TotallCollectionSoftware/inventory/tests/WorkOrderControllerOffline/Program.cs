#nullable enable
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

var checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}
bool Prop(IActionResult result, string name) =>
    (bool)((Result)result).Value.GetType().GetProperty(name)!.GetValue(((Result)result).Value)!;

// Actual Reply action plus fake transaction/SMS/notification services.
var db = new FakeDb();
var notify = new FakeNotify(db);
var controller = new ReplyHarness(db, notify);
db.WorkOrders.Add(new WorkOrder { Id = 1, OwnerUserId = 1, CompanyId = 1, Title = "اصلی", Number = "WO/1405/1" });
db.WorkOrders.Add(new WorkOrder { Id = 2, OwnerUserId = 2, CompanyId = 1, ParentOrderId = 1, Title = "زیر دستور", Number = "WO/1405/2" });
var assignee = new WorkOrderAssignee { OrderId = 2, UserId = 3 };
db.WorkOrderAssignees.Add(assignee);
notify.Fail.Add(2);
var request = new ReplyHarness.ReplyDto { Done = true, Text = "متن محرمانه", ClientRequestId = "request-1" };
var result = await controller.Reply(2, request);
Check(((Result)result).Kind == "ok" && Prop(result, "notificationsFailed"),
    "a failed post-commit notice reports a warning, not a failed reply");
Check(db.SnapshotAtCommit.Contains("2:Reply") && db.SnapshotAtCommit.Contains("1:SubReply") &&
      db.Events.IndexOf("stage") < db.Events.IndexOf("commit") && db.Events.IndexOf("notify-2") > db.Events.IndexOf("commit"),
    "reply and ancestor history commit before external notices, with staging inside the transaction");
Check(db.Stages.Count == 1 && db.Stages[0].Recipients.SequenceEqual(new[] { 2 }) && db.Stages[0].Type == WorkOrderSmsEventType.Reply,
    "SMS staging addresses only the current order's owner");
Check(notify.Attempts.SequenceEqual(new[] { 2, 1 }) && notify.Broadcasts == 1 && db.Clears == 1 && !db.Dirty,
    "a failed owner notice does not implicitly retry or suppress an independent ancestor notice and broadcast");
var beforeStage = db.Stages.Count;
var beforeAttempts = notify.Attempts.Count;
result = await controller.Reply(2, request);
Check(Prop(result, "duplicate") && db.Stages.Count == beforeStage && notify.Attempts.Count == beforeAttempts,
    "replaying the same reply request makes no second SMS or notification");
result = await controller.Reply(2, new ReplyHarness.ReplyDto { Done = false, Text = "متن دیگر", ClientRequestId = "request-1" });
Check(((Result)result).Kind == "conflict" && db.Stages.Count == beforeStage && notify.Attempts.Count == beforeAttempts,
    "reusing a reply request ID with different contents is rejected");
var unchanged = new ReplyHarness.ReplyDto { Done = true, Text = "  متن محرمانه  ", ClientRequestId = "request-2" };
result = await controller.Reply(2, unchanged);
Check(Prop(result, "duplicate") && db.SnapshotAtCommit.Contains("2:ReplyUnchanged") && db.Stages.Count == beforeStage,
    "an identical active reply with a fresh id records a no-op, not an SMS");
assignee.RepliedAt = null; assignee.Done = null;
result = await controller.Reply(2, unchanged);
Check(Prop(result, "duplicate") && db.Stages.Count == beforeStage && assignee.RepliedAt is null,
    "a delayed replay of the no-op id cannot resubmit after a reset");
notify.Fail.Clear();
result = await controller.Reply(2, new ReplyHarness.ReplyDto { Done = true, Text = "متن محرمانه", ClientRequestId = "request-3" });
Check(!Prop(result, "duplicate") && db.Stages.Count == beforeStage + 1 && !Prop(result, "notificationsFailed"),
    "a genuinely new reply after reset can be submitted and notified");
controller.StageResult = false;
result = await controller.Reply(2, new ReplyHarness.ReplyDto { Done = false, Text = "متن جدید", ClientRequestId = "request-4" });
Check(Prop(result, "smsStageFailed") && assignee.Done == false && db.SnapshotAtCommit.Contains("2:Reply"),
    "recoverable SMS staging failure does not roll back the reply");
var priorLogs = db.WorkOrderLogs.Items.Count;
beforeStage = db.Stages.Count; beforeAttempts = notify.Attempts.Count; db.FailSave = true;
try
{
    await controller.Reply(2, new ReplyHarness.ReplyDto { Done = true, Text = "آزمایش", ClientRequestId = "request-5" });
    throw new Exception("expected save failure");
}
catch (InvalidOperationException) { }
Check(db.WorkOrderLogs.Items.Count == priorLogs && db.Stages.Count == beforeStage && notify.Attempts.Count == beforeAttempts && assignee.Done == false,
    "business save failure rolls back reply/history and sends no post-commit notice");
controller.StageThrows = true;
try
{
    await controller.Reply(2, new ReplyHarness.ReplyDto { Done = true, Text = "آزمایش", ClientRequestId = "request-6" });
    throw new Exception("expected staging failure");
}
catch (InvalidOperationException) { }
Check(db.WorkOrderLogs.Items.Count == priorLogs && db.Stages.Count == beforeStage && notify.Attempts.Count == beforeAttempts && assignee.Done == false,
    "unrecoverable staging failure aborts the business operation and post-commit notices");
controller.StageThrows = false; db.FailCommit = true;
try
{
    await controller.Reply(2, new ReplyHarness.ReplyDto { Done = true, Text = "آزمایش", ClientRequestId = "request-7" });
    throw new Exception("expected commit failure");
}
catch (InvalidOperationException) { }
Check(db.WorkOrderLogs.Items.Count == priorLogs && db.Stages.Count == beforeStage && notify.Attempts.Count == beforeAttempts && assignee.Done == false,
    "commit failure aborts the fake transaction and does not try notifications");

// Actual post-commit tails for Create/Update/AddAssignees, with the real deferred-letter
// helper. Each simulated failed SaveChanges leaves tracked data that Clear must remove.
FakeDb NoticeDb()
{
    var data = new FakeDb();
    data.LetterSources.Add(new FakeLetterSource { Id = 1, SourceType = 1, InnerLetter = new FakeLetter { Title = "نامه یک", LetterNumber = "L1" } });
    data.LetterSources.Add(new FakeLetterSource { Id = 2, SourceType = 1, InnerLetter = new FakeLetter { Title = "نامه دو", LetterNumber = "L2" } });
    data.Users.Add(new FakeUser { Id = 3, Username = "owner" });
    return data;
}
var order = new WorkOrder { Id = 42, OwnerUserId = 3, CompanyId = 1, Number = "WO/1405/42", Title = "کار", OwnerName = "مالک", ProjectId = 9, DueAt = DateTime.Now, Priority = 2 };
var parent = new WorkOrder { Id = 40, OwnerUserId = 7, Number = "WO/1405/40" };
var users = new List<FakeUser> { new() { Id = 3 }, new() { Id = 4 }, new() { Id = 5 } };
var cleanDb = NoticeDb(); var cleanNotify = new FakeNotify(cleanDb); var clean = new ReplyHarness(cleanDb, cleanNotify);
result = await clean.CreateNotices(order, parent, users, 2, true, 2, true);
Check(!Prop(result, "notificationsFailed") && !Prop(result, "smsStageFailed") && cleanNotify.Details.Count == 5 &&
      cleanNotify.Scopes.SequenceEqual(new[] { "workorders", "letters", "incoming-letters", "outgoing-letters", "workprojects" }),
    "successful creation notices and all independent broadcasts report no warning");

var failingDb = NoticeDb(); var failingNotify = new FakeNotify(failingDb); var failing = new ReplyHarness(failingDb, failingNotify);
failingNotify.FailWhen = (id, _, form) => id == 4 && form == "نامه داخلی";
failingNotify.FailScopes.Add("letters");
result = await failing.CreateNotices(order, parent, users, 2, true, 2, true);
Check(Prop(result, "notificationsFailed") && !Prop(result, "smsStageFailed") &&
      failingNotify.Details.Any(x => x.User == 5 && x.Form == "نامه داخلی") &&
      failingNotify.Details.Any(x => x.User == 7 && x.Form == "دستور کار") &&
      failingNotify.Details.Any(x => x.User == 5 && x.Form == "دستور کار") &&
      failingNotify.Scopes.Contains("incoming-letters") && failingNotify.Scopes.Contains("workprojects") &&
      failingDb.Clears == 2 && !failingDb.Dirty,
    "failed letter notice and broadcast do not suppress later recipients, work-order notices, or broadcasts");

var queryDb = NoticeDb(); var queryNotify = new FakeNotify(queryDb); var query = new ReplyHarness(queryDb, queryNotify);
Ext.FailNextProjectedQuery = true;
result = await query.CreateNotices(order, null, users, 0, false, 1, true, twoLetters: true);
Check(Prop(result, "notificationsFailed") && queryNotify.Details.Any(x => x.User == 6 && x.Form == "نامه داخلی") &&
      queryNotify.Details.Any(x => x.User == 4 && x.Form == "دستور کار") && queryNotify.Scopes.Contains("outgoing-letters"),
    "a deferred-letter query failure does not suppress another letter or independent work-order notices");

var updateDb = NoticeDb(); var updateNotify = new FakeNotify(updateDb); var update = new ReplyHarness(updateDb, updateNotify);
updateNotify.Fail.Add(4);
result = await update.UpdateNotices(order, new List<int> { 4, 5 }, new List<int> { 6 }, 0, 8, 0, true);
Check(Prop(result, "notificationsFailed") && updateNotify.Attempts.SequenceEqual(new[] { 4, 5, 6 }) &&
      updateNotify.Scopes.Contains("workorders") && updateNotify.Scopes.Contains("workprojects") && updateDb.Clears == 1,
    "failed edit notice leaves later additions, removals and broadcasts independently attemptable");

var assignDb = NoticeDb(); var assignNotify = new FakeNotify(assignDb); var assign = new ReplyHarness(assignDb, assignNotify);
assignNotify.Fail.Add(4);
result = await assign.AssignNotices(order, users, 1, "توضیح", 0, true);
Check(Prop(result, "notificationsFailed") && !Prop(result, "smsStageFailed") &&
      assignNotify.Attempts.SequenceEqual(new[] { 4, 5 }) && assignNotify.Scopes.Contains("workorders") && !assignDb.Dirty,
    "failed workflow-add notice does not suppress the next recipient or broadcast");
var warningDb = NoticeDb(); var warningNotify = new FakeNotify(warningDb); var warning = new ReplyHarness(warningDb, warningNotify);
result = await warning.AssignNotices(order, users, 0, null, 0, false);
Check(!Prop(result, "notificationsFailed") && Prop(result, "smsStageFailed"),
    "staging and in-app notification warnings are independent");

var serverDb = NoticeDb(); var serverNotify = new FakeNotify(serverDb); var server = new ServerHarness(serverDb, serverNotify);
serverNotify.Fail.Add(4);
result = await server.CreateNotices(order, new FakeUser { Id = 4 }, false, "مالک", true);
Check(Prop(result, "notificationsFailed") && !Prop(result, "smsStageFailed") &&
      serverNotify.Scopes.SequenceEqual(new[] { "workorders" }) && serverDb.Clears == 1 && !serverDb.Dirty,
    "server-alert notice failure clears failed tracked entities and still attempts its broadcast");
var serverCleanDb = NoticeDb(); var serverCleanNotify = new FakeNotify(serverCleanDb); var serverClean = new ServerHarness(serverCleanDb, serverCleanNotify);
result = await serverClean.CreateNotices(order, new FakeUser { Id = 4 }, true, "مالک", false);
Check(!Prop(result, "notificationsFailed") && Prop(result, "smsStageFailed") && serverCleanNotify.Broadcasts == 1,
    "server-alert staging warning remains separate from successful notifications");

// Execute the production title construction/guard; no database or SMS is involved here.
var titlePrefix = "رسیدگی به هشدار ارتباط iLO: ";
var normalName = new string('ا', 150);
result = await server.TitleGuard(new FakeServerAlert { Category = "Connection" }, new FakeManagedServer { Name = normalName });
var savedTitle = (string)((Result)result).Value.GetType().GetProperty("title")!.GetValue(((Result)result).Value)!;
Check(((Result)result).Kind == "ok" && savedTitle == titlePrefix + normalName,
    "a normal maximum-length server name remains intact in its work-order title");
var boundaryName = new string('ا', 200 - titlePrefix.Length);
result = await server.TitleGuard(new FakeServerAlert { Category = "Connection" }, new FakeManagedServer { Name = boundaryName });
savedTitle = (string)((Result)result).Value.GetType().GetProperty("title")!.GetValue(((Result)result).Value)!;
Check(((Result)result).Kind == "ok" && savedTitle.Length == 200 && savedTitle == titlePrefix + boundaryName,
    "a generated title of exactly 200 characters is accepted without shortening");
result = await server.TitleGuard(new FakeServerAlert { Category = "Connection" }, new FakeManagedServer { Name = boundaryName + "ا" });
Check(((Result)result).Kind == "badrequest",
    "an oversized legacy server name is rejected rather than silently truncating the saved title");
Console.WriteLine($"Extracted controller fake-DB harness: {checks} checks passed.");
