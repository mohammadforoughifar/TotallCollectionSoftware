"""Contract checks only; the EF statement probe must also be run with .NET."""
import unittest
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2] / 'src'
class StatementContract(unittest.TestCase):
    def test_detail_keeps_charge_separate_from_goods_and_balance(self):
        source = (ROOT / 'Inventory.Api/Services/Catalog/InventoryService.cs').read_text(encoding='utf8')
        detail = source[source.index('public async Task<PartyStatementDetailDto> GetPartyStatementDetailAsync'):]
        for part in ('LoadChequeStatementHistoryAsync(new[] { partyId })', 'saleHistory?.CurrentIncome',
                     'saleHistory?.InitialIncome', 'result.SaleGoodsTotal += debit', 'result.SaleChequeIncomeTotal += income',
                     'Title = "تاخیر تادیه چک"', 'Title = "تعدیل تاخیر تادیه چک"',
                     'result.SaleChequeIncomeTotal += adjustment.Amount', 'result.TotalDebit += income',
                     't.CashAmount : t.SettledAmount', '"لغو وصول چک" : "وصول چک"', 'debit += t.SettledAmount'):
            self.assertIn(part, detail)
        self.assertIn('history.Sum(e => e.IncomeDelta) != charge.Amount - charge.CustomerCredit', source)
        self.assertIn('lines.Sum(l => l.Credit - l.Debit) != item.IncomeDelta', source)

    def test_last_day_is_inclusive_in_summary_and_detail(self):
        source = (ROOT / 'Inventory.Api/Services/Catalog/InventoryService.cs').read_text(encoding='utf8')
        reports = source[source.index('public async Task<PartyStatementReportDto> GetPartyStatementSummaryReportAsync'):]
        self.assertGreaterEqual(reports.count('t.Date.Date <= to.Value.Date'), 2)
        self.assertGreaterEqual(reports.count('t.Date.Date < from.Value.Date'), 2)
        self.assertIn('adjustment.Date.Date <= to.Value.Date', reports)
        self.assertIn('x.Date.Date < from.Value.Date', reports)

    def test_customer_list_uses_verified_history_and_net_settlement(self):
        source = (ROOT / 'Inventory.Api/Services/Catalog/InventoryService.cs').read_text(encoding='utf8')
        section = source[source.index('public async Task<List<Party>> GetPartiesAsync'):source.index('public async Task<Party> SavePartyAsync')]
        for item in ('LoadChequeStatementHistoryAsync(ids)', 'history.Adjustments.Sum(a => a.Amount)',
                     'history.Receipts.Sum(r => r.Amount)', 't.Amount - Math.Max(0, t.SettledAmount)',
                     '-t.Amount + Math.Max(0, t.SettledAmount)'):
            self.assertIn(item, section)

    def test_dashboard_does_not_subtract_cash_or_cheque_receipts_twice(self):
        source = (ROOT / 'Inventory.Api/Services/Catalog/InventoryService.cs').read_text(encoding='utf8')
        dashboard = source[source.index('public async Task<AdminDashboard> GetAdminDashboardAsync'):source.index('public async Task<SaleChequeReschedulePreview> PreviewSaleChequeRescheduleAsync')]
        self.assertIn('t.Amount - customerCredit - CashOf(t) - ChequeOf(t) - InstOf(t)', dashboard)
        self.assertIn('t.SettledAmount - CashOf(t) - paidInstallments - collectedCheques.GetValueOrDefault(t.Id)', dashboard)
        self.assertIn('Amount = outstandingCredit', dashboard)
        self.assertIn('if (outstandingCredit == 0) continue', dashboard)

    def test_ui_displays_period_sums(self):
        s = (ROOT / 'Inventory.Client/Pages/Operations/PartyStatementsReport.razor').read_text(encoding='utf8')
        self.assertIn('selectedPartyDetail.SaleGoodsTotal.Money()', s)
        self.assertIn('selectedPartyDetail.SaleChequeIncomeTotal.Money()', s)
if __name__ == '__main__': unittest.main()
