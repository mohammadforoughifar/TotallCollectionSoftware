"""Static checks for the initial shared quote contract; not integration tests."""
from pathlib import Path
import re
import sqlite3
import unittest

SRC = Path(__file__).resolve().parents[2] / 'src/Inventory.Shared/Dtos/SalesProformaDtos.cs'

class SalesProformaContractTests(unittest.TestCase):
    def test_quote_has_no_payment_or_inventory_mutations(self):
        text = SRC.read_text(encoding='utf-8')
        command = text.split('public sealed class SalesProformaCommand', 1)[1].split('public sealed class SalesProformaLine', 1)[0]
        for field in ('PaymentMethod', 'CashAmount', 'Cheques', 'Installments', 'Stock'):
            self.assertNotIn(field, command)
        self.assertIn('ShopInvoiceAmounts.Total(', text)
        self.assertIn('int? SaleOrderId', text)

    def test_conversion_uses_quote_items_and_not_payment_items(self):
        code = (SRC.parents[1] / 'SalesProformaConversion.cs').read_text(encoding='utf-8')
        self.assertIn('Type = TransactionType.Sale', code)
        self.assertIn('PartyId = quote.PartyId', code)
        self.assertIn('Lines = quote.Lines.Select', code)
        self.assertNotIn('Lines = payment.Lines', code)
        self.assertIn('same transaction as the sale', code)

class PersistenceAndWorkflowContracts(unittest.TestCase):
    def test_sqlite_schema_is_idempotent_and_stores_no_stock_or_payment(self):
        schema = (SRC.parents[2] / 'Inventory.Api/Data/SalesProformaSchemaV1.cs').read_text(encoding='utf-8')
        sql = re.search(r'public const string Sqlite = @"(.*?)";', schema, re.S).group(1)
        with sqlite3.connect(':memory:') as db:
            db.executescript(sql)
            db.executescript(sql)
            db.execute("insert into SalesProformas values (?, ?, ?, ?, ?, ?)", ('a'*32, 7, 'PF-test', '2026-01-01', '{}', None))
            self.assertEqual(db.execute('select SaleOrderId from SalesProformas where CompanyId=7').fetchone()[0], None)
            self.assertEqual(db.execute('select count(*) from SalesProformas where CompanyId=8').fetchone()[0], 0)
            self.assertEqual(db.execute("select count(*) from sqlite_master where type='table' and name='Transactions'").fetchone()[0], 0)

    def test_save_and_conversion_boundaries(self):
        controller = (SRC.parents[2] / 'Inventory.Api/Controllers/Sales/OrdersController.cs').read_text(encoding='utf-8')
        save = controller[controller.index('CreateProforma('):controller.index('[HttpPost("proformas/{id:guid}/convert")]')]
        self.assertIn('db.SalesProformas.Add(row)', save)
        self.assertNotIn('CreateOrderAsync', save)
        convert = controller[controller.index('ConvertProforma('):controller.index('/// <summary>ویرایش سند خرید/فروش.')]
        for required in ['IsolationLevel.Serializable', 'q.CompanyId == db.ActiveCompanyId', 'row.SaleOrderId is > 0',
                         'SalesProformaConversion.CreateSale(ReadProforma(row), payment)',
                         '_service.CreateOrderAsync(cmd, CurrentActor)', 'row.SaleOrderId = order.Id', 'transaction.CommitAsync()']:
            self.assertIn(required, convert)
        self.assertLess(convert.index('row.SaleOrderId = order.Id'), convert.index('transaction.CommitAsync()'))

    def test_sales_ui_print_and_confirmation_use_existing_components(self):
        root = SRC.parents[2] / 'Inventory.Client/Pages/Orders'
        form = (root / 'OrderForm.razor').read_text(encoding='utf-8')
        listing = (root / 'Orders.razor').read_text(encoding='utf-8')
        printing = (root / 'ShopSaleInvoicePrint.cs').read_text(encoding='utf-8')
        for required in ['SaveProforma', 'ConversionQuote', 'data-order-payment', 'InvokeAsync<bool>("confirm"']:
            self.assertIn(required, form)
        for required in ['پیش‌فاکتور جدید', 'تبدیل به فروش', 'PrintProforma', 'InvoiceCopyTemplate.From']:
            self.assertIn(required, listing)
        self.assertIn('BuildProforma', printing)
        self.assertIn('proforma ? "" : Payments', printing)

if __name__ == '__main__':
    unittest.main()
