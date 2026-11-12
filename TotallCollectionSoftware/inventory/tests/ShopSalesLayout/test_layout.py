"""Source routing contracts, not compiled .NET execution."""
from pathlib import Path
import re
import unittest
ROOT=Path(__file__).resolve().parents[2]/'src'
def read(p):return (ROOT/p).read_text()
class ShopSaleLayoutTests(unittest.TestCase):
    def test_all_shop_sales_entry_points_use_new_builder(self):
        for file in ['Orders/Orders.razor','Orders/OrderForm.razor','Repairs/Repairs.razor']:
            source=read('Inventory.Client/Pages/'+file)
            self.assertIn('ShopSaleInvoicePrint.Build(',source)
            self.assertIn('"shopPrintPreview.choosePaper"',source)
        self.assertIn(': ShopPurchaseInvoicePrint.Build(order',read('Inventory.Client/Pages/Orders/Orders.razor'))
    def test_template_matches_approved_structure(self):
        source=read('Inventory.Client/Pages/Orders/ShopSaleInvoicePrint.cs')
        template=re.search(r'public const string Template = """\n(.*?)\n""";',source,re.S)[1]
        for id in ['invoice','invoice-number','invoice-date','printed-customer-name','item-rows','printed-notes']:
            self.assertIn('id="'+id+'"',template)
        self.assertEqual(template.count('<th scope="col">'),6) # first heading also has row-heading
        for text in ['مبلغ اولیه','تخفیف','پس از تخفیف','__UNIT_LABEL__']:
            self.assertIn(text,template)
        for text in ['invoice-stamp','grand-total','روش واریز','این نمونه']:
            self.assertNotIn(text,source)
    def test_invoice_customer_is_read_from_authorized_party(self):
        source=read('Inventory.Api/Services/Catalog/InventoryService.cs')
        for text in ['p.Id == t.PartyId.Value','PartyPhone =','PartyAddress = invoiceParty?.Address','PartyTaxId = invoiceParty?.TaxId']:
            self.assertIn(text,source)
        source=read('Inventory.Client/Pages/Orders/ShopSaleInvoicePrint.cs')
        for text in ['Text(order.PartyPhone)','Text(order.PartyAddress)','Text(order.PartyTaxId)']:
            self.assertIn(text,source)
    def test_no_financial_recomputation_or_fixed_tax(self):
        source=read('Inventory.Client/Pages/Orders/ShopSaleInvoicePrint.cs')
        for text in ['order.TotalAmount','line.Gross','line.AfterDiscount','l.VatAmount','MoneyExact()','ReturnedGoodsAmount','ChequeIncomeAmount','order.IsCancelled']:
            self.assertIn(text,source)
        self.assertNotIn('0.1m',source)
        self.assertNotIn('378840000',source)
        self.assertIn('WebUtility.HtmlEncode',source)
        self.assertIn('Regex.Replace(Template',source)
        self.assertIn('ShopPrintBankRules.Validate',source)

class CompleteTemplatesTests(unittest.TestCase):
    def test_purchase_routes_do_not_use_legacy_builder(self):
        for file in ['Orders.razor','OrderForm.razor']:
            source=read('Inventory.Client/Pages/Orders/'+file)
            self.assertIn('ShopPurchaseInvoicePrint.Build(order, companyProfile, printPaperSize)',source)
            self.assertNotRegex(source,r'(?<!Shop)SaleInvoicePrint\.Build\(')
        self.assertIn('ShopSaleInvoicePrint.BuildPurchase',read('Inventory.Client/Pages/Orders/ShopPurchaseInvoicePrint.cs'))
    def test_full_purchase_mode(self):
        source=read('Inventory.Client/Pages/Orders/ShopSaleInvoicePrint.cs')
        for text in ['بهای واحد خرید','فروشنده / تأمین‌کننده','امضای مسئول خرید','امضا و مهر تأمین‌کننده','چک‌های پرداختی به تأمین‌کننده','مانده بدهی هنگام ثبت','purchase ? "پرداخت‌شده" : "وصول‌شده"','purchase ? "" : SellerSection']:
            self.assertIn(text,source)
        self.assertIn('<dd>-</dd>',source) # Supplier invoice is unavailable, never use a demo number.
    def test_dispatch_template_contains_no_unpersisted_fields(self):
        source=read('Inventory.Client/Pages/Catalog/GoodsIssuesPrint.cs')
        template=re.search(r'public const string Template = """\n(.*?)\n""";',source,re.S)[1]
        for text in ['invoice-header','title-cell','document-meta','گیرندهٔ کالا','ملاحظات','امضای تحویل‌دهنده','امضای تحویل‌گیرنده','bank-frame-brand']:
            self.assertIn(text,template)
        for text in ['فاکتور مرتبط','واحد</th>','انبار مبدأ','حمل‌کننده','تلفن','نشانی','مالیات','تخفیف']:
            self.assertNotIn(text,template)
        self.assertIn('ShopSaleInvoicePrint.Style',source)
