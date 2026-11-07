"""Focused SQLite and source-contract checks. They do not replace an EF/.NET runtime test."""
from pathlib import Path
import re
import sqlite3
import unittest

ROOT = Path(__file__).resolve().parents[2]
API = ROOT / 'src/Inventory.Api'
CLIENT = ROOT / 'src/Inventory.Client'


def source(path):
    return (ROOT / path).read_text(encoding='utf-8')


def schema(name):
    text = (API / 'Data' / f'{name}SchemaV1.cs').read_text(encoding='utf-8')
    match = re.search(r'public const string Sqlite\s*=\s*@"(.*?)";', text, re.S)
    assert match, name
    return match.group(1).replace('""', '"')


def database():
    db = sqlite3.connect(':memory:')
    db.execute('PRAGMA foreign_keys=ON')
    for table in ('Parties', 'Technicians', 'Warehouses', 'Transactions', 'Products', 'Expenses', 'Cheques', 'TrsAccounts', 'TrsVouchers'):
        db.execute(f'CREATE TABLE [{table}] (Id INTEGER PRIMARY KEY)')
        db.execute(f'INSERT INTO [{table}] (Id) VALUES (1), (2), (3)')
    for name in ('ShopTradeIn', 'ShopWarranty', 'ShopExpenseCheque'):
        sql = schema(name)
        db.executescript(sql)
        db.executescript(sql)  # startup repair must be idempotent
    return db


def expense(db, operation, exp=1, kind=1, state=1, source=None, account=1, number='100', status_op=None, voucher=None):
    db.execute('''INSERT INTO ShopExpenseCheques
      (CompanyId,OperationId,ExpenseId,Kind,State,SourceChequeId,SourceTransactionId,IssuingAccountId,
       SettlementVoucherId,Number,Recipient,Amount,DueDate,HandedAt,StatusOperationId,SettledAt,CreatedAt,StatusAt)
      VALUES (1,?,?,?,?,?,?,?,?,?,'فروشنده','100','2026-12-01','2026-11-07',?,?, '2026-11-07',?)''',
      (operation, exp, kind, state, source, 1 if source else None, account, voucher, number, status_op,
       '2026-11-08' if state != 1 else None, '2026-11-08' if state != 1 else None))


class SchemaTests(unittest.TestCase):
    def setUp(self): self.db = database()
    def tearDown(self): self.db.close()

    def test_all_schema_repair_and_tables(self):
        names = {row[0] for row in self.db.execute("SELECT name FROM sqlite_master WHERE type='table'")}
        self.assertTrue({'ShopTradeIns', 'ShopTradeInUses', 'ShopWarranties', 'ShopWarrantyItems', 'ShopExpenseCheques'} <= names)

    def test_expense_issued_uniqueness_and_no_duplicate_active(self):
        expense(self.db, 'op-1')
        with self.assertRaises(sqlite3.IntegrityError): expense(self.db, 'op-1', exp=2, number='101')
        with self.assertRaises(sqlite3.IntegrityError): expense(self.db, 'op-2', exp=1, number='101')
        with self.assertRaises(sqlite3.IntegrityError): expense(self.db, 'op-3', exp=2, number='100')
        self.db.execute("UPDATE ShopExpenseCheques SET State=3,StatusOperationId='return',SettledAt='2026-11-08',StatusAt='2026-11-08' WHERE OperationId='op-1'")
        expense(self.db, 'replacement', exp=1, number='102')
        # The same cheque number can never be reissued even after it was returned.
        with self.assertRaises(sqlite3.IntegrityError): expense(self.db, 'op-4', exp=2, number='100')

    def test_received_cheque_cannot_be_double_spent_or_double_settled(self):
        expense(self.db, 'received-1', exp=1, kind=2, source=1, account=None, number='R1')
        with self.assertRaises(sqlite3.IntegrityError): expense(self.db, 'received-2', exp=2, kind=2, source=1, account=None, number='R1')
        self.db.execute("UPDATE ShopExpenseCheques SET State=3, StatusOperationId='returned', SettledAt='2026-11-08',StatusAt='2026-11-08' WHERE OperationId='received-1'")
        # A physically returned and explicitly reset source can be endorsed again;
        # the service (not the index) checks that its status is back in hand.
        expense(self.db, 'received-3', exp=2, kind=2, source=1, account=None, number='R1')
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("UPDATE ShopExpenseCheques SET State=2, StatusOperationId='returned',SettledAt='2026-11-09',StatusAt='2026-11-09' WHERE OperationId='received-3'")

    def test_settlement_voucher_unique_and_parent_restricted(self):
        expense(self.db, 'op-a', exp=1, state=2, number='A', status_op='s-a', voucher=1)
        with self.assertRaises(sqlite3.IntegrityError): expense(self.db, 'op-b', exp=2, state=2, number='B', status_op='s-b', voucher=1)
        with self.assertRaises(sqlite3.IntegrityError): self.db.execute('DELETE FROM Expenses WHERE Id=1')
        with self.assertRaises(sqlite3.IntegrityError): self.db.execute('DELETE FROM TrsVouchers WHERE Id=1')

    def test_warranty_stock_reference_is_unique_and_restricted(self):
        row = (1,'w-1','WR-1',1,1,'phone','2026-11-01','2026-11-07',1,'2026-11-01')
        sql = '''INSERT INTO ShopWarranties (CompanyId,OperationId,Number,PartyId,WarehouseId,DeviceType,ReceivedAt,FinalizedAt,StockTransactionId,CreatedAt) VALUES (?,?,?,?,?,?,?,?,?,?)'''
        self.db.execute(sql, row)
        with self.assertRaises(sqlite3.IntegrityError): self.db.execute(sql, (1,'w-2','WR-2',1,1,'phone','2026-11-01','2026-11-07',1,'2026-11-01'))
        with self.assertRaises(sqlite3.IntegrityError): self.db.execute('DELETE FROM Transactions WHERE Id=1')

    def test_tradein_use_idempotency_and_source_is_retained(self):
        self.db.execute("INSERT INTO ShopTradeIns (CompanyId,OperationId,Number,PartyId,DeviceType,Value,ReceivedAt,CreatedAt) VALUES (1,'t1','TI-1',1,'phone','50','2026-11-01','2026-11-01')")
        sql = "INSERT INTO ShopTradeInUses (CompanyId,TradeInId,OperationId,Kind,TargetSaleId,Amount,Date,CreatedAt) VALUES (1,1,'u1',1,1,'20','2026-11-07','2026-11-07')"
        self.db.execute(sql)
        with self.assertRaises(sqlite3.IntegrityError): self.db.execute(sql)
        with self.assertRaises(sqlite3.IntegrityError): self.db.execute('DELETE FROM ShopTradeIns WHERE Id=1')

    def test_expense_count_is_not_increased_by_cheque_status(self):
        self.db.execute('ALTER TABLE Expenses ADD COLUMN CategoryId INTEGER')
        self.db.execute('ALTER TABLE Expenses ADD COLUMN Amount INTEGER')
        self.db.execute('ALTER TABLE Expenses ADD COLUMN Date TEXT')
        self.db.execute("UPDATE Expenses SET CategoryId=1,Amount=100,Date='2026-11-07' WHERE Id=1")
        expense(self.db, 'issued', exp=1)
        query = "SELECT COUNT(*),SUM(Amount) FROM Expenses WHERE CategoryId=1"
        before = self.db.execute(query).fetchone()
        self.db.execute("UPDATE ShopExpenseCheques SET State=2,StatusOperationId='cleared',SettledAt='2026-11-09',StatusAt='2026-11-09',SettlementVoucherId=1 WHERE OperationId='issued'")
        self.assertEqual(before, (1,100))
        self.assertEqual(self.db.execute(query).fetchone(), before)


class SourceContractTests(unittest.TestCase):
    def test_signed_chart_and_print_table(self):
        s = source('src/Inventory.Client/Pages/Operations/OperationsOrderReport.razor')
        for term in ('TrendZeroStyle', 'TrendBarStyle', 'point.Amount < 0', 'order-trend-negative', 'd-print-block', 'MoneyExact()'):
            self.assertIn(term, s)
        # A negative return bucket extends below the zero baseline, never above it.
        minimum, maximum, value = -40, 60, -20
        zero = -minimum / (maximum - minimum)
        bottom = zero - abs(value) / (maximum - minimum)
        self.assertLess(bottom, zero)
        self.assertAlmostEqual(bottom + abs(value) / (maximum - minimum), zero)

    def test_warranty_zero_customer_no_sale_and_stock_out_consumers(self):
        s = source('src/Inventory.Api/Services/Catalog/InventoryService.cs')
        block = s[s.index('public async Task<int?> ConsumeWarrantyStockAsync'):s.index('// =============================== ابزار', s.index('public async Task<int?> ConsumeWarrantyStockAsync'))]
        for term in ('TransactionType.WarrantyOut', 'Amount = 0m', 'CashAmount = 0m', 'SettledAmount = 0m', 'Price = 0m', 'RecalcStockAsync'):
            self.assertIn(term, block)
        self.assertIn('TransactionType.WarrantyOut', source('src/Inventory.Api/Services/Warehousing/WarehousingService.cs'))
        self.assertIn('TransactionType.WarrantyOut', source('src/Inventory.Api/Services/Invoicing/SaleChequeReturnService.cs'))
        self.assertIn('WarrantyDecision.Accepted or WarrantyDecision.Rejected', source('src/Inventory.Api/Services/Repairs/ShopWarrantyService.cs'))

    def test_tradein_no_inventory_and_statement_allocations(self):
        s = source('src/Inventory.Api/Services/Repairs/ShopTradeInService.cs')
        self.assertNotIn('Stocks.', s)
        self.assertNotIn('TransactionLine', s)
        self.assertIn('sale.SettledAmount += cmd.Amount', s)
        self.assertIn('EnsureAvailableOnAndAfterAsync', s)
        statement = source('src/Inventory.Api/Services/Catalog/InventoryService.cs')
        for term in ('LoadTradeInStatementEventsAsync', 'tradeInAllocations.GetValueOrDefault(t.Id)', 'اعتبار طرح تعویض', 'مصرف اعتبار تعویض', 'تسویه از اعتبار تعویض'):
            self.assertIn(term, statement)
        # Paired allocation entries transfer an existing liability; do not create a second credit.
        events = [('sale',100,0),('trade-in',0,30),('consume',30,0),('settle',0,30)]
        self.assertEqual(sum(debit-credit for _, debit, credit in events), 70)

    def test_cheque_issuance_and_received_clearance_are_not_bank_receipts(self):
        s = source('src/Inventory.Api/Services/Finance/ShopExpenseChequeService.cs')
        created = s[s.index('public async Task<int> CreateAsync'):s.index('public async Task ChangeStatusAsync')]
        self.assertNotIn('new TrsVoucher', created)
        self.assertIn('ChequeStatus.Endorsed', created)
        status = s[s.index('public async Task ChangeStatusAsync'):]
        self.assertIn('PayMethod.Cheque', status)
        self.assertIn('EnsureAvailableOnAndAfterAsync', status)
        received = status[status.index('else if (routeKind == ExpenseChequeKind.Received)'):]
        self.assertNotIn('new TrsVoucher', received)
        self.assertIn('sale.SettledAmount += link.Amount', received)
        self.assertIn('BeginTransactionAsync(IsolationLevel.Serializable)', s)
        self.assertIn('ShopExpenseChequeIntegrity.Validate', source('src/Inventory.Api/Services/Treasury/TreasuryBalanceCalculator.cs'))

    def test_shop_only_routes_and_protected_history(self):
        service = source('src/Inventory.Api/Services/Finance/ExpenseService.cs')
        self.assertIn('تاریخچه چک', service)
        self.assertIn('SaleChequeTreasuryGuard.ListCompanyAccountsAsync', source('src/Inventory.Api/Services/Finance/ShopExpenseChequeService.cs'))
        ctl = source('src/Inventory.Api/Controllers/Finance/ExpensesController.cs')
        self.assertIn('RequirePermission("Orders", "Update")', ctl)
        self.assertIn('ShopExpenseCheques.IgnoreQueryFilters()', source('src/Inventory.Api/Services/Treasury/TreasuryService.cs'))
        self.assertIn('target-sales', source('src/Inventory.Client/Pages/Repairs/TradeInTab.razor'))

if __name__ == '__main__': unittest.main()
