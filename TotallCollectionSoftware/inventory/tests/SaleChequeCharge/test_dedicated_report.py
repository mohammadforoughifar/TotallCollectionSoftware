"""Check report wiring and authorization; EF/Razor behavior still needs .NET execution."""
import unittest
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2] / 'src'
def read(path): return (ROOT / path).read_text(encoding='utf8')

class DedicatedReportContract(unittest.TestCase):
    def test_income_report_uses_event_and_confirmed_ledger_lines(self):
        s = read('Inventory.Api/Services/Reports/OperationsReportsService.cs')
        start = s.index('public async Task<ChequeIncomeReportDto> GetChequeIncomeReportAsync')
        s = s[start:s.index('public async Task<OperationsDashboardDto>', start)]
        for part in ('SaleChequeChargeEvents.AsNoTracking()', 'VoucherStatus.Confirmed',
                     'e.AccVoucher.Date >= from.Value', 'e.AccVoucher.Date < end',
                     'Transaction.PartyId == partyId', 'l.Credit - l.Debit',
                     'IncomeAccountId\n                ?? throw', 'incomeLines.Count == 0',
                     'income != item.IncomeDelta', 'result.TotalIncome += income'):
            self.assertIn(part, s)
        self.assertNotIn('IncomeAccountId.HasValue', s)
        self.assertNotIn('IgnoreQueryFilters', s)

    def test_api_and_ui_require_profit_permission(self):
        controller = read('Inventory.Api/Controllers/Reports/OperationsReportsController.cs')
        start = controller.index('[HttpGet("cheque-income")]')
        section = controller[start:controller.index('/// <summary>', start)]
        self.assertIn('ForbiddenUnlessAnyAsync("Orders", "View", "Read", "Access")', section)
        self.assertIn('ForbiddenUnlessAnyAsync("Orders", "ViewProfit")', section)
        page = read('Inventory.Client/Pages/Operations/ChequeIncomeReport.razor')
        self.assertIn('Auth.Has("Orders.ViewProfit")', page)
        self.assertIn('OrderSvc.GetChequeIncomeReportAsync(from, to, partyId)', page)
        self.assertIn('row.Income.Money()', page)
        self.assertIn('report.TotalIncome.Money()', page)
        self.assertIn('/operations/reports/cheque-income', read('Inventory.Client/Pages/Operations/OperationsOrderReport.razor'))

if __name__ == '__main__': unittest.main()
