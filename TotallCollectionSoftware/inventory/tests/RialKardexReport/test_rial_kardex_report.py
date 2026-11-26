"""قرارداد «کاردکس ریالی کار می‌کند و به‌تفکیک کالا نمایش داده می‌شود».

گزارش کاربر: «گزارش کاردکس ریالی کار نمی‌کند؛ انتظار: گزارش به‌درستی بارگذاری و
نمایش داده شود (به تفکیک هر کالا)».

ریشه‌های واقعی که این تست قفل می‌کند:

۱) مجوز — اکشن‌های مدول `Shop` با شکل «{Key}.View» ذخیره می‌شوند، اما بررسی
   `ShopSectionPermissionAsync` با «Shop.{Key}.View» مقایسه می‌کرد؛ تکرار پیشوند
   یعنی هیچ‌گاه تطبیق نمی‌خورد و نقش تیک‌خوردهٔ «کاردکس کالا» از API ۴۰۳ می‌گرفت.
۲) منبع داده — endpoint فقط `InvLedger` (دفتر ماژول انبارداری) را می‌خواند. خرید و
   فروش سامانهٔ فروشگاهی در `Transactions` ثبت می‌شود و آن دفتر را پر نمی‌کند؛ نتیجه
   گزارش با کارت‌های صفر و «گردشی ثبت نشده» باز می‌شد. حالا هر دو منبع ادغام می‌شوند.
۳) ظاهر — کلاس‌های `fin-card` / `fin-card-body` / `fin-card-header` / `fw-800` /
   `p-3.5` در صفحه استفاده می‌شدند ولی در CSS تعریف نشده بودند.

اجرا:
    python3 -m unittest discover -s inventory/tests/RialKardexReport -p 'test_*.py'
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2] / "src"
PAGE = (ROOT / "Inventory.Client/Pages/Warehousing/InvKardex.razor").read_text(encoding="utf-8")
SERVICE = (ROOT / "Inventory.Api/Services/Warehousing/WarehousingService.cs").read_text(encoding="utf-8")
RBAC = (ROOT / "Inventory.Api/Controllers/RbacControllerBase.cs").read_text(encoding="utf-8")
CTRL = (ROOT / "Inventory.Api/Controllers/Warehousing/InvDocsController.cs").read_text(encoding="utf-8")
CSS = (ROOT / "Inventory.Client/wwwroot/css/app.css").read_text(encoding="utf-8")
INDEX = (ROOT / "Inventory.Client/wwwroot/index.html").read_text(encoding="utf-8")
DTO = (ROOT / "Inventory.Shared/Dtos/WarehousingDtos.cs").read_text(encoding="utf-8")


def kardex_method() -> str:
    """بدنهٔ کامل GetKardexAsync سرویس انبارداری."""
    start = SERVICE.index("public async Task<InvKardexResult> GetKardexAsync(")
    end = SERVICE.index("public async Task<PagedResult<InvStockRow>> GetStockAsync(", start)
    return SERVICE[start:end]


class ShopSectionPermission(unittest.TestCase):
    """مجوز «بخش» باید با شکل واقعی اکشن‌ها در جدول Permissions تطبیق داده شود."""

    def test_action_is_matched_without_the_duplicated_module_prefix(self):
        self.assertIn('granted.Contains($"{section.Key}.View", StringComparer.OrdinalIgnoreCase)', RBAC)
        self.assertNotIn('granted.Contains($"Shop.{section.Key}.View")', RBAC)

    def test_permission_actions_really_are_key_dot_action(self):
        sections = (ROOT / "Inventory.Shared/ShopSections.cs").read_text(encoding="utf-8")
        self.assertIn('Writable ? new[] { $"{Key}.View", $"{Key}.Manage" } : new[] { $"{Key}.View" }',
                      sections)

    def test_rial_endpoint_still_accepts_the_shop_kardex_section(self):
        self.assertIn('ShopSectionPermissionAsync("Kardex", write: false)', CTRL)
        self.assertIn('HasAsync("ReportPages", "Kardex")', CTRL)


class BothStockSourcesAreRead(unittest.TestCase):
    """کاردکس ریالی باید گردش فروشگاهی و دفتر انبارداری را با هم ببیند."""

    def test_reads_the_warehouse_ledger(self):
        self.assertIn("_db.InvLedger", kardex_method())

    def test_reads_shop_transactions(self):
        body = kardex_method()
        self.assertIn("_db.Transactions", body)
        self.assertIn("TransactionType.Purchase", body)
        self.assertIn("TransactionType.Sale", body)
        self.assertIn("TransactionType.SaleReturn", body)
        self.assertIn("TransactionType.WarrantyOut", body)

    def test_initial_and_adjustment_reset_the_quantity_instead_of_adding(self):
        body = kardex_method()
        # موجودی اول دوره/تعدیل مقدار را «تنظیم» می‌کنند؛ جمع ساده باعث دو برابر شدن می‌شد.
        self.assertIn("ResetQty", body)
        self.assertIn("TransactionType.Initial", SERVICE)
        self.assertIn("TransactionType.Adjustment", SERVICE)

    def test_running_balance_never_reports_value_without_quantity(self):
        self.assertIn("if (runQty == 0) runValue = 0;", kardex_method())

    def test_every_row_carries_its_own_product_balance(self):
        # «به تفکیک هر کالا»: هر ردیف مانده و ارزش ماندهٔ همان کالا را دارد.
        for token in ("BalanceQty = runQty", "BalanceValue = runValue"):
            self.assertIn(token, kardex_method())

    def test_totals_and_closing_are_computed_over_the_whole_range(self):
        body = kardex_method()
        self.assertIn("result.TotalInQty += inQty;", body)
        self.assertIn("result.TotalOutValue += outValue;", body)
        self.assertIn("result.ClosingQty = runQty;", body)
        self.assertIn("result.ClosingValue = runValue;", body)


class RowDocumentLink(unittest.TestCase):
    """ردیف‌های فروشگاهی شناسهٔ سند انبار ندارند؛ لینک مرده ساخته نشود."""

    def test_row_flags_its_source(self):
        self.assertIn("public bool FromWarehouseDoc { get; set; }", DTO)
        self.assertIn("FromWarehouseDoc = e.Source == 1", kardex_method())

    def test_page_only_links_warehouse_documents(self):
        self.assertIn("@if (r.FromWarehouseDoc)", PAGE)
        self.assertIn('href="@($"/inv/docs/edit/{r.DocId}")"', PAGE)


class PageLoadsReliably(unittest.TestCase):
    """بارگذاری صفحه نباید با انتخاب دوباره/پاسخ دیررس خراب شود."""

    def test_stale_responses_are_discarded(self):
        self.assertIn("private int loadVersion;", PAGE)
        self.assertIn("if (version != loadVersion || requestedProduct != productId) return;", PAGE)

    def test_reselecting_the_same_product_does_not_clear_the_table(self):
        self.assertIn("if (productId == id) return;", PAGE)

    def test_product_from_the_query_string_gets_a_name(self):
        # لینک «کاردکس» صفحهٔ موجودی/کالاها شناسه را در آدرس می‌فرستد؛ اگر کالا در
        # سقف ۵۰۰ ردیف lookup نباشد، کادر جستجو باید همچنان نامش را نشان دهد.
        self.assertIn("products.All(p => p.Id != productId)", PAGE)
        self.assertIn("ProductSvc.GetAsync(productId)", PAGE)

    def test_page_guard_matches_the_api_rule(self):
        self.assertIn('Auth.Has("ReportPages.Kardex")', PAGE)
        self.assertIn('ShopAccess.CanSee(Auth, "Kardex")', PAGE)


class RenderedStylesExist(unittest.TestCase):
    """کلاس‌هایی که صفحه استفاده می‌کند باید واقعاً تعریف شده باشند."""

    REQUIRED_IN_CSS = (".fin-card {", ".fin-card-body {", ".fin-card-header {", ".fw-800 {", ".fw-600 {")

    def test_card_and_weight_classes_are_defined(self):
        for token in self.REQUIRED_IN_CSS:
            with self.subTest(token=token):
                self.assertIn(token, CSS)

    def test_half_step_spacing_used_by_the_page_is_defined(self):
        for token in (".p-3\\.5 {", ".g-2\\.5 {", ".py-1\\.5 {", ".px-2\\.5 {", ".mt-1\\.5 {"):
            with self.subTest(token=token):
                self.assertIn(token, CSS)

    def test_stylesheet_cache_key_is_bumped(self):
        self.assertRegex(INDEX, r"css/app\.css\?v=\d+")


if __name__ == "__main__":
    unittest.main()
