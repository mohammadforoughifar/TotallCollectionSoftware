"""Source contracts. Executable C# helper tests live in Program.cs (requires .NET 8)."""
from pathlib import Path
import re
import unittest

SRC = Path(__file__).resolve().parents[2] / 'src'
def read(rel): return (SRC / rel).read_text()

class ShopInputContracts(unittest.TestCase):
    def test_shop_stock_reports_never_render_quantities_as_integers(self):
        for rel in ('Pages/Reports/Kardex.razor','Pages/Reports/Reorder.razor','Shared/AdminHome.razor','Pages/Repairs/Repairs.razor'):
            page = read('Inventory.Client/' + rel)
            self.assertNotRegex(page, r'(?:TotalStock|ReorderPoint|MaxStock|Shortage|Suggested|InQty|OutQty|i\.Quantity)\.Num\(\)')
        self.assertIn('p.TotalStock.Qty()', read('Inventory.Client/Pages/Catalog/Products.razor'))
        self.assertIn('s.Quantity.Qty()', read('Inventory.Client/Pages/Catalog/Stock.razor'))
        kardex = read('Inventory.Client/Pages/Warehousing/InvKardex.razor')
        self.assertIn('? value.Qty() : value.Num()', kardex)
        self.assertIn('operations/reports/kardex-rial', kardex)
        service = read('Inventory.Api/Services/Catalog/InventoryService.cs')
        stock = service.split('private async Task RecalcStockAsync(',1)[1].split('public async Task<ShopCreditSummary>',1)[0]
        self.assertIn('decimal qty = 0', stock)
        self.assertIn('qty -= l.Quantity;', stock)
        self.assertIn('stock.Quantity = qty;', stock)

    def test_input_events_refresh_local_search_and_reset_page(self):
        credit = read('Inventory.Client/Pages/Shop/ShopCreditManagement.razor')
        self.assertIn('@bind:after="ApplyChequeFilter"', credit)
        self.assertIn('@bind:after="ApplyInstallmentFilter"', credit)
        self.assertNotIn('@onkeyup="ApplyChequeFilter"', credit)
        categories = read('Inventory.Client/Pages/Catalog/Categories.razor')
        self.assertIn('@bind:after="ResetSearchPage"', categories)
        self.assertIn('ResetSearchPage() => page = 1;', categories)

    def test_server_live_search_clear_is_immediate_and_old_results_cannot_win(self):
        for rel in ('Finance/Expenses','Repairs/Repairs','Treasury/TrsVouchers','Treasury/TrsCheques'):
            page = read(f'Inventory.Client/Pages/{rel}.razor')
            self.assertIn('@oninput="OnSearchInput"', page)
            self.assertIn('if (!string.IsNullOrWhiteSpace(search)) await Task.Delay(300, cts.Token);', page)
            self.assertIn('if (version != loadVersion) return;', page)
            self.assertIn('searchCts?.Cancel();', page)
            self.assertIn('cts.Dispose();', page)
            self.assertIn('public void Dispose()', page)
            self.assertNotIn('System.Timers.Timer', page)

    def test_strict_bank_save_is_shop_only_and_authorized(self):
        controller = read('Inventory.Api/Controllers/Treasury/TreasuryController.cs')
        shop = controller.split('[HttpPost("shop")]',1)[1].split('[HttpDelete',1)[0]
        self.assertIn('HasAsync("Shop", "Banks.Manage")', shop)
        self.assertIn('ForbiddenUnlessAsync("TrsAccounts"', shop)
        self.assertLess(shop.index('ShopIdentifierRules.ValidateBank'), shop.index('_svc.SaveAccountAsync'))
        general = controller.split('[HttpPost]',1)[1].split('[HttpPost("shop")]',1)[0]
        self.assertNotIn('ShopIdentifierRules', general)
        for rel in ('Shared/QuickBankModal.razor','Pages/Treasury/TrsAccounts.razor'):
            page = read('Inventory.Client/' + rel)
            self.assertIn('ShopIdentifierRules.ValidateBank(form)', page)
            self.assertIn('AccountSvc.SaveShopAsync(form)', page)

    def test_identifiers_preserve_leading_zeros_and_validate_complete_values(self):
        rules = read('Inventory.Shared/ShopIdentifierRules.cs')
        self.assertIn('Fa.ToEn(value)', rules)
        for check in ('CardNumber.Length != 16','Iban.Length != 24','PostalCode.Length != 10','SayadId.Length != 16'):
            self.assertIn(check, rules)
        self.assertIn("c is >= '0' and <= '9'", rules)
        self.assertNotIn('ParseInt', rules)
        self.assertIn('ValidateParty(party)', read('Inventory.Client/Shared/PartyFormModal.razor'))
        self.assertIn('ValidateParty(dto)', read('Inventory.Api/Services/Catalog/InventoryService.cs'))

    def test_shop_sayad_is_validated_without_changing_generic_treasury_save(self):
        controller = read('Inventory.Api/Controllers/Treasury/TreasuryController.cs').split('class TrsChequesController', 1)[1]
        shop = controller.split('[HttpPost("shop")]', 1)[1].split('[HttpPost("action")]', 1)[0]
        self.assertIn('ForbiddenUnlessAsync("TrsCheques", dto.Id == 0 ? "Create" : "Update")', shop)
        self.assertIn('ShopIdentifierRules.ValidateCheque(dto)', shop)
        opening = read('Inventory.Client/Pages/Shop/OpeningBalanceWizard.razor')
        self.assertIn('ValidateCheque(newCheque)', opening)
        self.assertIn('ChequeSvc.SaveShopAsync(newCheque)', opening)

    def test_management_reuses_invoice_form_and_scopes_card_styles(self):
        management = read('Inventory.Client/Pages/Treasury/TrsAccounts.razor')
        self.assertIn('<QuickBankModal', management)
        self.assertIn('if (a is null) { showQuickCreate = true; return; }', management)
        self.assertEqual(management.count('stat-card shop-account-stat'), 4)
        css = read('Inventory.Client/Pages/Treasury/TrsAccounts.razor.css')
        self.assertIn('border: 1px solid', css)
        self.assertIn('.shop-account-stat', css)
        quick = read('Inventory.Client/Shared/QuickBankModal.razor')
        self.assertIn('if (Visible && !wasVisible) ResetForm();', quick)
        self.assertIn('CompanyId = InitialCompanyId, SortOrder = InitialSortOrder', quick)

if __name__ == '__main__': unittest.main()
