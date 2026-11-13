"""Source contracts plus SQLite date-boundary probes; not an EF/.NET integration test."""
from pathlib import Path
import sqlite3
import unittest

SRC = Path(__file__).resolve().parents[2] / 'src'
CLIENT = (SRC / 'Inventory.Client/Shared/AdminHome.razor').read_text()
SERVICE = (SRC / 'Inventory.Api/Services/Catalog/InventoryService.cs').read_text()
ADMIN = SERVICE.split('public async Task<AdminDashboard> GetAdminDashboardAsync()', 1)[1].split('public async Task<List<ReferrerProductItem>>', 1)[0]

class ShopFinancialDashboardTests(unittest.TestCase):
    def test_permission_matches_financial_endpoint_not_admin_role(self):
        self.assertNotIn('Auth.IsAdmin', CLIENT)
        self.assertIn('Auth.Has("Dashboards.Financial") && admin is not null', CLIENT)
        controller = (SRC / 'Inventory.Api/Controllers/System/DashboardController.cs').read_text()
        self.assertIn('[RequirePermission("Dashboards", "Financial")]', controller)

    def test_financial_load_is_independent_and_errors_are_visible(self):
        init = CLIENT.split('protected override async Task OnInitializedAsync()', 1)[1].split('private bool chartsDrawn;', 1)[0]
        self.assertLess(init.index('admin = await'), init.index('ReportSvc.GetReorderAsync'))
        self.assertEqual(init.count('await LoadSectionAsync('), 4)
        self.assertIn('catch (Exception ex) { loadErrors.Add', init)
        self.assertIn('اطلاعات داشبورد کامل بارگذاری نشده است', CLIENT)

    def test_all_cheque_sources_keep_company_filters_and_own_actions(self):
        for table in ('_db.Cheques', '_db.TrsCheques', '_db.ShopExpenseCheques'):
            self.assertIn(table, ADMIN)
        self.assertNotIn('IgnoreQueryFilters', ADMIN)
        self.assertIn('c.Kind == ExpenseChequeKind.Issued && c.State == ExpenseChequeState.Outstanding', ADMIN)
        self.assertIn('if (c.Kind == ChequeKind.Issued) dash.IssuedCheques.Add(alert);', ADMIN)
        clear = CLIENT.split('private async Task ClearCheque(ChequeAlertItem c)', 1)[1].split('private ChequeAlertItem?', 1)[0]
        self.assertLess(clear.index('if (c.Source != "Invoice")'), clear.index('DashboardSvc.ClearChequeAsync'))
        self.assertIn('Navigation.NavigateTo', clear)
        self.assertIn('return;', clear)

    def test_due_today_includes_time_component(self):
        self.assertNotIn('DueDate <= today', ADMIN)
        for field in ('t.DueDate < tomorrow', 'i.DueDate < tomorrow', 'c.DueDate < tomorrow'):
            self.assertIn(field, ADMIN)
        with sqlite3.connect(':memory:') as db:
            db.execute('CREATE TABLE due(id INTEGER, date TEXT)')
            db.executemany('INSERT INTO due VALUES (?,?)', [(1,'2026-11-08 16:00:00'),(2,'2026-11-09 00:00:00'),(3,'2026-11-09 23:59:59'),(4,'2026-11-10 00:00:00')])
            rows = db.execute('SELECT id FROM due WHERE date < ? ORDER BY id', ('2026-11-10 00:00:00',)).fetchall()
        self.assertEqual(rows, [(1,), (2,), (3,)])

    def test_installments_are_customer_debt_not_supplier_payables(self):
        self.assertIn('t.Type != TransactionType.Sale) continue;', ADMIN)
        self.assertIn('if (cancelledAll.Contains(i.TransactionId)) continue;', ADMIN)

    def test_30_day_window_can_cross_quarter_and_quarter_totals_stay_scoped(self):
        self.assertIn('new[] { quarterStart, weekStart, today.AddDays(-29) }.Min()', ADMIN)
        self.assertIn('SalesQuarter = sales.Where(t => t.Date >= quarterStart)', ADMIN)
        self.assertIn('if (t.Date >= quarterStart) dash.ProfitQuarter += profit;', ADMIN)
        self.assertIn('dash.PayCheque = activeQuarterSales.Sum(ChequeOf);', ADMIN)
        self.assertIn('t.Date < tomorrow', ADMIN)

if __name__ == '__main__':
    unittest.main()
