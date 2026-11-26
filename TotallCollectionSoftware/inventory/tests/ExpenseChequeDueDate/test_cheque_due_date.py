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
HANDOVER_MESSAGE = ('تاریخ تحویل چک نمی‌تواند پیش از تاریخ سند هزینه باشد؛ تاریخ سند را به روز تحویل '
                    '(یا قبل‌تر) تغییر دهید یا تاریخ تحویل را به روز سند ببرید.')


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

    def test_handover_before_expense_rule_is_shared_too(self):
        # قاعدهٔ دوم (پرداخت پیش از سند) هم در Shared است تا فرم و سرور یک پیام بدهند.
        self.assertIn('public static bool HandoverIsBeforeExpense(DateTime? handedAt, DateTime? expenseDate)', RULE)
        self.assertIn('handedAt.Value.Date < expenseDate.Value.Date', RULE)
        self.assertIn('public static string? ValidateHandover(DateTime? handedAt, DateTime? expenseDate)', RULE)
        self.assertIn('HandoverIsBeforeExpense(handedAt, expenseDate) ? HandoverBeforeExpenseMessage : null', RULE)
        self.assertIn(f'public const string HandoverBeforeExpenseMessage =\n        "{HANDOVER_MESSAGE}";', RULE)
        # پیام باید هر دو راه حل را بگوید، چون «مشخصات ... معتبر نیست» قبلی هیچ راهنمایی نداشت.
        self.assertIn('تاریخ سند را به روز تحویل', RULE)
        self.assertIn('تاریخ تحویل را به روز سند ببرید', RULE)


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


class HandoverBeforeExpenseRule(unittest.TestCase):
    """پرداخت نمی‌تواند پیش از خودِ سند هزینه باشد؛ پیام باید فیلد و راه‌حل را بگوید."""

    def test_form_shows_the_rule_next_to_the_handover_field(self):
        self.assertIn('data-expense-cheque-handover-warning', FORM)
        self.assertIn('@ExpenseChequeDates.HandoverBeforeExpenseMessage', FORM)
        self.assertIn('private bool HandoverBeforeExpense =>', FORM)
        self.assertIn('ExpenseChequeDates.HandoverIsBeforeExpense(handedAt, expDate ?? form.Date)', FORM)
        self.assertIn('private string HandoverCss => HandoverBeforeExpense ? "is-invalid" : "";', FORM)
        self.assertIn('<DatePicker @bind-Value="handedAt" CssClass="@HandoverCss" />', FORM)
        # هشدار در همان ستون تاریخ تحویل است، نه جای دیگری از فرم
        handover_block = FORM.split('تاریخ تحویل چک *', 1)[1].split('دریافت‌کننده / طرف هزینه', 1)[0]
        self.assertIn('data-expense-cheque-handover-warning', handover_block)

    def test_user_can_align_the_expense_date_with_one_click(self):
        self.assertIn('id="btn-align-expense-date"', FORM)
        self.assertIn('@onclick="AlignExpenseDateToHandover"', FORM)
        align = FORM.split('private void AlignExpenseDateToHandover()', 1)[1].split('private async Task', 1)[0]
        self.assertIn('expDate = handedAt.Value.Date;', align)
        self.assertIn('form.Date = expDate.Value;', align)

    def test_save_refuses_before_calling_the_api(self):
        save = FORM.split('private async Task Save()', 1)[1]
        self.assertIn('ExpenseChequeDates.HandoverIsBeforeExpense(handedAt, form.Date)', save)
        self.assertLess(save.index('HandoverIsBeforeExpense(handedAt, form.Date)'),
                        save.index('CreateChequeExpenseAsync'))

    def test_server_uses_the_shared_message_not_a_generic_one(self):
        self.assertIn('ExpenseChequeDates.HandoverIsBeforeExpense(cmd.HandedAt, dto.Date)', SERVICE)
        self.assertIn('ExpenseChequeDates.HandoverBeforeExpenseMessage', SERVICE)
        self.assertNotIn('مشخصات هزینه، دریافت‌کننده، مبلغ یا تاریخ تحویل چک معتبر نیست.', SERVICE)

    def test_each_rejected_field_names_itself(self):
        # به‌جای یک پیام مبهم، هر شرط پیام خودش را دارد.
        create = SERVICE.split('public async Task<int> CreateAsync', 1)[1].split('public async Task ChangeStatusAsync', 1)[0]
        for message in ('نوع هزینه را انتخاب کنید.', 'تاریخ سند هزینه را وارد کنید.',
                        'تاریخ تحویل چک را وارد کنید.', 'مبلغ هزینه را وارد کنید.',
                        'دریافت‌کننده / طرف هزینه را وارد کنید.'):
            with self.subTest(message=message):
                self.assertIn(message, create)


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
        # بررسی باید پیش از شروع تراکنشِ همان متد ثبت باشد تا هیچ سندی ثبت نشود.
        # توجه: فایل چند متد تراکنشی دارد (مثل SettleReturnedAsync)، پس محدودهٔ
        # CreateAsync جدا می‌شود.
        create = SERVICE.split('public async Task<int> CreateAsync', 1)[1]
        create = create.split('public async Task ChangeStatusAsync', 1)[0]
        self.assertIn('ExpenseChequeDates.DueIsBeforeHandover', create)
        self.assertLess(create.index('ExpenseChequeDates.DueIsBeforeHandover'),
                        create.index('BeginTransactionAsync'))

    def test_handover_not_before_expense_date_is_enforced_with_its_own_message(self):
        self.assertIn('ExpenseChequeDates.HandoverIsBeforeExpense(cmd.HandedAt, dto.Date)', SERVICE)
        # پیش از آغاز تراکنش رد می‌شود تا هیچ سندی ثبت نشود.
        create = SERVICE.split('public async Task<int> CreateAsync', 1)[1].split('public async Task ChangeStatusAsync', 1)[0]
        self.assertLess(create.index('ExpenseChequeDates.HandoverIsBeforeExpense'),
                        create.index('BeginTransactionAsync'))


if __name__ == '__main__':
    unittest.main()
