"""قرارداد سورس «جستجوی مشتری بر اساس شماره تلفن» در سامانهٔ فروشگاهی.

بدون .NET SDK اجرا میشود:
    python3 -m unittest discover -s inventory/tests/SalesVisit -p 'test_*.py' -q

پوشش: لایهٔ همسانسازی شماره، فیلد پنهان SearchText در LookupItem،
همهٔ کمبوهای مشتری، جستجوهای لیستی سمت سرور/کلاینت، و عدم بازگشت
تغییرات حذفشدهٔ «موجودی صفر» به فرم فاکتور فروش.
"""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / "src"
CLIENT = ROOT / "Inventory.Client"
API = ROOT / "Inventory.Api"
SHARED = ROOT / "Inventory.Shared"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class PhoneNormalizationTests(unittest.TestCase):
    """لایهٔ مشترک PhoneSearch — بدون وابستگی به EF یا Blazor."""

    @classmethod
    def setUpClass(cls):
        cls.src = read(SHARED / "PhoneSearch.cs")
        cls.dto = read(SHARED / "Dtos/CatalogDtos.cs")

    def test_helper_exists_in_shared_namespace(self):
        self.assertIn("namespace Inventory.Shared;", self.src)
        self.assertIn("public static class PhoneSearch", self.src)

    def test_digits_handles_persian_and_arabic_digits(self):
        self.assertIn("'\\u06F0'", self.src)   # ۰
        self.assertIn("'\\u06F9'", self.src)   # ۹
        self.assertIn("'\\u0660'", self.src)   # ٠
        self.assertIn("'\\u0669'", self.src)   # ٩
        self.assertIn("new System.Text.StringBuilder", self.src)

    def test_country_code_and_leading_zero_are_normalized(self):
        self.assertIn('StartsWith("0098"', self.src)
        self.assertIn('StartsWith("98"', self.src)
        self.assertIn("TrimStart('0')", self.src)

    def test_matches_is_conservative_and_sql_keys_never_empty(self):
        self.assertIn("public static bool Matches", self.src)
        self.assertIn("key.Length >= 3", self.src, "اعداد خیلی کوتاه نباید تطبیق تلفنی بسازند")
        self.assertIn("StringComparison.OrdinalIgnoreCase", self.src)
        self.assertIn("public static (string Digits, string NoZero, string WithZero) SqlKeys", self.src)
        # در نبود رقم، مقادیر جایگزین همان متن خاماند -> Contains("") پیش نمیآید
        for expr in ["d.Length >= 3 ? d : s", "k.Length >= 3 ? k : s",
                     'k.Length >= 3 ? "0" + k : s']:
            self.assertIn(expr, self.src)

    def test_lookup_item_has_hidden_search_text(self):
        self.assertIn("public string? SearchText { get; set; }", self.dto)
        self.assertIn("public string? InfoText { get; set; }", self.dto)
        self.assertIn("public string Name { get; set; } = \"\";", self.dto)


class PhoneNormalizationReferenceTests(unittest.TestCase):
    """مرجع رفتاری الگوریتم: پیادهسازی معادل پایتون از ``PhoneSearch.cs``.

    این کلاس *C# اجرا نمیکند* (در این محیط SDK موجود نیست)؛ قرارداد رفتاری
    همسانسازی را مستند و قابلآزمون میکند و سورس C# همان عملیاتها را دارد.
    """

    @staticmethod
    def digits(value):
        out = []
        for ch in value or '':
            c = ord(ch)
            if 48 <= c <= 57: out.append(chr(c))
            elif 0x06F0 <= c <= 0x06F9: out.append(chr(48 + c - 0x06F0))
            elif 0x0660 <= c <= 0x0669: out.append(chr(48 + c - 0x0660))
        d = ''.join(out)
        if d.startswith('0098'): return '0' + d[4:]
        if d.startswith('98') and len(d) >= 12: return '0' + d[2:]
        return d

    @classmethod
    def key(cls, value):
        return cls.digits(value).lstrip('0')

    @classmethod
    def matches(cls, query, fields):
        q = (query or '').strip()
        if not q: return True
        k = cls.key(q)
        for f in fields:
            if not f or not f.strip(): continue
            if q.casefold() in f.casefold(): return True
            if len(k) >= 3 and k in cls.key(f): return True
        return False

    def test_reference_cases(self):
        cases = [
            ('۰۹۱۲', '0912-345-6789', True),          # ارقام فارسی + جداکننده
            ('9123456789', '0912-345-6789', True),   # بدون صفر آغازین
            ('+98 912 111 2233', '+98 912 111 2233', True),
            ('9121112233', '+98 912 111 2233', True),  # پیششمارهٔ کشور
            ('021-55', '021-55667788', True),         # تلفن ثابت
            ('۰۲۱۶۶', '۰۲۱۶۶۴۴۲۲۱۱', True),           # ارقام فارسی ذخیرهشده
            ('آریا', 'شرکت آریا تجارت', True),        # جستجوی معمولی نام
            ('۱۲', '0912-345-6789', False),           # کمتر از ۳ رقم -> ممنوع
            ('۱۲۳', '0912-345-6789', True),
            ('555', '0912-345-6789', False),
        ]
        for q, field, want in cases:
            with self.subTest(q=q):
                self.assertEqual(self.matches(q, [field]), want)

    def test_csharp_source_uses_the_same_operations(self):
        src = read(SHARED / 'PhoneSearch.cs')
        for expr in ["StartsWith(\"0098\"", "StartsWith(\"98\"", "TrimStart('0')",
                     "key.Length >= 3", "StringComparison.OrdinalIgnoreCase"]:
            self.assertIn(expr, src)


class ComboSearchTests(unittest.TestCase):
    """همهٔ انتخابگرهای مشتری باید شماره تلفن را در اختیار SearchSelect بگذارند."""

    @classmethod
    def setUpClass(cls):
        cls.ss = read(CLIENT / "Shared/SearchSelect.razor")
        cls.svc = read(CLIENT / "Services/InventoryApiServices.cs")
        cls.order = read(CLIENT / "Pages/Orders/OrderForm.razor")
        cls.wizard = read(CLIENT / "Pages/Shop/OpeningBalanceWizard.razor")

    def test_shared_combo_matches_search_text(self):
        self.assertIn("PhoneSearch.Matches(q, it.SearchText)", self.ss)
        self.assertIn("it.SearchText is null", self.ss,
                      "انتخابگرهای دیگر (کالا/حساب) نباید هزینهٔ اضافی بگیرند")
        self.assertIn("var idx = it.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase);", self.ss,
                      "چینش نامبهنام باید دستنخورده بماند")

    def test_party_service_populates_both_phone_fields(self):
        self.assertIn("return parties.Select(p => ToLookup(p)).ToList();", self.svc)
        helper = self.svc.split("public static LookupItem ToLookup", 1)[1]
        self.assertIn("p.Mobile, p.Phone", helper)
        self.assertIn("SearchText =", helper)
        self.assertIn("InfoText =", helper)
        self.assertIn("Name = displayName ?? p.Name", helper,
                      "عنوان گزینه نباید با شماره تلفن قاطی شود")

    def test_order_form_customer_lookups_keep_phones(self):
        self.assertEqual(self.order.count("PartyService.ToLookup(c,"), 2)
        self.assertNotIn(".Select(c => new LookupItem", self.order,
                         "ساخت دستی LookupItem قدیمی باید حذف شده باشد")
        self.assertNotIn("Id = c.Id,", self.order.split("partyLookup = customers", 2)[1].split(".ToList();", 1)[0],
                         "هیچیک از دو فهرست مشتری نباید فاقد SearchText باشند")
        self.assertIn("معرف: {c.ReferrerName}", self.order,
                      "نمایش معرف در فرم سفارش حفظ شود")

    def test_opening_balance_wizard_does_not_drop_search_text(self):
        block = self.wizard.split("allPartiesLookup = custs.Select", 1)[1].split(".ToList();", 1)[0]
        self.assertEqual(block.count("SearchText ="), 2)
        self.assertEqual(block.count("InfoText ="), 2)
        self.assertIn("customersLookup = custs;", self.wizard)

    def test_every_customer_combo_page_uses_the_shared_lookup_service(self):
        pages = [
            "Pages/Catalog/GoodsIssues.razor",
            "Pages/Invoicing/FacInvoiceForm.razor",
            "Pages/Operations/OperationsOrderReport.razor",
            "Pages/Orders/Orders.razor",
            "Pages/Repairs/Repairs.razor",
            "Pages/Accounting/AccVoucherForm.razor",
            "Pages/Treasury/TrsCheques.razor",
            "Pages/Treasury/TrsVoucherForm.razor",
            "Pages/Warehousing/InvDocForm.razor",
            "Pages/Shop/OpeningBalanceWizard.razor",
        ]
        for rel in pages:
            with self.subTest(page=rel):
                self.assertIn("PartySvc.GetLookupsAsync", read(CLIENT / rel))

    def test_remote_visit_customer_search_still_goes_to_server(self):
        form = read(CLIENT / "Pages/SalesVisit/VisitForm.razor")
        self.assertIn('RemoteSearch="true"', form)
        self.assertIn('SearchChanged="SearchCustomersAsync"', form)
        self.assertIn("CustomerInfo(c)", form)

    def test_invoice_forms_still_have_no_zero_stock_alarm(self):
        # این دو فرم در این تغییر دستکاری شدند؛ تغییرات حذفشده نباید برگردند.
        for rel in ["Pages/Orders/OrderForm.razor", "Pages/Invoicing/FacInvoiceForm.razor"]:
            with self.subTest(page=rel):
                s = read(CLIENT / rel)
                self.assertNotIn("IsOutOfStock", s)
                self.assertNotIn("zs-row", s)
                self.assertNotIn("zs-alert", s)


class ServerSearchTests(unittest.TestCase):
    """جستجوهای سمت سرور و فیلترهای کلاینتِ لیستها."""

    @classmethod
    def setUpClass(cls):
        cls.pred = read(API / "Services/PartyPhoneQuery.cs")
        cls.vis = read(API / "Services/SalesVisit/VisOrderService.cs")
        cls.goods = read(API / "Services/Catalog/GoodsIssueService.cs")
        cls.repairs = read(API / "Services/Repairs/RepairService.cs")
        cls.inv = read(API / "Services/Catalog/InventoryService.cs")
        cls.credit = read(CLIENT / "Pages/Shop/ShopCreditManagement.razor")

    def test_ef_predicate_is_translatable_and_covers_both_phone_columns(self):
        self.assertIn("namespace Inventory.Api.Services;", self.pred)
        self.assertIn("IQueryable<Inventory.Api.Data.Party>", self.pred)
        self.assertIn("PhoneSearch.SqlKeys(s)", self.pred)
        self.assertIn("p.Name.Contains(s)", self.pred)
        for expr in ["p.Mobile.Contains(s)", "p.Phone.Contains(s)"]:
            self.assertIn(expr, self.pred)
        self.assertNotIn(".Replace(", self.pred,
                         "فقط Contains/Like قابلترجمه؛ ترجمهٔ SQLite/SQL Server تضمین شود")
        self.assertIn("using Microsoft.EntityFrameworkCore;", self.pred)
        self.assertIn("EF.Functions.Like(p.Mobile, like)", self.pred)
        self.assertIn("EF.Functions.Like(p.Phone, like)", self.pred)
        self.assertIn("PhoneSearch.Key(s)", self.pred)

    def test_all_document_searches_use_the_same_predicate(self):
        self.assertIn("q = q.MatchingPhone(search.Trim());", self.vis)
        self.assertIn("_db.Parties.MatchingPhone(s).Any(p => p.Id == o.PartyId)", self.vis)
        self.assertIn("_db.Parties.MatchingPhone(s).Any(p => p.Id == g.PartyId)", self.goods)
        self.assertIn("_db.Parties.MatchingPhone(s).Any(p => p.Id == r.PartyId)", self.repairs)
        self.assertIn("partiesQ = partiesQ.MatchingPhone(search.Trim());", self.inv)

    def test_nested_namespace_services_import_the_predicate(self):
        for s in (self.vis, self.goods):
            self.assertIn("using Inventory.Api.Services;", s)

    def test_shop_credit_dtos_and_filters_include_landline(self):
        dtos = read(SHARED / "Dtos/OperationDtos.cs")
        self.assertEqual(dtos.count("public string? PartyPhone { get; set; }"), 2)
        self.assertEqual(self.inv.count("PartyPhone = party?.Phone,"), 2)
        self.assertEqual(self.inv.count("PhoneSearch.Matches(s, item.PartyMobile, item.PartyPhone)"), 2)
        self.assertIn("PhoneSearch.Matches(s, c.PartyMobile, c.PartyPhone)", self.credit)
        self.assertIn("PhoneSearch.Matches(s, i.PartyMobile, i.PartyPhone)", self.credit)

    def test_search_labels_now_advertise_phone(self):
        labels = [
            (CLIENT / "Pages/Catalog/GoodsIssues.razor", "نام/تلفن مشتری"),
            (CLIENT / "Pages/Shop/OpeningBalanceWizard.razor", "تلفن یا موبایل طرف حساب"),
            (CLIENT / "Pages/Shop/ShopCreditManagement.razor", "تلفن"),
            (CLIENT / "Pages/SalesVisit/MyVisits.razor", "تلفن مشتری"),
            (CLIENT / "Pages/SalesVisit/AllRequests.razor", "تلفن مشتری"),
            (CLIENT / "Pages/Operations/PartyStatementsReport.razor", "موبایل یا تلفن"),
            (CLIENT / "Pages/Repairs/Repairs.razor", "تلفن مشتری"),
            (CLIENT / "Pages/Orders/OrderForm.razor", "موبایل یا تلفن"),
        ]
        for path, needle in labels:
            with self.subTest(page=str(path)):
                self.assertIn(needle, read(path))


if __name__ == "__main__":
    unittest.main()
