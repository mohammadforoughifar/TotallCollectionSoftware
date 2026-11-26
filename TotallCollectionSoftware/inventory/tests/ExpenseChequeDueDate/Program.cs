using Inventory.Shared;

static void Check(bool ok, string message)
{
    if (!ok) throw new Exception("FAIL: " + message);
    Console.WriteLine("PASS: " + message);
}

var handover = new DateTime(2026, 11, 23);

// ---------- قاعدهٔ اصلی: سررسید نباید پیش از تحویل باشد ----------
Check(!ExpenseChequeDates.DueIsBeforeHandover(handover, handover), "due on the hand-over day is valid");
Check(!ExpenseChequeDates.DueIsBeforeHandover(handover.AddDays(1), handover), "due after hand-over is valid");
Check(ExpenseChequeDates.DueIsBeforeHandover(handover.AddDays(-1), handover), "due one day before hand-over is rejected");
Check(ExpenseChequeDates.DueIsBeforeHandover(handover.AddMonths(-3), handover), "due months before hand-over is rejected");

// ---------- فقط بخش تاریخ مهم است، نه ساعت ----------
Check(!ExpenseChequeDates.DueIsBeforeHandover(new DateTime(2026, 11, 23, 8, 0, 0), new DateTime(2026, 11, 23, 18, 30, 0)),
      "same day with earlier time-of-day is still valid");
Check(!ExpenseChequeDates.DueIsBeforeHandover(new DateTime(2026, 11, 24, 0, 0, 0), new DateTime(2026, 11, 23, 23, 59, 0)),
      "next day 00:00 versus late hand-over is valid");
Check(ExpenseChequeDates.DueIsBeforeHandover(new DateTime(2026, 11, 22, 23, 59, 0), new DateTime(2026, 11, 23, 0, 1, 0)),
      "previous day late evening is rejected");

// ---------- مقادیر خالی هرگز خطا نمی‌دهند (اعتبارسنجی «الزامی» جدا است) ----------
Check(!ExpenseChequeDates.DueIsBeforeHandover(null, handover), "missing due date is not a date-order error");
Check(!ExpenseChequeDates.DueIsBeforeHandover(handover, null), "missing hand-over date is not a date-order error");
Check(!ExpenseChequeDates.DueIsBeforeHandover(null, null), "both missing is not a date-order error");

// ---------- ValidateIssued: پیام فارسی یکسان در فرم و سرور ----------
Check(ExpenseChequeDates.ValidateIssued(handover, handover) is null, "valid due date returns no message");
Check(ExpenseChequeDates.ValidateIssued(handover.AddDays(3), handover) is null, "later due date returns no message");
Check(ExpenseChequeDates.ValidateIssued(handover.AddDays(-1), handover) == ExpenseChequeDates.DueBeforeHandoverMessage,
      "invalid due date returns the shared Persian message");
Check(ExpenseChequeDates.DueBeforeHandoverMessage.Contains("سررسید"), "message names the due date field");
Check(ExpenseChequeDates.DueBeforeHandoverMessage.Contains("تحویل"), "message names the hand-over date");

// ---------- MinDueDate: کمینهٔ تقویم = روز تحویل ----------
Check(ExpenseChequeDates.MinDueDate(handover) == handover.Date, "calendar minimum is the hand-over day");
Check(ExpenseChequeDates.MinDueDate(new DateTime(2026, 11, 23, 15, 45, 0)) == handover.Date, "calendar minimum drops the time part");
Check(ExpenseChequeDates.MinDueDate(null) is null, "no hand-over date means no calendar minimum");

// ---------- قاعدهٔ دوم: پرداخت (تحویل چک) نمی‌تواند پیش از سند هزینه باشد ----------
var expenseDate = handover.AddDays(4);   // سند هزینه بعد از روز تحویل ثبت شده است
Check(ExpenseChequeDates.HandoverIsBeforeExpense(handover, handover), "hand-over on the expense day is valid");
Check(!ExpenseChequeDates.HandoverIsBeforeExpense(handover.AddDays(4), handover), "hand-over after the expense day is valid");
Check(ExpenseChequeDates.HandoverIsBeforeExpense(handover, expenseDate), "hand-over four days before the expense day is rejected");
Check(!ExpenseChequeDates.HandoverIsBeforeExpense(new DateTime(2026, 11, 23, 23, 59, 0), new DateTime(2026, 11, 23, 0, 1, 0)),
      "same calendar day compares by date only, even if the times differ");
Check(!ExpenseChequeDates.HandoverIsBeforeExpense(null, expenseDate), "missing hand-over date is not an order error");
Check(ExpenseChequeDates.ValidateHandover(handover, expenseDate) == ExpenseChequeDates.HandoverBeforeExpenseMessage,
      "the order error returns the shared hand-over message");
Check(ExpenseChequeDates.ValidateHandover(handover, handover) is null, "same day returns no message");
Check(ExpenseChequeDates.HandoverBeforeExpenseMessage.Contains("سند"), "message names the expense document date");
Check(ExpenseChequeDates.HandoverBeforeExpenseMessage.Contains("تحویل"), "message names the hand-over date");

// ---------- سناریوی فرم: تغییر تحویل بعد از انتخاب سررسید ----------
var due = handover;                      // سررسید = امروز
var handoverLater = handover.AddDays(5); // کاربر تحویل را ۵ روز جلو می‌برد
Check(ExpenseChequeDates.DueIsBeforeHandover(due, handoverLater), "moving hand-over past the due date makes the pair invalid");
Check(ExpenseChequeDates.ValidateIssued(due, handoverLater) == ExpenseChequeDates.DueBeforeHandoverMessage,
      "the same message is produced after moving the hand-over date");

Console.WriteLine("ALL EXPENSE CHEQUE DATE CHECKS PASSED");
