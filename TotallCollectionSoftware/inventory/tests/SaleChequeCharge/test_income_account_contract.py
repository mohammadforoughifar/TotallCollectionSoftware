"""Static checks and a SQLite DDL check; not a substitute for the .NET integration probe."""
import re
import sqlite3
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'src'
def read(path): return (ROOT / path).read_text(encoding='utf-8')

class IncomeAccountContract(unittest.TestCase):
    def test_add_column_preserves_existing_rule(self):
        source = read('Inventory.Api/Data/SaleChequeIncomeAccountSchemaV1.cs')
        match = re.search(r'alter.CommandText = "(.*?)";', source)
        self.assertIsNotNone(match)
        sql = match.group(1).replace(r'\"', '"')
        db = sqlite3.connect(':memory:')
        db.execute('CREATE TABLE FacRules (Id INTEGER PRIMARY KEY, MainAccountId INTEGER)')
        db.execute('INSERT INTO FacRules VALUES (1, 42)')
        db.execute(sql)
        self.assertEqual(db.execute('SELECT MainAccountId, ChequeIncomeAccountId FROM FacRules').fetchone(), (42, None))
        self.assertIn('if (!present)', source)
        self.assertIn('COL_LENGTH', source)
        db.close()

    def test_setting_does_not_get_erased_by_an_old_client(self):
        code = read('Inventory.Api/Services/Invoicing/InvoicingService.cs')
        self.assertIn('if (dto.ChequeIncomeAccountId.HasValue)', code)
        self.assertIn('income.Type != AccountType.Income || income.IsPermanent', code)
        self.assertIn('income.IsActive', code)
        self.assertIn('income.IsPostable', code)
        self.assertIn('Contains(e.ChequeIncomeAccountId)', code)
        self.assertIn('ChequeIncomeAccountName = Name(r.ChequeIncomeAccountId)', code)
        form = read('Inventory.Client/Pages/Invoicing/FacRules.razor')
        self.assertIn('rule.Kind == InvoiceKind.Sale', form)
        self.assertIn('ValueChanged="v => rule.ChequeIncomeAccountId = v"', form)

    def test_database_initialization_and_migration(self):
        self.assertEqual(read('Inventory.Api/Data/DbInitializer.cs').count('SaleChequeIncomeAccountSchemaV1.EnsureAsync(db)'), 2)
        self.assertIn('SaleChequeIncomeAccountSchemaV1.SqlServer', read('Inventory.Api/Migrations/20261104150000_AddSaleChequeIncomeAccount.cs'))
        self.assertIn('ChequeIncomeAccountId', read('Inventory.Api/Migrations/AppDbContextModelSnapshot.cs'))

if __name__ == '__main__': unittest.main()
