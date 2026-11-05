"""SQLite schema and source contract; actual C#/EF receipt execution requires .NET."""
import re
import sqlite3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'src'


class ReceiptSchema(unittest.TestCase):
    def test_receipt_unique_and_restricts_voucher_deletion(self):
        db = sqlite3.connect(':memory:')
        db.executescript('''PRAGMA foreign_keys=ON;
            CREATE TABLE Cheques (Id INTEGER PRIMARY KEY);
            CREATE TABLE Transactions (Id INTEGER PRIMARY KEY);
            CREATE TABLE TrsAccounts (Id INTEGER PRIMARY KEY);
            CREATE TABLE AccVouchers (Id INTEGER PRIMARY KEY);
            INSERT INTO Cheques VALUES(1); INSERT INTO Transactions VALUES(2);
            INSERT INTO TrsAccounts VALUES(3); INSERT INTO AccVouchers VALUES(4);''')
        text = (ROOT / 'Inventory.Api/Data/SaleChequeReceiptSchemaV1.cs').read_text(encoding='utf8')
        sql = re.search(r'public const string Sqlite = @"(.*?)";', text, re.S).group(1).replace('""', '"')
        db.executescript(sql)
        db.executescript(sql)
        args = ('op-1', 1, 2, 3, 10, 11, 4, '2026-06-05', '250')
        statement = '''INSERT INTO SaleChequeReceipts
            (OperationId, ChequeId, TransactionId, TrsAccountId, BankAccountId, PartyAccountId, AccVoucherId, Date, Amount)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)'''
        db.execute(statement, args)
        with self.assertRaises(sqlite3.IntegrityError): db.execute(statement, args)
        with self.assertRaises(sqlite3.IntegrityError): db.execute('DELETE FROM AccVouchers WHERE Id=4')
        db.close()

    def test_accounting_treasury_and_statement_hooks_are_present(self):
        receipt = (ROOT / 'Inventory.Api/Services/Invoicing/SaleChequeReceiptPosting.cs').read_text(encoding='utf8')
        for s in ('IsolationLevel.Serializable', 'TreasuryAccountKind.Bank', 'SaleChequeTreasuryGuard.VerifyCompanyOwnershipAsync',
                  'sale.SettledAmount += cheque.Amount', 'SaleChequeReceipts.Add(receipt)', 'tx.RollbackAsync()',
                  'x.SaleChequeChargeId == charge.Id && x.Date > date',
                  'x.TransactionId == sale.Id && (x.Date > date || x.ReversalDate > date)'):
            self.assertIn(s, receipt)
        accounts = (ROOT / 'Inventory.Api/Services/Accounting/AccountingService.cs').read_text(encoding='utf8')
        self.assertIn('SaleChequeReceipts.IgnoreQueryFilters()', accounts)
        treasury = (ROOT / 'Inventory.Api/Services/Treasury/TreasuryService.cs').read_text(encoding='utf8')
        calculator = (ROOT / 'Inventory.Api/Services/Treasury/TreasuryBalanceCalculator.cs').read_text(encoding='utf8')
        self.assertIn('SaleChequeReceipts.IgnoreQueryFilters().AsNoTracking()', calculator)
        self.assertIn('myReceipts.Sum(r => r.Amount)', calculator)
        self.assertIn('وصول چک فروش شماره', treasury)
        statement = (ROOT / 'Inventory.Api/Services/Catalog/InventoryService.cs').read_text(encoding='utf8')
        self.assertIn('"لغو وصول چک" : "وصول چک"', statement)
        ledger = (ROOT / 'Inventory.Api/Services/Invoicing/SaleChequeSettlementLedger.cs').read_text(encoding='utf8')
        self.assertIn('ReversalDate == null', ledger)
        self.assertIn('SaleChequeShopLedgerIntegrity.EnsureAsync', ledger)


if __name__ == '__main__':
    unittest.main()
