"""Source contracts; does not replace API, concurrency or browser integration tests."""
import unittest
from pathlib import Path

SRC = Path(__file__).resolve().parents[2] / 'src'
def read(path): return (SRC / path).read_text(encoding='utf-8')
SERVICE = read('Inventory.Api/Services/Catalog/GoodsIssueService.cs')
PAGE = read('Inventory.Client/Pages/Catalog/GoodsIssues.razor')
SCHEMA = read('Inventory.Api/Data/GoodsIssueSchemaV1.cs')
CONTROLLER = read('Inventory.Api/Controllers/Catalog/GoodsIssuesController.cs')

class Contracts(unittest.TestCase):
    def test_number_allocation_uses_max_and_transaction(self):
        self.assertIn('BeginTransactionAsync(System.Data.IsolationLevel.Serializable)', SERVICE)
        self.assertIn('IgnoreQueryFilters()', SERVICE)
        self.assertIn('int.TryParse(number.Substring(prefix.Length)', SERVICE)
        self.assertIn('checked(max + 1)', SERVICE)
        self.assertNotIn('CountAsync(g => g.Number.StartsWith(prefix))', SERVICE)
        self.assertLess(SERVICE.index('await _db.SaveChangesAsync();'), SERVICE.index('await transaction.CommitAsync();'))

    def test_warehouses_are_company_scoped_and_saved(self):
        query = SERVICE.split('public Task<List<LookupItem>> GetWarehouseOptionsAsync()', 1)[1].split('/// <summary>', 1)[0]
        self.assertIn('_db.Warehouses', query)
        self.assertNotIn('IgnoreQueryFilters', query)
        self.assertIn('[HttpGet("warehouse-options")]', CONTROLLER)
        for term in ('entity.WarehouseId = warehouse?.Id;', 'entity.WarehouseName = warehouse?.Name;', 'WarehouseName = g.WarehouseName,'):
            self.assertIn(term, SERVICE)
        self.assertIn('WarehouseId = form.WarehouseId', PAGE)
        self.assertIn('Items="warehouseLookup"', PAGE)

    def test_existing_database_columns_are_added_for_both_providers(self):
        self.assertIn("COL_LENGTH(N'dbo.GoodsIssues', N'WarehouseId')", SCHEMA)
        self.assertIn('PRAGMA table_info(GoodsIssues)', SCHEMA)
        self.assertIn('ALTER TABLE GoodsIssues ADD COLUMN WarehouseName TEXT NULL', SCHEMA)

if __name__ == '__main__': unittest.main()
