"""Contract checks for goods-only dashboards. Real EF coverage remains in Program.cs."""
import sqlite3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'src'


class GoodsOnlyDashboard(unittest.TestCase):
    def test_invoices_with_and_without_cheque_income(self):
        db = sqlite3.connect(':memory:')
        db.executescript('''
            CREATE TABLE invoices (id INTEGER PRIMARY KEY, total NUMERIC NOT NULL);
            CREATE TABLE charges (invoice_id INTEGER PRIMARY KEY, income NUMERIC NOT NULL);
            INSERT INTO invoices VALUES (1, 250), (2, 100);
            INSERT INTO charges VALUES (1, 50);
        ''')
        value = db.execute('''SELECT SUM(i.total - COALESCE(c.income, 0))
                              FROM invoices i LEFT JOIN charges c ON c.invoice_id = i.id''').fetchone()[0]
        self.assertEqual(value, 300)
        db.close()

    def test_all_sales_dashboard_series_use_goods_amount(self):
        s = (ROOT / 'Inventory.Api/Services/Catalog/InventoryService.cs').read_text(encoding='utf8')
        start = s.index('public async Task<DashboardSummary> GetDashboardAsync()')
        end = s.index('public async Task<List<ReferrerProductItem>>', start)
        dashboard = s[start:end]
        for fragment in ('Sum(t => GoodsAmount(t, chequeIncomes))',
                         'SalesQuarter = sales.Sum(t => GoodsAmount(t, chequeIncomes))',
                         'Sales = dayTxns.Sum(t => GoodsAmount(t, chequeIncomes))',
                         'Sales = mTxns.Sum(t => GoodsAmount(t, chequeIncomes))'):
            self.assertIn(fragment, dashboard)
        # Payment composition remains based on actual payment parts, not goods-only sales.
        self.assertIn('dash.PayCheque = sales.Sum(ChequeOf)', dashboard)
        ops = (ROOT / 'Inventory.Api/Services/Reports/OperationsReportsService.cs').read_text(encoding='utf8')
        self.assertIn('SaleChequeChargeMath.GoodsTotal(t.Amount, chequeIncomes.GetValueOrDefault(t.Id))', ops)


if __name__ == '__main__':
    unittest.main()
