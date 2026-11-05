"""Source contracts; a real .NET/browser integration test is still required."""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'src'

def read(path):
    return (ROOT / path).read_text(encoding='utf-8')

class InvoiceReliability(unittest.TestCase):
    def test_parallel_initial_requests_and_stock_lookup(self):
        form = read('Inventory.Client/Pages/Orders/OrderForm.razor')
        init = form[form.index('protected override async Task OnInitializedAsync()'):form.index('private Task<bool> LoadWarehouseStocksAsync()')]
        self.assertLess(init.index('var bankTask ='), init.index('whLookup = await whTask'))
        self.assertLess(init.index('var customerTask ='), init.index('whLookup = await whTask'))
        self.assertLess(init.index('if (IsEdit) warehouseId = EditOrder!.WarehouseId'), init.index('var stockTask ='))
        self.assertIn('customers = await customerTask!', init)
        service = read('Inventory.Api/Services/Catalog/InventoryService.cs')
        self.assertIn('var stocksByProduct = stocks.ToLookup(s => s.ProductId)', service)
        self.assertIn('ToProductDto(p, stocksByProduct[p.Id], whNames)', service)

    def test_validation_errors_are_in_the_modal(self):
        form = read('Inventory.Client/Pages/Orders/OrderForm.razor')
        self.assertIn('class="order-form-error" role="alert"', form)
        validation = form[form.index('private bool ValidateInvoice()'):]
        self.assertNotIn('Toast.Error(', validation)
        self.assertIn('ShowFormError(', validation)
        css = read('Inventory.Client/Pages/Orders/OrderForm.razor.css')
        self.assertIn('.order-form-error { position: fixed;', css)

    def test_tracking_is_validated_persisted_and_restored(self):
        form = read('Inventory.Client/Pages/Orders/OrderForm.razor')
        for part in ['Type == TransactionType.Sale && CashKind == CashType.CardTransfer', '@bind="cashTrackingNumber"',
                     'cashTrackingNumber = o.CashTrackingNumber', 'cmd.CashTrackingNumber =', 'string.IsNullOrWhiteSpace(cashTrackingNumber)']:
            self.assertIn(part, form)
        dto = read('Inventory.Shared/Dtos/OperationDtos.cs')
        self.assertEqual(dto.count('public string? CashTrackingNumber { get; set; }'), 2)
        service = read('Inventory.Api/Services/Catalog/InventoryService.cs')
        self.assertIn('CashTrackingNumber = t.CashTrackingNumber', service)
        self.assertIn('? reference ?? txn.CashTrackingNumber : null', service)
        controller = read('Inventory.Api/Controllers/Sales/OrdersController.cs')
        self.assertIn('TrackingError(cmd, existing.Type, existing)', controller)
        self.assertIn('string.IsNullOrWhiteSpace(reference)', controller)
        self.assertIn('viewing.CashTrackingNumber', read('Inventory.Client/Pages/Orders/Orders.razor'))

    def test_schema_covers_new_and_existing_databases(self):
        schema = read('Inventory.Api/Data/OrderTrackingSchemaV1.cs')
        self.assertIn("COL_LENGTH(N'dbo.Transactions', N'CashTrackingNumber') IS NULL", schema)
        self.assertIn('PRAGMA table_info', schema)
        self.assertIn('if (!found)', schema)
        self.assertEqual(read('Inventory.Api/Data/DbInitializer.cs').count('OrderTrackingSchemaV1.EnsureAsync(db)'), 2)
        self.assertIn('[MaxLength(100)]', read('Inventory.Api/Entities/Sales/Transaction.cs'))
        self.assertIn('CashTrackingNumber', read('Inventory.Api/Migrations/AppDbContextModelSnapshot.cs'))
        self.assertIn('OrderTrackingSchemaV1.SqlServer', read('Inventory.Api/Migrations/20261104130000_AddOrderCashTracking.cs'))

if __name__ == '__main__':
    unittest.main()
