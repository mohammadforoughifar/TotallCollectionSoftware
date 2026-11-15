"""Source contracts/SQLite storage; not an executed .NET or authenticated API test."""
from pathlib import Path
import re
import sqlite3
import unittest
ROOT=Path(__file__).resolve().parents[2]/'src'
def read(file):return (ROOT/file).read_text()
class CompanyLogoTests(unittest.TestCase):
    def test_company_form_add_edit_preview_remove(self):
        page=read('Inventory.Client/Pages/System/SystemCompany.razor')
        for text in ['OnChange="OnLogoFile"','LogoDataUrl = c.LogoDataUrl','newCompany.LogoDataUrl = null','src="@newCompany.LogoDataUrl"','file.OpenReadStream(2L * 1024 * 1024)']:
            self.assertIn(text,page)
        self.assertIn('OnLetterheadFile',page)
    def test_server_checks_and_persistence(self):
        controller=read('Inventory.Api/Controllers/System/SystemCompaniesController.cs')
        self.assertEqual(controller.count('CompanyLogoImage.Normalize(c.LogoDataUrl)'),2)
        self.assertIn('item.LogoDataUrl = c.LogoDataUrl',controller)
        helper=read('Inventory.Api/Services/Core/CompanyLogoImage.cs')
        for text in ['Image.DetectFormat(bytes)','Image.Identify(bytes)','Image.Load(','MaxFrames = 1','info.Width > 2048','image.SaveAsPng(output)','output.Length > MaxBytes']:
            self.assertIn(text,helper)
    def test_logo_from_active_company(self):
        self.assertIn('LogoDataUrl = company?.LogoDataUrl',read('Inventory.Api/Controllers/System/CompanyProfileController.cs'))
        for file in ['Pages/Orders/SaleInvoicePrint.cs','Pages/Catalog/GoodsIssuesPrint.cs']:
            self.assertIn('data:image/png;base64,',read('Inventory.Client/'+file))
        self.assertIn('companyProfile.LogoDataUrl',read('Inventory.Client/Pages/Catalog/GoodsIssues.razor'))
    def test_legacy_sqlite_column_and_storage(self):
        schema=read('Inventory.Api/Data/CompanyLogoSchemaV1.cs')
        sql=re.search(r'ExecuteSqlRawAsync\("(ALTER TABLE SystemCompanies.*?;)"',schema)[1]
        db=sqlite3.connect(':memory:');self.addCleanup(db.close)
        db.execute('CREATE TABLE SystemCompanies (Id INTEGER PRIMARY KEY, Name TEXT)')
        db.execute('INSERT INTO SystemCompanies VALUES(1,?)',('Existing',))
        db.execute(sql)
        self.assertIsNone(db.execute('SELECT LogoDataUrl FROM SystemCompanies').fetchone()[0])
        value='data:image/png;base64,example'
        db.execute('UPDATE SystemCompanies SET LogoDataUrl=? WHERE Id=1',(value,))
        self.assertEqual(db.execute('SELECT LogoDataUrl FROM SystemCompanies WHERE Id=1').fetchone()[0],value)
        self.assertIn('exists && !hasLogo',schema)
    def test_profile_logo_is_read_only_in_shop_settings(self):
        source=read('Inventory.Client/Pages/Shop/PrintSettings.razor')
        self.assertIn('src="@profile.LogoDataUrl"',source)
        self.assertNotIn('OnLogoFile',source)
