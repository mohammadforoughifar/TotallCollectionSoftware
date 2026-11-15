"""Source contracts + actual SQLite schema checks, NOT a compiled API test."""
import json
import pathlib
import re
import sqlite3
import unittest
ROOT=pathlib.Path(__file__).resolve().parents[2]
def read(p): return (ROOT/'src'/p).read_text()
class SettingsTests(unittest.TestCase):
    def test_menu_route_permissions(self):
        self.assertIn('"PrintSettings", "تنظیمات چاپ"',read('Inventory.Shared/ShopSections.cs'))
        self.assertIn('ShopAccess.CanSee(Auth, "PrintSettings")',read('Inventory.Client/Layout/NavMenu.razor'))
        page=read('Inventory.Client/Pages/Shop/PrintSettings.razor')
        self.assertIn('@page "/shop/print-settings"',page)
        self.assertIn('ShopAccess.CanManage(Auth, "PrintSettings")',page)
        self.assertNotIn('type="select"',page)
        self.assertNotIn('<select',page)
        self.assertIn('SettingsClient.SaveAsync',page)
    def test_api_authority(self):
        source=read('Inventory.Api/Controllers/Catalog/ShopPrintSettingsController.cs')
        self.assertIn('[ApiController, Authorize',source)
        self.assertIn('Shop.PrintSettings.Manage',source)
        self.assertIn('if (_db.ActiveCompanyId is not > 0)',source)
        self.assertEqual(source.count('x.CompanyId == _db.ActiveCompanyId'),2)
        self.assertLess(source.index('ShopPrintBankRules.Validate(dto)'),source.index('row.BanksJson ='))
        self.assertIn('await _db.SaveChangesAsync()',source)
    def test_schema_round_trip_and_company_uniqueness(self):
        schema=read('Inventory.Api/Data/ShopPrintSettingsSchemaV1.cs')
        sql=re.search(r'const string Sqlite = @"(.*?)";',schema,re.S)[1]
        db=sqlite3.connect(':memory:');self.addCleanup(db.close)
        db.executescript(sql);db.executescript(sql)
        banks=[{'Card':'1234567890123456','Iban':'IR123456789012345678901234'}]
        db.execute('INSERT INTO ShopPrintSettings(CompanyId,BanksJson) VALUES(?,?)',(1,json.dumps(banks)))
        db.execute('INSERT INTO ShopPrintSettings(CompanyId,BanksJson) VALUES(?,?)',(2,'[]'))
        self.assertEqual(json.loads(db.execute('SELECT BanksJson FROM ShopPrintSettings WHERE CompanyId=1').fetchone()[0]),banks)
        with self.assertRaises(sqlite3.IntegrityError): db.execute('INSERT INTO ShopPrintSettings(CompanyId,BanksJson) VALUES(1,?)',('[]',))
        db.execute('UPDATE ShopPrintSettings SET BanksJson=? WHERE CompanyId=1',('[]',))
        self.assertEqual(db.execute('SELECT BanksJson FROM ShopPrintSettings WHERE CompanyId=2').fetchone()[0],'[]')
    def test_shop_sale_only_integration(self):
        source=read('Inventory.Client/Pages/Orders/SaleInvoicePrint.cs')
        self.assertIn('if (isSale && shopSettings is not null)',source)
        self.assertIn('ShopPrintBankRules.Validate(shopSettings)',source)
        for file in ['Orders.razor','OrderForm.razor']:
            source=read('Inventory.Client/Pages/Orders/'+file)
            self.assertIn('order.IsSale ? await PrintSettingsClient.GetAsync() : null',source)
        self.assertIn('ShopPrintSetting>().HasQueryFilter',read('Inventory.Api/Data/AppDbContext.cs'))
    def test_schema_initialization(self):
        self.assertEqual(read('Inventory.Api/Data/DbInitializer.cs').count('await ShopPrintSettingsSchemaV1.EnsureAsync(db)'),3)
