"""SQLite schema and structural contract checks for atomic return workflow."""
import re
import sqlite3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'src'


class ReturnWorkflowSchema(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(':memory:')
        self.db.executescript('''
            PRAGMA foreign_keys=ON;
            CREATE TABLE Transactions (Id INTEGER PRIMARY KEY);
            CREATE TABLE TransactionLines (Id INTEGER PRIMARY KEY);
            CREATE TABLE SaleChequeCharges (Id INTEGER PRIMARY KEY);
            CREATE TABLE AccVouchers (Id INTEGER PRIMARY KEY);
            CREATE TABLE SaleChequeChargeEvents (Id INTEGER PRIMARY KEY);
            INSERT INTO Transactions VALUES(1),(2),(3);
            INSERT INTO TransactionLines VALUES(10),(20),(30);
            INSERT INTO SaleChequeCharges VALUES(5);
            INSERT INTO AccVouchers VALUES(7),(8);
            INSERT INTO SaleChequeChargeEvents VALUES(9);
        ''')
        text = (ROOT / 'Inventory.Api/Data/SaleChequeReturnSchemaV1.cs').read_text(encoding='utf8')
        sql = re.search(r'public const string Sqlite = @"(.*?)";', text, re.S).group(1).replace('""', '"')
        self.db.executescript(sql)
        self.db.executescript(sql)

    def tearDown(self):
        self.db.close()

    def test_unique_constraints_and_foreign_keys(self):
        self.db.execute('''
            INSERT INTO SaleChequeReturns
            (OperationId, SaleTransactionId, ReturnTransactionId, SaleChequeChargeId, AccVoucherId, SaleChequeChargeEventId,
             SalesAccountId, VatAccountId, IncomeAccountId, PartyAccountId, Date,
             GoodsAmount, VatAmount, IncomeAmount, EpochRescheduleId, EpochBase, EpochIncome,
             PreviouslyReturnedBaseInEpoch, BalanceAfter, UncollectedChequeExcess, Decision, CreatedAt)
            VALUES ('op-ret-1', 1, 2, 5, 7, 9, 101, 102, 103, 104, '2026-06-10',
                    '90', '10', '25', NULL, '200', '50', '0', '125', '125', 1, '2026-06-10')
        ''')
        ret_id = self.db.execute('SELECT Id FROM SaleChequeReturns').fetchone()[0]
        self.db.execute('''
            INSERT INTO SaleChequeReturnLines
            (SaleChequeReturnId, OriginalLineId, ReturnLineId, Quantity, GoodsAmount, VatAmount)
            VALUES (?, 10, 20, '1', '90', '10')
        ''', (ret_id,))
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute('''
                INSERT INTO SaleChequeReturns
                (OperationId, SaleTransactionId, ReturnTransactionId, SaleChequeChargeId, AccVoucherId, SaleChequeChargeEventId,
                 SalesAccountId, VatAccountId, IncomeAccountId, PartyAccountId, Date,
                 GoodsAmount, VatAmount, IncomeAmount, EpochBase, EpochIncome,
                 PreviouslyReturnedBaseInEpoch, BalanceAfter, UncollectedChequeExcess, Decision, CreatedAt)
                VALUES ('op-ret-1', 1, 3, 5, 8, NULL, 101, NULL, 103, 104, '2026-06-11',
                        '50', '0', '0', '100', '0', '0', '75', '0', 0, '2026-06-11')
            ''')
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute('DELETE FROM Transactions WHERE Id = 2')

    def test_service_and_guards_wiring(self):
        svc = (ROOT / 'Inventory.Api/Services/Invoicing/SaleChequeReturnService.cs').read_text(encoding='utf8')
        for part in ('IsolationLevel.Serializable', 'transaction.RollbackAsync()', 'db.ChangeTracker.Clear()',
                     'SaleChequeReturnPlanner.PlanLine(', 'SaleChequeReturnPlanner.Plan(',
                     'SaleChequeChargeEventKind.GoodsReturn',
                     'SaleChequeReturnDecision.ReplaceCheque', 'SaleChequeReturnDecision.KeepChequeForGoods',
                     'TransactionType.SaleReturn', 'RebuildLegacyStockAsync'):
            self.assertIn(part, svc)
        acc = (ROOT / 'Inventory.Api/Services/Accounting/AccountingService.cs').read_text(encoding='utf8')
        self.assertIn('SaleChequeReturns.IgnoreQueryFilters().AnyAsync', acc)
        init = (ROOT / 'Inventory.Api/Data/DbInitializer.cs').read_text(encoding='utf8')
        self.assertEqual(init.count('SaleChequeReturnSchemaV1.EnsureAsync(db)'), 2)


if __name__ == '__main__':
    unittest.main()
