"""Execute the actual SQLite repair SQL without requiring the .NET SDK."""
import re
import sqlite3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'src'
SCHEMA = (ROOT / 'Inventory.Api/Data/SaleChequeChargeSchemaV1.cs').read_text(encoding='utf8')
match = re.search(r'public const string Sqlite = @"(.*?)";', SCHEMA, re.S)
if not match:
    raise RuntimeError('SQLite schema constant not found')
SQL = match.group(1).replace('""', '"')

class SchemaTests(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(':memory:')
        self.db.execute('PRAGMA foreign_keys=ON')
        self.db.execute('CREATE TABLE Transactions (Id INTEGER PRIMARY KEY)')
        self.db.execute('INSERT INTO Transactions (Id) VALUES (1), (2)')

    def tearDown(self):
        self.db.close()

    def test_idempotent_and_old_invoices_untouched(self):
        self.db.executescript(SQL)
        self.db.executescript(SQL)
        self.assertEqual(self.db.execute('SELECT COUNT(*) FROM Transactions').fetchone()[0], 2)
        self.assertEqual(self.db.execute('SELECT COUNT(*) FROM SaleChequeCharges').fetchone()[0], 0)
        columns = {row[1] for row in self.db.execute('PRAGMA table_info(SaleChequeCharges)')}
        self.assertIn('CompanyId', columns)
        self.assertNotIn('ProductId', columns)

    def test_old_and_new_invoice_reporting_example(self):
        self.db.executescript(SQL)
        self.db.execute('ALTER TABLE Transactions ADD COLUMN Amount NUMERIC')
        self.db.execute('UPDATE Transactions SET Amount = 250 WHERE Id = 1')
        self.db.execute('UPDATE Transactions SET Amount = 100 WHERE Id = 2')
        self.db.execute("""INSERT INTO SaleChequeCharges
            (TransactionId, BaseAmount, RatePercent, PeriodDays, InitialAmount, Amount, CustomerCredit)
            VALUES (1, 200, 3, 30, 50, 50, 0)""")
        rows = self.db.execute("""SELECT t.Id, t.Amount - COALESCE(c.Amount, 0),
            CAST(COALESCE(c.Amount, 0) AS NUMERIC), t.Amount FROM Transactions t
            LEFT JOIN SaleChequeCharges c ON c.TransactionId=t.Id ORDER BY t.Id""").fetchall()
        self.assertEqual(rows, [(1, 200, 50, 250), (2, 100, 0, 100)])
        with_income = self.db.execute("SELECT COUNT(*) FROM Transactions t WHERE EXISTS (SELECT 1 FROM SaleChequeCharges c WHERE c.TransactionId=t.Id AND CAST(c.Amount AS NUMERIC)>0)").fetchone()[0]
        self.assertEqual(with_income, 1)

    def test_unique_and_cascade(self):
        self.db.executescript(SQL)
        insert = '''INSERT INTO SaleChequeCharges (TransactionId, BaseAmount, RatePercent, PeriodDays, InitialAmount, Amount, CustomerCredit)
                    VALUES (?, '200', '3', 30, '50', '50', '0')'''
        self.db.execute(insert, (1,))
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute(insert, (1,))
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute(insert, (99,))
        self.db.execute('DELETE FROM Transactions WHERE Id = 1')
        self.assertEqual(self.db.execute('SELECT COUNT(*) FROM SaleChequeCharges').fetchone()[0], 0)
        self.assertEqual(self.db.execute('SELECT COUNT(*) FROM Transactions').fetchone()[0], 1)

if __name__ == '__main__':
    unittest.main()
