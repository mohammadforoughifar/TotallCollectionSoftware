"""SQLite DDL execution and safeguards; .NET integration is in Program.cs."""
import re
import sqlite3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'src'
def read(path): return (ROOT / path).read_text(encoding='utf8')

class OriginalDueDateTests(unittest.TestCase):
    def test_migration_preserves_old_cheque_without_inventing_a_date(self):
        src = read('Inventory.Api/Data/ChequeOriginalDueDateSchemaV1.cs')
        match = re.search(r'alter.CommandText = "(.*?)";', src)
        self.assertIsNotNone(match)
        sql = match.group(1).replace(r'\"', '"')
        db = sqlite3.connect(':memory:')
        db.execute('CREATE TABLE Cheques (Id INTEGER PRIMARY KEY, DueDate TEXT NOT NULL)')
        db.execute("INSERT INTO Cheques VALUES (1, '2026-07-01')")
        db.execute(sql)
        self.assertEqual(db.execute('SELECT DueDate, OriginalDueDate FROM Cheques').fetchone(), ('2026-07-01', None))
        self.assertIn('if (!present)', src)
        self.assertIn('COL_LENGTH', src)
        db.close()

    def test_new_snapshot_and_old_client_compatibility_are_explicit(self):
        service = read('Inventory.Api/Services/Catalog/InventoryService.cs')
        self.assertIn('originalDueDates = txn.Cheques.Where(c => c.Id > 0)', service)
        self.assertIn('OriginalDueDate = txn.Id == 0 ? c.DueDate.Date', service)
        self.assertIn('originalDueDates.TryGetValue(c.Id, out var originalDate) ? originalDate : null', service)
        self.assertEqual(read('Inventory.Api/Data/DbInitializer.cs').count('ChequeOriginalDueDateSchemaV1.EnsureAsync(db)'), 2)
        self.assertIn('ChequeOriginalDueDateSchemaV1.SqlServer', read('Inventory.Api/Migrations/20261104160000_AddChequeOriginalDueDate.cs'))
        self.assertIn('Property<DateTime?>("OriginalDueDate")', read('Inventory.Api/Migrations/AppDbContextModelSnapshot.cs'))

if __name__ == '__main__': unittest.main()
