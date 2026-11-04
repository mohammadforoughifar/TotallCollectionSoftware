"""Static VAT wiring checks; use the .NET probe for actual calculations and persistence."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
def read(path):
    return (ROOT / 'src' / path).read_text(encoding='utf-8')

class VatContract(unittest.TestCase):
    def test_shared_boundary_and_client_payment_base(self):
        calc = read('Inventory.Shared/OrderLineAmounts.cs')
        self.assertIn('Math.Round(quantity * price, 2)', calc)
        self.assertIn('value <= 100m', calc)
        self.assertIn('Math.Round(Math.Max(0, afterDiscount) * value / 100m, 2)', calc)
        self.assertIn(': value;', calc)
        self.assertIn('decimal.Round(value, 2) == value', calc)
        self.assertIn('afterDiscount + Vat(afterDiscount, vatValue)', calc)
        form = read('Inventory.Client/Pages/Orders/OrderForm.razor')
        self.assertIn('OrderLineAmounts.Vat(AfterDiscount, VatValue)', form)
        self.assertIn('goodsTotal => TotalLines.Sum(l => l.Total)', form)
        self.assertIn('grandTotal => goodsTotal + saleChequeIncome', form)
        self.assertIn('data-order-last-field', form.split('aria-label="ارزش افزوده درصد یا ریال"', 1)[1].split('/>', 1)[0])
        self.assertIn('VatValue = l.VatValue', form)

    def test_server_create_update_payment_and_legacy_compatibility(self):
        service = read('Inventory.Api/Services/Catalog/InventoryService.cs')
        self.assertEqual(2, service.count('VatAmount = OrderLineAmounts.Vat(Math.Max(0, gross - discAmount), l.VatValue ?? 0)'))
        self.assertEqual(2, service.count('txn.Amount += afterDiscount + OrderLineAmounts.Vat(afterDiscount, l.VatValue ?? 0)'))
        self.assertIn('OrderLineAmounts.Total(l.Quantity, l.Price, l.DiscountPercent, l.DiscountAmount, l.DiscountIsAmount, l.VatValue ?? 0)', service)
        self.assertIn('line.VatValue ??= existingLineVat.TryGetValue', service)
        self.assertIn('oldVat.ProductId == line.ProductId', service)
        self.assertIn('l.Profit = decimal.Round(l.AfterDiscount', service)
        self.assertIn('VatAmount = l.VatAmount', service)

    def test_product_default_uses_separate_column_and_old_rates_fallback(self):
        for path in ['Inventory.Api/Services/Catalog/InventoryService.cs', 'Inventory.Api/Services/Warehousing/WarehousingService.cs']:
            service = read(path)
            self.assertIn('p.InvoiceVatValue ?? (p.IsVatIncluded && p.VatRate >= 0 && p.VatRate <= 100 ? p.VatRate : 0)', service)
            self.assertIn('entity.InvoiceVatValue = dto.InvoiceVatValue.Value', service)
        for path in ['Inventory.Client/Pages/Catalog/Products.razor', 'Inventory.Client/Pages/Warehousing/InvProducts.razor']:
            self.assertIn('@bind="form.InvoiceVatValue"', read(path))

    def test_schema_upgrade_and_print(self):
        schema = read('Inventory.Api/Data/OrderVatSchemaV1.cs')
        self.assertIn('[InvoiceVatValue] decimal(18,2) NULL', schema)
        self.assertIn('[GrossAmount] decimal(18,2) NULL', schema)
        for name in ['VatValue', 'VatAmount']:
            self.assertIn(f'[{name}] decimal(18,2) NOT NULL DEFAULT(0)', schema)
        init = read('Inventory.Api/Data/DbInitializer.cs')
        self.assertEqual(init.count('await OrderVatSchemaV1.EnsureAsync(db)'), 2)
        self.assertLess(init.index('await OrderVatSchemaV1.EnsureAsync(db)'), init.index('await TransactionLineCostSchemaV1.EnsureAsync(db)'))
        printed = read('Inventory.Client/Pages/Orders/SaleInvoicePrint.cs')
        for term in ['line.AfterDiscount.Money()', 'line.VatAmount.Money()', 'line.Total.Money()', 'order.TotalAmount.Money()']:
            self.assertIn(term, printed)

if __name__ == '__main__': unittest.main()
