"""Source-level style contracts; does not replace a browser visual check."""
from pathlib import Path
import re
import unittest

PAGE = (Path(__file__).resolve().parents[2] / 'src/Inventory.Client/Pages/Operations/OperationsOrderReport.razor').read_text()

class OrderTrendStyleTests(unittest.TestCase):
    def test_sparse_bars_have_a_centered_width_cap(self):
        bar = re.search(r'\.order-trend-bar \{([^}]+)', PAGE).group(1)
        self.assertIn('max-width: 20px', bar)
        self.assertIn('width: 40%', bar)
        self.assertIn('left: 50%', bar)
        self.assertIn('translateX(-50%)', bar)
        # Even with a single 1200px slot the bar stays 20px wide;
        # dense print columns still shrink rather than overlap.
        for slot in (1200, 600, 300, 52, 8):
            self.assertLessEqual(min(slot * .4, 20), 20)
            self.assertLess(min(slot * .4, 20), slot)

    def test_minimal_palette_and_no_vertical_cell_borders(self):
        plot = re.search(r'\.order-trend-plot \{([^}]+)', PAGE).group(1)
        self.assertNotIn('border-left', plot)
        self.assertIn('.order-trend-purchase { background: #818cf8; }', PAGE)
        self.assertIn('.order-trend-sale { background: #34b995; }', PAGE)

    def test_scrolling_keyboard_and_negative_values_remain_supported(self):
        self.assertIn('overflow-x: auto', PAGE)
        self.assertIn('tabindex="0"', PAGE)
        self.assertIn('order-trend-negative', PAGE)
        self.assertIn('var bottom = value < 0 ? zero - height : zero;', PAGE)
        self.assertIn('.order-trend-column { min-width: 0; flex-basis: 0; }', PAGE)

if __name__ == '__main__':
    unittest.main()
