"""Source-level checks for the shop guide's title and restored tabs."""
from pathlib import Path
import unittest

SRC = Path(__file__).resolve().parents[2] / 'src/Inventory.Client'
PAGE = (SRC / 'Pages/Shop/ShopGuide.razor').read_text(encoding='utf-8')
TABS = (SRC / 'Services/TabService.cs').read_text(encoding='utf-8')

class ShopGuideTitleTests(unittest.TestCase):
    def test_guide_sets_browser_layout_and_heading_titles(self):
        self.assertIn('<PageTitle>راهنمای سامانه</PageTitle>', PAGE)
        self.assertIn('Layout.SetTitle("راهنمای سامانه")', PAGE)
        self.assertIn('Title="راهنمای سامانه"', PAGE)

    def test_new_and_previously_saved_tabs_are_named(self):
        self.assertIn('("shop-guide",                  "راهنمای سامانه"', TABS)
        self.assertIn('key.Split(\'?\')[0] == "shop-guide"', TABS)
        self.assertIn('title == "صفحه" || title == "(صفحه)"', TABS)
        self.assertIn('IsStaleShopGuideTitle(key, savedTitle)', TABS)
        self.assertIn('IsStaleShopGuideTitle(key, t.Title)', TABS)

if __name__ == '__main__': unittest.main()
