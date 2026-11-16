global using System;
global using System.Collections.Generic;
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
Console.WriteLine($"Work-order SMS appearance: {checks} offline checks passed.");
