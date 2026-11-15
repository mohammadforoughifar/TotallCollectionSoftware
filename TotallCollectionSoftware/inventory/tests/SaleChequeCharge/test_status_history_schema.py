"""SQLite schema and source checks only; C#/SQL Server runtime still requires testing."""
import re
import sqlite3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'src'


class StatusHistorySchema(unittest.TestCase):
    def test_unique_operation_and_restricted_cheque(self):
        db = sqlite3.connect(':memory:')
        db.executescript('PRAGMA foreign_keys=ON; CREATE TABLE Cheques (Id INTEGER PRIMARY KEY); INSERT INTO Cheques VALUES (1);')
        text = (ROOT / 'Inventory.Api/Data/SaleChequeStatusEventSchemaV1.cs').read_text(encoding='utf8')
        sql = re.search(r'public const string Sqlite = @"(.*?)";', text, re.S).group(1).replace('""', '"')
        db.executescript(sql)
        db.executescript(sql)
        query = '''INSERT INTO SaleChequeStatusEvents
          (OperationId, ChequeId, PreviousStatus, NewStatus, CreatedAt) VALUES (?, 1, 0, 2, '2026-06-01')'''
        db.execute(query, ('op-1',))
        with self.assertRaises(sqlite3.IntegrityError): db.execute(query, ('op-1',))
        with self.assertRaises(sqlite3.IntegrityError): db.execute('DELETE FROM Cheques WHERE Id=1')
        db.close()

    def test_status_changes_and_receipts_record_history(self):
        inventory = (ROOT / 'Inventory.Api/Services/Catalog/InventoryService.cs').read_text(encoding='utf8')
        self.assertIn('_db.SaleChequeStatusEvents.Add', inventory)
        self.assertIn('e.OperationId == cmd.OperationId', inventory)
        self.assertIn('if (cheque.Status == target)', inventory)
        receipt = (ROOT / 'Inventory.Api/Services/Invoicing/SaleChequeReceiptPosting.cs').read_text(encoding='utf8')
        self.assertIn('db.SaleChequeStatusEvents.Add', receipt)
        self.assertIn('NewStatus = ChequeStatus.Cleared', receipt)
        initializer = (ROOT / 'Inventory.Api/Data/DbInitializer.cs').read_text(encoding='utf8')
        self.assertEqual(initializer.count('SaleChequeStatusEventSchemaV1.EnsureAsync(db)'), 2)


if __name__ == '__main__':
    unittest.main()
