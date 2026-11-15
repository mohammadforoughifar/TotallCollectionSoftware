"""SQLite schema and structural contracts for replacement, bank company scope, credit settlement, installments, edit and cancellation."""
import re
import sqlite3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'src'


def extract_sqlite_sql(rel_path: str) -> str:
    text = (ROOT / rel_path).read_text(encoding='utf8')
    return re.search(r'public const string Sqlite = @"(.*?)";', text, re.S).group(1).replace('""', '"')


class CompleteWorkflowsSchema(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(':memory:')
        self.db.executescript('''
            PRAGMA foreign_keys=ON;
            CREATE TABLE Transactions (Id INTEGER PRIMARY KEY);
            CREATE TABLE TransactionLines (Id INTEGER PRIMARY KEY);
            CREATE TABLE Cheques (Id INTEGER PRIMARY KEY);
            CREATE TABLE Installments (Id INTEGER PRIMARY KEY);
            CREATE TABLE SaleChequeCharges (Id INTEGER PRIMARY KEY);
            CREATE TABLE SaleChequeChargeEvents (Id INTEGER PRIMARY KEY);
            CREATE TABLE TrsAccounts (Id INTEGER PRIMARY KEY, Code TEXT);
            CREATE TABLE AccVouchers (Id INTEGER PRIMARY KEY);
            CREATE TABLE TrsVouchers (Id INTEGER PRIMARY KEY);
            INSERT INTO Transactions VALUES (1), (2), (3);
            INSERT INTO TransactionLines VALUES (10), (20);
            INSERT INTO Cheques VALUES (100), (101);
            INSERT INTO Installments VALUES (200);
            INSERT INTO SaleChequeCharges VALUES (300);
            INSERT INTO SaleChequeChargeEvents VALUES (400);
            INSERT INTO TrsAccounts VALUES (500, 'BNK-01');
            INSERT INTO AccVouchers VALUES (600), (601), (602);
            INSERT INTO TrsVouchers VALUES (700);
        ''')

    def tearDown(self):
        self.db.close()

    def test_cheque_replacement_schema_and_contract(self):
        sql = extract_sqlite_sql('Inventory.Api/Data/SaleChequeReplacementSchemaV1.cs')
        self.db.executescript(sql)
        self.db.executescript(sql)
        self.db.execute('''
            INSERT INTO SaleChequeReplacements
            (OperationId, TransactionId, SaleChequeChargeId, OldChequeId, NewChequeId, Date, OldAmount, NewAmount, CreatedAt)
            VALUES ('rep-1', 1, 300, 100, 101, '2026-06-12', '250', '125', '2026-06-12')
        ''')
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute('''
                INSERT INTO SaleChequeReplacements
                (OperationId, TransactionId, SaleChequeChargeId, OldChequeId, NewChequeId, Date, OldAmount, NewAmount, CreatedAt)
                VALUES ('rep-2', 1, 300, 100, 101, '2026-06-12', '250', '125', '2026-06-12')
            ''')
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute('DELETE FROM Cheques WHERE Id = 100')
        svc = (ROOT / 'Inventory.Api/Services/Invoicing/SaleChequeReplacementService.cs').read_text(encoding='utf8')
        for part in ('IsolationLevel.Serializable', 'ChequeStatus.Cancelled', 'OriginalDueDate = dueDate', 'SaleChequeReplacements.Add'):
            self.assertIn(part, svc)

    def test_treasury_company_column_and_multi_company_guard(self):
        self.db.execute('ALTER TABLE "TrsAccounts" ADD COLUMN "CompanyId" INTEGER NULL')
        self.db.execute('CREATE INDEX IF NOT EXISTS "IX_TrsAccounts_CompanyId" ON "TrsAccounts" ("CompanyId")')
        self.db.execute('UPDATE TrsAccounts SET CompanyId = 7 WHERE Id = 500')
        self.assertEqual(self.db.execute('SELECT CompanyId FROM TrsAccounts WHERE Id = 500').fetchone()[0], 7)
        guard = (ROOT / 'Inventory.Api/Services/Invoicing/SaleChequeTreasuryGuard.cs').read_text(encoding='utf8')
        self.assertIn('SystemCompanies.CountAsync() > 1', guard)
        self.assertIn('account.CompanyId != saleCompanyId', guard)
        self.assertNotIn('mb.Entity<TrsAccount>().HasQueryFilter', (ROOT / 'Inventory.Api/Data/AppDbContext.cs').read_text(encoding='utf8'))

    def test_credit_settlement_schema_and_contract(self):
        sql = extract_sqlite_sql('Inventory.Api/Data/SaleChequeCreditSettlementSchemaV1.cs')
        self.db.executescript(sql)
        self.db.executescript(sql)
        self.db.execute('''
            INSERT INTO SaleChequeCreditSettlements
            (OperationId, Kind, SaleTransactionId, TargetSaleTransactionId, SaleChequeChargeId,
             TrsAccountId, TreasuryLedgerAccountId, PartyAccountId, TargetPartyAccountId,
             AccVoucherId, TrsVoucherId, Date, Amount, CreatedAt)
            VALUES ('cs-1', 1, 1, NULL, 300, 500, 11, 12, NULL, 600, 700, '2026-06-15', '50', '2026-06-15')
        ''')
        self.db.execute('''
            INSERT INTO SaleChequeCreditSettlements
            (OperationId, Kind, SaleTransactionId, TargetSaleTransactionId, SaleChequeChargeId,
             TrsAccountId, TreasuryLedgerAccountId, PartyAccountId, TargetPartyAccountId,
             AccVoucherId, TrsVoucherId, Date, Amount, CreatedAt)
            VALUES ('cs-2', 2, 1, 2, 300, NULL, NULL, 12, 12, 601, NULL, '2026-06-16', '75', '2026-06-16')
        ''')
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute('DELETE FROM AccVouchers WHERE Id = 600')
        svc = (ROOT / 'Inventory.Api/Services/Invoicing/SaleChequeCreditSettlementService.cs').read_text(encoding='utf8')
        for part in ('IsolationLevel.Serializable', 'SaleChequeCreditSettlementKind.RefundPayment',
                     'SaleChequeCreditSettlementKind.ApplyToSale', 'SaleChequeTreasuryGuard.VerifyCompanyOwnershipAsync',
                     'targetSale.SettledAmount += amount'):
            self.assertIn(part, svc)

    def test_installment_receipt_and_reversal_schema_and_contract(self):
        sql = extract_sqlite_sql('Inventory.Api/Data/SaleChequeInstallmentReceiptSchemaV1.cs')
        self.db.executescript(sql)
        self.db.executescript(sql)
        ins = '''
            INSERT INTO SaleChequeInstallmentReceipts
            (OperationId, InstallmentId, TransactionId, TrsAccountId, BankAccountId, PartyAccountId, AccVoucherId, Date, Amount)
            VALUES (?, 200, 1, 500, 11, 12, ?, '2026-06-20', '100')
        '''
        self.db.execute(ins, ('inst-1', 600))
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute(ins, ('inst-2', 601))
        self.db.execute("UPDATE SaleChequeInstallmentReceipts SET ReversalOperationId='rev-1', ReversalVoucherId=602, ReversalDate='2026-06-21' WHERE OperationId='inst-1'")
        self.db.execute(ins, ('inst-2', 601))
        svc = (ROOT / 'Inventory.Api/Services/Invoicing/SaleChequeInstallmentPosting.cs').read_text(encoding='utf8')
        for part in ('IsolationLevel.Serializable', 'SaleChequeTreasuryGuard.VerifyCompanyOwnershipAsync',
                     'receipt.ReversalDate = date', 'sale.SettledAmount += inst.Amount',
                     'sale.SettledAmount -= receipt.Amount'):
            self.assertIn(part, svc)

    def test_cancellation_and_edit_schema_and_contract(self):
        sql = extract_sqlite_sql('Inventory.Api/Data/SaleChequeCancellationSchemaV1.cs')
        self.db.executescript(sql)
        self.db.executescript(sql)
        self.db.execute('''
            INSERT INTO SaleChequeCancellations
            (OperationId, SaleTransactionId, SaleChequeChargeId, ReturnTransactionId, AccVoucherId, SaleChequeChargeEventId,
             RefundTrsVoucherId, SalesAccountId, VatAccountId, IncomeAccountId, PartyAccountId, CashLedgerAccountId,
             Date, ReversedGoodsAmount, ReversedVatAmount, ReversedIncomeDelta, RefundedCashAmount, CreatedAt)
            VALUES ('cn-1', 1, 300, 3, 600, 400, 700, 13, 14, 15, 12, 11, '2026-06-25', '180', '20', '-50', '50', '2026-06-25')
        ''')
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute('DELETE FROM AccVouchers WHERE Id = 600')
        svc = (ROOT / 'Inventory.Api/Services/Invoicing/SaleChequeCancellationService.cs').read_text(encoding='utf8')
        for part in ('IsolationLevel.Serializable', 'RefundTrsVoucher = refundTrs',
                     'SaleChequeChargeEventKind.Cancellation', 'SaleChequeReturnService.RebuildLegacyStockAsync'):
            self.assertIn(part, svc)
        inv = (ROOT / 'Inventory.Api/Services/Catalog/InventoryService.cs').read_text(encoding='utf8')
        self.assertIn('public async Task<Order> EditSaleChequeOrderAsync', inv)
        init = (ROOT / 'Inventory.Api/Data/DbInitializer.cs').read_text(encoding='utf8')
        for helper in ('SaleChequeReplacementSchemaV1', 'TreasuryAccountCompanySchemaV1',
                       'SaleChequeCreditSettlementSchemaV1', 'SaleChequeInstallmentReceiptSchemaV1',
                       'SaleChequeCancellationSchemaV1'):
            self.assertEqual(init.count(f'{helper}.EnsureAsync(db)'), 2, helper)


if __name__ == '__main__':
    unittest.main()
