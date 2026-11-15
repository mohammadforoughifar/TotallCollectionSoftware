"""Execute the actual SQLite history table DDL and check retention/integrity."""
import re
import sqlite3
import unittest
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2] / 'src'
SOURCE = (ROOT / 'Inventory.Api/Data/SaleChequeChargeEventsSchemaV1.cs').read_text(encoding='utf8')
match = re.search(r'public const string Sqlite = @"(.*?)";', SOURCE, re.S)
if not match: raise RuntimeError('events SQLite DDL not found')
SQL = match.group(1).replace('""', '"')

class EventSchemaTests(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(':memory:')
        self.db.execute('PRAGMA foreign_keys=ON')
        self.db.executescript('CREATE TABLE SaleChequeCharges(Id INTEGER PRIMARY KEY); CREATE TABLE AccVouchers(Id INTEGER PRIMARY KEY); INSERT INTO SaleChequeCharges VALUES (1); INSERT INTO AccVouchers VALUES (10),(11);')
        self.db.executescript(SQL)
        self.db.executescript(SQL)

    def tearDown(self): self.db.close()

    def test_event_uniqueness_and_restrict_preserve_financial_history(self):
        insert = '''INSERT INTO SaleChequeChargeEvents(OperationId,SaleChequeChargeId,AccVoucherId,Kind,Date,IncomeDelta)
                    VALUES (?, 1, ?, 1, '2026-06-01', '50')'''
        self.db.execute(insert, ('operation-1', 10))
        with self.assertRaises(sqlite3.IntegrityError): self.db.execute(insert, ('operation-1', 11))
        with self.assertRaises(sqlite3.IntegrityError): self.db.execute(insert, ('operation-2', 10))
        with self.assertRaises(sqlite3.IntegrityError): self.db.execute('DELETE FROM AccVouchers WHERE Id=10')
        with self.assertRaises(sqlite3.IntegrityError): self.db.execute('DELETE FROM SaleChequeCharges WHERE Id=1')
        self.assertEqual(self.db.execute('SELECT IncomeDelta FROM SaleChequeChargeEvents').fetchone(), ('50',))

    def test_wiring_and_company_filter(self):
        ctx = (ROOT / 'Inventory.Api/Data/AppDbContext.cs').read_text(encoding='utf8')
        self.assertIn('SaleChequeChargeEvent>().HasQueryFilter', ctx)
        self.assertIn('SaleChequeChargeEvent>().HasIndex(e => e.OperationId).IsUnique()', ctx)
        self.assertEqual((ROOT / 'Inventory.Api/Data/DbInitializer.cs').read_text(encoding='utf8').count('SaleChequeChargeEventsSchemaV1.EnsureAsync(db)'), 2)
        posting = (ROOT / 'Inventory.Api/Services/Invoicing/SaleChequeChargePosting.cs').read_text(encoding='utf8')
        self.assertIn('Kind = SaleChequeChargeEventKind.Initial', posting)
        self.assertIn('IncomeDelta = charge.Amount', posting)

if __name__ == '__main__': unittest.main()
