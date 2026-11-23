"""Regression contracts for clearing the date filter on the expenses page.

Reported bug (Persian): «هزینه‌ها — حذف فیلتر تاریخ: در بخش فیلتر، پس از یک بار فیلتر با
تاریخ، امکان حذف تاریخ وجود ندارد. انتظار: کاربر بتواند مقدار تاریخ را پاک کند یا فیلتر
تاریخ را حذف کند.»

Root cause: the shared ``DatePicker`` only renders its clear («×») button when
``AllowClear`` is ``true``; the expenses filter bound ``from``/``to`` without it, so once a
date was picked there was no way back to "no date filter" except reloading the page.

These are static source contracts; no Razor is compiled and no browser is used.
"""
import re
import unittest
from pathlib import Path

CLIENT = Path(__file__).resolve().parents[2] / 'src/Inventory.Client'
EXPENSES = (CLIENT / 'Pages/Finance/Expenses.razor').read_text(encoding='utf-8')
CSS = (CLIENT / 'Pages/Finance/Expenses.razor.css').read_text(encoding='utf-8')
PICKER = (CLIENT / 'Shared/DatePicker.razor').read_text(encoding='utf-8')
SERVICES = (CLIENT / 'Services/InventoryApiServices.cs').read_text(encoding='utf-8')


def tag(text, needle):
    """The single markup element containing ``needle``, from '<' up to the first '>'."""
    start = text.index(needle)
    open_at = text.rindex('<', 0, start)
    close_at = text.index('>', start)
    return text[open_at:close_at + 1]


class DatePickerCanClear(unittest.TestCase):
    """The shared component already supports clearing — pin that it stays that way."""

    def test_clear_button_is_conditional_on_allow_clear(self):
        self.assertIn('@if (AllowClear && Value.HasValue)', PICKER)
        self.assertIn('[Parameter] public bool AllowClear { get; set; }', PICKER)

    def test_clear_resets_value_and_notifies_parent(self):
        clear = PICKER.split('private async Task Clear()', 1)[1].split('private void Toggle', 1)[0]
        self.assertIn('_text = "";', clear)
        self.assertIn('_last = null;', clear)
        self.assertIn('Value = null;', clear)
        self.assertIn('await ValueChanged.InvokeAsync(null);', clear)


class ExpensesFilterDatesAreClearable(unittest.TestCase):
    def test_both_filter_dates_allow_clearing(self):
        self.assertIn('AllowClear="true"', tag(EXPENSES, '@bind-Value="from"'))
        self.assertIn('AllowClear="true"', tag(EXPENSES, '@bind-Value="to"'))

    def test_filter_bar_has_apply_and_reset_buttons(self):
        self.assertIn('data-expense-filter-apply', EXPENSES)
        self.assertIn('data-expense-filter-clear', EXPENSES)
        # the reset button is only enabled while a filter is active
        clear = tag(EXPENSES, 'data-expense-filter-clear')
        self.assertIn('ClearFilters', clear)
        self.assertIn('disabled="@(!HasActiveFilters)"', clear)

    def test_each_active_filter_can_be_removed_individually(self):
        self.assertIn('data-expense-filter-from-chip', EXPENSES)
        self.assertIn('data-expense-filter-to-chip', EXPENSES)
        self.assertIn('data-expense-filter-search-chip', EXPENSES)
        self.assertIn('data-expense-filter-category-chip', EXPENSES)
        self.assertIn('ClearFilterDate(true)', EXPENSES)
        self.assertIn('ClearFilterDate(false)', EXPENSES)

    def test_reset_clears_every_filter_and_reloads(self):
        body = EXPENSES.split('private async Task ClearFilters()', 1)[1].split('private async Task ClearFilterDate', 1)[0]
        for assignment in ('from = null;', 'to = null;', 'catFilter = 0;', 'search = "";'):
            self.assertIn(assignment, body)
        self.assertIn('await ApplyFilter();', body)

    def test_removing_one_date_keeps_the_other(self):
        body = EXPENSES.split('private async Task ClearFilterDate(bool isFrom)', 1)[1].split('private async Task ClearFilterSearch', 1)[0]
        self.assertIn('if (isFrom) from = null; else to = null;', body)
        # never touches the opposite end of the range by accident
        self.assertNotIn('from = null;\n        to = null;', body)

    def test_empty_dates_are_simply_absent_from_the_request(self):
        # the API only receives from/to when they have a value, so clearing = no filter
        body = SERVICES.split('public Task<PagedResult<ExpenseDto>> GetExpensesAsync', 1)[1].split('public Task<ExpenseReportDto> GetReportAsync', 1)[0]
        self.assertIn('if (from.HasValue) url += $"&from={from:yyyy-MM-dd}";', body)
        self.assertIn('if (to.HasValue) url += $"&to={to:yyyy-MM-dd}";', body)

    def test_category_select_reflects_programmatic_reset(self):
        # without an explicit value binding, clearing catFilter left the old choice on screen
        select = tag(EXPENSES, 'data-expense-filter-category')
        self.assertIn('value="@catFilter"', select)
        self.assertIn('@onchange="OnCatFilter"', select)

    def test_reversed_range_is_explained(self):
        self.assertIn('data-expense-filter-range-error', EXPENSES)
        self.assertIn('تاریخ شروع بعد از تاریخ پایان است', EXPENSES)


class FilterChipsAreStyled(unittest.TestCase):
    def test_chip_styles_and_remove_button_exist(self):
        self.assertIn('[data-expense-filter-chips]', CSS)
        self.assertIn('.filter-chip-x', CSS)

    def test_chips_use_the_icon_font_not_the_bootstrap_svg(self):
        # btn-close depends on a data-URI SVG that a custom build can drop; use bi icons.
        self.assertNotIn('btn-close', EXPENSES)
        self.assertEqual(EXPENSES.count('filter-chip-x'), 4)


class FormDateStaysRequired(unittest.TestCase):
    """The document date is mandatory, so it must not become clearable."""

    def test_document_date_picker_has_no_clear_button(self):
        picker = tag(EXPENSES, '@bind-Value="expDate"')
        self.assertNotIn('AllowClear', picker)

    def test_save_still_defaults_a_missing_document_date(self):
        self.assertIn('form.Date = expDate ?? DateTime.Now;', EXPENSES)


if __name__ == '__main__':
    unittest.main()
