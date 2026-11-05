"""Source-level checks only. Real C# planner probes are in Program.cs and need .NET."""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


class ReturnPlannerContract(unittest.TestCase):
    def test_cumulative_allocation_and_epoch_checks_exist(self):
        src = (ROOT / 'src/Inventory.Shared/SaleChequeReturnPlanner.cs').read_text(encoding='utf8')
        for fragment in ('BigInteger.DivRem', 'Allocated(originalVat, end, originalQuantity)',
                         'previouslyReturnedQuantity > originalQuantity', 'baseAtEpoch - previouslyReturnedBaseInEpoch != remainingBase',
                         'incomeAtEpoch - priorIncome != remainingIncome', 'ProportionalReturn(baseAtEpoch, incomeAtEpoch',
                         'remainingBase + remainingIncome - existingCustomerCredit - settledAmount',
                         'uncollectedCheques - Math.Max(0, after)'):
            self.assertIn(fragment, src)
        self.assertNotIn('SaveChanges', src)

    def test_new_charged_sale_snapshots_cost_before_posting(self):
        api = (ROOT / 'src/Inventory.Api/Services/Catalog/InventoryService.cs').read_text(encoding='utf8')
        section = api[api.index('private async Task FreezeChargedSaleCostsAsync'):api.index('private async Task EnrichSaleProfitAsync')]
        self.assertIn('_db.Database.CurrentTransaction is null', section)
        self.assertIn('ComputeSaleCosts(txns, group.Key, costingMethod, product.PurchasePrice)', section)
        self.assertIn('line.UnitCostSnapshot = decimal.Round(cost, 2)', section)
        self.assertIn('if (product.IsService) continue', section)
        self.assertIn('_db.InvStocks.AnyAsync', section)
        self.assertIn('_db.InvLedger.AnyAsync', section)
        self.assertIn('line.UnitCostSnapshot = null', section)
        create = api[api.index('public async Task<Order> CreateOrderAsync'):api.index('public async Task<PagedResult<Order>> GetOrdersAsync')]
        self.assertLess(create.index('FreezeChargedSaleCostsAsync(txn, products, settings.CostingMethod)'),
                        create.index('SaleChequeChargeRegistration.RegisterAsync'))

    def test_installment_settlement_uses_dedicated_accounting_route(self):
        api = (ROOT / 'src/Inventory.Api/Services/Catalog/InventoryService.cs').read_text(encoding='utf8')
        self.assertIn('(cmd.ChequeCharge is not null || trackShopCash) && cmd.Installments.Any(i => i.IsPaid)', api)
        self.assertIn('SaleChequeInstallmentPosting.ReceiveAsync', api)
        self.assertIn('SaleChequeInstallmentPosting.ReverseAsync', api)
        credit_ui = (ROOT / 'src/Inventory.Client/Pages/Shop/ShopCreditManagement.razor').read_text(encoding='utf8')
        self.assertIn('OrderSvc.ReceiveChequeInstallmentAsync', credit_ui)
        self.assertIn('OrderSvc.ReverseChequeInstallmentReceiptAsync', credit_ui)


if __name__ == '__main__':
    unittest.main()
