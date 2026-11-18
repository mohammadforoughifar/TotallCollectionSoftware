"""Source regression contracts for the nine reported shop-module bugs.

These are static contracts (plus the C# probes in ShopPricing/OrderVat); they do
not replace a Razor build, a live API test or a browser test. The .NET SDK is
not available in this environment.

    python3 -m unittest discover -s inventory/tests/ShopBugFixes -p 'test_*.py' -q
"""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'src'
API = ROOT / 'Inventory.Api'
CLIENT = ROOT / 'Inventory.Client'
SHARED = ROOT / 'Inventory.Shared'


def read(path: Path) -> str:
    return path.read_text(encoding='utf-8')


RULES = read(SHARED / 'ShopIdentifierRules.cs')
MODAL = read(CLIENT / 'Shared/PartyFormModal.razor')
PARTIES_CTRL = read(API / 'Controllers/Catalog/PartiesController.cs')
PRODUCTS = read(CLIENT / 'Pages/Catalog/Products.razor')
CATEGORIES = read(CLIENT / 'Pages/Catalog/Categories.razor')
PRICING = read(CLIENT / 'Pages/Shop/Pricing.razor')
PRODUCTS_CTRL = read(API / 'Controllers/Catalog/ProductsController.cs')
ORDERS_CTRL = read(API / 'Controllers/Sales/OrdersController.cs')
SERVICE = read(API / 'Services/Catalog/InventoryService.cs')
I_SERVICE = read(API / 'Services/Catalog/IInventoryService.cs')
I_CLIENT = read(CLIENT / 'Services/IInventoryApi.cs')
CLIENT_SVC = read(CLIENT / 'Services/InventoryApiServices.cs')
ORDER_FORM = read(CLIENT / 'Pages/Orders/OrderForm.razor')
STOCK_SNAPSHOT = read(CLIENT / 'Pages/Orders/InvoiceWarehouseStock.cs')
DTOS = read(SHARED / 'Dtos/CatalogDtos.cs')


class Bug1And2PartyValidation(unittest.TestCase):
    """ارور اعتبارسنجی یعنی عدم ذخیره؛ فرم تأمینکننده هم validation دارد."""

    def test_error_never_saves_client_or_server(self):
        # تک دروازهٔ ذخیره: قبل از هر API بررسی میکند و برمیگردد.
        self.assertIn('if (!ValidateForm()) return;', MODAL)
        self.assertIn('private bool ValidateForm()', MODAL)
        self.assertIn('ShopIdentifierRules.ValidateParty(party)', MODAL)
        # نگهبان نهایی جلوی هر مسیر جایگزین (F1، کلیک دوتایی، کلاینت قدیمی) را میگیرد.
        save = MODAL.split('private async Task Save()', 1)[1]
        self.assertLess(save.index('if (!validated'), save.index('SaveShopQuickAsync'))
        self.assertIn('ShopIdentifierRules.ValidateParty(party) is not null', save)
        self.assertIn('return;', save.split('if (!validated', 1)[1].split('var saved =', 1)[0])
        # سرور: هیچکدام از سه مسیر ذخیره بدون اعتبارسنجی وارد تراکنش نمیشود.
        self.assertEqual(PARTIES_CTRL.count('ShopIdentifierRules.ValidateParty(party)'), 2)
        self.assertIn('ValidateParty(dto)', SERVICE)
        self.assertIn('if (string.IsNullOrWhiteSpace(dto.Name))', SERVICE)

    def test_supplier_form_has_the_same_fields_and_rules(self):
        # فیلدهای شناسه/کد پستی دیگر فقط مخصوص مشتری نیستند.
        self.assertIn('Type == PartyType.Supplier ? "تأمین', MODAL)
        self.assertIn('@bind="party.TaxId"', MODAL)
        self.assertIn('@bind="party.PostalCode"', MODAL)
        self.assertNotIn('@if (Type == PartyType.Customer)\n', MODAL)
        # ValidateParty هیچ شرطی روی نوع طرف حساب نمیگذارد.
        self.assertNotIn('party.Type', RULES.split('public static string? ValidateParty', 1)[1])


class Bug3SharedIdentifierRules(unittest.TestCase):
    """کد پستی ۱۰ رقم و شماره تلفن/موبایل ۱۱ رقم در تمام فرمها."""

    def test_rules_are_shared_and_normalize_persian_digits(self):
        self.assertIn('NormalizePhone(string? value)', RULES)
        self.assertIn('Fa.ToEn(value)', RULES)
        self.assertIn('0098', RULES)
        self.assertIn('phone.Length == 11 && IsDigits(phone)', RULES)
        self.assertIn('postal.Length == 10 && IsDigits(postal)', RULES)
        self.assertIn('ValidateContact(string? phone, string? postalCode', RULES)
        self.assertIn('party.Mobile = NormalizePhone(party.Mobile)', RULES)
        self.assertIn('party.Phone = NormalizePhone(party.Phone)', RULES)

    def test_all_shop_forms_call_the_shared_rule(self):
        for path in (
            CLIENT / 'Pages/Catalog/Warehouses.razor',
            CLIENT / 'Pages/Catalog/Referrers.razor',
            CLIENT / 'Pages/Repairs/TechnicianFormModal.razor',
            CLIENT / 'Pages/Moadian/MoadianTabCustomers.razor',
            CLIENT / 'Pages/Moadian/MoadianTabCompany.razor',
            CLIENT / 'Pages/System/SystemCompany.razor',
        ):
            with self.subTest(form=path.name):
                text = read(path)
                self.assertTrue('ShopIdentifierRules.Validate' in text, text[:80])
        # server-side guards for the forms that do not use a Party
        self.assertIn('ShopIdentifierRules.ValidatePhone(dto.Phone)', SERVICE)
        self.assertIn('SaveWarehouseAsync', SERVICE)


class Bug4WholeUnitThresholds(unittest.TestCase):
    """واحد «فقط صحیح» نباید نقطهٔ سفارش/تعداد مطلوب اعشاری بگیرد."""

    def test_client_blocks_fractional_thresholds(self):
        self.assertIn('private bool UnitAllowsFraction()', PRODUCTS)
        self.assertIn('ShopQuantityRules.DefaultAllowsFraction(name)', PRODUCTS)
        self.assertIn('!ShopQuantityRules.IsWhole(reorder) || !ShopQuantityRules.IsWhole(maxStock)', PRODUCTS)
        self.assertIn('فقط مقدار صحیح میپذیرد', PRODUCTS)

    def test_server_repeats_the_rule_for_excel_and_legacy_clients(self):
        self.assertIn('allowsFraction == false', SERVICE)
        self.assertIn('!ShopQuantityRules.IsWhole(dto.ReorderPoint)', SERVICE)
        self.assertIn('!ShopQuantityRules.IsWhole(dto.MaxStock)', SERVICE)


class Bug5GroupPriceDirection(unittest.TestCase):
    """انتخاب «کاهش» باید واقعاً کاهش بدهد."""

    def test_select_is_string_backed_and_maps_to_bool(self):
        self.assertIn('@bind="priceDirection"', PRICING)
        self.assertIn('<option value="increase">', PRICING)
        self.assertIn('<option value="decrease">', PRICING)
        self.assertIn('private string priceDirection = "increase";', PRICING)
        self.assertIn('!string.Equals(priceDirection, "decrease"', PRICING)
        self.assertNotIn('@bind="increase"', PRICING)
        self.assertIn('Increase = increase', PRICING)

    def test_runtime_decrease_is_covered(self):
        probe = (Path(__file__).resolve().parent.parent / 'ShopPricing' / 'Program.cs').read_text(encoding='utf-8')
        self.assertIn('Percent = 10m, Increase = false', probe)
        self.assertIn('group decrease lowers the price', probe)
        self.assertIn('request.Increase ? request.Percent : -request.Percent',
                      read(API / 'Services/Catalog/ShopPricingService.cs'))


class Bug6LeafOnlyCategories(unittest.TestCase):
    """گروه دارای زیرمجموعه نباید مستقیماً کالا بگیرد."""

    def test_product_form_disables_parent_groups(self):
        self.assertIn('private bool HasChildren(ProductCategory c)', PRODUCTS)
        self.assertIn('disabled="@(HasChildren(c))"', PRODUCTS)
        self.assertIn('CategoryIsParent(form.Category)', PRODUCTS)
        self.assertIn('ثبت کالا روی گروه والد مجاز نیست', PRODUCTS)

    def test_server_enforces_leaves_for_any_client_or_excel(self):
        self.assertIn('c.ParentId.HasValue && leafIds.Contains(c.ParentId.Value)', SERVICE)
        self.assertIn('گروه «{entity.Category}» دارای زیرگروه است', SERVICE)
        # والدشدن گروهی که خودش کالا دارد هم جلوگیری میشود.
        self.assertIn('p.Category == parent.Name || p.CategoryId == parent.Id', SERVICE)
        self.assertIn('parent.ProductCount > 0', CATEGORIES)


class Bug7And8ExcelImport(unittest.TestCase):
    """ورود اکسل خدمات + ستونهای ارزش افزوده و وضعیت."""

    def test_services_import_is_wired_end_to_end(self):
        self.assertIn('enum ProductImportKind', DTOS)
        self.assertIn('Services = 1', DTOS)
        self.assertIn('OpenImport(ProductImportKind.Services)', PRODUCTS)
        self.assertIn('ورود خدمات از اکسل', PRODUCTS)
        self.assertIn('ImportExcelAsync(ms, importFile.Name, importWarehouseId, importKind)', PRODUCTS)
        self.assertIn('DownloadTemplateAsync(importKind)', PRODUCTS)
        self.assertIn('ProductImportKind kind = ProductImportKind.Goods', PRODUCTS_CTRL)
        self.assertIn('ImportProductsAsync(ms, warehouseId, kind)', PRODUCTS_CTRL)
        self.assertIn('BuildProductTemplate(kind)', PRODUCTS_CTRL)
        self.assertIn('Services-Template.xlsx', PRODUCTS_CTRL)
        self.assertIn('ProductImportKind kind = ProductImportKind.Goods', I_CLIENT)
        self.assertIn('api/products/import?warehouseId={warehouseId}&kind={kind}', CLIENT_SVC)
        self.assertIn('var isService = kind == ProductImportKind.Services', SERVICE)
        self.assertIn('IsService = isService', SERVICE)
        self.assertIn('Unit = isService ? "خدمت"', SERVICE)
        self.assertIn('WarehouseId = isService ? null', SERVICE)

    def test_vat_and_status_columns_exist_in_template_and_reader(self):
        for header in ('"ارزش افزوده"', '"وضعیت کالا"', '"وضعیت خدمت"', '"شناسه مالیاتی"'):
            with self.subTest(header=header):
                self.assertIn(header, SERVICE)
        self.assertIn('ReadOptionalVat', SERVICE)
        self.assertIn('ReadImportActive', SERVICE)
        self.assertIn('InvoiceVatValue = vat', SERVICE)
        self.assertIn('IsActive = active', SERVICE)
        self.assertIn('!OrderLineAmounts.IsValidVatValue(value)', SERVICE)
        # فایلهای قدیمی ۱۰ ستونه همچنان پذیرفته میشوند (پیشفرضهای مکانی).
        self.assertIn('["desc"] = 10', SERVICE)
        self.assertIn('ResolveImportColumns(ws.Row(1))', SERVICE)


class Bug9DatedStock(unittest.TestCase):
    """موجودی فاکتور بر اساس تاریخ سند، نه تجمیع فعلی."""

    def test_api_and_service_expose_as_of_quantities(self):
        self.assertIn('GetStockQuantitiesAsOfAsync(', I_SERVICE)
        self.assertIn('public async Task<Dictionary<(int WarehouseId, int ProductId), decimal>> GetStockQuantitiesAsOfAsync(', SERVICE)
        self.assertIn('t.Date < end', SERVICE)
        self.assertIn('excludeTransactionId', SERVICE)
        self.assertIn('[FromQuery] DateTime? asOf = null', ORDERS_CTRL)
        self.assertIn('GetStockQuantitiesAsOfAsync(asOf.Value, warehouseId)', ORDERS_CTRL)
        self.assertIn('GetStockAsync(warehouseId, null, false,', ORDERS_CTRL)
        self.assertIn('&asOf={asOf.Value:yyyy-MM-dd}', CLIENT_SVC)
        self.assertIn('DateTime? asOf = null', I_CLIENT)

    def test_client_snapshot_is_keyed_by_invoice_date(self):
        self.assertIn('public DateTime? AsOf { get; private set; }', STOCK_SNAPSHOT)
        self.assertIn('AsOf = asOf?.Date;', STOCK_SNAPSHOT)
        self.assertIn('getPage(warehouseId, page, PageSize, AsOf)', STOCK_SNAPSHOT)
        self.assertIn('@bind-Value:after="OnInvoiceDateChanged"', ORDER_FORM)
        self.assertIn('warehouseStock.LoadAsync(warehouseId, date,', ORDER_FORM)
        self.assertIn('date = IsEdit ? EditOrder!.Date : DateTime.Now;', ORDER_FORM)

    def test_create_edit_and_warranty_checks_use_document_date(self):
        self.assertIn('GetStockQuantitiesAsOfAsync(date, cmd.WarehouseId, productIds)', SERVICE)
        self.assertIn('GetStockQuantitiesAsOfAsync(editDate, cmd.WarehouseId, productIds, saleId)', SERVICE)
        self.assertIn('GetStockQuantitiesAsOfAsync(updateAsOf, cmd.WarehouseId, updateIds, txn.Id)', SERVICE)
        self.assertIn('GetStockQuantitiesAsOfAsync(at, warehouseId, ids)', SERVICE)

    def test_csharp_probe_exercises_future_receipt_and_dated_overload(self):
        probe = (Path(__file__).resolve().parent.parent / 'OrderVat' / 'Program.cs').read_text(encoding='utf-8')
        self.assertIn('controller.WarehouseStock(wh.Id, 1, 1, DateTime.Today)', probe)
        self.assertIn('datedStock.LoadAsync(6, past, (id, page, size, asOf)', probe)
        self.assertIn('the snapshot remembers its as-of date', probe)
        self.assertIn('future receipt funded a backdated sale', probe)
        self.assertIn('a backdated sale cannot consume stock that only arrives later', probe)


if __name__ == '__main__':
    unittest.main()
