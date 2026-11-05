"""SQLite reversal schema logic and source contracts; C#/provider runtime probes need .NET."""
import re
import sqlite3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'src'


class ReversalSchema(unittest.TestCase):
    def test_legacy_receipts_can_be_reversed_and_recollected(self):
        db = sqlite3.connect(':memory:')
        db.executescript('''PRAGMA foreign_keys=ON;
          CREATE TABLE Cheques (Id INTEGER PRIMARY KEY);
          CREATE TABLE Transactions (Id INTEGER PRIMARY KEY);
          CREATE TABLE TrsAccounts (Id INTEGER PRIMARY KEY);
          CREATE TABLE AccVouchers (Id INTEGER PRIMARY KEY);
          INSERT INTO Cheques VALUES (1); INSERT INTO Transactions VALUES (2);
          INSERT INTO TrsAccounts VALUES (3); INSERT INTO AccVouchers VALUES (4), (5), (6);''')
        original = (ROOT / 'Inventory.Api/Data/SaleChequeReceiptSchemaV1.cs').read_text(encoding='utf8')
        sql = re.search(r'public const string Sqlite = @"(.*?)";', original, re.S).group(1).replace('""', '"')
        db.executescript(sql)
        db.executescript('''ALTER TABLE SaleChequeReceipts ADD COLUMN ReversalOperationId TEXT NULL;
          ALTER TABLE SaleChequeReceipts ADD COLUMN ReversalVoucherId INTEGER NULL REFERENCES AccVouchers(Id) ON DELETE RESTRICT;
          ALTER TABLE SaleChequeReceipts ADD COLUMN ReversalDate TEXT NULL;
          ALTER TABLE SaleChequeReceipts ADD COLUMN PreviousAccountKnown INTEGER NOT NULL DEFAULT 0;
          ALTER TABLE SaleChequeReceipts ADD COLUMN PreviousTrsAccountId INTEGER NULL;''')
        schema = (ROOT / 'Inventory.Api/Data/SaleChequeReceiptReversalSchemaV1.cs').read_text(encoding='utf8')
        indexes = re.search(r'indexes.CommandText = @"(.*?)";\s*await indexes.ExecuteNonQueryAsync', schema, re.S).group(1).replace('""', '"')
        db.executescript(indexes)
        insert = '''INSERT INTO SaleChequeReceipts
          (OperationId, ChequeId, TransactionId, TrsAccountId, BankAccountId, PartyAccountId, AccVoucherId, Date, Amount)
          VALUES (?, 1, 2, 3, 10, 11, ?, '2026-06-05', '250')'''
        db.execute(insert, ('first', 4))
        with self.assertRaises(sqlite3.IntegrityError): db.execute(insert, ('second', 5))
        db.execute("UPDATE SaleChequeReceipts SET ReversalVoucherId=6, ReversalOperationId='undo', ReversalDate='2026-06-06' WHERE OperationId='first'")
        db.execute(insert, ('second', 5))
        with self.assertRaises(sqlite3.IntegrityError): db.execute('DELETE FROM AccVouchers WHERE Id=6')
        db.executescript(indexes)  # repair is repeatable
        db.close()

    def test_reversal_is_serialized_and_keeps_history(self):
        s = (ROOT / 'Inventory.Api/Services/Invoicing/SaleChequeReceiptReversal.cs').read_text(encoding='utf8')
        for part in ('IsolationLevel.Serializable', 'tx.RollbackAsync()', 'ReversalOperationId == operationId',
                     'PreviousAccountKnown', 'receipt.ReversalDate = date', 'sale.SettledAmount -= receipt.Amount',
                     'cheque.TrsAccountId = receipt.PreviousTrsAccountId', 'balance.Value < receipt.Amount'):
            self.assertIn(part, s)
        self.assertNotIn('AccVouchers.Remove', s)
        treasury = (ROOT / 'Inventory.Api/Services/Treasury/TreasuryService.cs').read_text(encoding='utf8')
        self.assertIn('receipt.ReversalDate.HasValue', treasury)
        calculator = (ROOT / 'Inventory.Api/Services/Treasury/TreasuryBalanceCalculator.cs').read_text(encoding='utf8')
        self.assertIn('r.ReversalDate.HasValue', calculator)
        self.assertIn('لغو وصول چک فروش', treasury)


if __name__ == '__main__':
    unittest.main()
