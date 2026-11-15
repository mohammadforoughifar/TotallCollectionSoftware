"""Offline SQLite schema and source contracts; does not call an SMS provider."""
from pathlib import Path
import re
import sqlite3
import unittest

ROOT = Path(__file__).resolve().parents[2]
def read(path): return (ROOT / path).read_text(encoding='utf-8')
SCHEMA = read('src/Inventory.Api/Data/CompanySmsSettingsSchemaV1.cs')
SQLITE = re.search(r'public const string Sqlite = @"(.*?)";', SCHEMA, re.S).group(1)

class CompanySmsSchemaTests(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(':memory:')
        self.db.execute('PRAGMA foreign_keys=ON')
        self.db.executescript('''CREATE TABLE SystemCompanies (Id INTEGER PRIMARY KEY, Name TEXT, IsActive INTEGER);
            INSERT INTO SystemCompanies VALUES (1,'اول',1),(2,'دوم',1);
            CREATE TABLE RepairSmsNotifications (Id INTEGER PRIMARY KEY, CompanyId INTEGER, Status INTEGER, Recipient TEXT, CompletedAtUtc TEXT);''')
        self.db.executescript(SQLITE)
        self.db.executescript(SQLITE)
    def tearDown(self): self.db.close()

    def add(self, company, module, enabled=None):
        if enabled is None:
            self.db.execute("INSERT INTO CompanySmsSettings (CompanyId,ModuleKey,UpdatedAtUtc) VALUES (?,?,'now')", (company, module))
        else:
            self.db.execute("INSERT INTO CompanySmsSettings (CompanyId,ModuleKey,Enabled,UpdatedAtUtc) VALUES (?,?,?,'now')", (company,module,enabled))

    def test_per_company_and_module_independent_with_default_off(self):
        self.add(1, 'Shop.Repairs'); self.add(1, 'Shop.Other', 1); self.add(2, 'Shop.Repairs', 1)
        self.assertEqual(self.db.execute('SELECT Enabled FROM CompanySmsSettings WHERE CompanyId=1 AND ModuleKey=?', ('Shop.Repairs',)).fetchone()[0], 0)
        self.assertEqual(self.db.execute('SELECT Enabled FROM CompanySmsSettings WHERE CompanyId=2 AND ModuleKey=?', ('Shop.Repairs',)).fetchone()[0], 1)
        with self.assertRaises(sqlite3.IntegrityError): self.add(1, 'Shop.Repairs', 1)
        with self.assertRaises(sqlite3.IntegrityError): self.add(99, 'Shop.Repairs', 1)
        self.db.execute('DELETE FROM SystemCompanies WHERE Id=1')
        self.assertEqual(self.db.execute('SELECT COUNT(*) FROM CompanySmsSettings').fetchone()[0], 1)

    def test_old_outbox_revision_column_is_additive_and_null(self):
        self.db.execute("INSERT INTO RepairSmsNotifications VALUES (1,1,1,'09',NULL)")
        migration = read('src/Inventory.Api/Data/RepairSmsPermissionRevisionSchemaV1.cs')
        alter = re.search(r'public const string SqliteAlter = "(.*?)";', migration).group(1)
        def ensure():
            columns = [r[1] for r in self.db.execute('PRAGMA table_info(RepairSmsNotifications)')]
            if 'PermissionRevision' not in columns: self.db.execute(alter)
        ensure(); ensure()
        self.assertIsNone(self.db.execute('SELECT PermissionRevision FROM RepairSmsNotifications WHERE Id=1').fetchone()[0])

    def test_disable_and_pending_cancellation_are_one_transaction(self):
        self.add(1, 'Shop.Repairs', 1); self.add(2, 'Shop.Repairs', 1); self.add(1, 'Shop.Other', 1)
        self.db.executemany('INSERT INTO RepairSmsNotifications VALUES (?,?,?,?,?)', [(1,1,1,'09',None),(2,2,1,'09',None),(3,1,2,'09',None)])
        self.db.commit()
        def disable():
            self.db.execute("UPDATE CompanySmsSettings SET Enabled=0 WHERE CompanyId=1 AND ModuleKey='Shop.Repairs'")
            self.db.execute("UPDATE RepairSmsNotifications SET Status=11,Recipient=NULL,CompletedAtUtc='now' WHERE CompanyId=1 AND Status=1")
        self.db.execute('BEGIN'); disable(); self.db.rollback()
        self.assertEqual(self.db.execute('SELECT Status FROM RepairSmsNotifications WHERE Id=1').fetchone()[0], 1)
        self.assertEqual(self.db.execute("SELECT Enabled FROM CompanySmsSettings WHERE CompanyId=1 AND ModuleKey='Shop.Repairs'").fetchone()[0], 1)
        self.db.execute('BEGIN'); disable(); self.db.commit()
        self.db.execute("UPDATE CompanySmsSettings SET Enabled=1 WHERE CompanyId=1 AND ModuleKey='Shop.Repairs'")
        self.assertEqual(self.db.execute('SELECT Id,Status,Recipient FROM RepairSmsNotifications ORDER BY Id').fetchall(), [(1,11,None),(2,1,'09'),(3,2,'09')])
        self.assertEqual(self.db.execute("SELECT Enabled FROM CompanySmsSettings WHERE CompanyId=1 AND ModuleKey='Shop.Other'").fetchone()[0], 1)

class CompanySmsSourceContracts(unittest.TestCase):
    def test_catalog_policy_and_both_repair_checks_use_exact_module(self):
        catalog = read('src/Inventory.Shared/SmsModuleCatalog.cs')
        policy = read('src/Inventory.Api/Services/Notification/CompanySmsServicePolicy.cs')
        queue = read('src/Inventory.Api/Services/Repairs/RepairSmsQueue.cs')
        self.assertIn('"Shop.Repairs"', catalog)
        self.assertIn('"سامانه فروشگاهی", "تعمیرات"', catalog)
        self.assertIn('!SmsModuleCatalog.Contains(moduleKey)', policy)
        self.assertIn('s.CompanyId == companyId.Value', policy)
        self.assertIn('s.ModuleKey == moduleKey && s.Enabled', policy)
        self.assertIn('expectedRevision is > 0', policy)
        self.assertIn('c.Id == companyId.Value && c.IsActive', policy)
        self.assertIn('CompanySmsServicePolicy.GetEnabledRevisionAsync(', queue)
        self.assertGreaterEqual(queue.count('CompanySmsServicePolicy.IsEnabledForRevisionAsync('), 2)
        self.assertIn('PermissionRevision = revision', queue)
        self.assertIn('job.PermissionRevision', queue)
        self.assertIn('repair.CompanyId, SmsModuleCatalog.ShopRepairs', queue)
        self.assertIn('job.CompanyId, SmsModuleCatalog.ShopRepairs', queue)
        self.assertIn('gateway.SendLookupAsync(', queue)
        self.assertNotIn('gateway.SendAsync(', queue)

    def test_schema_migration_filter_and_revocation_hook(self):
        ctx = read('src/Inventory.Api/Data/AppDbContext.cs')
        self.assertIn('new { x.CompanyId, x.ModuleKey }', ctx)
        self.assertIn('Entity<CompanySmsSetting>().HasQueryFilter', ctx)
        self.assertEqual(read('src/Inventory.Api/Data/DbInitializer.cs').count('CompanySmsSettingsSchemaV1.EnsureAsync(db)'), 2)
        self.assertIn('CompanySmsSettingsSchemaV1.SqlServer', read('src/Inventory.Api/Migrations/20261110100000_AddCompanySmsSettings.cs'))
        self.assertEqual(read('src/Inventory.Api/Data/DbInitializer.cs').count('RepairSmsPermissionRevisionSchemaV1.EnsureAsync(db)'), 2)
        self.assertIn('PermissionRevision', read('src/Inventory.Api/Data/RepairSmsPermissionRevisionSchemaV1.cs'))
        revoke = read('src/Inventory.Api/Services/Repairs/RepairSmsRevocationHandler.cs')
        self.assertIn('n.CompanyId == companyId && n.Status == RepairSmsStatus.Pending', revoke)
        self.assertIn('RepairSmsStatus.ModuleDisabled', revoke)
        controller = read('src/Inventory.Api/Controllers/System/CompanySmsSettingsController.cs')
        self.assertIn('IsolationLevel.Serializable', controller)
        self.assertIn('row.Revision++', controller)
        self.assertLess(controller.index('db.SaveChangesAsync(ct)'), controller.index('handler.CancelPendingAsync('))
        self.assertLess(controller.index('handler.CancelPendingAsync('), controller.index('tx.CommitAsync(ct)'))

    def test_no_erp_permission_dependency_and_stale_context_fails(self):
        controller = read('src/Inventory.Api/Controllers/System/CompanySmsSettingsController.cs')
        self.assertIn('dto.CompanyId != companyId.Value', controller)
        self.assertIn('SmsSettings.Update', controller)
        self.assertIn('SmsSettings.Read', controller)
        self.assertNotIn('Moadian', controller)
        self.assertNotIn('ERP', controller)
        nav = read('src/Inventory.Client/Layout/NavMenu.razor')
        layout = read('src/Inventory.Client/Layout/MainLayout.razor')
        self.assertIn('CanSeeSmsSettings ||', nav)
        self.assertIn('href="settings/sms-service"', nav)
        self.assertLess(layout.index('p == "settings/sms-service"'), layout.index('foreach (var (prefix, perm, title) in RouteGuards)'))
        ui = read('src/Inventory.Client/Pages/Settings/SmsService.razor')
        self.assertIn('@model.CompanyName', ui)
        self.assertIn('CompanyId = model.CompanyId', ui)
        self.assertIn('@bind="module.Enabled"', ui)
        self.assertNotIn('ApiKey', ui)

if __name__ == '__main__': unittest.main()
