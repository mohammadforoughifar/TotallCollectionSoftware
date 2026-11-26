"""Source regression contracts for the eleven reported shop-module bugs.

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
SHOP_PRICING_DTOS = read(SHARED / 'Dtos/ShopPricingDtos.cs')
PRICING_SVC = read(API / 'Services/Catalog/ShopPricingService.cs')
TAB_SERVICE = read(CLIENT / 'Services/TabService.cs')
TAB_REFRESH = read(CLIENT / 'Shared/TabAutoRefresh.razor')
VISITS = {name: read(CLIENT / f'Pages/SalesVisit/{name}.razor')
          for name in ('MyVisits', 'AllRequests', 'Approvals', 'FinanceQueue', 'VisitForm')}


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
        self.assertIn('@bind-Value="party.TaxId"', MODAL)
        self.assertIn('@bind-Value="party.PostalCode"', MODAL)
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
        self.assertIn('ShopIdentifierRules.ValidatePhone(dto.Phone,', SERVICE)
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


class InitialQuantityOnProductCreate(unittest.TestCase):
    """«تعداد اولیه» هم در فرم افزودن کالا و هم در ستون اکسل گرفته می‌شود."""

    WAREOUS = read(API / 'Services/Warehousing/WarehousingService.cs')
    INV_PRODUCTS = read(CLIENT / 'Pages/Warehousing/InvProducts.razor')
    WAREHOUSE_DTOS = read(SHARED / 'Dtos/WarehousingDtos.cs')

    def test_dto_has_initial_quantity_on_both_forms(self):
        self.assertIn('public decimal? InitialQuantity { get; set; }', DTOS)
        self.assertIn('public decimal? InitialQuantity { get; set; }', self.WAREHOUSE_DTOS)

    def test_shop_form_asks_for_initial_quantity(self):
        self.assertIn('تعداد اولیه', PRODUCTS)
        self.assertIn('private string fSale = "", fPurchase = "", fReorder = "", fMax = "", fInitial = "";', PRODUCTS)
        self.assertIn('if (!editing)', PRODUCTS)
        self.assertIn('form.InitialQuantity = initial > 0 ? initial : null;', PRODUCTS)
        # ویرایش کالا موجودی را تغییر نمی‌دهد
        self.assertIn('else form.InitialQuantity = null;', PRODUCTS)

    def test_warehousing_wizard_asks_for_initial_quantity(self):
        self.assertIn('تعداد اولیه', self.INV_PRODUCTS)
        self.assertIn(', fInitial = "";', self.INV_PRODUCTS)
        self.assertIn('form.InitialQuantity = initial > 0 ? initial : null;', self.INV_PRODUCTS)

    def test_server_creates_an_opening_stock_document(self):
        self.assertIn('if (dto.Id == 0) await ApplyInitialQuantityAsync(entity, dto);', SERVICE)
        self.assertIn('TransactionType.Initial => "INIT"', SERVICE)
        self.assertIn('private async Task ApplyInitialQuantityAsync(Db.Product entity, Product dto)', SERVICE)
        self.assertIn('if (dto.InitialQuantity is > 0)', self.WAREOUS)
        self.assertIn('private async Task ApplyOpeningStockAsync(Db.Product product, decimal quantity)', self.WAREOUS)
        # سند قطعی «رسید موجودی اول دوره» و بازسازی کاردکس
        self.assertIn('t.Code == "RC-OPEN"', self.WAREOUS)
        self.assertIn('Status = InvDocStatus.Confirmed,', self.WAREOUS)
        self.assertIn('await RebuildManyAsync(new[] { (product.Id, warehouseId) }.ToList());', self.WAREOUS)

    def test_excel_template_and_reader_carry_the_column(self):
        self.assertIn('("initialQty", new[] { "تعداد اولیه"', SERVICE)
        self.assertIn('["initialQty"] = 0', SERVICE)
        self.assertIn('var initialQty = isService ? 0m : Number(row, "initialQty");', SERVICE)
        self.assertIn('InitialQuantity = initialQty > 0 ? initialQty : null', SERVICE)
        self.assertIn('"تعداد اولیه"\n        };', SERVICE)
        # فایل نمونهٔ مخزن هم همان ستون را دارد (بررسی متنی xlsx)
        import zipfile
        sample = zipfile.ZipFile(ROOT.parent / 'docs' / 'samples' / 'Products-Template.xlsx')
        sheet = sample.read('xl/worksheets/sheet1.xml').decode('utf-8')
        self.assertIn('<t>تعداد اولیه</t>', sheet)
        self.assertIn('r="K1"', sheet)


class Bug9DatedStock(unittest.TestCase):
    """موجودی فاکتور بر اساس تاریخ سند، نه تجمیع فعلی."""

    def test_api_and_service_expose_as_of_quantities(self):
        self.assertIn('GetStockQuantitiesAsOfAsync(', I_SERVICE)
        self.assertIn('public async Task<Dictionary<(int WarehouseId, int ProductId), decimal>> GetStockQuantitiesAsOfAsync(', SERVICE)
        self.assertIn('t.Date < end', SERVICE)
        self.assertIn('excludeTransactionId', SERVICE)
        # تاریخ مبنا به‌صورت رشته خوانده می‌شود تا فرهنگ سیستم آن را به شمسی تفسیر نکند.
        self.assertIn('[FromQuery] string? asOf = null', ORDERS_CTRL)
        self.assertIn('DateQuery.Parse(asOf)', ORDERS_CTRL)
        self.assertIn('GetStockQuantitiesAsOfAsync(asOfDate, warehouseId)', ORDERS_CTRL)
        # نبودِ asOf یعنی امروز، نه موجودی تجمیعی بی‌تاریخ.
        self.assertIn('var asOfDate = parsedAsOf ?? DateTime.Today;', ORDERS_CTRL)
        self.assertNotIn('if (asOf.HasValue)', ORDERS_CTRL)
        self.assertIn('GetStockAsync(warehouseId, null, false,', ORDERS_CTRL)
        self.assertIn('(asOf ?? DateTime.Today).Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)', CLIENT_SVC)
        self.assertIn('&asOf={day}', CLIENT_SVC)
        self.assertIn('DateTime? asOf = null', I_CLIENT)

    def test_client_snapshot_is_keyed_by_invoice_date(self):
        self.assertIn('public DateTime? AsOf { get; private set; }', STOCK_SNAPSHOT)
        self.assertIn('AsOf = (asOf ?? DateTime.Today).Date;', STOCK_SNAPSHOT)
        self.assertIn('getPage(warehouseId, page, PageSize, AsOf)', STOCK_SNAPSHOT)
        self.assertIn('@bind-Value:after="OnInvoiceDateChanged"', ORDER_FORM)
        self.assertIn('warehouseStock.LoadAsync(warehouseId, date ?? DateTime.Today,', ORDER_FORM)
        self.assertIn('date = IsEdit ? EditOrder!.Date : DateTime.Now;', ORDER_FORM)

    def test_date_parser_is_culture_independent(self):
        helper = read(API / 'Helpers/DateQuery.cs')
        # خواندن میلادی مستقل از فرهنگ + تبدیل تاریخ شمسیِ احتمالی
        self.assertIn('CultureInfo.InvariantCulture', helper)
        self.assertIn('"yyyy-MM-dd"', helper)
        self.assertIn('Fa.ToEn(text)', helper)
        self.assertIn('PersianDate.ToGregorian(day.Year, day.Month, day.Day)', helper)
        self.assertIn('MaxPersianYear = 1500', helper)
        # ورودی نامعتبر نباید بی‌سروصدا «موجودی جاری» بسازد
        self.assertIn('return null;', helper)
        self.assertIn('تاریخ مبنای موجودی معتبر نیست', ORDERS_CTRL)

    def test_warehousing_movements_are_dated_too(self):
        # رسید/حوالهٔ انبارداری هم نباید از آینده در فاکتور دیروز دیده شود.
        self.assertIn('_db.InvLedger.AsNoTracking()', SERVICE)
        self.assertIn('UpToDate = g.Sum(e => e.Date < end ? e.QtyIn - e.QtyOut : 0m)', SERVICE)
        self.assertIn('Math.Abs(ledgerQty.Total - s.Quantity) < 0.0005m', SERVICE)

    def test_invoice_menu_shows_the_as_of_day(self):
        self.assertIn('data-stock-asof="@StockAsOfKey"', ORDER_FORM)
        self.assertIn('(تا @StockAsOfText)', ORDER_FORM)
        self.assertIn("private string StockAsOfKey => StockAsOf.ToString(\"yyyy-MM-dd\", System.Globalization.CultureInfo.InvariantCulture);", ORDER_FORM)
        self.assertIn('private string StockAsOfText => PersianDate.ToShortFa(StockAsOf);', ORDER_FORM)
        self.assertIn('StockAsOf => warehouseStock.AsOf ?? date?.Date ?? DateTime.Today', ORDER_FORM)
        css = (CLIENT / 'Pages/Orders/OrderForm.razor.css').read_text(encoding='utf-8')
        self.assertIn('.invoice-stock-asof', css)
        index = (CLIENT / 'wwwroot/index.html').read_text(encoding='utf-8')
        self.assertIn('Inventory.Client.styles.css?v=15', index)

    def test_create_edit_and_warranty_checks_use_document_date(self):
        self.assertIn('GetStockQuantitiesAsOfAsync(date, cmd.WarehouseId, productIds)', SERVICE)
        self.assertIn('GetStockQuantitiesAsOfAsync(editDate, cmd.WarehouseId, productIds, saleId)', SERVICE)
        self.assertIn('GetStockQuantitiesAsOfAsync(updateAsOf, cmd.WarehouseId, updateIds, txn.Id)', SERVICE)
        self.assertIn('GetStockQuantitiesAsOfAsync(at, warehouseId, ids)', SERVICE)

    def test_csharp_probe_exercises_future_receipt_and_dated_overload(self):
        probe = (Path(__file__).resolve().parent.parent / 'OrderVat' / 'Program.cs').read_text(encoding='utf-8')
        # سناریوی گزارش: خرید فردا، سند فروش دیروز.
        self.assertIn('controller.WarehouseStock(wh.Id, 1, 1,\n        yesterday.ToString("yyyy-MM-dd"', probe)
        self.assertIn('a backdated invoice cannot see a receipt dated after its own date', probe)
        self.assertIn('controller.WarehouseStock(wh.Id, 1, 1,\n        DateTime.Today.ToString("yyyy-MM-dd"', probe)
        self.assertIn('invoice stock is dated by default', probe)
        self.assertIn('a persian-formatted asOf is converted', probe)
        self.assertIn('an unparseable asOf is rejected', probe)
        self.assertIn('datedStock.LoadAsync(6, past, (id, page, size, asOf)', probe)
        self.assertIn('the snapshot remembers its as-of date', probe)
        self.assertIn('the legacy no-date overload defaults to today', probe)
        self.assertIn('future receipt funded a backdated sale', probe)
        self.assertIn('a backdated sale cannot consume stock that only arrives later', probe)



class Bug10UnitPriceAutofill(unittest.TestCase):
    """قیمت واحد فاکتور فروش خودکار از تعریف کالا/قیمت فروش پر می‌شود و قابل ویرایش می‌ماند."""

    def test_server_suggests_the_catalog_price_when_the_day_has_no_history(self):
        suggestion = PRICING_SVC.split('public async Task<DatedSalePriceDto> GetPriceAsync', 1)[1] \
                                  .split('public static void ValidatePrice', 1)[0]
        # قیمت همان روز در تاریخچه اولویت دارد ...
        self.assertIn('ResolveAsync(_db, new[] { productId }, date)', suggestion)
        self.assertIn('DatedSalePriceDto.HistorySource', suggestion)
        # ... و اگر ثبت نشده باشد، تعریف کالا پیشنهاد می‌شود تا فیلد خالی نماند.
        self.assertIn('product.SalePrice > 0', suggestion)
        self.assertIn('DatedSalePriceDto.CatalogSource', suggestion)
        self.assertIn('?? throw new InvalidOperationException("کالا یافت نشد.")', suggestion)
        # تاریخچهٔ قیمت خودش هیچ قیمت امروزی را به گذشته نسبت نمی‌دهد.
        history = PRICING_SVC.split('public static async Task<Dictionary<int, Db.ShopSalePrice>> ResolveAsync', 1)[1] \
                               .split('public async Task<DatedSalePriceDto> GetPriceAsync', 1)[0]
        self.assertIn('x.EffectiveDate < end', history)
        self.assertNotIn('product.SalePrice', history)

    def test_dto_marks_the_source_of_the_suggestion(self):
        self.assertIn('public const string HistorySource = "history";', SHOP_PRICING_DTOS)
        self.assertIn('public const string CatalogSource = "catalog";', SHOP_PRICING_DTOS)
        self.assertIn('public string? Source { get; set; }', SHOP_PRICING_DTOS)
        self.assertIn('public bool IsCatalogFallback => Source == CatalogSource;', SHOP_PRICING_DTOS)

    def test_form_fills_the_price_and_keeps_it_editable(self):
        # انتخاب کالا: تاریخچه مبنای اول است، تعریف کالا پشتوانه؛ بدون تاریخ سند هم پر می‌شود.
        self.assertIn('ApplySuggestedUnitPrice(l, p, p.SalePrice, catalogFallback: true);', ORDER_FORM)
        self.assertIn('suggested.Price ?? (p.SalePrice > 0 ? p.SalePrice : (decimal?)null)', ORDER_FORM)
        self.assertIn('catalogFallback: suggested.Price is null || suggested.Source == DatedSalePriceDto.CatalogSource', ORDER_FORM)
        # پر شدن خودکار هرگز قیمت دستی را بازنویسی نمی‌کند و فیلد ویرایش‌پذیر می‌ماند.
        self.assertIn('!string.IsNullOrWhiteSpace(l.PriceText)) return;', ORDER_FORM)
        self.assertIn('l.PriceIsAuto = false;', ORDER_FORM)
        self.assertIn('public bool PriceIsAuto { get; set; }', ORDER_FORM)
        self.assertIn('قیمت این تاریخ ثبت نشده؛ از قیمت فروش تعریف کالا درج شد (قابل ویرایش).', ORDER_FORM)
        price_input = ORDER_FORM.split('aria-label="قیمت واحد به ریال"', 1)[1].split('/>', 1)[0]
        self.assertIn('@oninput=', price_input)
        self.assertNotIn('disabled=', price_input)
        self.assertNotIn('readonly=', price_input)

    def test_invoice_date_change_reprices_only_auto_prices(self):
        self.assertIn('private async Task RefreshAutoUnitPricesAsync()', ORDER_FORM)
        refresh = ORDER_FORM.split('private async Task RefreshAutoUnitPricesAsync()', 1)[1] \
                            .split('private bool? QuantityAllowsFraction', 1)[0]
        self.assertIn('lines.Where(x => x.ProductId > 0 && x.PriceIsAuto)', refresh)
        self.assertIn('!l.PriceIsAuto) continue;', refresh)
        self.assertIn('GetDatedSalePriceAsync(productId, invoiceDate)', refresh)
        changed = ORDER_FORM.split('private async Task OnInvoiceDateChanged()', 1)[1] \
                            .split('private bool IsProductSelectable', 1)[0]
        self.assertEqual(changed.count('await RefreshAutoUnitPricesAsync();'), 2)
        # خرید رفتار قبلی (بهای خرید) را دارد و به قیمت فروش تغییر نمی‌کند.
        purchase = ORDER_FORM.split('if (Type != TransactionType.Sale)', 1)[1].split('// قیمت واحد در فاکتور فروش', 1)[0]
        self.assertIn('p.PurchasePrice > 0', purchase)

    def test_csharp_probe_exercises_the_catalog_suggestion(self):
        probe = (Path(__file__).resolve().parent.parent / 'ShopPricing' / 'Program.cs').read_text(encoding='utf-8')
        self.assertIn('var catalogOnly = new Db.Product { Name = "کالای بدون تاریخچه", Code = "PRICE-C", Unit = "عدد", SalePrice = 1200 };', probe)
        self.assertIn('catalogSuggestion.Price == 1200', probe)
        self.assertIn('catalogSuggestion.Source == DatedSalePriceDto.CatalogSource', probe)
        # کالای بدون قیمت فروش هنوز هیچ قیمتی نمی‌سازد.
        self.assertIn('"no invented early selling price"', probe)

    def test_reported_scenario_now_gets_a_price(self):
        # منطق پیشنهاد: تاریخچهٔ همان روز، سپس تعریف کالا.
        def suggest(history, catalog, day):
            rows = [(d, price) for d, price in history.items() if d <= day]
            if rows:
                return max(rows)[1], 'history'
            return (catalog, 'catalog') if catalog > 0 else (None, None)

        history = {}
        self.assertEqual(suggest(history, 1_200_000, '1405-08-20'), (1_200_000, 'catalog'))
        history['1405-08-25'] = 1_500_000
        # قیمت آینده به فاکتور دیروز نسبت داده نمی‌شود؛ تعریف کالا پیشنهاد می‌شود.
        self.assertEqual(suggest(history, 1_500_000, '1405-08-20'), (1_500_000, 'catalog'))
        self.assertEqual(suggest(history, 1_500_000, '1405-08-25'), (1_500_000, 'history'))
        self.assertEqual(suggest({}, 0, '1405-08-20'), (None, None))



class Bug11PageAutoRefresh(unittest.TestCase):
    """صفحهٔ سفارش‌های فروش/ویزیتوری پس از عملیات و با بازگشت به تب خودکار تازه می‌شود."""

    def test_tabs_announce_activation_and_only_on_change(self):
        self.assertIn('public event Func<TabItem, Task>? ActivatedAsync;', TAB_SERVICE)
        sync = TAB_SERVICE.split('public void Sync(string relativeUrl, RouteData routeData)', 1)[1] \
                         .split('public void SetNoActive()', 1)[0]
        self.assertIn('var wasActiveKey = _active?.Key;', sync)
        self.assertIn('if (!string.Equals(wasActiveKey, t.Key, StringComparison.Ordinal)) _ = RaiseActivatedAsync(t);', sync)
        # اعلام فعال‌شدن بعد از به‌روزرسانی وضعیت تب‌ها و بیرون از چرخهٔ رندر اجرا می‌شود.
        self.assertLess(sync.index('Changed?.Invoke();'), sync.index('RaiseActivatedAsync(t)'))
        raise_fn = TAB_SERVICE.split('private async Task RaiseActivatedAsync(TabItem tab)', 1)[1] \
                              .split('/// <summary>', 1)[0]
        self.assertIn('await Task.Yield();', raise_fn)
        self.assertIn('GetInvocationList().Cast<Func<TabItem, Task>>()', raise_fn)
        self.assertIn('catch { /* تازه‌سازیِ یک تب نباید بقیه‌ی برنامه را بشکند. */ }', raise_fn)

    def test_refresh_component_is_bound_to_its_own_tab(self):
        self.assertIn('@implements IDisposable', TAB_REFRESH)
        self.assertIn('[Parameter] public Func<Task>? OnRefresh { get; set; }', TAB_REFRESH)
        self.assertIn('key = Tabs.Active?.Key ?? TabService.KeyOf(Nav.ToBaseRelativePath(Nav.Uri));', TAB_REFRESH)
        self.assertIn('Tabs.ActivatedAsync += OnActivatedAsync;', TAB_REFRESH)
        self.assertIn('public void Dispose() => Tabs.ActivatedAsync -= OnActivatedAsync;', TAB_REFRESH)
        # فقط تبِ همین صفحه و فقط یک‌بار در هر فعال‌شدن.
        handler = TAB_REFRESH.split('private async Task OnActivatedAsync(TabItem tab)', 1)[1]
        self.assertIn('if (!string.Equals(tab.Key, key, StringComparison.Ordinal)) return;', handler)
        self.assertIn('var refresh = OnRefresh;', handler)
        self.assertIn('if (refresh is null || running) return;', handler)
        self.assertIn('await InvokeAsync(refresh);', handler)

    def test_visit_pages_reload_when_their_tab_returns(self):
        expected = {
            'MyVisits': 'OnRefresh="ReloadAsync"',
            'AllRequests': 'OnRefresh="ReloadOnTabAsync"',
            'Approvals': 'OnRefresh="ReloadOnTabAsync"',
            'FinanceQueue': 'OnRefresh="ReloadOnTabAsync"',
        }
        for name, marker in expected.items():
            with self.subTest(page=name):
                page = VISITS[name]
                self.assertIn('<TabAutoRefresh ' + marker + ' />', page)
                self.assertIn('protected override async Task OnInitializedAsync()', page)
        for name in ('AllRequests', 'Approvals', 'FinanceQueue'):
            with self.subTest(method=name):
                body = VISITS[name].split('private async Task ReloadOnTabAsync()', 1)[1]
                self.assertIn('await LoadAsync();', body)
                self.assertIn('await InvokeAsync(StateHasChanged);', body)
        myvisits = VISITS['MyVisits'].split('private async Task ReloadAsync()', 1)[1]
        self.assertIn('await LoadAsync();', myvisits)
        self.assertIn('await InvokeAsync(StateHasChanged);', myvisits)
        # دکمهٔ نوسازی و تغییر فیلتر هم از همان مسیر می‌گذرند.
        self.assertIn('@onclick="ReloadAsync"', VISITS['MyVisits'])
        self.assertIn('@bind:after="ReloadAsync"', VISITS['MyVisits'])

    def test_credit_cheque_page_also_reloads_on_return(self):
        credit = read(CLIENT / 'Pages/Shop/ShopCreditManagement.razor')
        self.assertIn('<TabAutoRefresh OnRefresh="ReloadOnTabAsync" />', credit)
        body = credit.split('private async Task ReloadOnTabAsync()', 1)[1]
        self.assertIn('await LoadDataAsync();', body)
        self.assertIn('await InvokeAsync(StateHasChanged);', body)

    def test_half_filled_visit_form_is_never_reloaded(self):
        # فرم ویزیتور نیمه‌پر است؛ تازه‌سازی خودکار نباید ورودی کاربر را پاک کند.
        self.assertNotIn('TabAutoRefresh', VISITS['VisitForm'])
        self.assertNotIn('OnRefresh="ReloadOnTabAsync"', VISITS['VisitForm'])

    def test_orders_page_keeps_its_page_number_on_return(self):
        orders_page = read(CLIENT / 'Pages/Orders/Orders.razor')
        on_params = orders_page.split('protected override async Task OnParametersSetAsync()', 1)[1] \
                               .split('private async Task LoadLookups()', 1)[0]
        self.assertIn('var typeKey = txnType.ToString();', on_params)
        self.assertIn('if (!string.Equals(loadedTypeKey, typeKey, StringComparison.Ordinal))', on_params)
        self.assertIn('page = 1;', on_params)
        # صفحه فقط در شاخهٔ تغییر نوع سند صفر می‌شود، نه در هر رندر مجدد.
        self.assertLess(on_params.index('loadedTypeKey'), on_params.index('page = 1;'))
        self.assertLess(on_params.index('page = 1;'), on_params.index('await Load();'))

    def test_only_the_returning_tab_refreshes(self):
        # شبیه‌سازی رفتار جزء: ۱۰۰٪ تطبیق کلید تب و اجرای یک‌باره.
        calls = []

        def activated(tab_key, page_key, running=False):
            if running or tab_key != page_key:
                return False
            calls.append(page_key)
            return True

        self.assertTrue(activated('sales/visits', 'sales/visits'))
        self.assertFalse(activated('sales/approvals', 'sales/visits'))
        self.assertFalse(activated('sales/visits', 'sales/visits', running=True))
        self.assertEqual(calls, ['sales/visits'])


if __name__ == '__main__':
    unittest.main()
