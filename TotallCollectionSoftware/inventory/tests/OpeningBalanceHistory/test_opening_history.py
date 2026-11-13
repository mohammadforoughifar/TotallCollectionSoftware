"""Source-contract regressions; run with python3 -m unittest discover -s this directory."""
import unittest
from pathlib import Path

SRC = Path(__file__).resolve().parents[2] / 'src'

class OpeningHistoryTests(unittest.TestCase):
    def test_history_is_loaded_on_reopen(self):
        page = (SRC / 'Inventory.Client/Pages/Shop/OpeningBalanceWizard.razor').read_text()
        load = page.split('private async Task LoadDataAsync()', 1)[1].split('private async Task LoadOpeningHistoryAsync()', 1)[0]
        self.assertIn('await LoadOpeningHistoryAsync();', load)
        self.assertIn('openingOnly: true', page)
        self.assertIn('loaded >= result.TotalCount', page)
        for target in ('recentPartyBalances = balances;', 'recentInstallmentOrders = installments;', 'recentStockLines = stocks;'):
            self.assertIn(target, page)
        for kind in ('PARTY', 'INSTALLMENT', 'STOCK'):
            self.assertIn(f'Description = "[OPENING:{kind}] " +', page)
        self.assertNotIn('در این نشست', page)

    def test_filter_is_applied_before_pagination_and_supports_legacy(self):
        service = (SRC / 'Inventory.Api/Services/Catalog/InventoryService.cs').read_text().split('public async Task<PagedResult<Order>> GetOrdersAsync(', 1)[1]
        self.assertLess(service.index('if (openingOnly)'), service.index('var total = await q.CountAsync();'))
        for legacy in ('OPN-SVC', 'OPN-GDS', 'مانده اقساط بدهی باز اول دوره', 'موجودی اولیه اول دوره انبار'):
            self.assertIn(legacy, service)

    def test_filter_is_forwarded_through_api(self):
        client = (SRC / 'Inventory.Client/Services/InventoryApiServices.cs').read_text()
        controller = (SRC / 'Inventory.Api/Controllers/Sales/OrdersController.cs').read_text()
        self.assertIn('&openingOnly={openingOnly.ToString().ToLowerInvariant()}', client)
        self.assertIn('hasChequeIncome, openingOnly);', controller)

if __name__ == '__main__':
    unittest.main()
