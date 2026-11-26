"""قرارداد منبع برای نوار فیلتر فشردهٔ «طرح تعویض».

این تست فقط ساختار منبع را می‌سنجد؛ رندر واقعی Blazor در این محیط اجرا نمی‌شود.

    python3 -m unittest discover -s inventory/tests/TradeInFilterBar -p 'test_*.py' -q
"""
import unittest
from pathlib import Path

PAGE = (Path(__file__).resolve().parents[2] / 'src/Inventory.Client/Pages/Repairs/TradeInTab.razor').read_text(encoding='utf-8')


class CompactFilterBar(unittest.TestCase):
    def test_bar_is_one_compact_row_with_search_status_and_dates(self):
        self.assertIn('class="ti-filter-bar d-print-none"', PAGE)
        self.assertIn('class="ti-search-input"', PAGE)
        self.assertIn('class="form-select form-select-sm ti-status"', PAGE)
        self.assertIn('class="ti-control ti-date-range"', PAGE)
        # دو تاریخ در یک گروه با خط تیره، نه دو ستون جدا در گرید
        self.assertIn('class="ti-range-dash"', PAGE)

    def test_filters_apply_live_without_an_apply_button(self):
        self.assertNotIn('اعمال فیلتر', PAGE)
        self.assertIn('@bind="search"', PAGE)
        self.assertIn('@bind:event="oninput"', PAGE)
        self.assertIn('@bind:after="DebouncedLoadAsync"', PAGE)
        self.assertIn('@bind="statusFilter" @bind:after="Load"', PAGE)
        self.assertIn('ValueChanged="OnFromChanged"', PAGE)
        self.assertIn('ValueChanged="OnToChanged"', PAGE)
        # وضعیت خالی یعنی «همه وضعیت‌ها» و نباید به‌صورت status= به API برود
        self.assertIn('if (!string.IsNullOrWhiteSpace(statusFilter)) query.Add($"status=', PAGE)
        self.assertIn('string.IsNullOrWhiteSpace(statusFilter) ||', PAGE)

    def test_active_filters_are_shown_as_removable_chips(self):
        self.assertIn('class="ti-chips d-print-none"', PAGE)
        self.assertEqual(PAGE.count('class="ti-chip"'), 5)
        self.assertIn('@onclick="ClearSearchAsync"', PAGE)
        self.assertIn('@onclick="ClearStatusAsync"', PAGE)
        self.assertIn('@onclick="ClearFromAsync"', PAGE)
        self.assertIn('@onclick="ClearToAsync"', PAGE)
        self.assertIn('private string StatusText => statusFilter switch', PAGE)

    def test_table_never_disappears_while_typing(self):
        self.assertIn('@if (loading && items.Count == 0)', PAGE)
        self.assertIn('@(loading ? "ti-stale" : "")', PAGE)
        self.assertIn('.ti-stale {', PAGE)

    def test_clear_button_and_count_live_in_the_bar(self):
        self.assertIn('class="ti-filter-tail"', PAGE)
        self.assertIn('@onclick="ResetFilters"', PAGE)
        self.assertIn('@items.Count.Num() مورد', PAGE)

    def test_quick_ranges_feed_the_same_from_parameter(self):
        # بازه‌های آماده فیلتر جدید نمی‌سازند؛ فقط «from» را پر می‌کنند تا API ثابت بماند
        self.assertIn('class="ti-presets"', PAGE)
        self.assertIn('private DateTime? EffectiveFrom =>', PAGE)
        self.assertIn('DateTime.Today.AddDays(-(quickRangeDays - 1))', PAGE)
        self.assertEqual(PAGE.count('SetQuickRange('), 5)  # سه دکمه + چیپ + تعریف‌ها
        self.assertIn('بازه: <b>@QuickRangeLabel اخیر</b>', PAGE)
        self.assertNotIn('range=', PAGE)

    def test_inverted_range_is_explained_instead_of_listed(self):
        self.assertIn('تاریخ «از» پس از تاریخ «تا» است', PAGE)
        load = PAGE.split('private async Task Load()', 1)[1].split('private string? statusFilter', 1)[0]
        self.assertLess(load.index('start.Value.Date > to.Value.Date'), load.index('Api.GetAsync'))


if __name__ == '__main__':
    unittest.main()
