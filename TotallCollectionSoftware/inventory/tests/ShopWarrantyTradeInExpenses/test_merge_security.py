"""Merge safeguards for independent ERP/shop access and restored shop routes.

Source contracts only: runtime authorization tests are still required.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]


class ShopMergeSecurityTests(unittest.TestCase):
    def test_shop_routes_remain_gated_without_global_erp_permission_aliases(self):
        src = (ROOT / 'src/Inventory.Shared/ShopSections.cs').read_text(encoding='utf-8')
        rows = re.findall(r'new\("(\w+)"[^\n]*\n\s*Array\.Empty<string>\(\), \[([^\]]*)\]\)', src)
        routes = dict(rows)
        self.assertGreaterEqual(len(routes), 22)
        self.assertIn('"orders/sale"', routes['Sale'])
        self.assertIn('"repairs"', routes['Repairs'])
        self.assertIn('"operations/reports/warranty"', routes['Repairs'])
        self.assertIn('"operations/reports/expenses"', routes['Expenses'])
        self.assertIn('"shop/pricing"', routes['Pricing'])
        self.assertRegex(src, r'AliasAllows\([^\n]*\)\s*=>\s*false')

    def test_explicit_controller_access_is_not_a_generic_erp_grant(self):
        auth = (ROOT / 'src/Inventory.Api/Infrastructure/ShopPermissionAuthorizer.cs').read_text(encoding='utf-8')
        non_order = auth.split('if (!module.Equals("Orders"', 1)[1].split('// ViewProfit', 1)[0]
        for name in ('"ShopWarranty"', '"ShopTradeIn"', '"Expenses"', '"ExpenseCategories"'):
            self.assertIn(name, non_order)
        self.assertIn('known && Section(permissions, "Repairs", writeShop)', non_order)
        self.assertIn('known && Section(permissions, "Expenses", writeShop)', non_order)
        self.assertIn('_ => false', non_order)
        self.assertIn('return false;', non_order)
        self.assertIn('action.Equals("ViewProfit", StringComparison.OrdinalIgnoreCase)', auth)


if __name__ == '__main__':
    unittest.main()
