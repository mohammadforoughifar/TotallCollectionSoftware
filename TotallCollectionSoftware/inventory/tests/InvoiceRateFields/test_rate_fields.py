"""قرارداد فیلدهای «تخفیف» و «ارزش افزوده» در فرم فاکتور فروش/خرید.

قاعدهٔ کاربر:
  • مقدار پیش‌فرض صفر است ولی نباید به‌صورت «۰» در فیلد بنشیند.
  • کلیک/فوکوس روی فیلد، مقدار را پاک می‌کند تا کاربر مستقیم تایپ کند.
  • اگر کاربر بدون تایپ از فیلد خارج شود، فیلد دوباره خالی (یعنی صفر) می‌شود.
"""

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / "src"
CLIENT = ROOT / "Inventory.Client"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def method_body(source: str, name: str) -> str:
    """بدنهٔ تعریفِ یک متد — نه فراخوانی آن در مارک‌آپ."""
    kinds = ("void", "static string", "static void", "async Task", "Task", "string")
    matches = [source.index(f"private {kind} {name}(")
               for kind in kinds if f"private {kind} {name}(" in source]
    if not matches:
        raise AssertionError(f"{name} تعریف نشده است")
    start = max(matches)
    line_end = source.index("\n", start)
    if source[:line_end].rstrip().endswith(";"):
        return source[start:line_end]  # expression-bodied method
    return source[start:source.index("\n    }\n", start)]


class RateHelperTests(unittest.TestCase):
    """فایل کمکی مشترک: صفر خالی نمایش داده می‌شود و متن خالی صفر خوانده می‌شود."""

    @classmethod
    def setUpClass(cls):
        cls.helper = read(CLIENT / "Shared/InvoiceRateInput.cs")

    def test_zero_renders_as_zero(self):
        self.assertIn('if (value <= 0) return "0";', self.helper)

    def test_focus_clears_zero_and_keeps_numbers(self):
        self.assertIn("public static string Focused(decimal value, bool grouped = false) => value > 0 ? Text(value, grouped) : \"\";",
                      self.helper)

    def test_blur_restores_zero_when_nothing_typed(self):
        self.assertIn('string.IsNullOrWhiteSpace(typed) ? Text(0) : Text(value, grouped)', self.helper)

    def test_text_uses_plain_latin_digits_for_ltr_field(self):
        self.assertIn('value.ToString("0.##", CultureInfo.InvariantCulture)', self.helper)

    def test_grouping_available_for_rial_amounts(self):
        self.assertIn("Fa.FormatTyping(text)", self.helper)

    def test_value_clamps_to_zero(self):
        self.assertIn("Math.Max(0m, Fa.ParseDecimal(text))", self.helper)

    def test_helper_is_shared_with_test_project(self):
        csproj = read(ROOT.parent / "tests/InvoiceRateFields/InvoiceRateFields.csproj")
        self.assertIn("Shared/InvoiceRateInput.cs", csproj)


class ShopOrderFormRateFieldsTests(unittest.TestCase):
    """فرم فاکتور فروش/خرید (OrderForm.razor)."""

    @classmethod
    def setUpClass(cls):
        cls.form = read(CLIENT / "Pages/Orders/OrderForm.razor")

    def test_discount_field_clears_on_focus(self):
        block = self._field_block("data-rate-field=\"discount\"")
        self.assertIn('@onfocus="() => OnLineDiscountFocus(l)"', block)
        self.assertIn('@onblur="() => OnLineDiscountBlur(l)"', block)
        self.assertIn('value="@l.DiscountText"', block)

    def test_vat_field_clears_on_focus(self):
        block = self._field_block("data-rate-field=\"vat\"")
        self.assertIn('@onfocus="() => OnVatFocus(l)"', block)
        self.assertIn('@onblur="() => OnVatBlur(l)"', block)
        self.assertIn('value="@l.VatText"', block)

    def test_focus_handler_empties_zero_only(self):
        body = self._method("OnLineDiscountFocus")
        self.assertIn("if (l.DiscountPercent > 0 || l.DiscountAmount > 0) return;", body)
        self.assertIn("l.DiscountText = InvoiceRateInput.Focused(0);", body)

    def test_vat_focus_handler_empties_zero_only(self):
        body = self._method("OnVatFocus")
        self.assertIn("if (l.VatValue > 0) return;", body)
        self.assertIn("l.VatText = InvoiceRateInput.Focused(0);", body)

    def test_blur_without_typing_returns_to_zero(self):
        body = self._method("OnLineDiscountBlur")
        self.assertIn("string.IsNullOrWhiteSpace(l.DiscountText)", body)
        self.assertIn("l.DiscountPercent = 0;", body)
        self.assertIn("l.DiscountAmount = 0;", body)
        # متن نهایی از همان تابع مشترک می‌آید، پس «0» برمی‌گردد.
        self.assertIn("l.DiscountText = DiscountTextOf(l);", body)
        self.assertIn('InvoiceRateInput.Text(0)', self._method("DiscountTextOf"))

    def test_vat_blur_never_writes_a_bare_zero(self):
        body = self._method("OnVatBlur")
        self.assertIn("l.VatText = VatTextOf(l.VatValue);", body)

    def test_zero_is_seeded_through_the_shared_helper(self):
        self.assertNotIn('l.VatText = "0"', self.form)
        self.assertIn('public string VatText { get; set; } = InvoiceRateInput.Text(0);', self.form)
        self.assertIn('public string DiscountText { get; set; } = InvoiceRateInput.Text(0);', self.form)

    def test_loaded_lines_never_show_zero_vat(self):
        # همهٔ جاهایی که متن ارزش افزوده از مقدار مدل ساخته می‌شود باید از همین تابع بگذرند
        # تا صفر به‌صورت «۰» در فیلد ننشیند (پرکردن سطرها، کپی فاکتور، انتخاب کالا).
        self.assertEqual(self.form.count("VatText = VatTextOf("), 3,
                         "پرکردن سطرها، خروج از فیلد و انتخاب کالا")
        # کپی فاکتور قالب سابق ExactDecimal را نگه می‌دارد.
        self.assertIn('l.VatValue > 0 ? FormatNum(ExactDecimal(l.VatValue)) : InvoiceRateInput.Text(0)',
                      self.form)
        body = self._method("VatTextOf")
        self.assertIn('value > 0 ? FormatNum(ExactDecimal(value)) : InvoiceRateInput.Text(0)', body)

    def test_typing_goes_through_the_shared_cleaner(self):
        body = self._method("OnVatInput")
        self.assertIn("InvoiceRateInput.Typing(raw?.ToString(), grouped: true)", body)

    def _field_block(self, marker: str) -> str:
        start = self.form.index(marker)
        begin = self.form.rindex("<input", 0, start)
        end = self.form.index("/>", start)
        return self.form[begin:end]

    def _method(self, name: str) -> str:
        return method_body(self.form, name)


class FacInvoiceFormRateFieldsTests(unittest.TestCase):
    """فرم فاکتور ماژول FAC (FacInvoiceForm.razor)."""

    @classmethod
    def setUpClass(cls):
        cls.form = read(CLIENT / "Pages/Invoicing/FacInvoiceForm.razor")

    def test_no_plain_number_inputs_left_for_discount_and_vat(self):
        for field in ("discount-percent", "discount-amount", "vat-rate", "invoice-discount"):
            with self.subTest(field=field):
                self.assertIn(f'data-rate-field="{field}"', self.form)
        self.assertNotIn('value="@line.DiscountPercent" @onchange="e => OnDiscPercent(idx, e)"', self.form)
        self.assertNotIn('value="@line.VatRate" @onchange="e => OnVatRate(idx, e)"', self.form)

    def test_each_field_has_focus_and_blur(self):
        fields = {"discount-percent": "RateFieldDiscountPercent",
                  "discount-amount": "RateFieldDiscountAmount",
                  "vat-rate": "RateFieldVat"}
        for marker, constant in fields.items():
            with self.subTest(field=marker):
                block = self._field_block(f'data-rate-field="{marker}"')
                self.assertIn(f'@onfocus="() => RateFocus({constant}, idx', block)
                self.assertIn(f'@onblur="() => RateBlur({constant}, idx', block)
                self.assertIn("@oninput=", block)
                self.assertIn('placeholder="0"', block)
                self.assertIn(f'value="@RateText({constant}, idx', block)
        # نام فیلدها ثابت‌اند تا در مارک‌آپ رشته داخل رشته نوشته نشود (Razor).
        for constant, value in (("RateFieldDiscountPercent", "discount-percent"),
                                ("RateFieldDiscountAmount", "discount-amount"),
                                ("RateFieldVat", "vat-rate"),
                                ("RateFieldInvoiceDiscount", "invoice-discount")):
            with self.subTest(constant=constant):
                self.assertIn(f'private const string {constant} = "{value}";', self.form)

    def test_invoice_discount_field_clears_on_focus(self):
        block = self._field_block('data-rate-field="invoice-discount"')
        self.assertIn('@onfocus="OnInvoiceDiscountFocus"', block)
        self.assertIn('@onblur="OnInvoiceDiscountBlur"', block)
        self.assertIn('value="@InvoiceDiscountText"', block)

    def test_blur_without_typing_restores_zero(self):
        body = self._method("RateBlur")
        self.assertIn("cell.Focused = false;", body)
        self.assertIn("InvoiceRateInput.AfterBlur(cell.Text, value, grouped)", body)

    def test_zero_is_shown_through_the_shared_helper(self):
        # تخفیف کلی و سه فیلد سطری: همه از مسیر مشترک می‌گذرند و صفر «0» نمایش داده می‌شود.
        body = self._method("RateText")
        self.assertIn("InvoiceRateInput.Text(value, grouped)", body)
        self.assertIn("cell.Focused", body)

    def test_typing_strips_foreign_characters(self):
        # پاک‌سازی مشترک در فایل کمکی انجام می‌شود تا دو فرم یک رفتار داشته باشند.
        self.assertEqual(self.form.count("InvoiceRateInput.Typing(raw"), 4)
        helper = read(CLIENT / "Shared/InvoiceRateInput.cs")
        self.assertIn("Fa.ToEn(raw", helper)
        self.assertIn("char.IsAsciiDigit(c)", helper)

    def test_line_removal_shifts_typed_text(self):
        body = self._method("RemoveLine")
        self.assertIn("k.Index >= idx", body)
        self.assertIn("rateCells[(key.Field, key.Index - 1)] = cell;", body)

    def test_reload_resets_typed_text(self):
        load = self._method("Load")
        self.assertIn("rateCells.Clear();", load)

    def _field_block(self, marker: str) -> str:
        start = self.form.index(marker)
        begin = self.form.rindex("<input", 0, start)
        end = self.form.index("/>", start)
        return self.form[begin:end]

    def _method(self, name: str) -> str:
        return method_body(self.form, name)


if __name__ == "__main__":
    unittest.main()
