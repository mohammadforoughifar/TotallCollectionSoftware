"""قرارداد «یکپارچگی طراحی بخش تعمیرات» (تعمیرات / گارانتی / طرح تعویض).

خواستهٔ کارفرما: فرم پذیرش گارانتی مینیمال و خوب شده؛ بقیهٔ فرم‌های بخش تعمیرات
هم باید همان زبان طراحی را داشته باشند — دکمه‌ها، چیدمان بلوک‌ها و سبک المان‌ها.

این تست فقط ساختار منبع را می‌سنجد (بدون Blazor/مرورگر/دیتابیس):

    python3 -m unittest discover -s inventory/tests/RepairSectionDesign -p 'test_*.py'
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2] / "src/Inventory.Client"
PAGES = ROOT / "Pages/Repairs"
CSS = (ROOT / "wwwroot/css/app.css").read_text(encoding="utf-8")
INDEX = (ROOT / "wwwroot/index.html").read_text(encoding="utf-8")

REPAIRS = (PAGES / "Repairs.razor").read_text(encoding="utf-8")
WARRANTY = (PAGES / "WarrantyTab.razor").read_text(encoding="utf-8")
TRADEIN = (PAGES / "TradeInTab.razor").read_text(encoding="utf-8")


def form_section(src: str) -> str:
    """کل بلوک <Modal …> فرم پذیرش را برمی‌گرداند (بدون مودال‌های دیگر)."""
    start = src.index('<Modal Visible="showForm"')
    return src[start:src.index("</Modal>", start)]


class SharedFormShell(unittest.TestCase):
    """هر سه فرم از یک پوسته/بلوک مشترک استفاده می‌کنند."""

    def test_all_forms_use_the_shared_form_shell(self):
        for name, src in (("Repairs", REPAIRS), ("Warranty", WARRANTY), ("TradeIn", TRADEIN)):
            with self.subTest(form=name):
                self.assertIn('repair-form', form_section(src))

    def test_sections_use_minimal_blocks_not_gradient_cards(self):
        for name, src in (("Repairs", REPAIRS), ("Warranty", WARRANTY)):
            body = form_section(src)
            with self.subTest(form=name):
                self.assertRegex(body, r'<(?:section|div|article) class="repair-block"')
                self.assertIn('class="repair-block-head"', body)
                # کلاس‌های قدیمی (کارت گرادیانی/آیکن بزرگ) در فرم‌ها بمان نمانند
                self.assertNotIn("repair-section-icon", body)
                self.assertNotIn("repair-common-card", body)

    def test_trade_in_form_groups_customer_and_device_in_two_blocks(self):
        body = form_section(TRADEIN)
        self.assertEqual(2, body.count('class="repair-block"'))
        self.assertIn("مشتری و تاریخ", body)
        self.assertIn("دستگاه و ارزش تعیین‌شده", body)

    def test_device_card_uses_shared_index_chip(self):
        for name, src in (("Repairs", REPAIRS), ("Warranty", WARRANTY)):
            with self.subTest(form=name):
                self.assertIn('class="repair-block-index"', form_section(src))

    def test_totals_are_presented_in_one_quiet_bar(self):
        for name, src in (("Repairs", REPAIRS), ("Warranty", WARRANTY)):
            with self.subTest(form=name):
                self.assertIn('class="repair-total"', form_section(src))


class SharedFooterActions(unittest.TestCase):
    """ترتیب و نوع دکمه‌ها در همهٔ فرم‌ها یکسان است."""

    def test_cancel_is_light_and_primary_sits_on_the_left(self):
        for name, src in (("Repairs", REPAIRS), ("Warranty", WARRANTY), ("TradeIn", TRADEIN)):
            body = form_section(src)
            footer = body[body.index("<Footer>"):]
            with self.subTest(form=name):
                self.assertIn('class="btn btn-light"', footer)
                self.assertIn('class="btn btn-primary me-auto"', footer)
                self.assertIn("انصراف (Esc)", footer)
                # دکمهٔ اصلی همیشه آخر (سمت چپ در RTL) می‌آید
                self.assertLess(footer.index("btn btn-light"), footer.index("btn btn-primary me-auto"))

    def test_repair_intake_secondary_action_is_neutral(self):
        body = form_section(REPAIRS)
        self.assertIn('btn btn-outline-secondary" data-hotkey="f4"', body)
        self.assertNotIn('btn btn-outline-primary" data-hotkey="f4"', body)

    def test_invoice_dialog_uses_the_same_primary_action(self):
        start = REPAIRS.index('<Modal Visible="invoicing is not null"')
        body = REPAIRS[start:REPAIRS.index("</Modal>", start)]
        self.assertIn('class="btn btn-primary" data-hotkey="f1" @onclick="DoInvoice"', body)
        self.assertNotIn('class="btn btn-success" data-hotkey="f1"', body)

    def test_buttons_declare_type_button(self):
        """در فرم داخل مودال، دکمهٔ بدون type رفتار پیش‌فرض submit می‌گیرد."""
        for name, src in (("Repairs", REPAIRS), ("Warranty", WARRANTY), ("TradeIn", TRADEIN)):
            body = form_section(src)
            with self.subTest(form=name):
                self.assertNotIn('<button class="btn', body)
                self.assertIn('<button type="button" class="btn', body)


class SharedStyles(unittest.TestCase):
    """کلاس‌های مشترک در CSS سراسری تعریف و نسخهٔ کش آن به‌روز شده‌اند."""

    def test_shared_classes_exist_in_app_css(self):
        for token in (".repair-form", ".repair-block {", ".repair-block-head {",
                      ".repair-block-index {", ".repair-total {"):
            with self.subTest(token=token):
                self.assertIn(token, CSS)

    def test_old_form_styles_are_kept_for_backward_compatibility(self):
        # صفحات/اسنپ‌شات‌های دیگر از کلاس‌های قدیمی استفاده می‌کنند؛ حذف نشوند.
        self.assertIn(".repair-intake-form {", CSS)
        self.assertIn(".repair-device-card,", CSS)

    def test_stylesheet_cache_key_is_bumped(self):
        self.assertRegex(INDEX, r"css/app\.css\?v=\d+")


class ListRowsStayFunctional(unittest.TestCase):
    """یکپارچه‌سازی آیکن‌ها نباید دکمه‌های عملیات فهرست را جابه‌جا/حذف کند."""

    def test_desktop_and_mobile_rows_keep_every_action(self):
        desktop = REPAIRS[REPAIRS.index("@* ---------- نمای جدولی"):REPAIRS.index("@* ---------- نمای کارتی")]
        mobile = REPAIRS[REPAIRS.index("@* ---------- نمای کارتی"):REPAIRS.index("@if (total > 0)")]
        for block in (desktop, mobile):
            for method in ("OpenView(r)", "PrintDeliveryReceipt(r)", "PrintInvoice(r)",
                           "OpenEdit(r)", "OpenInvoice(r)", "Delete(r)", "creditRepair = r"):
                with self.subTest(method=method):
                    self.assertIn(method, block)


class ItemsTableAlignment(unittest.TestCase):
    """جدول «کارها و قطعات مصرفی» باید ستون‌های هم‌عرض و فیلدهای هم‌تراز داشته باشد.

    ایراد کارفرما: فیلدهای ردیف در یک راستا نبودند و راهنماهای زیر هر فیلد
    (واحد/نوع مقدار و سود ردیف) ردیف را به‌هم می‌ریختند.
    """

    TABLE = REPAIRS[REPAIRS.index("repair-items-table"):REPAIRS.index('<div class="repair-total">', REPAIRS.index("repair-items-table"))]

    def test_items_section_uses_the_dedicated_table_class(self):
        self.assertIn('class="repair-block repair-items-block"', REPAIRS)
        self.assertIn('class="table table-sm repair-items-table"', REPAIRS)

    def test_no_legacy_centered_table_layout(self):
        # align-middle + استایل‌های inline قدیمی باعث ناهم‌ترازی ارتفاع فیلدها می‌شدند.
        self.assertNotIn('class="table align-middle"', REPAIRS)
        self.assertNotIn('style="min-width:220px"', REPAIRS)
        self.assertNotIn('style="width:90px"', REPAIRS)

    def test_every_row_cell_wraps_content_in_the_alignment_stack(self):
        body = self.TABLE[self.TABLE.index("<tbody>"):]
        # در سورس ردیف یک `@for` دارد؛ ۶ سلول محتوایی (سلول هفتم فقط دکمهٔ حذف).
        self.assertEqual(body.count('class="ri-cell'), 6)
        self.assertIn('class="ri-cell ri-profit', body)

    def test_column_widths_are_declared_once_for_the_whole_table(self):
        self.assertIn("<colgroup>", self.TABLE)
        self.assertEqual(self.TABLE.count("<col "), 7)
        self.assertIn("table-layout: fixed", CSS)

    def test_hints_do_not_break_the_row(self):
        self.assertIn(".ri-hint {", CSS)
        hint_block = CSS[CSS.index(".ri-hint {"):CSS.index(".ri-hint {") + 400]
        self.assertIn("white-space: nowrap", hint_block)
        self.assertIn("text-overflow: ellipsis", hint_block)

    def test_header_keeps_all_seven_columns(self):
        head = self.TABLE[self.TABLE.index("<thead>") : self.TABLE.index("</thead>")]
        for label in ("شرح کار", "قطعه از انبار", "مقدار", "هزینهٔ داخلی", "قیمت فروش", "سود"):
            with self.subTest(label=label):
                self.assertIn(f"<th>{label}</th>", head)


if __name__ == "__main__":
    unittest.main()
