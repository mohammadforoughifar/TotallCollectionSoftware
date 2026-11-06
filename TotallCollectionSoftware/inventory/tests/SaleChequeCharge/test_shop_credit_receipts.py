"""SQLite schema checks and independent financial mirrors, not execution of the C# services."""
import re
import sqlite3
import unittest
from decimal import Decimal
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / 'src'


def read(path):
    return (SRC / path).read_text(encoding='utf8')


def sqlite_schema():
    source = read('Inventory.Api/Data/ShopOrderCreditSchemaV1.cs')
    return re.search(r'public const string Sqlite = @"(.*?)";', source, re.S).group(1).replace('""', '"')


class ShopCreditSchema(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(':memory:')
        self.db.executescript('''PRAGMA foreign_keys=ON;
            CREATE TABLE Transactions(Id INTEGER PRIMARY KEY);
            CREATE TABLE TrsAccounts(Id INTEGER PRIMARY KEY);
            INSERT INTO Transactions VALUES(1),(2);
            INSERT INTO TrsAccounts VALUES(10),(11);''')
        self.db.executescript(sqlite_schema())
        self.db.executescript(sqlite_schema())

    def tearDown(self):
        self.db.close()

    def add(self, operation, order=1, kind=2, amount='70', date='2026-06-20'):
        self.db.execute('''INSERT INTO ShopOrderCreditReceipts
            (CompanyId,OperationId,TransactionId,OrderNumber,OrderType,PartyId,TrsAccountId,Amount,Date,CreatedAt)
            VALUES(1,?,?,?, ?, 5, 10, ?, ?, '2026-06-20')''',
            (operation, order, f'INV-{order}', kind, amount, date))

    def test_repeatable_schema_multiple_partial_payments_and_unique_operations(self):
        self.add('first')
        self.add('second', amount='80')
        with self.assertRaises(sqlite3.IntegrityError):
            self.add('first', order=2)
        self.db.execute("UPDATE ShopOrderCreditReceipts SET ReversalDate='2026-06-22', ReversalOperationId='reverse-1', ReversedAt='2026-06-22' WHERE OperationId='first'")
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("UPDATE ShopOrderCreditReceipts SET ReversalDate='2026-06-23', ReversalOperationId='reverse-1', ReversedAt='2026-06-23' WHERE OperationId='second'")
        self.assertEqual(self.db.execute('SELECT COUNT(*) FROM ShopOrderCreditReceipts').fetchone()[0], 2)

    def test_account_and_invoice_are_restricted_even_after_reversal(self):
        self.add('receipt')
        self.db.execute("UPDATE ShopOrderCreditReceipts SET ReversalDate='2026-06-22', ReversalOperationId='reverse', ReversedAt='2026-06-22'")
        for sql in ('DELETE FROM TrsAccounts WHERE Id=10', 'DELETE FROM Transactions WHERE Id=1'):
            with self.assertRaises(sqlite3.IntegrityError):
                self.db.execute(sql)
        self.assertEqual(self.db.execute('SELECT COUNT(*) FROM ShopOrderCreditReceipts').fetchone()[0], 1)

    def test_sale_and_purchase_flow_and_period_statement_mirror(self):
        self.add('sale', order=1, kind=2, amount='70')
        self.add('purchase', order=2, kind=1, amount='30', date='2026-06-21')
        self.db.execute("UPDATE ShopOrderCreditReceipts SET ReversalDate='2026-06-24', ReversalOperationId='r-sale', ReversedAt='2026-06-24' WHERE OperationId='sale'")
        self.db.execute("UPDATE ShopOrderCreditReceipts SET ReversalDate='2026-06-25', ReversalOperationId='r-purchase', ReversedAt='2026-06-25' WHERE OperationId='purchase'")
        rows = self.db.execute('SELECT OrderType, Amount, Date, ReversalDate FROM ShopOrderCreditReceipts').fetchall()
        movements = []
        for kind, amount, date, reversed_date in rows:
            sign = 1 if kind == 2 else -1
            movements.append((date, sign * Decimal(amount)))
            if reversed_date:
                movements.append((reversed_date, -sign * Decimal(amount)))
        self.assertEqual(Decimal(100) + sum(v for d, v in movements if d <= '2026-06-23'), Decimal(140))
        self.assertEqual(Decimal(100) + sum(v for d, v in movements if d <= '2026-06-24'), Decimal(70))
        self.assertEqual(Decimal(100) + sum(v for _, v in movements), Decimal(100))


class ShopCreditSourceContracts(unittest.TestCase):
    def test_workflow_guards_and_idempotency(self):
        posting = read('Inventory.Api/Services/Invoicing/ShopOrderCreditPosting.cs')
        for token in ('IsolationLevel.Serializable', 'RequireSerializable(db)', 'cmd.Amount > amounts.Available',
                      'outstanding - chequeAmount - installmentAmount', 'balance.Value < cmd.Amount',
                      'balance.Value < receipt.Amount', 'VerifyCompanyOwnershipAsync',
                      'EnsureSettledAmountConsistentAsync', 'prior.Amount != cmd.Amount',
                      'ReversalOperationId == cmd.OperationId', 'HasLaterEventsAsync',
                      'rawCreditAfterReversal', 'order.SettledAmount -= receipt.Amount'):
            self.assertIn(token, posting)
        self.assertNotIn('AccVoucher', posting)
        self.assertNotIn('FacRule', posting)
        service = read('Inventory.Api/Services/Catalog/InventoryService.cs')
        for name in ('GetOrderCreditStateAsync', 'ReceiveOrderCreditAsync', 'ReverseOrderCreditAsync'):
            section = service[service.index(' ' + name + '('):]
            section = section[:section.index('\n    public ', 1)]
            self.assertIn('IsolationLevel.Serializable', section)
            self.assertIn('_db.ChangeTracker.Clear()', section)

    def test_dated_debit_low_point_contract(self):
        # Independent day-level examples; C# production code needs its own runtime probe.
        def safe(opening, flows, date, debit):
            running = opening + sum(incoming - outgoing for day, incoming, outgoing in flows if day <= date)
            if running < debit:
                return False
            for day in sorted({d for d, _, _ in flows if d > date}):
                running += sum(i - o for d, i, o in flows if d == day)
                if running < debit:
                    return False
            return True
        flows = [(10, 100, 0), (20, 0, 80), (30, 100, 0)]
        self.assertFalse(safe(0, flows, 5, 40))
        self.assertFalse(safe(0, flows, 15, 30))
        self.assertTrue(safe(0, flows, 15, 20))
        self.assertTrue(safe(0, [(10, 100, 0)], 10, 100))
        calculator = read('Inventory.Api/Services/Treasury/TreasuryBalanceCalculator.cs')
        for token in ('EnsureAvailableOnAndAfterAsync', 'transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable',
                      'c.StatusDate == null', 'balance.Opening + rows.Sum(r => r.In - r.Out) != balance.Amount',
                      'GroupBy(r => r.Date.Date)'):
            self.assertIn(token, calculator)
        for name in ('ShopOrderCreditPosting', 'ShopOrderCashLedger', 'SaleChequeReceiptPosting',
                     'SaleChequeReceiptReversal', 'SaleChequeInstallmentPosting',
                     'SaleChequeCancellationService', 'SaleChequeCreditSettlementService'):
            self.assertIn('EnsureAvailableOnAndAfterAsync', read(f'Inventory.Api/Services/Invoicing/{name}.cs'), name)
        cash = read('Inventory.Api/Services/Invoicing/ShopOrderCashLedger.cs')
        self.assertIn('previous.TrsAccountId, date, 0m', cash)

    def test_report_and_account_integration(self):
        ledger = read('Inventory.Api/Services/Invoicing/SaleChequeSettlementLedger.cs')
        self.assertIn('activeCreditReceipts', ledger)
        self.assertIn('creditReceipts.Any', ledger)
        self.assertIn('!allowUnpostedLegacy', ledger)
        self.assertIn('i.IsPaid && !installments.Any', ledger)
        self.assertIn('receiptAccounts.TryGetValue(r.TrsAccountId', ledger)
        self.assertIn('receiptMultiCompany', ledger)
        self.assertIn('!cheques.Any(r => r.ChequeId == c.Id', ledger)
        reports = read('Inventory.Api/Services/Reports/OperationsReportsService.cs')
        self.assertIn('charge.Transaction, allowUnpostedLegacy: true', reports)
        inv = read('Inventory.Api/Services/Catalog/InventoryService.cs')
        for token in ('LoadShopCreditStatementEventsAsync', 'activeShopCredits.GetValueOrDefault(t.Id)',
                      'shopCreditsByParty[p.Id]', 'foreach (var e in shopCreditEvents.Where',
                      'فاکتور دارای تاریخچه تسویه نسیه'):
            self.assertIn(token, inv)
        calc = read('Inventory.Api/Services/Treasury/TreasuryBalanceCalculator.cs')
        for token in ('creditReceipts.IgnoreQueryFilters()', 'myCredit.Where(r => r.OrderType == TransactionType.Sale)',
                      'myCredit.Where(r => r.OrderType == TransactionType.Purchase)', 'r.CompanyId != r.ParentCompany'):
            # DbSet capitalization is intentional in the source.
            self.assertIn(token.replace('creditReceipts.Ignore', 'ShopOrderCreditReceipts.Ignore'), calc)
        flow = read('Inventory.Api/Services/Treasury/TreasuryService.cs')
        self.assertIn('var creditFlow = await _db.ShopOrderCreditReceipts.IgnoreQueryFilters()', flow)
        self.assertIn('creditReceipt || creditRefund', flow)
        for name in ('SaleChequeReceiptPosting', 'SaleChequeReceiptReversal', 'SaleChequeInstallmentPosting',
                     'SaleChequeReturnService', 'SaleChequeCancellationService', 'SaleChequeRescheduling'):
            self.assertIn('ShopOrderCreditReceipts.AnyAsync', read(f'Inventory.Api/Services/Invoicing/{name}.cs'))

    def test_schema_routes_permissions_and_store_ui(self):
        ctx = read('Inventory.Api/Data/AppDbContext.cs')
        for token in ('DbSet<ShopOrderCreditReceipt>', 'HasQueryFilter(x => ActiveCompanyId == null',
                      'HasIndex(x => x.OperationId).IsUnique()', 'DeleteBehavior.Restrict'):
            self.assertIn(token, ctx)
        self.assertIn('ShopOrderCreditSchemaV1.EnsureAsync(db)', read('Inventory.Api/Data/DbInitializer.cs'))
        self.assertIn('ShopOrderCreditReceipts', read('Inventory.Api/Migrations/AppDbContextModelSnapshot.cs'))
        self.assertIn('ShopOrderCreditSchemaV1.SqlServer', read('Inventory.Api/Migrations/20261105090000_AddShopCreditReceipts.cs'))
        controller = read('Inventory.Api/Controllers/Sales/OrdersController.cs')
        self.assertIn('{id:int}/credit-receipts/{receiptId:int}/reversal', controller)
        permissions = read('Inventory.Api/Infrastructure/ModulePermissionFilter.cs')
        self.assertIn('["Orders.ReceiveCredit"] = new("Orders", "Update")', permissions)
        self.assertIn('["Orders.ReverseCreditReceipt"] = new("Orders", "Update")', permissions)
        auth = read('Inventory.Api/Infrastructure/ShopPermissionAuthorizer.cs')
        self.assertIn('OrderType(permissions, creditType.Value, write)', auth)
        ui = read('Inventory.Client/Pages/Orders/Orders.razor')
        for token in ('OpenOrderCredit(o)', 'مانده نسیه آزاد', 'حساب پرداخت‌کننده', 'تاریخ لغو',
                      'SubmitOrderCreditAsync', 'ReverseOrderCreditAsync', 'orderCreditOperationId'):
            self.assertIn(token, ui)


if __name__ == '__main__':
    unittest.main()
