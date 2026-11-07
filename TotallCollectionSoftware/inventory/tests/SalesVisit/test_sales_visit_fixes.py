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
     در هر سطر، تراکم فشردهٔ تبلت/موبایل، نوار بازبینی سهتایی؛ پالت بدون تغییر.
  ۵) رفع پرش/بازنشدن کمبوی کالا و بههمریختگی موبایل: کمبوی درونسطری بدون منوی
     fixed/بدون فوکوس، گرید ۲ ستونهٔ مقاوم به عرض.
  ۶) کوچک شدن پیوستهٔ باکسها و ردیفها با clamp()+vw بهجای دو مقدار ثابت؛ برچسب
     کنار ورودی، فیلدهای کوتاه دوتایی، و نوار اقدام چسبانِ جابهجا شده بالای
     نوار پایین اپ (app-mobile).
  ۷) فاصلهٔ آیکون و متن کفدار (`--vis-icon-gap`): با تراکم بقیه له نمیشود.
  ۸) آلارم «موجودی صفر» در همان سطر اقلام، **فقط** در ثبت سفارش ویزیتوری؛
     تغییرات دو فرم «فاکتور فروش» بنا به درخواست کاربر برگردانده شد.
  ۹) حذف «ثبت سفارش جدید» از سایدبار ماژول فروش و ویزیتوری؛ ورودی فقط همان
     دکمهٔ صفحهٔ «سفارش‌های من» است.
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

    def test_every_line_has_its_own_inline_product_combo_opened_by_f2(self):
        self.assertIn('class="vis-combo"', self.form_markup)
        self.assertIn('@onfocus="@(() => OpenCombo(l))"', self.form_markup)
        self.assertIn('@oninput="e => OnComboInput(l, e.Value?.ToString())"', self.form_markup)
        self.assertIn('data-combo-menu', self.form_markup)
        self.assertIn('@onclick="@(() => PickCombo(l, opt.Id))"', self.form_markup)
        self.assertIn('data-hotkey="f2"', self.form_markup)
        self.assertIn("comboLine = line;", self.form)
        self.assertIn("CloseCombo();", self.form)
        self.assertIn("private List<LookupItem> ComboMatches()", self.form)
        self.assertIn(".Take(5).ToList()", self.form)
        self.assertIn('@onclick="AddEmptyLine"', self.form_markup)

    def test_combo_has_no_fixed_menu_focus_or_js_positioning(self):
        css = self.form[self.form.index('<style>'):]
        c0 = css.index('.vis-form .vis-combo {')
        c1 = css.index('.vis-form .vis-empty {', c0)
        combo_css = css[c0:c1]
        self.assertNotIn("position: fixed", combo_css,
                         "منوی fixed همراه uiRect/Popover.Below روی موبایل میپرید")
        self.assertNotIn("uiRect", self.form)
        self.assertNotIn("FocusAsync", self.form)
        self.assertNotIn("OnAfterRenderAsync", self.form)
        self.assertNotIn("AutoOpen", self.form)
        self.assertNotIn("NoScrollMenu", self.form)
        # منو در جریان عادی صفحه است و اسکرول داخلی نمیسازد
        self.assertIn(".vis-form .vis-combo-menu", css)
        self.assertNotRegex(self.form, r"\.vis-combo-menu[^}]*overflow\s*:")

    def test_visit_form_does_not_enable_invoice_only_combo_options(self):
        ss = read(SEARCH_SELECT)
        for gone in ("AutoOpen", "NoScrollMenu", "FocusAsync", "ss-menu--tight"):
            self.assertNotIn(gone, ss)
        self.assertIn('if (open && ConstrainToModal)', ss)
        self.assertIn('[Parameter] public bool ConstrainToModal { get; set; }', ss)
        self.assertNotIn('ConstrainToModal=', self.form)
        self.assertNotIn('ShowAllOnFocus=', self.form)
        self.assertIn('<div class="ss-menu" style="@MenuStyle"', ss)

    def test_compact_density_and_sticky_bar_clears_bottom_nav(self):
        for name, src in (("VisitForm", self.form), ("VisitDetail", self.detail)):
            with self.subTest(page=name):
                self.assertIn("@@media (max-width: 1080px)", src)
                self.assertIn("@@media (max-width: 720px)", src)
                self.assertIn('"name name" "qty price" "disc total"', src)
                self.assertIn("repeat(2, minmax(0, 1fr)) !important", src)
                self.assertIn("overflow-x: clip", src)
                self.assertIn("position: sticky; bottom: 0", src,
                              "دکمهها همیشه در دسترس باشند تا کاربر تا ته اسکرول نکند")
                self.assertIn(".app-mobile .vis-" + ("form .vis-actionbar" if name == "VisitForm" else "detail .vis-actions"), src,
                              "در حالت موبایل نوار اقدام باید بالاتر از app-bottom-nav بایستد")
                self.assertIn("calc(62px + env(safe-area-inset-bottom, 0px))", src)
                self.assertGreaterEqual(src.count("grid-template-areas"), 2)

    def test_boxes_and_rows_shrink_continuously_with_viewport(self):
        for name, src in (("VisitForm", self.form), ("VisitDetail", self.detail)):
            with self.subTest(page=name):
                self.assertIn("--vis-gap: clamp(", src)
                self.assertIn("--vis-gap-sm: clamp(", src)
                self.assertIn("--vis-pad: clamp(", src)
                self.assertIn("--vis-radius: clamp(", src)
                self.assertIn("--vis-ctl-f: clamp(", src)
                self.assertGreaterEqual(len(re.findall(r"clamp\([^)]*vw", src)), 8,
                                        "مقیاس باید به عرض نما وابسته باشد، نه دو پرش ثابت")
                self.assertIn("var(--vis-ctl-f)", src)
                self.assertIn("var(--vis-ctl-h)" if name == "VisitForm" else "var(--vis-gap-sm)", src)
                # برچسب کنار ورودی/مقدار → هر سلول یک خط بهجای دو خط عمودی
                selector = (".vis-line .vis-cell:not(.c-name):not(.c-del)" if name == "VisitForm"
                            else ".vis-detail-line .vis-cell:not(.c-name)")
                self.assertIn(selector, src)
                i = src.index(selector)
                self.assertIn("display: flex", src[i:i + 220])
        self.assertIn("min-height: var(--vis-ctl-h)", self.form)
        self.assertEqual(self.form.count('class="col-12 col-md-4 vis-field vis-field--sm"'), 3,
                         "روش پرداخت و دو تاریخ → سه فیلد کوتاه")
        self.assertIn(".vis-grid > .vis-field.vis-field--sm", self.form,
                      "قانون دوتایی شدن فیلدهای کوتاه در 720px")
        self.assertIn("flex: 0 0 50% !important", self.form)

    def test_icon_and_text_keep_a_minimum_gap_when_page_shrinks(self):
        for name, src in (("VisitForm", self.form), ("VisitDetail", self.detail)):
            with self.subTest(page=name):
                self.assertIn("--vis-icon-gap: .55rem;", src,
                              "فاصلهٔ پایهٔ آیکون/متن باید تعریف شود")
                self.assertIn("--vis-icon-gap: clamp(.42rem, calc(.2vw + .36rem), .6rem);", src,
                              "کف ۶٫۷px: تراکم بقیه نباید آیکون را به متن بچسباند")
                self.assertGreaterEqual(src.count("gap: var(--vis-icon-gap)"), 3)
                self.assertIn("display: inline-flex; align-items: center; justify-content: center;", src,
                              "دکمهها flex شوند تا gap حتی برای متن خام هم اعمال شود")
                self.assertIn("margin-inline-end: var(--vis-icon-gap)", src)
                self.assertIn("card-header.vis-card-header {\n    display: flex;\n    align-items: center;\n    gap: var(--vis-icon-gap);", src)
                # نباید سربرگ کارت همچنان از توکن ریز فاصلهٔ سلولها استفاده کند
                self.assertNotIn("card-header.vis-card-header {\n    display: flex;\n    align-items: center;\n    gap: var(--vis-gap-sm);", src)
        self.assertIn("gap: var(--vis-icon-gap); min-width: 0;", self.form,
                      "کمبوی کالا: آیکون جعبه و اینپوت جدا بمانند")
        self.assertIn("gap: var(--vis-icon-gap);\n    padding: .55rem 0;", self.detail,
                      "نقطهٔ گردش سفارش و متنش فاصلهٔ کافی داشته باشند")
        preview = read(ROOT.parent / "tests/SalesVisit/visit-forms-proposal.html")
        self.assertIn("--pv-icon-gap: 7px", preview)
        self.assertIn("gap: var(--pv-icon-gap)", preview)

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


class ZeroStockAlarmTests(unittest.TestCase):
    """آلارم موجودی صفر فقط در ماژول فروش و ویزیتوری؛ فرمهای فاکتور فروش دستنخورده."""

    @classmethod
    def setUpClass(cls):
        cls.app_css = read(ROOT / "Inventory.Client/wwwroot/css/app.css")
        cls.form = read(ROOT / "Inventory.Client/Pages/SalesVisit/VisitForm.razor")
        cls.order = read(ROOT / "Inventory.Client/Pages/Orders/OrderForm.razor")
        cls.invoice = read(ROOT / "Inventory.Client/Pages/Invoicing/FacInvoiceForm.razor")
        cls.preview = read(ROOT.parent / "tests/SalesVisit/visit-forms-proposal.html")
        cls.folder = Path(__file__).resolve().parent

    # ---------- استایل مشترک (فقط آنچه ویزیتوری استفاده میکند) ----------
    def test_shared_css_kept_for_sales_visit_only(self):
        for cls in (".zs-alert {", ".zs-alert--warn", ".zs-row", ".zs-row--card",
                    ".zs-qty-input", ".zs-badge", ".zs-opt"):
            self.assertIn(cls, self.app_css)
        self.assertNotIn(".zs-row--table", self.app_css,
                         "قواعد جدول فاکتور فروش حذف شدند")
        self.assertIn("var(--danger)", self.app_css)
        self.assertIn("var(--warning)", self.app_css)
        self.assertIn("@media (max-width: 720px)", self.app_css)
        self.assertIn("rgba(239, 68, 68, .035)", self.app_css)
        self.assertIn("border-inline-start: 3px solid var(--danger)", self.app_css)
        self.assertIn("--danger: #ef4444;", self.app_css)
        self.assertIn("--warning: #f59e0b;", self.app_css)
        i = self.app_css.index(".zs-alert {")
        block = self.app_css[i:self.app_css.index("@media (max-width: 720px)", i)]
        self.assertNotRegex(block, r"(max-height|overflow)\s*:")

    # ---------- ثبت سفارش ویزیتوری: باقی ماند ----------
    def test_visit_form_detects_zero_stock_on_selection(self):
        self.assertIn("private bool IsOutOfStock(VisOrderLineDto l)", self.form)
        self.assertIn("!p.IsService && p.Stock <= 0", self.form)
        self.assertIn("private int ZeroStockCount => model.Lines.Count(IsOutOfStock);", self.form)
        self.assertIn("data-zero-stock", self.form)
        self.assertIn("zs-row zs-row--card", self.form)
        self.assertIn("zs-qty-input", self.form)
        self.assertIn("data-zero-stock-alert", self.form)
        self.assertIn("zs-alert zs-alert--warn", self.form)
        self.assertIn("zeroStockIds = list.Where(p => !p.IsService && p.Stock <= 0)", self.form)
        self.assertIn('<em class="zs-opt">ناموجود</em>', self.form)
        self.assertIn("data-zero-count", self.form)
        self.assertIn("private async Task ProceedWithShortage()", self.form)
        self.assertIn("model.SubmitAfterSave = submit && CanSubmit;", self.form)
        self.assertIn('@(ShortLines.Any(s => s.Available <= 0) ? "کالای ناموجود', self.form)

    # ---------- فاکتور فروش: به حالت قبل برگشت ----------
    def test_invoice_forms_are_back_to_original_state(self):
        for gone in ("zs-", "IsOutOfStock", "stocksLoaded", "data-zero-stock",
                     "ZeroStockCount", " · ناموجود"):
            self.assertNotIn(gone, self.order, f"OrderForm: {gone} نباید بماند")
        self.assertIn("· موجودی انبار:", self.order)
        # The shop invoice uses an independently loaded warehouse snapshot and
        # quantity-aware formatting; the visit-only zero-stock alarm stays absent.
        self.assertIn('InvoiceProductOption.FormatStock(avail)', self.order)
        self.assertIn('warehouseStock.IsLoading', self.order)
        for gone in ("zs-", "IsZeroStock", "data-zero-stock", "ZeroStockCount"):
            self.assertNotIn(gone, self.invoice, f"FacInvoiceForm: {gone} نباید بماند")
        self.assertIn('<span class="badge badge-soft-primary ms-auto">@doc.Lines.Count.Num() قلم</span>', self.invoice)
        self.assertIn("· موجودی: @line.CurrentStock.Num()", self.invoice)

    # ---------- پیشنمایش: فقط نماهای فروش و ویزیتوری ----------
    def test_preview_shows_only_sales_visit_alarm(self):
        for gone in ('data-view="invoice"', 'id="view-invoice"', 'pv-inv-',
                     '/orders/new', 'zs-row--table'):
            self.assertNotIn(gone, self.preview)
        for view in ('id="view-form"', 'id="view-detail"', 'id="view-states"'):
            self.assertIn(view, self.preview)
        self.assertIn("zs-alert", self.preview)
        self.assertIn("zs-badge", self.preview)
        self.assertIn("zs-opt", self.preview)
        self.assertNotIn("pv-stock-alert", self.preview)
        self.assertGreaterEqual(self.preview.count("data-no-scroll"), 3)
        self.assertFalse((self.folder / "visit-forms-zero-stock-preview.html").exists(),
                         "پیشنمایش مخصوص فاکتور فروش حذف شد")
        server = read(self.folder / "serve_proposal.py")
        self.assertNotIn("/zero-stock", server)


class SalesVisitSidebarTests(unittest.TestCase):
    """منوی ماژول فروش و ویزیتوری: بدون ورودی تکراری «ثبت سفارش جدید»."""

    @classmethod
    def setUpClass(cls):
        cls.menu = read(ROOT / "Inventory.Client/Layout/NavMenu.razor")
        cls.layout = read(ROOT / "Inventory.Client/Layout/MainLayout.razor")
        cls.my_visits = read(ROOT / "Inventory.Client/Pages/SalesVisit/MyVisits.razor")

    def test_sidebar_no_longer_links_to_new_order(self):
        self.assertNotIn('href="sales/visits/new"', self.menu)
        self.assertNotIn("<span>ثبت سفارش جدید</span>", self.menu)
        self.assertNotIn("bi bi-plus-square", self.menu)
        # گروه و بقیهٔ آیتمها سر جای خودشان هستند
        self.assertIn("فروش و ویزیتوری", self.menu)
        self.assertIn('href="sales/visits"', self.menu)
        self.assertIn("سفارش‌های من", self.menu)
        self.assertIn('href="sales/approvals"', self.menu)
        self.assertIn('href="sales/requests"', self.menu)
        self.assertIn('href="sales/finance-queue"', self.menu)

    def test_new_order_button_still_lives_in_my_visits(self):
        self.assertIn("data-new-order", self.my_visits)
        self.assertIn('Nav.NavigateTo("/sales/visits/new")', self.my_visits)
        self.assertIn("me?.CanCreate == true", self.my_visits)

    def test_direct_route_guard_is_untouched(self):
        self.assertIn('("sales/visits/new",    "SalesVisit.Create"', self.layout,
                      "محافظ مسیر مستقیم و عنوان تب نباید حذف شوند")
        self.assertIn("RouteGuards", self.layout)


if __name__ == "__main__":
    unittest.main()
