"""قراردادهای منبع برای باگ‌های ۷ تا ۱۱: تاریخ دریافت، مقدار اعشاری، بازهٔ تاریخ و کاردکس."""
from pathlib import Path
import unittest

SRC = Path(__file__).resolve().parents[2] / 'src'
def read(rel): return (SRC / rel).read_text()

VISIT_FORM = 'Inventory.Client/Pages/SalesVisit/VisitForm.razor'
VISIT_SERVICE = 'Inventory.Api/Services/SalesVisit/VisOrderService.cs'


class DeliveryDateContract(unittest.TestCase):
    def test_client_blocks_delivery_before_order_date(self):
        page = read(VISIT_FORM)
        self.assertIn('private bool DeliveryBeforeOrder', page)
        self.assertIn('data-delivery-date-error', page)
        self.assertIn('تاریخ دریافت محصول ({deliveryDate.Value.ToFa()}) نمی‌تواند پیش از تاریخ سفارش', page)

    def test_server_rejects_delivery_and_final_date_before_order_date(self):
        svc = read(VISIT_SERVICE)
        self.assertIn('if (cmd.DeliveryDate.HasValue && cmd.DeliveryDate.Value.Date < orderDate)', svc)
        self.assertIn('if (chosen < order.Date.Date)', svc)


class FractionalQuantityContract(unittest.TestCase):
    def test_product_lite_exposes_unit_quantity_kind(self):
        dto = read('Inventory.Shared/Dtos/SalesVisitDtos.cs')
        self.assertIn('public bool? AllowsFractionalQuantity { get; set; }', dto)
        self.assertIn('public string QuantityKindText', dto)

    def test_products_endpoint_fills_quantity_kind_from_measure_units(self):
        svc = read(VISIT_SERVICE)
        self.assertIn('rules.TryGetValue((p.CompanyId, unit), out var rule)', svc)
        self.assertIn('product.AllowsFractionalQuantity = rule;', svc)
        self.assertIn('ShopQuantityRules.DefaultAllowsFraction(unit)', svc)

    def test_visit_form_rejects_fractional_for_whole_units(self):
        page = read(VISIT_FORM)
        self.assertIn('private bool? UnitAllowsFraction(VisOrderLineDto line)', page)
        self.assertIn('data-fraction-error', page)
        self.assertIn('if (UnitAllowsFraction(line) == false && !ShopQuantityRules.IsWhole(line.Quantity))', page)
        self.assertIn('decimal.Truncate(parsed)', page)

    def test_server_rejects_fractional_for_whole_units(self):
        svc = read(VISIT_SERVICE)
        self.assertIn('allows == false && !ShopQuantityRules.IsWhole(line.Quantity)', svc)


class DateRangeContract(unittest.TestCase):
    def test_shared_rule_exists(self):
        rule = read('Inventory.Shared/DateRangeFilter.cs')
        self.assertIn('from.Value.Date > to.Value.Date', rule)
        self.assertIn('public static void EnsureValid', rule)

    def test_every_filter_page_guards_start_after_end(self):
        pages = (
            'Accounting/AccJournal', 'Accounting/AccLedger', 'Accounting/AccTrialBalance',
            'Catalog/GoodsIssues', 'Invoicing/FacDashboardPage', 'Invoicing/FacSummary',
            'Operations/ChequeIncomeReport', 'Operations/OperationsDashboard',
            'Operations/PartyStatementsReport', 'Reports/Kardex', 'SalesVisit/AllRequests',
            'Settings/AuditLog', 'Stocktaking/StkSessions', 'Treasury/TrsCheques',
            'Treasury/TrsDashboardPage', 'Treasury/TrsFlow', 'Treasury/TrsVouchers',
            'Warehousing/InvKardex',
        )
        for rel in pages:
            page = read(f'Inventory.Client/Pages/{rel}.razor')
            self.assertIn('DateRangeFilter.IsInvalid(', page, rel)
            self.assertIn('Toast.Error(DateRangeFilter.InvalidMessage)', page, rel)

    def test_report_services_reject_invalid_range(self):
        for rel in ('Reports/OperationsReportsService.cs', 'Treasury/TreasuryService.cs',
                    'Accounting/AccountingService.cs', 'Catalog/InventoryService.cs',
                    'Invoicing/InvoicingService.cs', 'Warehousing/WarehousingService.cs',
                    'Finance/ExpenseService.cs', 'Stocktaking/StocktakingService.cs'):
            svc = read('Inventory.Api/Services/' + rel)
            self.assertIn('DateRangeFilter.EnsureValid(from, to);', svc, rel)


class KardexContract(unittest.TestCase):
    def test_quantity_kardex_reloads_on_product_change(self):
        page = read('Inventory.Client/Pages/Reports/Kardex.razor')
        self.assertIn('ValueChanged="OnProductChanged"', page)
        self.assertIn('private async Task OnProductChanged(int id)', page)
        self.assertIn('if (productId == id) return;', page)
        # پاسخ دیررس کالای قبلی نباید جدول کالای جدید را پر کند
        self.assertIn('private int loadVersion;', page)
        self.assertIn('if (version != loadVersion || requestedProduct != productId) return;', page)

    def test_rial_kardex_endpoint_accepts_shop_kardex_section(self):
        ctrl = read('Inventory.Api/Controllers/Warehousing/InvDocsController.cs')
        self.assertIn('ShopSectionPermissionAsync("Kardex", write: false)', ctrl)
        base = read('Inventory.Api/Controllers/RbacControllerBase.cs')
        self.assertIn('protected async Task<bool> ShopSectionPermissionAsync(string sectionKey, bool write)', base)
        # اکشن‌های مدول Shop به‌صورت «{Key}.View» ذخیره می‌شوند (پیشوند «Shop.» نام
        # خود مدول است و در ستون Action تکرار نمی‌شود). تطبیق با «Shop.{Key}.View»
        # هرگز برقرار نمی‌شد و کاردکس ریالی برای نقش تیک‌خورده ۴۰۳ می‌گرفت.
        self.assertIn('granted.Contains($"{section.Key}.View", StringComparer.OrdinalIgnoreCase)', base)
        self.assertNotIn('granted.Contains($"Shop.{section.Key}.View")', base)


if __name__ == '__main__':
    unittest.main()
