global using System;
global using System.Collections.Generic;
global using System.Threading;
global using System.Threading.Tasks;
using Inventory.Client.Pages;
using Inventory.Shared.Dtos;

var checks = 0;
void Check(bool value, string description)
{
    if (!value) throw new InvalidOperationException("FAIL: " + description);
    checks++;
    Console.WriteLine("PASS: " + description);
}

var success = new HashSet<WorkOrderSmsStatus>
{
    WorkOrderSmsStatus.Accepted, WorkOrderSmsStatus.Delivered
};
var failure = new HashSet<WorkOrderSmsStatus>
{
    WorkOrderSmsStatus.Failed, WorkOrderSmsStatus.InvalidMobile, WorkOrderSmsStatus.Disabled,
    WorkOrderSmsStatus.NotConfigured, WorkOrderSmsStatus.Cancelled, WorkOrderSmsStatus.ModuleDisabled,
    WorkOrderSmsStatus.InvalidCompanyName, WorkOrderSmsStatus.InvalidOrderNumber,
    WorkOrderSmsStatus.NoCompanyAccess, WorkOrderSmsStatus.InvalidDeadline, WorkOrderSmsStatus.InvalidTitle
};
foreach (var status in Enum.GetValues<WorkOrderSmsStatus>())
{
    Check(WorkOrderSmsAppearance.IsSuccess(status) == success.Contains(status) &&
          WorkOrderSmsAppearance.IsFailure(status) == failure.Contains(status) &&
          !(WorkOrderSmsAppearance.IsSuccess(status) && WorkOrderSmsAppearance.IsFailure(status)),
        $"{status}: success, failure and neutral states remain distinct");
    if (failure.Contains(status))
        Check(WorkOrderSmsAppearance.Text(status) == "ناموفق", $"{status}: compact failure label");
}

Check(WorkOrderSmsAppearance.Text(WorkOrderSmsStatus.Accepted).Contains("تحویل قطعی نیست") &&
      WorkOrderSmsAppearance.Text(WorkOrderSmsStatus.Accepted) != WorkOrderSmsAppearance.Text(WorkOrderSmsStatus.Delivered) &&
      WorkOrderSmsAppearance.Text(WorkOrderSmsStatus.Delivered) == "تحویل‌شده",
    "provider acceptance and confirmed delivery have distinct accessible descriptions");
Check(WorkOrderSmsAppearance.Text(WorkOrderSmsStatus.Pending) == "در صف ارسال" &&
      WorkOrderSmsAppearance.Text(WorkOrderSmsStatus.Sending) == "در حال ارسال" &&
      WorkOrderSmsAppearance.Text(WorkOrderSmsStatus.Unknown) == "نامشخص",
    "pending, sending and ambiguous outcomes do not claim success or failure");
Check(!WorkOrderSmsAppearance.IsSuccess((WorkOrderSmsStatus)999) &&
      !WorkOrderSmsAppearance.IsFailure((WorkOrderSmsStatus)999) &&
      WorkOrderSmsAppearance.Text((WorkOrderSmsStatus)999) == "نامشخص",
    "unrecognized future states fail closed as unknown");

var sms = new WorkOrderSmsEventDto { Status = WorkOrderSmsStatus.Failed, FailureCode = 427 };
Check(WorkOrderSmsAppearance.ErrorCode(sms) == 427, "the actual provider failure code is retained");
sms.FailureCode = 431;
Check(WorkOrderSmsAppearance.ErrorCode(sms) == 431, "a simulated token rejection code is shown without changing it");
sms.FailureCode = 6;
Check(WorkOrderSmsAppearance.ErrorCode(sms) == 6, "a returned failed message status is retained");
sms.FailureCode = 200;
Check(WorkOrderSmsAppearance.ErrorCode(sms) is null, "API success code 200 is not shown as a failure");
sms.FailureCode = null;
Check(WorkOrderSmsAppearance.ErrorCode(sms) is null, "a failure without a returned code invents none");
sms.FailureCode = 427;
foreach (var status in new[] { WorkOrderSmsStatus.Accepted, WorkOrderSmsStatus.Delivered,
                               WorkOrderSmsStatus.Pending, WorkOrderSmsStatus.Sending, WorkOrderSmsStatus.Unknown })
{
    sms.Status = status;
    Check(WorkOrderSmsAppearance.ErrorCode(sms) is null, $"{status}: no failure code is displayed");
}
// Status refresh only reads the outbox; it must stop for terminal/unknown results.
foreach (var status in Enum.GetValues<WorkOrderSmsStatus>())
{
    var rows = new[] { new WorkOrderSmsEventDto { Status = status } };
    Check(WorkOrderSmsStatusPoller.NeedsRefresh(rows) ==
          (status is WorkOrderSmsStatus.Pending or WorkOrderSmsStatus.Sending),
        $"{status}: background refresh is used only while an event is pending or sending");
}
Check(!WorkOrderSmsStatusPoller.NeedsRefresh(Array.Empty<WorkOrderSmsEventDto>()) &&
      WorkOrderSmsStatusPoller.NeedsRefresh(new[] {
          new WorkOrderSmsEventDto { Status = WorkOrderSmsStatus.Failed },
          new WorkOrderSmsEventDto { Status = WorkOrderSmsStatus.Pending } }),
    "empty history does not poll, but a mixed history with a pending event does");

var reads = 0;
var delays = 0;
using (var poller = new WorkOrderSmsStatusPoller(TimeSpan.FromSeconds(5), 10, (interval, ct) => {
    Check(interval == TimeSpan.FromSeconds(5) && !ct.IsCancellationRequested, "status reads are delayed");
    delays++;
    return Task.CompletedTask;
}))
{
    await poller.StartAsync(ct => Task.FromResult(++reads < 3));
    Check(reads == 3 && delays == 3, "polling stops when the status read reports a terminal result");
}
reads = 0;
using (var bounded = new WorkOrderSmsStatusPoller(TimeSpan.Zero, 3, (_, _) => Task.CompletedTask))
{
    await bounded.StartAsync(ct => { reads++; return Task.FromResult(true); });
    Check(reads == 3, "pending status polling is bounded and leaves manual refresh available");
    await bounded.StartAsync(ct => Task.FromException<bool>(new InvalidOperationException("fake read failure")));
    await bounded.StartAsync(ct => { reads++; return Task.FromResult(false); });
    Check(reads == 4, "a read error stops polling but does not disable a later manual refresh");
}

reads = 0;
var delayStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
using (var cancel = new WorkOrderSmsStatusPoller(TimeSpan.Zero, 3, (_, ct) => {
    delayStarted.TrySetResult(true);
    return Task.Delay(Timeout.InfiniteTimeSpan, ct);
}))
{
    var first = cancel.StartAsync(ct => { reads++; return Task.FromResult(true); });
    await delayStarted.Task;
    var replacement = cancel.StartAsync(ct => { reads++; return Task.FromResult(true); });
    await first;
    Check(first.IsCompleted && !replacement.IsCompleted && reads == 0,
        "opening another detail cancels the old watch without issuing a status read");
    cancel.Stop();
    await replacement;
    Check(reads == 0, "closing the detail cancels its pending timer");
    cancel.Dispose();
    await cancel.StartAsync(ct => { reads++; return Task.FromResult(true); });
    Check(reads == 0, "disposing the page prevents a background watch from restarting");
}

var readStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
var completedRead = false;
using (var inFlight = new WorkOrderSmsStatusPoller(TimeSpan.Zero, 3, (_, _) => Task.CompletedTask))
{
    var running = inFlight.StartAsync(async ct => {
        readStarted.TrySetResult(true);
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        completedRead = true;
        return true;
    });
    await readStarted.Task;
    inFlight.Stop();
    await running;
    Check(!completedRead, "closing the detail cancels an in-flight status read");
}

Console.WriteLine($"Work-order SMS appearance and refresh: {checks} offline checks passed.");
