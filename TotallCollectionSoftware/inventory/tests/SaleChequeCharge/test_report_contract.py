"""Safeguard sales report filter wiring; this does not execute EF/Razor."""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'src'
def read(path):
    return (ROOT / path).read_text(encoding='utf8')

class ReportContract(unittest.TestCase):
    def test_filter_reaches_both_endpoints_and_services(self):
        for path in ('Inventory.Api/Controllers/Sales/OrdersController.cs',
                     'Inventory.Api/Controllers/Reports/OperationsReportsController.cs',
                     'Inventory.Api/Services/Catalog/InventoryService.cs',
                     'Inventory.Api/Services/Reports/OperationsReportsService.cs',
                     'Inventory.Client/Services/InventoryApiServices.cs'):
            self.assertIn('hasChequeIncome', read(path), path)
        ui = read('Inventory.Client/Pages/Operations/OperationsOrderReport.razor')
        self.assertIn('page, PageSize, SelectedChequeIncome', ui)
        self.assertIn('warehouseId, SelectedChequeIncome', ui)
        self.assertIn('order.GoodsAmount.MoneyExact()', ui)
        self.assertIn('order.ChequeIncomeAmount.MoneyExact()', ui)
        self.assertIn('summary.TotalAmount.MoneyExact()', ui)

    def test_statistics_use_goods_without_income(self):
        s = read('Inventory.Api/Services/Reports/OperationsReportsService.cs')
        self.assertIn('result.GoodsAmount / result.DocumentCount', s)
        self.assertIn('t.Amount - (includeChequeIncome ? chargeById.GetValueOrDefault(t.Id) : 0m)', s)
        self.assertIn('includeChequeIncome ? chequeIncomeTotal : 0m', s)
        self.assertIn('documents.Sum(t => t.Lines.Count)', s)
        self.assertIn('documents.Sum(t => t.Lines.Sum(l => l.Quantity))', s)

if __name__ == '__main__': unittest.main()
