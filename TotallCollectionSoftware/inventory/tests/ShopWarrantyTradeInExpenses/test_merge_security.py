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


    def test_catalog_permissions_are_scoped_to_matching_endpoints(self):
        auth = (ROOT / 'src/Inventory.Api/Infrastructure/ShopPermissionAuthorizer.cs').read_text(encoding='utf-8')
        for controller, section in (('Products', 'Products'), ('Categories', 'Categories'), ('Units', 'Units')):
            self.assertRegex(auth, rf'"{controller}" when operation is [^\n]+\n\s*=> Section\(permissions, "{section}", writeShop\)')
        for section in ('Warehouses', 'GoodsIssue', 'Referrers'):
            self.assertIn(f'Section(permissions, "{section}", writeShop)', auth)
        self.assertIn('controller == "Kardex" && operation == "Get"', auth)
        treasury = (ROOT / 'src/Inventory.Api/Controllers/Treasury/TreasuryController.cs').read_text(encoding='utf-8')
        account_controller = treasury.split('class TrsAccountsController', 1)[1].split('class TrsVouchersController', 1)[0]
        # General save, strict shop save, and delete each retain an explicit write check.
        self.assertEqual(account_controller.count('HasAsync("Shop", "Banks.Manage")'), 3)
        shop_save = account_controller.split('SaveShop(', 1)[1].split('[HttpDelete', 1)[0]
        self.assertIn('ForbiddenUnlessAsync("TrsAccounts", dto.Id == 0 ? "Create" : "Update")', shop_save)

    def test_quick_party_create_cannot_edit_or_grant_directory_access(self):
        controller = (ROOT / 'src/Inventory.Api/Controllers/Catalog/PartiesController.cs').read_text(encoding='utf-8')
        quick = controller.split('ShopQuickCreate(', 1)[1].split('/// <summary>ایجاد یا ویرایش طرف حساب.', 1)[0]
        self.assertIn('party.Id != 0', quick)
        self.assertIn('PartyType.Customer or PartyType.Supplier', quick)
        for perm in ('Shop.Sale.Manage', 'Shop.Purchase.Manage', 'Shop.Repairs.Manage', 'Shop.GoodsIssue.Manage'):
            self.assertIn(perm, quick)
        self.assertIn('if (!allowed) return await Save(party)', quick)
        self.assertIn('_db.Referrers.AsNoTracking().AnyAsync', quick)
        self.assertIn('PersistWithAuditAsync(party)', quick)
        direct_read = controller.split('private static bool CanReadAll(', 1)[1].split('private static bool CanReadDirectory(', 1)[0]
        self.assertNotIn('Shop.Parties.', direct_read)  # statements retain a separate permission
        lookup = controller.split('ShopCustomers(', 1)[1].split('ShopQuickCreate(', 1)[0]
        self.assertIn('GetInvoicePartiesAsync(PartyType.Customer)', lookup)
        self.assertIn('"repairs" => "Repairs"', lookup)
        self.assertIn('"goods-issue" => "GoodsIssue"', lookup)
        self.assertIn('_ => null', lookup)
        modal = (ROOT / 'src/Inventory.Client/Shared/PartyFormModal.razor').read_text(encoding='utf-8')
        self.assertIn('ShopQuickCreate && !IsEdit', modal)
        self.assertIn('PartySvc.SaveShopQuickAsync(party)', modal)
        visit = (ROOT / 'src/Inventory.Client/Pages/SalesVisit/VisitForm.razor').read_text(encoding='utf-8')
        self.assertNotIn('ShopQuickCreate=', visit)  # visitor referrer scope remains enforced

    def test_financial_cross_section_filters_remain_independent(self):
        expenses = (ROOT / 'src/Inventory.Api/Controllers/Finance/ExpensesController.cs').read_text(encoding='utf-8')
        for action, method in (('Read', 'AvailableReceivedCheques'), ('Update', 'CreateReceivedCheque'), ('Update', 'ReceivedChequeStatus')):
            self.assertRegex(expenses, rf'\[RequirePermission\("Orders", "{action}"\)\]\s+public async Task<IActionResult> {method}')
        tradein = (ROOT / 'src/Inventory.Api/Controllers/Repairs/ShopTradeInController.cs').read_text(encoding='utf-8')
        self.assertIn('[RequirePermission("Orders", "Update")]', tradein)
        auth = (ROOT / 'src/Inventory.Api/Infrastructure/ShopPermissionAuthorizer.cs').read_text(encoding='utf-8')
        self.assertIn('Section(permissions, "Sale", true) || Section(permissions, "Installments", true)', auth)


if __name__ == '__main__':
    unittest.main()
