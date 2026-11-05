"""Check report wiring and authorization; EF/Razor behavior still needs .NET execution."""
import unittest
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2] / 'src'
def read(path): return (ROOT / path).read_text(encoding='utf8')

class DedicatedReportContract(unittest.TestCase):
    def test_income_report_uses_dated_operational_events_and_validates_history(self):
        s = read('Inventory.Api/Services/Reports/OperationsReportsService.cs')
        start = s.index('public async Task<ChequeIncomeReportDto> GetChequeIncomeReportAsync')
        s = s[start:s.index('public async Task<OperationsDashboardDto>', start)]
        for part in ('SaleChequeChargeEvents.AsNoTracking()', 'e.Date >= from.Value', 'e.Date < end',
                     'Transaction.PartyId == partyId', 'SaleChequeSettlementLedger.EnsureSettledAmountConsistentAsync',
                     'EventId = item.Id', 'Income = item.IncomeDelta', 'result.TotalIncome += item.IncomeDelta'):
            self.assertIn(part, s)
        for forbidden in ('IgnoreQueryFilters', 'AccVoucher.Status', 'AccVoucherLines', 'AccFiscalYears'):
            self.assertNotIn(forbidden, s)
        dto = read('Inventory.Shared/Dtos/OperationsReportDtos.cs')
        self.assertIn('int EventId', dto)
        self.assertIn('int VoucherNumber', dto)  # old numeric JSON shape remains valid
        self.assertIn('item.AccVoucher?.Number ?? 0', s)  # no invented voucher; preserve historical number

    def test_api_and_ui_require_profit_permission(self):
        controller = read('Inventory.Api/Controllers/Reports/OperationsReportsController.cs')
        start = controller.index('[HttpGet("cheque-income")]')
        section = controller[start:controller.index('/// <summary>', start)]
        self.assertIn('ShopPermissionAuthorizer.OrderType(await permissions.GetAsync(User), TransactionType.Sale, false)', section)
        self.assertIn('ForbiddenUnlessAnyAsync("Orders", "ViewProfit")', section)
        page = read('Inventory.Client/Pages/Operations/ChequeIncomeReport.razor')
        self.assertIn('Auth.Has("Orders.ViewProfit")', page)
        self.assertIn('OrderSvc.GetChequeIncomeReportAsync(from, to, partyId)', page)
        self.assertIn('row.Income.Money()', page)
        self.assertIn('report.TotalIncome.Money()', page)
        self.assertIn('/operations/reports/cheque-income', read('Inventory.Client/Pages/Operations/OperationsOrderReport.razor'))

if __name__ == '__main__': unittest.main()
