"""Executable SQLite schema checks and source contracts; EF/API need a .NET SDK."""
import datetime
import re
import sqlite3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCHEMA = (ROOT / 'src/Inventory.Api/Data/ShopPricingSchemaV1.cs').read_text()
PRICING = (ROOT / 'src/Inventory.Api/Services/Catalog/ShopPricingService.cs').read_text()
GUARD = (ROOT / 'src/Inventory.Api/Services/Catalog/ShopMinimumProfitGuard.cs').read_text()
INVENTORY = (ROOT / 'src/Inventory.Api/Services/Catalog/InventoryService.cs').read_text()
CTRL = (ROOT / 'src/Inventory.Api/Controllers/Sales/OrdersController.cs').read_text()
FORM = (ROOT / 'src/Inventory.Client/Pages/Orders/OrderForm.razor').read_text()


def verbatim(pattern):
    found = re.search(pattern, SCHEMA, re.S)
    if not found:
        raise AssertionError('SQL constant not found')
    return found.group(1).replace('""', '"')


class PricingSchemaTests(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(':memory:')
        self.db.execute('PRAGMA foreign_keys=ON')
        self.db.execute('CREATE TABLE Products (Id INTEGER PRIMARY KEY, CompanyId INTEGER, SalePrice TEXT NOT NULL)')
        self.db.executemany('INSERT INTO Products VALUES(?,?,?)', [(1, 10, '120.25'), (2, 20, '240'), (3, None, '80')])
        self.sql = verbatim(r'public const string Sqlite = @"(.*?)";')
        self.ownership = verbatim(r'var ownership = db.Database.IsSqlite\(\) \? @"(.*?)"\s*:')
        self.baseline = verbatim(r'var baseline = db.Database.IsSqlite\(\) \? @"(.*?)"\s*:')

    def repair(self):
        self.db.executescript(self.sql)
        self.db.executescript(self.ownership)
        self.db.executescript(self.baseline)

    def test_repair_repeat_is_idempotent_and_baseline_is_only_today(self):
        self.repair(); self.repair()
        rows = self.db.execute('SELECT ProductId, CompanyId, EffectiveDate, Price, Source FROM ShopSalePrices ORDER BY ProductId').fetchall()
        self.assertEqual(len(rows), 3)
        today = self.db.execute("SELECT date('now','localtime')").fetchone()[0]
        self.assertTrue(all(row[2] == today + ' 00:00:00' and row[4] == 'baseline' for row in rows))
        self.assertEqual([r[1] for r in rows], [10, 20, None])
        yesterday = (datetime.date.fromisoformat(today) - datetime.timedelta(days=1)).isoformat() + ' 23:59:59'
        self.assertEqual(self.db.execute('SELECT COUNT(*) FROM ShopSalePrices WHERE EffectiveDate <= ?', (yesterday,)).fetchone()[0], 0)

    def test_owner_follows_known_product_without_cross_company_guess(self):
        self.repair()
        self.db.execute('UPDATE Products SET CompanyId=10 WHERE Id=3')
        self.repair()
        self.assertEqual(self.db.execute('SELECT CompanyId FROM ShopSalePrices WHERE ProductId=3').fetchone()[0], 10)
        self.assertEqual(self.db.execute('SELECT CompanyId FROM ShopSalePrices WHERE ProductId=2').fetchone()[0], 20)
        self.assertEqual(self.db.execute('SELECT COUNT(*) FROM ShopSalePrices').fetchone()[0], 3)

    def test_dated_order_same_day_override_and_future_is_not_today(self):
        self.repair()
        today = self.db.execute("SELECT date('now','localtime')").fetchone()[0]
        tomorrow = (datetime.date.fromisoformat(today) + datetime.timedelta(days=1)).isoformat()
        def add(date, amount, source):
            self.db.execute('INSERT INTO ShopSalePrices(CompanyId,ProductId,EffectiveDate,Price,Source,CreatedAt) VALUES(10,1,?,?,?,?)',
                            (date + ' 00:00:00', str(amount), source, today + ' 12:00:00'))
        add(today, 135, 'purchase'); add(tomorrow, 150, 'group')
        query = 'SELECT Price FROM ShopSalePrices WHERE CompanyId=10 AND ProductId=1 AND EffectiveDate < ? ORDER BY EffectiveDate DESC, Id DESC LIMIT 1'
        self.assertEqual(self.db.execute(query, (tomorrow + ' 00:00:00',)).fetchone()[0], '135')
        day_after = (datetime.date.fromisoformat(tomorrow) + datetime.timedelta(days=1)).isoformat()
        self.assertEqual(self.db.execute(query, (day_after + ' 00:00:00',)).fetchone()[0], '150')
        self.assertIsNone(self.db.execute(query.replace('CompanyId=10', 'CompanyId=20'), (tomorrow + ' 00:00:00',)).fetchone())

    def test_unique_batch_and_product_cascade(self):
        self.repair()
        sql = 'INSERT INTO ShopSalePrices(CompanyId,ProductId,EffectiveDate,Price,Source,CreatedAt,OperationId) VALUES(10,1,?,?,?,?,?)'
        self.db.execute(sql, ('2026-01-01', '150', 'group', '2026-01-01', 'batch'))
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute(sql, ('2026-01-02', '170', 'group', '2026-01-02', 'batch'))
        self.db.execute('DELETE FROM Products WHERE Id=1')
        self.assertEqual(self.db.execute('SELECT COUNT(*) FROM ShopSalePrices WHERE ProductId=1').fetchone()[0], 0)


class PricingContractTests(unittest.TestCase):
    def test_razor_injected_properties_do_not_share_the_component_name(self):
        # Razor turns @inject into a member of the component's generated class.
        # A member named like its component produces CS0542 at compile time.
        client = ROOT / 'src/Inventory.Client'
        for page in client.rglob('*.razor'):
            source = page.read_text(encoding='utf-8')
            for match in re.finditer(r'^\s*@inject\s+[^\r\n]+?\s+(\w+)\s*$', source, re.MULTILINE):
                with self.subTest(page=str(page.relative_to(client)), member=match.group(1)):
                    self.assertNotEqual(match.group(1), page.stem)

    def test_shop_rule_applies_in_all_sale_mutations_and_never_to_services(self):
        self.assertEqual(INVENTORY.count('ShopMinimumProfitGuard.ValidateAsync('), 3)
        self.assertIn('!p.IsService', GUARD)
        self.assertIn('TransactionType.Purchase', GUARD)
        self.assertIn('t.Date < end', GUARD)
        self.assertIn('l.DiscountAmount', GUARD)
        self.assertIn('ReferrerGoodsPercentSnapshot', GUARD)
        self.assertIn('line.UnitCostSnapshot', GUARD)
        self.assertNotIn('VatAmount', GUARD)
        self.assertNotIn('ChequeCharge', GUARD)

    def test_existing_line_unchanged_and_duplicate_ids_not_exempt(self):
        self.assertIn('old.ProductId == line.ProductId && old.Quantity == line.Quantity && old.Price == line.Price', GUARD)
        self.assertIn('existingIds.Distinct().Count()', GUARD)
        self.assertIn('unchangedFloorLines', INVENTORY)

    def test_history_has_no_early_fallback_and_group_preview_is_atomic(self):
        self.assertIn('x.EffectiveDate < end', PRICING)
        self.assertIn('ThenByDescending(x => x.Id)', PRICING)
        self.assertIn('IsolationLevel.Serializable', PRICING)
        self.assertIn('preview.PreviewHash, cmd.PreviewHash', PRICING)
        self.assertIn('OperationId == cmd.OperationId', PRICING)
        self.assertIn('p.CompanyId == company', PRICING)
        self.assertIn('p.CategoryId == null && p.Category != null', PRICING)
        self.assertIn('گروه متنی مبهم', PRICING)

    def test_optional_purchase_price_is_permission_guarded_and_date_effective(self):
        self.assertIn('NewSalePrice.HasValue', CTRL)
        self.assertIn('Shop.Pricing.Manage', CTRL)
        self.assertIn('existing.Date.Date < DateTime.Today', CTRL)
        permission_check = CTRL.split('PricingPermissionErrorAsync', 1)[1].split('private static Order', 1)[0]
        self.assertIn('(existing is not null && existing.Date.Date < DateTime.Today)', permission_check)
        self.assertIn('EffectiveDate = transaction.Date.Date', PRICING)
        self.assertIn('PurchaseTransactionId = transaction.Id', PRICING)
        self.assertIn('Type == TransactionType.Purchase && canManagePricing', FORM)
        self.assertIn('placeholder="بدون تغییر"', FORM)

    def test_historical_suggestion_never_changes_existing_or_manual_price(self):
        self.assertIn('if (l.ProductId == id) return;', FORM)
        self.assertIn('GetDatedSalePriceAsync(id, invoiceDate.Value)', FORM)
        self.assertIn('!string.IsNullOrWhiteSpace(l.PriceText)', FORM)
        self.assertIn('قیمت این تاریخ ثبت نشده', FORM)
        self.assertIn('PriceText = FormatNum(l.Price.ToString', FORM)

if __name__ == '__main__':
    unittest.main()
