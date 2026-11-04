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

    def test_unimplemented_installment_route_stays_closed(self):
        api = (ROOT / 'src/Inventory.Api/Services/Catalog/InventoryService.cs').read_text(encoding='utf8')
        self.assertIn('cmd.ChequeCharge is not null && cmd.Installments.Count > 0', api)
        ui = (ROOT / 'src/Inventory.Client/Pages/Orders/OrderForm.razor').read_text(encoding='utf8')
        self.assertIn('saleChequeCharge is not null && useInstallment && installments.Count > 0', ui)


if __name__ == '__main__':
    unittest.main()
