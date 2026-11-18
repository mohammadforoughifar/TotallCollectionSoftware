"""Source contracts and SQLite schema checks (the API still needs runtime integration tests)."""
import re
import sqlite3
import unittest
from pathlib import Path

SRC = Path(__file__).resolve().parents[2] / 'src'
def read(path): return (SRC / path).read_text(encoding='utf-8')

class VisitorSettingsTests(unittest.TestCase):
    def test_sqlite_schema_is_idempotent_and_company_user_key_is_unique(self):
        source = read('Inventory.Api/Data/ShopVisitorDiscountSchemaV1.cs')
        sql = re.search(r'public const string Sqlite = @"(.*?)";', source, re.S).group(1)
        with sqlite3.connect(':memory:') as db:
            db.executescript(sql)
            db.executescript(sql)
            db.execute('INSERT INTO ShopVisitorDiscounts VALUES (1, 7, ?)', ('10',))
            db.execute('INSERT INTO ShopVisitorDiscounts VALUES (2, 7, ?)', ('20',))
            with self.assertRaises(sqlite3.IntegrityError):
                db.execute('INSERT INTO ShopVisitorDiscounts VALUES (1, 7, ?)', ('30',))
            self.assertEqual(db.execute('SELECT MaxDiscountPercent FROM ShopVisitorDiscounts WHERE CompanyId=2 AND UserId=7').fetchone()[0], '20')

    def test_settings_authorization_and_scope(self):
        source = read('Inventory.Api/Controllers/Catalog/ShopPrintSettingsController.cs')
        for required in ['CanAccessVisitorSettings(true)', 'Shop.PrintSettings.Manage',
                         'input.CompanyId != _db.ActiveCompanyId', 'ActiveVisitors(companyId)',
                         'u.IsActive', 'p.Action == "Create"', 'x.CompanyId == companyId']:
            self.assertIn(required, source)
        self.assertNotIn('entity.MaxDiscountPercent =', read('Inventory.Api/Services/Catalog/InventoryService.cs'))
        self.assertNotIn('fDiscountCap', read('Inventory.Client/Pages/Catalog/Referrers.razor'))

    def test_order_ownership_price_and_discount_checked_server_side(self):
        service = read('Inventory.Api/Services/SalesVisit/VisOrderService.cs')
        for required in ['o.CreatedByUserId == myUserId', 'if (mine) q = q.Where',
                         'ReferrerName = referrerName', 'var referrerName = actor.UserName;',
                         'x.UserId == entity.CreatedByUserId', 'l.DiscountAmount * 100m >',
                         'ShopPricingService.ResolveAsync(_db, productIds, DateTime.Today)',
                         'if (active == 0) throw', 'قیمت فروش تغییر کرده است']:
            self.assertIn(required, service)
        cartable = read('Inventory.Api/Services/Cartable/CartableService.cs')
        self.assertIn('o.CreatedByUserId == ctx.UserId', cartable)
        self.assertIn('Api.ListAsync(st, search, mine: true)', read('Inventory.Client/Pages/SalesVisit/MyVisits.razor'))

if __name__ == '__main__': unittest.main()
