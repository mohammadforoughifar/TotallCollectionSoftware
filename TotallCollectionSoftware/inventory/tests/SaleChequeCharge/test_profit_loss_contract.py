"""SQLite reporting example and static checks; Program.cs is the EF integration probe."""
import sqlite3
import unittest
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2] / 'src'
def read(path): return (ROOT / path).read_text(encoding='utf8')

class ProfitLossContract(unittest.TestCase):
    def test_additive_accounts_and_no_historical_backfill(self):
        db = sqlite3.connect(':memory:')
        db.execute('CREATE TABLE SaleChequeCharges (TransactionId INTEGER PRIMARY KEY, IncomeAccountId INTEGER)')
        db.execute('INSERT INTO SaleChequeCharges VALUES (1, NULL)')
        db.execute('ALTER TABLE SaleChequeCharges ADD COLUMN PartyAccountId INTEGER NULL')
        self.assertEqual(db.execute('SELECT IncomeAccountId, PartyAccountId FROM SaleChequeCharges').fetchone(), (None, None))
        db.close()
        src = read('Inventory.Api/Data/SaleChequeAccountsSnapshotSchemaV1.cs')
        self.assertIn('if (present.Contains(name)) continue', src)
        self.assertEqual(read('Inventory.Api/Data/DbInitializer.cs').count('SaleChequeAccountsSnapshotSchemaV1.EnsureAsync(db)'), 2)
        self.assertIn('SaleChequeAccountsSnapshotSchemaV1.SqlServer', read('Inventory.Api/Migrations/20261104180000_AddSaleChequeAccountSnapshots.cs'))

    def test_example_isolates_initial_income_from_unrelated_account_activity(self):
        db = sqlite3.connect(':memory:')
        db.executescript('''CREATE TABLE Charges (SaleId INTEGER, IncomeAccountId INTEGER);
            CREATE TABLE Vouchers (Id INTEGER, Source INTEGER, SourceId INTEGER, Status INTEGER);
            CREATE TABLE Events (VoucherId INTEGER, SaleId INTEGER);
            CREATE TABLE Lines (VoucherId INTEGER, AccountId INTEGER, Debit INTEGER, Credit INTEGER);
            INSERT INTO Charges VALUES(1, 2);
            INSERT INTO Vouchers VALUES(1, 6, 1, 1), (2, 0, NULL, 1);
            INSERT INTO Events VALUES(1, 1);
            INSERT INTO Lines VALUES(1, 1, 0, 200), (1, 2, 0, 50), (2, 2, 0, 7);''')
        income = db.execute('''SELECT SUM(l.Credit - l.Debit) FROM Lines l JOIN Vouchers v ON l.VoucherId=v.Id
           JOIN Events e ON v.Id=e.VoucherId
           JOIN Charges c ON e.SaleId=c.SaleId AND l.AccountId=c.IncomeAccountId
           WHERE v.Status=1''').fetchone()[0]
        self.assertEqual(income, 50)
        db.close()

    def test_dashboard_uses_snapshot_and_confirmed_vouchers(self):
        service = read('Inventory.Api/Services/Accounting/AccountingService.cs')
        for part in ('SaleChequeChargeEvents.IgnoreQueryFilters()', 'e.SaleChequeCharge.IncomeAccountId',
                     'l.Voucher!.Status == VoucherStatus.Confirmed', 'e.AccVoucher.Status == VoucherStatus.Confirmed',
                     'incomeLines.Count == 0', 'incomeLines.Sum(l => l.Credit - l.Debit) != item.IncomeDelta',
                     'l.AccountId == incomeAccountId', 'dash.ChequeIncomeTotal += -net'):
            self.assertIn(part, service)
        posting = read('Inventory.Api/Services/Invoicing/SaleChequeChargePosting.cs')
        self.assertNotIn('db.AccVouchers', posting)
        self.assertNotIn('db.FacRules', posting)
        self.assertIn('e.AccVoucherId != null', service)
        ui = read('Inventory.Client/Pages/Accounting/AccFinanceDashboard.razor')
        self.assertIn('data.ChequeIncomeTotal.Money()', ui)
        self.assertIn('مالی/غیرعملیاتی', ui)

if __name__ == '__main__': unittest.main()
