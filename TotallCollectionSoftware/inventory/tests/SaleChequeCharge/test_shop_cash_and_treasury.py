"""SQLite DDL and independent arithmetic mirrors; these do not execute the C#/EF services."""
import re
import sqlite3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / 'src'


def read(path):
    return (SRC / path).read_text(encoding='utf8')


def cash_sql():
    source = read('Inventory.Api/Data/ShopOrderCashSchemaV1.cs')
    return re.search(r'public const string SqliteTable = @"(.*?)";', source, re.S).group(1).replace('""', '"')


class ShopCashSchema(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(':memory:')
        self.db.executescript('''PRAGMA foreign_keys=ON;
            CREATE TABLE TrsAccounts (Id INTEGER PRIMARY KEY);
            CREATE TABLE TrsVouchers (Id INTEGER PRIMARY KEY);
            CREATE TABLE Transactions (Id INTEGER PRIMARY KEY);
            INSERT INTO TrsAccounts VALUES (1), (2);
            INSERT INTO TrsVouchers VALUES (20), (21);
            INSERT INTO Transactions VALUES (100), (101);''')
        self.db.executescript(cash_sql())
        self.db.executescript(cash_sql())  # repeatable creation

    def tearDown(self):
        self.db.close()

    def add_cash(self, order, operation, amount=50, kind=2, voucher=None, date='2026-06-01'):
        self.db.execute('''INSERT INTO ShopOrderCashEntries
            (CompanyId, TransactionId, OrderNumber, OrderType, PartyId, PartyName, TrsAccountId,
             MatchedTrsVoucherId, CashType, Amount, Date, OperationId, CreatedAt)
            VALUES (1, ?, ?, ?, 5, 'Party', 1, ?, 0, ?, ?, ?, '2026-06-01')''',
            (order, f'SL-{order}', kind, voucher, str(amount), date, operation))

    def test_active_and_operation_uniqueness_and_reversal_history(self):
        self.add_cash(100, 'a')
        with self.assertRaises(sqlite3.IntegrityError):
            self.add_cash(100, 'b')
        self.db.execute("UPDATE ShopOrderCashEntries SET ReversalDate='2026-06-03', ReversalOperationId='r1' WHERE OperationId='a'")
        self.add_cash(100, 'b')
        with self.assertRaises(sqlite3.IntegrityError):
            self.add_cash(101, 'b')
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("UPDATE ShopOrderCashEntries SET ReversalDate='2026-06-04', ReversalOperationId='r1' WHERE OperationId='b'")
        self.assertEqual(self.db.execute('SELECT COUNT(*) FROM ShopOrderCashEntries').fetchone()[0], 2)

    def test_matched_manual_voucher_is_unique_and_protected(self):
        self.add_cash(100, 'a', voucher=20)
        self.db.execute("UPDATE ShopOrderCashEntries SET ReversalDate='2026-06-03', ReversalOperationId='r1' WHERE OperationId='a'")
        with self.assertRaises(sqlite3.IntegrityError):
            self.add_cash(101, 'b', voucher=20)  # also protected after reversal
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute('DELETE FROM TrsVouchers WHERE Id=20')
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute('DELETE FROM TrsAccounts WHERE Id=1')
        self.db.execute('DELETE FROM Transactions WHERE Id=100')
        self.assertEqual(self.db.execute('SELECT TransactionId FROM ShopOrderCashEntries').fetchone()[0], 100)

    def test_new_and_matched_cash_with_reversals_are_counted_once(self):
        self.add_cash(100, 'sale', amount=50, kind=2)
        self.add_cash(101, 'purchase', amount=30, kind=1, voucher=20)
        self.db.execute("UPDATE ShopOrderCashEntries SET ReversalDate='2026-06-03', ReversalOperationId='r-sale' WHERE OperationId='sale'")
        self.db.execute("UPDATE ShopOrderCashEntries SET ReversalDate='2026-06-04', ReversalOperationId='r-purchase' WHERE OperationId='purchase'")
        rows = self.db.execute('''SELECT OrderType, CAST(Amount AS NUMERIC), Date, ReversalDate, MatchedTrsVoucherId
                                  FROM ShopOrderCashEntries''').fetchall()
        movements = [('2026-06-01', -30)]  # one existing confirmed manual purchase voucher
        for kind, amount, date, reverse, matched in rows:
            signed = amount if kind == 2 else -amount
            if matched is None:
                movements.append((date, signed))
            if reverse:
                movements.append((reverse, -signed))
        self.assertEqual(100 + sum(value for date, value in movements if date <= '2026-06-02'), 120)
        self.assertEqual(100 + sum(value for date, value in movements if date <= '2026-06-03'), 70)
        self.assertEqual(100 + sum(value for date, value in movements), 100)
        calc = read('Inventory.Api/Services/Treasury/TreasuryBalanceCalculator.cs')
        self.assertIn('!e.MatchedTrsVoucherId.HasValue', calc)
        self.assertIn('EnsureMatchedVouchersValidAsync', calc)
        flow = read('Inventory.Api/Services/Treasury/TreasuryService.cs')
        self.assertIn('if (!e.MatchedTrsVoucherId.HasValue)', flow)
        self.assertIn('MatchedTrsVoucherId == id', flow)

    def test_upgrade_and_reconciliation_wiring(self):
        snapshot = read('Inventory.Api/Migrations/AppDbContextModelSnapshot.cs')
        self.assertIn('ShopOrderCashEntry', snapshot)
        self.assertIn('ShopCashLedgerEnabled', snapshot)
        self.assertIn('MatchedTrsVoucherId', snapshot)
        ledger = read('Inventory.Api/Services/Invoicing/ShopOrderCashLedger.cs')
        for token in ('cmd.Confirmed', 'cmd.ExpectedCashAmount != order.CashAmount',
                      'voucher.InvoiceAllocations.Count != 0', 'ReconcileLegacyAsync',
                      'MatchesLegacyVoucher', 'EnsureMatchedVouchersValidAsync'):
            self.assertIn(token, ledger)
        self.assertIn('cash-reconciliation', read('Inventory.Api/Controllers/Sales/OrdersController.cs'))
        self.assertIn('تطبیق نقد', read('Inventory.Client/Pages/Orders/OrderForm.razor'))
        self.assertIn('["Orders.ReconcileCash"] = new("Orders", "Update")',
                      read('Inventory.Api/Infrastructure/ModulePermissionFilter.cs'))


class StatementAndCompanyContracts(unittest.TestCase):
    def test_sale_purchase_receipts_and_reversals_have_opposite_directions(self):
        # The same direction is used by both account flow and party statement.
        for order_type, direction in (('sale', 1), ('purchase', -1)):
            movements = [('2026-06-02', direction * 40), ('2026-06-05', -direction * 40)]
            self.assertEqual(sum(v for d, v in movements if d < '2026-06-04'), direction * 40, order_type)
            self.assertEqual(sum(v for d, v in movements if d >= '2026-06-04'), -direction * 40, order_type)
            self.assertEqual(sum(v for _, v in movements), 0, order_type)
        service = read('Inventory.Api/Services/Catalog/InventoryService.cs')
        self.assertIn('LoadOrdinaryChequeStatementEventsAsync', service)
        self.assertIn('activeOrdinaryCheques.GetValueOrDefault(t.Id)', service)
        self.assertIn('(e.Type == TransactionType.Purchase) != e.IsReversal', service)
        posting = read('Inventory.Api/Services/Invoicing/SaleChequeReceiptPosting.cs')
        self.assertIn('sale.Type == TransactionType.Purchase', posting)
        self.assertIn('balance.Value < cheque.Amount', posting)
        reversal = read('Inventory.Api/Services/Invoicing/SaleChequeReceiptReversal.cs')
        self.assertIn('sale.Type == TransactionType.Sale', reversal)
        self.assertIn('sale.Type != TransactionType.Purchase', reversal)

    def test_purchase_cash_is_checked_inside_the_order_transaction(self):
        ledger = read('Inventory.Api/Services/Invoicing/ShopOrderCashLedger.cs')
        self.assertIn('balance.Value < 0', ledger)
        self.assertIn('EnsurePurchaseBalanceAsync(db, order)', ledger)
        service = read('Inventory.Api/Services/Catalog/InventoryService.cs')
        for name in ('CreateOrderAsync', 'UpdateOrderAsync', 'DeleteOrderAsync', 'ReconcileLegacyOrderCashAsync'):
            section = service[service.index('public async Task' + ('<Order>' if name in ('CreateOrderAsync', 'UpdateOrderAsync') else '') + ' ' + name):]
            section = section[:section.index('\n    public ', 1)]
            self.assertIn('IsolationLevel.Serializable', section)
            self.assertIn('_db.ChangeTracker.Clear()', section)
        self.assertIn('فاکتور دارای تاریخچه نقدی است', service)

    def test_remapping_an_account_with_treasury_history_is_blocked(self):
        service = read('Inventory.Api/Services/Treasury/TreasuryService.cs')
        section = service[service.index('var mappingChanged'):service.index('e.CompanyId = requestedCompanyId')]
        for token in ('_db.TrsVoucherLines.IgnoreQueryFilters()', '_db.TrsVouchers.IgnoreQueryFilters()',
                      '_db.TrsCheques.IgnoreQueryFilters()', 'ordinaryCash', 'treasuryHistory'):
            self.assertIn(token, section)
        balance = read('Inventory.Api/Services/Treasury/TreasuryBalanceCalculator.cs')
        self.assertIn('accounts.Count != ids.Count', balance)
        self.assertIn('!Matches(owner, r.CompanyId)', balance)

    def test_purchase_receipt_reversal_labels_and_recorded_payer(self):
        ui = read('Inventory.Client/Pages/Shop/ShopCreditManagement.razor')
        for token in ('بازگشت مبلغ به حساب بانکی اولیه', 'حساب صادرکننده چک',
                      'item.TrsAccountId.Value', 'ثبت لغو پرداخت'):
            self.assertIn(token, ui)
        posting = read('Inventory.Api/Services/Invoicing/SaleChequeReceiptPosting.cs')
        self.assertIn('cheque.TrsAccountId != bank.Id', posting)
        self.assertIn('sale.ShopCashLedgerEnabled', posting)
        self.assertIn('VerifyCurrentAsync(db, sale)', posting)

    def test_charged_edit_validates_company_references_before_commit(self):
        service = read('Inventory.Api/Services/Catalog/InventoryService.cs')
        section = service[service.index('public async Task<Order> EditSaleChequeOrderAsync'):]
        section = section[:section.index('\n    public ', 1)]
        self.assertIn('await ValidateOrderReferencesAsync(cmd, productIds, products.Count)', section)
        self.assertLess(section.index('var savedOrder = await GetOrderAsync'), section.index('await trx.CommitAsync()'))

    def test_cash_quick_create_starts_with_compatible_account_kind(self):
        form = read('Inventory.Client/Pages/Orders/OrderForm.razor')
        self.assertIn('CashKind == CashType.Cash ? TreasuryAccountKind.Cash : TreasuryAccountKind.Bank', form)
        self.assertIn('AccountsForCashType(CashKind).Any(a => a.Id == newAcc.Id)', form)

    def test_ordinary_installments_are_dated_reversible_and_scoped(self):
        for invoice_kind, signed in (('sale', 1), ('purchase', -1)):
            movements = [('2026-06-20', signed * 120), ('2026-06-22', -signed * 120)]
            self.assertEqual(sum(v for d, v in movements if d <= '2026-06-19'), 0, invoice_kind)
            self.assertEqual(sum(v for d, v in movements if d <= '2026-06-21'), signed * 120, invoice_kind)
            self.assertEqual(sum(v for _, v in movements), 0, invoice_kind)
        posting = read('Inventory.Api/Services/Invoicing/SaleChequeInstallmentPosting.cs')
        for token in ('sale.Type == TransactionType.Purchase', 'balance.Value < inst.Amount',
                      'sale.Type == TransactionType.Sale', 'EnsureSettledAmountConsistentAsync',
                      'if (charge is not null)', 'VerifyCurrentAsync(db, sale)'):
            self.assertIn(token, posting)
        inv = read('Inventory.Api/Services/Catalog/InventoryService.cs')
        self.assertIn('LoadOrdinaryInstallmentStatementEventsAsync', inv)
        self.assertIn('activeOrdinaryInstallments.GetValueOrDefault(t.Id)', inv)
        self.assertIn('ordinaryInstallmentsByParty[p.Id]', inv)
        self.assertIn('foreach (var e in ordinaryInstallmentEvents.Where', inv)
        auth = read('Inventory.Api/Infrastructure/ShopPermissionAuthorizer.cs')
        self.assertIn('installment.TransactionId equals order.Id', auth)
        self.assertIn('OrderType(permissions, invoiceType.Value, true)', auth)
        ui = read('Inventory.Client/Pages/Shop/ShopCreditManagement.razor')
        self.assertIn('PayInstallment(ShopInstallmentItem i) => OpenInstallmentReceipt(i)', ui)
        self.assertIn('i.HasChequeCharge || i.HasTrackedReceipt', ui)

    def test_company_backfill_and_profit_filter_are_explicit(self):
        companies = read('Inventory.Api/Controllers/System/SystemCompaniesController.cs')
        self.assertIn('IsolationLevel.Serializable', companies)
        self.assertIn('SalesCompanySchemaV1.BackfillAsync(_db)', companies)
        backfill = read('Inventory.Api/Data/TreasuryCompanyScopeSchemaV1.cs')
        self.assertIn('companies.Count == 1 && companies[0].IsActive', backfill)
        self.assertIn('matchedCash', backfill)
        self.assertIn('WHERE Id = {voucher.Id} AND CompanyId IS NULL', backfill)
        self.assertIn('if (transaction is not null) await transaction.CommitAsync()', backfill)
        orders = read('Inventory.Api/Controllers/Sales/OrdersController.cs')
        self.assertIn('hasChequeIncome.HasValue && !canViewProfit', orders)
        reports = read('Inventory.Api/Controllers/Reports/OperationsReportsController.cs')
        self.assertIn('hasChequeIncome.HasValue && !canViewProfit', reports)
        self.assertIn('canSeeOrders && canSeeReferrers, canViewProfit', reports)


if __name__ == '__main__':
    unittest.main()
