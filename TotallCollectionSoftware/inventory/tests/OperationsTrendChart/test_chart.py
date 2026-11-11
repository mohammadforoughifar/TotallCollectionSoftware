"""Source contracts only; not a Razor compilation or live integration test."""
from pathlib import Path
import unittest
ROOT = Path(__file__).resolve().parents[2]
PAGE = (ROOT / 'src/Inventory.Client/Pages/Operations/OperationsDashboard.razor').read_text()
CSS = (ROOT / 'src/Inventory.Client/Pages/Operations/OperationsDashboard.razor.css').read_text()
class ChartTests(unittest.TestCase):
    def test_shared_scale_and_negative_values(self):
        self.assertIn('Math.Abs(value) / ChartRange', PAGE)
        self.assertIn('ChartZero - (value < 0 ? height : 0m)', PAGE)
        self.assertIn('Math.Max(1m, ChartMax - ChartMin)', PAGE)
    def test_permission_filtered_scale(self):
        values = PAGE.split('private IEnumerable<decimal> ChartValues')[1].split('private decimal ChartMax')[0]
        self.assertIn('Auth.CanSee("Orders")', values)
        self.assertIn('Auth.CanSee("Repairs")', values)
    def test_keyboard_scroll_and_exact_values(self):
        self.assertIn('tabindex="0" role="region"', PAGE)
        self.assertIn('overflow-x: auto', CSS)
        self.assertEqual(PAGE.count('class="ops-trend-bar ops-'), 3)
        self.assertIn('.ops-trend-bar:focus span', CSS)
        self.assertIn('point.Sale.Money()', PAGE)
    def test_empty_state_preserved(self):
        self.assertIn('data.Trend.Count == 0', PAGE)
        self.assertIn('در این بازه داده‌ای برای نمودار وجود ندارد', PAGE)
if __name__ == '__main__': unittest.main()
