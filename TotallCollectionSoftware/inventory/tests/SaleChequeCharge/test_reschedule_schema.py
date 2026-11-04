"""SQLite schema and source contract only; the C# workflow still requires .NET execution."""
import re
import sqlite3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'src'


class RescheduleSchema(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(':memory:')
        self.db.executescript('''
            PRAGMA foreign_keys=ON;
            CREATE TABLE Cheques (Id INTEGER PRIMARY KEY);
            CREATE TABLE SaleChequeCharges (Id INTEGER PRIMARY KEY);
            CREATE TABLE SaleChequeChargeEvents (Id INTEGER PRIMARY KEY);
            INSERT INTO Cheques VALUES(1);
            INSERT INTO SaleChequeCharges VALUES(2);
            INSERT INTO SaleChequeChargeEvents VALUES(3);
        ''')
        text = (ROOT / 'Inventory.Api/Data/SaleChequeRescheduleSchemaV1.cs').read_text(encoding='utf8')
        sql = re.search(r'public const string Sqlite = @"(.*?)";', text, re.S).group(1).replace('""', '"')
        self.db.executescript(sql)
        self.db.executescript(sql)

    def tearDown(self):
        self.db.close()

    def insert(self, op, event=None):
        self.db.execute('''INSERT INTO SaleChequeReschedules
            (OperationId, ChequeId, SaleChequeChargeId, SaleChequeChargeEventId, PreviousDueDate, NewDueDate,
             Date, BaseAmount, RatePercent, PeriodDays, IncomeDelta, CreatedAt)
            VALUES (?, 1, 2, ?, '2026-07-01', '2026-07-11', '2026-06-01', '200', '3', 30, '2', '2026-06-01')''', (op, event))

    def test_idempotent_schema_and_unique_operations(self):
        self.insert('op-1', 3)
        with self.assertRaises(sqlite3.IntegrityError): self.insert('op-1')
        with self.assertRaises(sqlite3.IntegrityError): self.insert('op-2', 3)
        with self.assertRaises(sqlite3.IntegrityError): self.db.execute('DELETE FROM Cheques WHERE Id=1')
        self.insert('zero-adjustment-1')
        self.insert('zero-adjustment-2')
        self.assertEqual(self.db.execute('SELECT COUNT(*) FROM SaleChequeReschedules').fetchone()[0], 3)

    def test_transaction_and_history_guards(self):
        text = (ROOT / 'Inventory.Api/Services/Invoicing/SaleChequeRescheduling.cs').read_text(encoding='utf8')
        for part in ('IsolationLevel.Serializable', 'transaction.RollbackAsync()', 'db.ChangeTracker.Clear()',
                     'x.OperationId == operationId', 'cheque.DueDate.Date != expectedDueDate',
                     'SaleChequeChargeMath.IncrementalDateAdjustment(', 'await CalculateDeltaAsync(', 'SaleChequeCorrectionAmounts.ForDueDate(delta)',
                     'SaleChequeChargeEventKind.DueDateAdjustment', 'charge.CustomerCredit = newCredit',
                     'sale.Amount = invoiceTotal', 'db.SaleChequeReschedules.Add(record)',
                     'r.TransactionId == sale.Id && (r.Date > operationDate || r.ReversalDate > operationDate)'):
            self.assertIn(part, text)
        self.assertNotIn('CommitAsync', text)
        initializer = (ROOT / 'Inventory.Api/Data/DbInitializer.cs').read_text(encoding='utf8')
        self.assertEqual(initializer.count('SaleChequeRescheduleSchemaV1.EnsureAsync(db)'), 2)


if __name__ == '__main__':
    unittest.main()
