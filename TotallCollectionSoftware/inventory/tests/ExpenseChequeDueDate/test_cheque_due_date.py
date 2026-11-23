"""Static contracts for the issued-cheque due-date rule on the expenses form.

Use the .NET probe (`dotnet run --project tests/ExpenseChequeDueDate`) for the
actual date comparison; these checks prove the rule is wired into the form, the
calendar and the API and cannot be bypassed.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]


def read(path):
    return (ROOT / 'src' / path).read_text(encoding='utf-8')


RULE = read('Inventory.Shared/ExpenseChequeDates.cs')
FORM = read('Inventory.Client/Pages/Finance/Expenses.razor')
PICKER = read('Inventory.Client/Shared/DatePicker.razor')
SERVICE = read('Inventory.Api/Services/Finance/ShopExpenseChequeService.cs')
CSS = read('Inventory.Client/wwwroot/css/app.css')

MESSAGE = 'تاریخ سررسید چک صادره نمی‌تواند پیش از تاریخ تحویل چک باشد.'


class SharedRule(unittest.TestCase):
    def test_rule_lives_in_shared_and_compares_dates_only(self):
        self.assertIn('namespace Inventory.Shared;', RULE)
        self.assertIn('public static bool DueIsBeforeHandover(DateTime? dueDate, DateTime? handedAt)', RULE)
        # مقایسه باید روی Date باشد، نه DateTime کامل (ساعت نباید روزِ برابر را رد کند)
        self.assertIn('dueDate.Value.Date < handedAt.Value.Date', RULE)
        self.assertIn('dueDate.HasValue && handedAt.HasValue', RULE)

    def test_single_persian_message(self):
        self.assertIn(f'public const string DueBeforeHandoverMessage =\n        "{MESSAGE}";', RULE)
        self.assertIn('public static string? ValidateIssued(DateTime? dueDate, DateTime? handedAt)', RULE)
        self.assertIn('public static DateTime? MinDueDate(DateTime? handedAt) => handedAt?.Date;', RULE)


class ExpenseForm(unittest.TestCase):
    def test_due_picker_is_bounded_by_the_handover_date(self):
        issued_block = FORM.split('paymentMode == "issued"', 2)[2].split('paymentMode == "received"', 1)[0]
        self.assertIn('<DatePicker @bind-Value="chequeDue" MinDate="@HandedAtDate"', issued_block)
        self.assertIn('BlockedTitle="@BlockedDueTitle"', issued_block)
        self.assertIn('CssClass="@DueCss"', issued_block)
        self.assertIn('private DateTime? HandedAtDate => handedAt?.Date;', FORM)
        self.assertIn('private string DueCss => DueBeforeHandover ? "is-invalid" : "";', FORM)

    def test_warning_is_shown_next_to_the_due_field(self):
        self.assertIn('@if (DueBeforeHandover)', FORM)
        self.assertIn('data-expense-cheque-due-warning', FORM)
        self.assertLess(FORM.index('data-expense-cheque-due-warning'),
                        FORM.index('private bool DueBeforeHandover'),
                        'warning markup must come before the @code block')
        self.assertIn('@ExpenseChequeDates.DueBeforeHandoverMessage', FORM)
        self.assertIn('private bool DueBeforeHandover => paymentMode == "issued" && '
                      'ExpenseChequeDates.DueIsBeforeHandover(chequeDue, handedAt);', FORM)

    def test_save_refuses_the_invalid_pair_before_calling_the_api(self):
        save = FORM.split('private async Task Save()', 1)[1]
        self.assertIn('if (paymentMode == "issued" && ExpenseChequeDates.DueIsBeforeHandover(chequeDue, handedAt))', save)
        self.assertIn('throw new InvalidOperationException(ExpenseChequeDates.DueBeforeHandoverMessage);', save)
        # بررسی باید پیش از فراخوانی سرویس باشد، وگرنه سند نیمه‌کاره ثبت می‌شود
        self.assertLess(save.index('DueIsBeforeHandover'),
                        save.index('CreateChequeExpenseAsync'))

    def test_defaults_and_mode_switch_cannot_start_invalid(self):
        self.assertIn('private void ResetChequeDates()', FORM)
        self.assertIn('handedAt = DateTime.Today; chequeDue = handedAt;', FORM)
        self.assertNotIn('chequeDue = DateTime.Today; handedAt = DateTime.Today;', FORM)
        # انتخاب «چک صادره» با سررسیدِ قدیمی‌تر از تحویل، سررسید را به تحویل می‌کشد
        prepare = FORM.split('private async Task PrepareMode(string mode)', 1)[1].split('catch (Exception ex)', 1)[0]
        self.assertIn('chequeDue.Value.Date < handedAt.Value.Date', prepare)
        self.assertIn('chequeDue = handedAt;', prepare)


class CalendarPicker(unittest.TestCase):
    def test_picker_supports_a_minimum_date(self):
        self.assertIn('[Parameter] public DateTime? MinDate { get; set; }', PICKER)
        self.assertIn('[Parameter] public string? BlockedTitle { get; set; }', PICKER)
        self.assertIn('private bool IsBlocked(DateTime g) => MinDate.HasValue && g != DateTime.MinValue && '
                      'g.Date < MinDate.Value.Date;', PICKER)
        # روزهای قبل از کمینه: نشانه‌دار، غیرفعال و غیرقابل کلیک
        self.assertIn('var blocked = IsBlocked(g);', PICKER)
        self.assertIn('(blocked ? " dp-disabled" : "")', PICKER)
        self.assertIn('disabled="@blocked"', PICKER)
        self.assertIn('title = blocked ? (BlockedTitle ?? title) : title;', PICKER)
        self.assertIn('if (dt == DateTime.MinValue || IsBlocked(dt))', PICKER)

    def test_disabled_days_are_visibly_disabled(self):
        self.assertIn('.dp-day.dp-disabled, .dp-day:disabled', CSS)
        self.assertIn('opacity: .32;', CSS)
        self.assertIn('cursor: not-allowed;', CSS)
        self.assertIn('line-through', CSS)


class ApiGuard(unittest.TestCase):
    def test_service_rejects_due_before_handover(self):
        self.assertIn('using Inventory.Shared;', SERVICE)
        self.assertIn('if (routeKind == ExpenseChequeKind.Issued && '
                      'ExpenseChequeDates.DueIsBeforeHandover(cmd.DueDate, cmd.HandedAt))', SERVICE)
        self.assertIn('throw new InvalidOperationException(ExpenseChequeDates.DueBeforeHandoverMessage);', SERVICE)
        # بررسی باید پیش از شروع تراکنش باشد تا هیچ سندی ثبت نشود
        self.assertLess(SERVICE.index('ExpenseChequeDates.DueIsBeforeHandover'),
                        SERVICE.index('BeginTransactionAsync'))

    def test_existing_handover_not_before_expense_date_rule_is_kept(self):
        self.assertIn('cmd.HandedAt.Date < dto.Date.Date', SERVICE)


if __name__ == '__main__':
    unittest.main()
