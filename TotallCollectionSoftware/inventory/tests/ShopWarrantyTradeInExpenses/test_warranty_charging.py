"""SQLite migration and source invariants; EF/provider execution remains a separate test."""
from pathlib import Path
import re
import sqlite3
import unittest

ROOT = Path(__file__).resolve().parents[2]

def read(path):
    return (ROOT / path).read_text(encoding='utf-8')

SCHEMA = read('src/Inventory.Api/Data/ShopWarrantyChargingSchemaV1.cs')
COLUMNS = re.findall(r'\("([^"]+)",\s*"([^"]+)",\s*"([^"]+)"\)', SCHEMA)
INDEXES = re.search(r'public const string SqliteIndexes\s*=\s*@"(.*?)";', SCHEMA, re.S).group(1)

def ensure_sqlite(db):
    # Execute the exact ALTER definitions and index statements used by EnsureAsync.
    for table, column, definition in COLUMNS:
        existing = {row[1].lower() for row in db.execute(f'PRAGMA table_info([{table}])')}
        if column.lower() not in existing:
            db.execute(f'ALTER TABLE [{table}] ADD COLUMN [{column}] {definition}')
    db.executescript(INDEXES)

class LegacySchemaTests(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(':memory:')
        self.db.execute('PRAGMA foreign_keys=ON')
        self.db.executescript('''
          CREATE TABLE Transactions (Id INTEGER PRIMARY KEY, Amount INTEGER);
          CREATE TABLE ShopWarranties (Id INTEGER PRIMARY KEY, FinalizedAt TEXT, StockTransactionId INTEGER);
          CREATE TABLE ShopWarrantyItems (Id INTEGER PRIMARY KEY, Decision INTEGER, Supplied INTEGER, UnitCost TEXT);
          CREATE TABLE RepairOrders (Id INTEGER PRIMARY KEY, InvoiceTransactionId INTEGER);
          CREATE TABLE RepairItems (Id INTEGER PRIMARY KEY, Price TEXT, Cost TEXT);
          INSERT INTO Transactions VALUES (1, 0), (2, 500), (3, 0);
          INSERT INTO ShopWarranties VALUES (1, '2026-01-01', 1), (2, NULL, NULL), (3, NULL, NULL);
          INSERT INTO ShopWarrantyItems VALUES (1, 2, 1, '70.00');
          INSERT INTO RepairOrders VALUES (1, 2), (2, NULL), (3, NULL);
          INSERT INTO RepairItems VALUES (1, '500.00', '70.00');
        ''')
    def tearDown(self): self.db.close()

    def test_old_rows_remain_old_and_schema_is_idempotent(self):
        self.assertEqual(len(COLUMNS), 9)
        ensure_sqlite(self.db)
        ensure_sqlite(self.db)
        self.assertEqual(self.db.execute('SELECT FinalizedAt, StockTransactionId, ChargePolicyVersion, ChargeTransactionId FROM ShopWarranties WHERE Id=1').fetchone(), ('2026-01-01', 1, 0, None))
        self.assertEqual(self.db.execute('SELECT Decision, Supplied, UnitCost, CustomerUnitPrice, Performed FROM ShopWarrantyItems').fetchone(), (2, 1, '70.00', None, 1))
        self.assertEqual(self.db.execute('SELECT InvoiceTransactionId, WarrantyProcessed, WarrantyStockTransactionId FROM RepairOrders WHERE Id=1').fetchone(), (2, 0, None))
        self.assertEqual(self.db.execute('SELECT Price, Cost, WarrantyDecision, WarrantySupplied, WarrantyPerformed FROM RepairItems').fetchone(), ('500.00', '70.00', 0, 1, 1))
        self.assertEqual(self.db.execute('SELECT Amount FROM Transactions WHERE Id=2').fetchone(), (500,))

    def test_new_links_are_unique_and_parent_deletion_is_restricted(self):
        ensure_sqlite(self.db)
        self.db.execute('UPDATE ShopWarranties SET ChargeTransactionId=2 WHERE Id=2')
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute('UPDATE ShopWarranties SET ChargeTransactionId=2 WHERE Id=3')
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute('DELETE FROM Transactions WHERE Id=2')
        self.db.execute('UPDATE RepairOrders SET WarrantyStockTransactionId=3 WHERE Id=2')
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute('UPDATE RepairOrders SET WarrantyStockTransactionId=3 WHERE Id=3')
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute('DELETE FROM Transactions WHERE Id=3')
        self.assertEqual(self.db.execute('PRAGMA foreign_key_check').fetchall(), [])

    def test_invalid_foreign_links_fail_without_touching_history(self):
        ensure_sqlite(self.db)
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute('UPDATE ShopWarranties SET ChargeTransactionId=999 WHERE Id=2')
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute('UPDATE RepairOrders SET WarrantyStockTransactionId=999 WHERE Id=2')
        self.assertEqual(self.db.execute('SELECT FinalizedAt, ChargePolicyVersion FROM ShopWarranties WHERE Id=1').fetchone(), ('2026-01-01', 0))

class ChargingInvariants(unittest.TestCase):
    def test_standalone_finalization_owns_a_serializable_transaction_and_retries(self):
        src = read('src/Inventory.Api/Services/Repairs/ShopWarrantyService.cs')
        block = src[src.index('public async Task<ShopWarrantyDto> FinalizeAsync'):src.index('private async Task<int> ChargeIdAsync')]
        for text in ('IsolationLevel.Serializable', 'if (record.FinalizedAt.HasValue)',
                     'WarrantyDecision.Accepted && i.Supplied', 'WarrantyDecision.Rejected',
                     'i.ProductId is > 0 ? i.Supplied : i.Performed', 'CustomerUnitPrice',
                     'PaymentMethod.Credit', 'IsWarrantyInvoice = true', 'ChargePolicyVersion = 1',
                     'ChargeTransactionId = saleId', 'CommitAsync()', 'RollbackAsync()'):
            self.assertIn(text, block)
        self.assertLess(block.index('ConsumeWarrantyStockAsync'), block.index('CreateOrderAsync'))
        self.assertLess(block.index('CreateOrderAsync'), block.index('ChargePolicyVersion = 1'))

    def test_repair_delivery_cannot_be_bypassed_and_ordinary_free_repair_is_not_reclassified(self):
        service = read('src/Inventory.Api/Services/Repairs/RepairService.cs')
        save = service[service.index('public async Task<RepairOrderDto> SaveRepairAsync'):service.index('public async Task DeleteRepairAsync')]
        self.assertIn('dto.Status is not (RepairStatus.Received or RepairStatus.InProgress or RepairStatus.Ready or RepairStatus.Cancelled)', save)
        invoice = service[service.index('public async Task<RepairOrderDto> InvoiceAsync'):service.index('private async Task<int> WarrantyInvoiceIdAsync')]
        for text in ('if (!hasWarranty && chargeable.Count == 0)', 'WarrantyProcessed = hasWarranty',
                     'trackShopCash: hasWarranty', 'IsWarrantyInvoice = hasWarranty',
                     'w.CompanyId == r.CompanyId', 'r.Items.Any(i => i.CompanyId != r.CompanyId)',
                     'ShopInvoiceAmounts.IsWholePrice(request.CashAmount)', 'RollbackAsync()'):
            self.assertIn(text, invoice)
        ui = read('src/Inventory.Client/Pages/Repairs/Repairs.razor')
        self.assertIn('private static bool IsFreeWarranty', ui)
        self.assertIn('repair.Items.Any(i => i.WarrantyDecision != WarrantyDecision.Unspecified)', ui)
        self.assertIn('@if (IsFreeWarranty(invoicing))', ui)

    def test_caller_owns_invoice_transaction_and_service_rows_do_not_create_stock(self):
        src = read('src/Inventory.Api/Services/Catalog/InventoryService.cs')
        block = src[src.index('public async Task<Order> CreateOrderAsync'):src.index('public async Task<Order?> GetOrderAsync')]
        for text in ('CurrentTransaction', 'ownsTransaction', 'if (ownsTransaction) await ownedTransaction!.CommitAsync()',
                     'cmd.IsWarrantyInvoice && products[pid].IsService', 'ShopMinimumProfitGuard.ValidateAsync',
                     'UnitCostSnapshot = l.UnitCostSnapshot'):
            self.assertIn(text, block)
        self.assertIn('cmd.IsWarrantyInvoice ? 0m : PaymentMix.Tolerance', src)
        freeze = src[src.index('private async Task FreezeChargedSaleCostsAsync'):src.index('// Conservative confidence check')]
        self.assertIn('!l.UnitCostSnapshot.HasValue', freeze)
        self.assertIn('foreach (var line in unpricedLines)', freeze)

    def test_whole_rial_applies_to_both_specific_and_generic_receipt_routes(self):
        src = read('src/Inventory.Api/Services/Invoicing/ShopOrderCreditPosting.cs')
        receive = src[src.index('public static async Task ReceiveAsync'):src.index('public static async Task ReverseAsync')]
        for text in ('decimal.Truncate(cmd.Amount)', 'db.ShopWarranties.AnyAsync', 'db.RepairOrders.AnyAsync',
                     'cmd.Amount > amounts.Available', 'VerifyCompanyOwnershipAsync', 'order.SettledAmount += cmd.Amount'):
            self.assertIn(text, receive)

    def test_linked_invoice_rejects_edit_delete_return_cancel(self):
        src = read('src/Inventory.Api/Services/Catalog/InventoryService.cs')
        for start, end in (
            ('public async Task<Order> UpdateOrderAsync', 'public async Task DeleteOrderAsync'),
            ('public async Task DeleteOrderAsync', 'public async Task<Order?> GetLastPurchaseAsync'),
            ('public async Task<SaleChequeReturnSummaryDto> ReturnSaleChequeOrderAsync', 'public async Task ReplaceSaleChequeAsync'),
            ('public async Task CancelSaleChequeOrderAsync', 'public async Task<Order> EditSaleChequeOrderAsync'),
            ('public async Task<Order> EditSaleChequeOrderAsync', 'public async Task<List<TrsAccount>> GetActiveCompanyTreasuryAccountsAsync')):
            self.assertIn('RejectLinkedWarrantyInvoiceMutationAsync', src[src.index(start):src.index(end)])

    def test_catalog_paths_lock_and_protect_warranty_references(self):
        guard = read('src/Inventory.Api/Services/Catalog/ShopCommissionProductKindGuard.cs')
        self.assertIn('db.ShopWarrantyItems.AnyAsync', guard)
        self.assertIn('db.RepairItems.AnyAsync', guard)
        for path in ('src/Inventory.Api/Services/Catalog/InventoryService.cs', 'src/Inventory.Api/Services/Warehousing/WarehousingService.cs'):
            src = read(path)
            save = src[src.index('SaveProductAsync('):src.index('DeleteProductAsync(')]
            delete = src[src.index('DeleteProductAsync('):]
            for block in (save, delete):
                self.assertIn('BeginTransactionAsync(IsolationLevel.Serializable)', block)
            self.assertIn('ShopCommissionProductKindGuard.EnsureChangeAllowedAsync', save)
            self.assertIn('ShopWarrantyItems.AnyAsync', delete)
            self.assertIn('RepairItems.AnyAsync', delete)

    def test_warranty_sale_aggregates_repeated_parts_and_blocks_future_stock(self):
        catalog = read('src/Inventory.Api/Services/Catalog/InventoryService.cs')
        create = catalog[catalog.index('public async Task<Order> CreateOrderAsync'):catalog.index('public async Task<Order?> GetOrderAsync')]
        self.assertIn('if (cmd.IsWarrantyInvoice)', create)
        self.assertIn('t.Date > date', create)
        self.assertIn('GroupBy(l => l.ProductId)', create)
        self.assertIn('group.Sum(l => l.Quantity)', create)
        self.assertIn('if (cmd.Type == TransactionType.Sale && !settings.AllowNegativeStock)', create)

    def test_draft_retry_checks_content_and_locks_customer_identity(self):
        warranty = read('src/Inventory.Api/Services/Repairs/ShopWarrantyService.cs')
        catalog = read('src/Inventory.Api/Services/Catalog/InventoryService.cs')
        draft = warranty[warranty.index('public async Task<ShopWarrantyDto> SaveDraftAsync'):warranty.index('public async Task<ShopWarrantyDto> FinalizeAsync')]
        self.assertIn('previous.Items.Zip(dto.Items).All', draft)
        self.assertIn('previous.Devices.Zip(devices).All', draft)
        self.assertIn('previous.ReceivedAt.Date == dto.ReceivedAt.Date', draft)
        self.assertIn('entity.Type != dto.Type', catalog)
        self.assertIn('w.PartyId == entity.Id', catalog)
        self.assertIn('r.Items.Any(i => i.WarrantyDecision != WarrantyDecision.Unspecified)', catalog)

    def test_changing_a_warranty_product_requires_fresh_price_and_consumption(self):
        warranty = read('src/Inventory.Client/Pages/Repairs/WarrantyTab.razor')
        product = warranty[warranty.index('private void SetProduct'):warranty.index('private async Task Save()')]
        self.assertIn('form.Items[i].Supplied = false', product)
        self.assertIn('form.Items[i].Performed = false', product)
        self.assertIn('form.Items[i].CustomerUnitPrice = null', product)
        self.assertIn('prices[i] = ""', product)
        self.assertIn('decision == WarrantyDecision.Rejected', product)
        repairs = read('src/Inventory.Client/Pages/Repairs/Repairs.razor')
        product = repairs[repairs.index('private async Task OnItemProduct'):repairs.index('private decimal ItemPrice')]
        self.assertIn('form.Items[i].WarrantySupplied = false', product)
        self.assertIn('form.Items[i].WarrantyPerformed = false', product)
        self.assertIn('itemPrice[i] = ""', product)
        self.assertIn('WarrantyDecision != WarrantyDecision.Unspecified ? ""', product)

    def test_repair_scoped_print_does_not_leak_sales_profit_or_commission(self):
        repair = read('src/Inventory.Api/Services/Repairs/RepairService.cs')
        linked = repair[repair.index('public async Task<Order> LinkedInvoiceAsync'):repair.index('// =============================== کمکی')]
        for field in ('order.TotalProfit = null', 'order.NetProfit = null', 'order.CommissionAmount = null',
                      'order.ReferrerGoodsCommissionPercent = null', 'order.ReferrerServiceCommissionPercent = null',
                      'line.UnitCost = null', 'line.Profit = null', 'line.Commission = null'):
            self.assertIn(field, linked)

    def test_financial_detail_is_internal_and_shop_only(self):
        operation = read('src/Inventory.Shared/Dtos/OperationDtos.cs')
        self.assertRegex(operation, r'\[JsonIgnore\]\s+public bool IsWarrantyInvoice')
        self.assertRegex(operation, r'\[JsonIgnore\]\s+public string\? SourceDescription')
        dialog = read('src/Inventory.Client/Pages/Repairs/RepairCreditDialog.razor')
        for text in ('StateUrl', 'ReceiptUrl', 'state.AvailableCredit', 'accountId', 'Guid.NewGuid()', 'state = null'):
            self.assertIn(text, dialog)
        authorizer = read('src/Inventory.Api/Infrastructure/ShopPermissionAuthorizer.cs')
        for text in ('ReceiveWarrantyCredit', 'ReverseWarrantyCredit', 'ReceiveCredit', 'ReverseCredit'):
            self.assertIn(text, authorizer)

if __name__ == '__main__':
    unittest.main()
