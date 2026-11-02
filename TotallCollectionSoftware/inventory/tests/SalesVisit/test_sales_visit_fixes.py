"""بررسی قرارداد سورس برای دو رفع ایراد ماژول «فروش و ویزیتوری».

بدون نیاز به .NET SDK، مرورگر، API یا پایگاهداده:
    python3 -m unittest discover -s inventory/tests/SalesVisit -p 'test_*.py' -v

موارد تحت پوشش:
  ۱) فرم «همهٔ درخواستها»: پهنای ستونها ثابت و مستقل از reflow/رندر مجدد تب.
  ۲) «ثبت سفارش» در کارتابل ویزیتور: هیچ مسیری پاسخ null / NullReferenceException
     به کلاینت برنگرداند.
  ۳) طراحی یکدست فرمهای «ثبت سفارش» و «جزئیات سفارش» + حذف دکمهٔ ارسال
     برای سفارش «در انتظار تأیید».
  ۴) اعمال نهایی طرح: حذف سرچ سراسری کالا و هرگونه اسکرول داخلی، کمبوباکس مستقل
     در هر سطر با باز شدن خودکار هنگام F2، تراکم فشردهٔ تبلت/موبایل، نوار بازبینی
     سهتایی و نوار اقدام چسبان؛ پالت app.css بدون تغییر.
"""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / "src"
ALL_REQUESTS = ROOT / "Inventory.Client/Pages/SalesVisit/AllRequests.razor"
VISIT_FORM = ROOT / "Inventory.Client/Pages/SalesVisit/VisitForm.razor"
VISIT_DETAIL = ROOT / "Inventory.Client/Pages/SalesVisit/VisitDetail.razor"
SEARCH_SELECT = ROOT / "Inventory.Client/Shared/SearchSelect.razor"
APP_CSS = ROOT / "Inventory.Client/wwwroot/css/app.css"
SERVICE = ROOT / "Inventory.Api/Services/SalesVisit/VisOrderService.cs"
CONTROLLER = ROOT / "Inventory.Api/Controllers/SalesVisit/SalesVisitController.cs"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class AllRequestsColumnsTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.src = read(ALL_REQUESTS)
        cls.markup = cls.src[: cls.src.index("<style>")]

    def test_ten_columns_declared_for_ten_header_cells(self):
        colgroup = re.search(r"<colgroup>(.*?)</colgroup>", self.markup, re.S)
        self.assertIsNotNone(colgroup, "<colgroup> باید وجود داشته باشد")
        cols = re.findall(r"<col\b[^>]*style=\"width:\s*\d+px\"[^>]*/>", colgroup.group(1))
        self.assertEqual(len(cols), 10, "ده ستون جدول باید پهنای صریح داشته باشد")
        header = re.search(r"<thead>(.*?)</thead>", self.markup, re.S).group(1)
        self.assertEqual(len(re.findall(r"<th\b", header)), 10)
        row = re.search(r"<tr\b[^>]*stripe-tr\b.*?</tr>", self.markup, re.S).group(0)
        self.assertEqual(len(re.findall(r"<td\b", row)), 10)
        self.assertIn('colspan="10"', self.markup, "ردیف کشویی باید سراسر جدول را بگیرد")

    def test_layout_is_fixed_and_independent_of_reflow(self):
        css = self.src[self.src.index("<style>"):]
        self.assertRegex(css, r"table-layout:\s*fixed\s*!important")
        self.assertRegex(css, r"min-width:\s*\d+px\s*!important")
        self.assertRegex(css, r"direction:\s*rtl\s*!important")
        self.assertIn(".stripe-dashboard .stripe-table-responsive > .stripe-table", css,
                      "selector باید اختصاصی باشد تا ترتیب استایل تبها روی آن اثر نکند")

    def test_rows_are_keyed_for_blazor_diffing(self):
        self.assertRegex(self.markup, r"<tr\s+@key=\"o\.Id\".*?stripe-tr")
        self.assertIn('@key="DrawerKey(o.Id)"', self.markup,
                      "ردیف کشویی colspan=10 نباید بهجای ردیف داده بازاستفاده شود")


class OrderSaveNullSafetyTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.service = read(SERVICE)
        cls.form = read(VISIT_FORM)
        cls.controller = read(CONTROLLER)

    def test_scoped_query_includes_orders_created_by_the_user(self):
        scoped = self.service[self.service.index("private IQueryable<VisOrder> ScopedQuery"):
                              self.service.index("private async Task<VisOrderDto> OperationResultAsync")]
        self.assertIn("o.CreatedByUserId == myUserId", scoped,
                      "سفارش ثبتشده توسط کاربر باید حتی با ReferrerId متفاوت دیده شود")

    def test_no_operation_returns_a_null_suppressed_dto(self):
        self.assertNotIn("(await GetAsync(actor", self.service,
                         "هیچ عملیات موفقی نباید GetAsync محدوده dependent برگرداند")
        self.assertIn("private async Task<VisOrderDto> OperationResultAsync", self.service)
        self.assertGreaterEqual(self.service.count("OperationResultAsync(actor"), 10)
        start = self.service.index("private async Task<VisOrderDto> OperationResultAsync")
        fallback = self.service[start:start + 2600]
        self.assertIn("IgnoreQueryFilters()", fallback,
                      "فیلتر سراسری شرکت نباید سفارش تازهذخیرهشده را پنهان کند")
        self.assertIn("order.CreatedByUserId != actor.UserId", fallback)
        owned = self.service[self.service.index("private async Task<VisOrder> LoadOwnedAsync"):]
        self.assertIn("_db.VisOrders.IgnoreQueryFilters()", owned[:700],
                      "LoadOwnedAsync هم باید سفارش همان کاربر را بیابد")
        self.assertIn("CompanyId = actor.CompanyId", self.service,
                      "سفارش جدید باید به شرکت مؤثر کاربر منسوب شود")

    def test_save_command_is_normalized_before_use(self):
        start = self.service.index("public async Task<VisOrderDto> SaveAsync")
        head = self.service[start:start + 1200]
        self.assertIn("cmd.Lines ??=", head)
        self.assertIn("RemoveAll(l => l is null)", head)
        self.assertIn("products.TryGetValue", self.service)

    def test_controller_never_returns_ok_null(self):
        start = self.controller.index("public async Task<ActionResult<VisOrderDto>> Save")
        save = self.controller[start:start + 700]
        self.assertIn("if (saved is null)", save)
        self.assertIn("StatusCode(500", save)

    def test_client_form_guards_null_result_and_null_inputs(self):
        self.assertIn("if (me is null)", self.form)
        self.assertIn("if (saved is null)", self.form)
        guard = self.form.index("if (saved is null)")
        number = self.form.index("saved.Number", guard)
        self.assertLess(guard, number, "نالچک باید پیش از دسترسی به saved.Number باشد")
        self.assertIn("model.DeliveryDate = deliveryDate?.Date ?? DateTime.Today;", self.form)
        self.assertIn("if (created is null)", self.form)


class VisitFormsDesignTests(unittest.TestCase):
    """فاصلهگذاری یکدست و دکمهٔ ارسال فقط برای پیشنویس/برگشتی."""

    @classmethod
    def setUpClass(cls):
        cls.form = read(VISIT_FORM)
        cls.detail = read(VISIT_DETAIL)
        cls.form_markup = cls.form[: cls.form.index("<style>")]
        cls.detail_markup = cls.detail[: cls.detail.index("<style>")]

    # ---------- طراحی / فاصلهها ----------
    def test_both_forms_share_one_spacing_scale(self):
        for name, src in (("VisitForm", self.form), ("VisitDetail", self.detail)):
            with self.subTest(page=name):
                self.assertRegex(src, r"--vis-gap:\s*1rem")
                self.assertRegex(src, r"--vis-pad:\s*1\.15rem")
                self.assertRegex(src, r"--vis-gap-sm:\s*\.5rem")
                self.assertIn("--bs-gutter-x: var(--vis-gap)", src)
                self.assertIn("--bs-gutter-y: var(--vis-gap)", src,
                              "فاصلهٔ عمودی و افقی ستونها باید یکسان باشد")

    def test_no_mixed_gutters_left_in_order_form(self):
        # ردیفهای تودرتوی قدیمی با g-2 و mt-1 حذف شدهاند
        self.assertNotIn('<div class="row g-2">', self.form_markup)
        self.assertNotIn('row g-2 mt-1', self.form_markup)
        self.assertNotIn('ms-auto fs-8 text-muted ms-2', self.form_markup,
                         "کلاسهای margin دوتایی باعث فاصلهٔ ناهماهنگ سربرگ بودند")
        self.assertEqual(self.form_markup.count('class="row vis-grid"'), 1)

    # ---------- اعمال نهایی: حذف اسکرول، گرید، کمبو در هر سطر، تراکم ----------
    def test_products_have_no_inner_or_horizontal_scroll(self):
        for name, src, markup in (("VisitForm", self.form, self.form_markup),
                                  ("VisitDetail", self.detail, self.detail_markup)):
            with self.subTest(page=name):
                self.assertNotIn('class="table-responsive"', markup,
                                 "ناحیهٔ کالاها نباید داخل کانتینر اسکرولپذیر باشد")
                self.assertNotRegex(src, r"min-width:\s*(?:780|660)px",
                                    "پهنای حداقل جدول، عامل اسکرول افقی بود")
                self.assertNotRegex(src, r"overflow:\s*(?:auto|scroll)",
                                    "هیچ اسکرول داخلی در کالاها مجاز نیست")
                self.assertNotRegex(src, r"max-height:\s*220px")
                self.assertNotIn("vis-suggest", src, "فهرست پیشنهاد اسکرولپذیر حذف شد")
                self.assertIn("data-no-scroll", markup)

    def test_products_use_responsive_css_grid(self):
        for name, src in (("VisitForm", self.form), ("VisitDetail", self.detail)):
            with self.subTest(page=name):
                self.assertIn("grid-template-areas", src)
                self.assertIn("minmax(0,", src)
                self.assertIn("vis-cell-label", src)
                self.assertIn("data-review-bar", src)
                self.assertIn("grid-template-columns: repeat(3, minmax(0, 1fr))", src,
                              "نوار بازبینی حتی روی موبایل هم یک ردیف ۳تایی است")

    def test_global_product_search_is_removed(self):
        for gone in ("productSearch", "productResults", "SearchProductsAsync",
                     "AddLine(VisProductLite", "_prodDebounce"):
            self.assertNotIn(gone, self.form, f"سرچ سراسری کالا باید کاملاً حذف شود: {gone}")
        self.assertIn("productIndex.TryGetValue(productId", self.form,
                      "اطلاعات کالا فقط از فهرست انبار بارگیریشده میآید")
        self.assertGreaterEqual(self.form.count("await ReloadProductLookupAsync();"), 2,
                                "در حالت ویرایش، فهرست کالاها باید پس از خواندن سفارش "
                                "از انبار واقعی آن دوباره بارگیری شود")

    def test_every_line_has_its_own_product_combo_opened_by_f2(self):
        self.assertIn('Items="productLookup"', self.form_markup)
        self.assertIn('AutoOpen="@(ReferenceEquals(l, autoOpenLine))"', self.form_markup)
        self.assertIn('NoScrollMenu="true"', self.form_markup)
        self.assertIn('MaxVisible="5"', self.form_markup)
        self.assertIn('data-hotkey="f2"', self.form_markup)
        self.assertIn("autoOpenLine = line;", self.form)
        self.assertIn("autoOpenLine = null;", self.form,
                      "پس از ریست فرم، هیچ سطری نباید همچنان AutoOpen بماند")
        self.assertIn('@onclick="AddEmptyLine"', self.form_markup)

    def test_search_select_supports_auto_open_and_tight_menu(self):
        ss = read(SEARCH_SELECT)
        self.assertIn("[Parameter] public bool AutoOpen", ss)
        self.assertIn("[Parameter] public bool NoScrollMenu", ss)
        self.assertIn("OnAfterRenderAsync(bool firstRender)", ss)
        self.assertIn("_input.FocusAsync(true)", ss)
        self.assertIn('ss-menu--tight', ss)

    def test_compact_density_and_sticky_action_bar(self):
        for name, src in (("VisitForm", self.form), ("VisitDetail", self.detail)):
            with self.subTest(page=name):
                self.assertIn("@@media (max-width: 1080px)", src)
                self.assertIn("@@media (max-width: 720px)", src)
                self.assertIn("position: sticky; bottom: 0", src)
                self.assertIn('"qty price disc total"', src,
                              "ردیف کالا روی موبایل/تبلت فقط ۲ ردیف است")
                self.assertGreaterEqual(src.count("grid-template-areas"), 2,
                                       "چیدمان دسکتاپ و چیدمان فشرده هر دو باید تعریف شوند")

    def test_business_rules_survive_the_redesign(self):
        self.assertIn("data-stock-alarm", self.form_markup)
        self.assertIn("MaxDiscountPercent", self.form)
        self.assertIn("model.SubmitAfterSave = submit && CanSubmit;", self.form)
        self.assertIn("if (!CanSubmit) submit = false;", self.form)
        self.assertIn("data-pending-note", self.form_markup)
        self.assertIn("data-edit-btn", self.detail_markup)
        self.assertIn("data-submit-btn", self.detail_markup)
        self.assertIn("VisOrderStatus.Submitted", self.detail,
                      "ویرایش سفارش در انتظار تأیید همچنان مجاز است")

    def test_palette_is_untouched(self):
        css = read(APP_CSS)
        root = css[css.index(":root {"):css.index(":root {") + 700]
        for token, value in (("--primary", "#4f46e5"), ("--primary-50", "#eef2ff"),
                             ("--bg", "#f1f5f9"), ("--border", "#e2e8f0"),
                             ("--success", "#10b981"), ("--danger", "#ef4444")):
            with self.subTest(token=token):
                self.assertIn(f"{token}: {value}", root)

    def test_cards_have_a_unified_header_and_padding(self):
        self.assertIn("vis-card-header", self.form)
        self.assertIn("vis-card-header", self.detail)
        self.assertIn(".vis-form .card-body { padding: var(--vis-pad); }", self.form)
        self.assertIn(".vis-detail .card-body { padding: var(--vis-pad); }", self.detail)

    # ---------- دکمهٔ ارسال ----------
    def test_detail_submit_button_only_for_draft_or_returned(self):
        can = self.detail[self.detail.index("private bool CanSubmit"):]
        can = can[: can.index("private bool CanEdit")]
        self.assertIn("VisOrderStatus.Draft", can)
        self.assertIn("VisOrderStatus.Returned", can)
        self.assertNotIn("VisOrderStatus.Submitted", can,
                         "سفارش در انتظار تأیید نباید قابل ارسال مجدد باشد")
        # دکمه داخل شرط CanSubmit است، نه داخل بلوک CanEdit
        edit_block = self.detail_markup[self.detail_markup.index("@if (CanEdit)"):]
        edit_block = edit_block[: edit_block.index("@if (CanSubmit)")]
        self.assertNotIn("ارسال برای تأیید", edit_block)
        self.assertIn("data-submit-btn", self.detail_markup)

    def test_order_form_hides_submit_when_editing_submitted_order(self):
        self.assertIn("private bool CanSubmit => Id == 0", self.form)
        self.assertIn("originalStatus = o.Status;", self.form)
        self.assertIn("@if (CanSubmit)", self.form_markup)
        self.assertIn("@if (!CanSubmit)", self.form_markup)
        self.assertIn("data-pending-note", self.form_markup)
        self.assertIn("model.SubmitAfterSave = submit && CanSubmit;", self.form)
        self.assertIn("if (!CanSubmit) submit = false;", self.form)

    def test_preview_assets_exist(self):
        folder = Path(__file__).resolve().parent
        self.assertTrue((folder / "visit-forms-preview.html").is_file())
        self.assertTrue((folder / "serve_preview.py").is_file())


if __name__ == "__main__":
    unittest.main()
