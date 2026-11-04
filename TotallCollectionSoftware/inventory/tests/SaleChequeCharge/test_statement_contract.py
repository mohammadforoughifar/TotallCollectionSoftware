"""Contract checks only; the EF statement probe must also be run with .NET."""
import unittest
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2] / 'src'
class StatementContract(unittest.TestCase):
    def test_detail_keeps_charge_separate_from_goods_and_balance(self):
        source = (ROOT / 'Inventory.Api/Services/Catalog/InventoryService.cs').read_text(encoding='utf8')
        detail = source[source.index('public async Task<PartyStatementDetailDto> GetPartyStatementDetailAsync'):]
        for part in ('_db.SaleChequeCharges.AsNoTracking()', 't.Amount - income',
                     'result.SaleGoodsTotal += debit', 'result.SaleChequeIncomeTotal += income',
                     'Title = "تاخیر تادیه چک"', 'result.TotalDebit += income',
                     'credit += t.SettledAmount', 'debit += t.SettledAmount'):
            self.assertIn(part, detail)
        self.assertIn('income < 0 || income > t.Amount', detail)

    def test_last_day_is_inclusive_in_summary_and_detail(self):
        source = (ROOT / 'Inventory.Api/Services/Catalog/InventoryService.cs').read_text(encoding='utf8')
        reports = source[source.index('public async Task<PartyStatementReportDto> GetPartyStatementSummaryReportAsync'):]
        self.assertEqual(reports.count('t.Date.Date <= to.Value.Date'), 2)
        self.assertEqual(reports.count('t.Date.Date < from.Value.Date'), 2)

    def test_ui_displays_period_sums(self):
        s = (ROOT / 'Inventory.Client/Pages/Operations/PartyStatementsReport.razor').read_text(encoding='utf8')
        self.assertIn('selectedPartyDetail.SaleGoodsTotal.Money()', s)
        self.assertIn('selectedPartyDetail.SaleChequeIncomeTotal.Money()', s)
if __name__ == '__main__': unittest.main()
