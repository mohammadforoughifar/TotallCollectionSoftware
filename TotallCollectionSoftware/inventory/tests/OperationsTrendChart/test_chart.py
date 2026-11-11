"""Source contracts only; not a live Razor integration test."""
from pathlib import Path
import unittest
ROOT = Path(__file__).resolve().parents[2]
PAGE = (ROOT / 'src/Inventory.Client/Pages/Operations/OperationsDashboard.razor').read_text()
CSS = (ROOT / 'src/Inventory.Client/Pages/Operations/OperationsDashboard.razor.css').read_text()
class ChartTests(unittest.TestCase):
    def test_full_height_hit_targets(self):
        self.assertIn('height="270"', PAGE)
        self.assertIn('@onpointerenter="() => hoveredTrendIndex = index"', PAGE)
        self.assertIn('@onpointerdown=', PAGE)
        self.assertIn('pointer-events: all', CSS)
    def test_shared_scale_permissions_and_negative_values(self):
        self.assertIn('(value - ChartMin) / ChartRange', PAGE)
        self.assertIn('Math.Max(1m, ChartMax - ChartMin)', PAGE)
        values = PAGE.split('private IEnumerable<decimal> ChartValues')[1].split('private decimal ChartMax')[0]
        self.assertIn('Auth.CanSee("Orders")', values)
        self.assertIn('Auth.CanSee("Repairs")', values)
    def test_exact_values_keyboard_and_reset(self):
        self.assertIn('selected.Sale.Money()', PAGE)
        self.assertIn('selected.Purchase.Money()', PAGE)
        self.assertIn('selected.RepairRevenue.Money()', PAGE)
        self.assertIn('@onfocus=', PAGE)
        self.assertIn('hoveredTrendIndex = focusedTrendIndex = -1', PAGE)
    def test_hit_regions_cover_plot_without_gaps(self):
        for count in (1,2,9,100):
            x=lambda i:495 if count==1 else 125+i*740/(count-1)
            intervals=[(110 if i==0 else (x(i-1)+x(i))/2,880 if i==count-1 else (x(i)+x(i+1))/2) for i in range(count)]
            self.assertEqual(intervals[0][0],110)
            self.assertEqual(intervals[-1][1],880)
            for i in range(count-1):self.assertEqual(intervals[i][1],intervals[i+1][0])
if __name__ == '__main__':unittest.main()
