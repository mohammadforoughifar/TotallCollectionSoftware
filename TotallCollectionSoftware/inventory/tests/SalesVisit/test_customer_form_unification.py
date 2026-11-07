"""همسانسازی فرم «مشتری جدید» با فرم «طرف حسابها» (/parties).

قرارداد: تنها یک کامپوننت فرم تعریف مشتری وجود داشته باشد و هر جا دکمهٔ
«مشتری جدید» هست، همان کامپوننت باز شود؛ فرمهای خلاصهٔ موازی ممنوعاند.

    python3 -m unittest discover -s inventory/tests/SalesVisit -p 'test_*.py' -q
"""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / "src"
CLIENT = ROOT / "Inventory.Client"
PAGES = CLIENT / "Pages"
MODAL = CLIENT / "Shared/PartyFormModal.razor"

# پنج نقطهای که «مشتری/طرف حساب جدید» باز میکنند
CONSUMERS = {
    "parties": PAGES / "Catalog/Parties.razor",          # مرجع: اطلاعات پایه
    "order": PAGES / "Orders/OrderForm.razor",
    "visit": PAGES / "SalesVisit/VisitForm.razor",
    "goods": PAGES / "Catalog/GoodsIssues.razor",
    "repairs": PAGES / "Repairs/Repairs.razor",
}


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class SingleCustomerFormTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.texts = {k: read(v) for k, v in CONSUMERS.items()}
        cls.modal = read(MODAL)

    def test_every_entry_point_uses_the_shared_component(self):
        for key, text in self.texts.items():
            with self.subTest(page=key):
                self.assertIn("<PartyFormModal", text)

    def test_reference_form_is_the_parties_page_and_keeps_all_fields(self):
        ref = self.texts["parties"]
        self.assertIn('Type="type"', ref)
        self.assertIn('AllowReferrerPicker="true"', ref)
        self.assertIn('Model="editSource"', ref)
        for field in ["party.Name", "party.Mobile", "party.Phone", "party.Address",
                      "party.TaxId", "party.PostalCode", "party.Note", "party.IsActive",
                      "referrerIdStr"]:
            with self.subTest(field=field):
                self.assertIn(field, self.modal)

    def test_shared_component_is_the_only_place_that_saves_a_party(self):
        hits = []
        for p in sorted(CLIENT.rglob("*.razor")):
            if "PartySvc.SaveAsync" in read(p):
                hits.append(p.relative_to(CLIENT).as_posix())
        self.assertEqual(hits, ["Shared/PartyFormModal.razor"],
                         "هیچ صفحهای نباید مستقیماً طرف حساب ذخیره کند")

    def test_no_parallel_quick_customer_modal_left(self):
        for key in ("goods", "repairs"):
            text = self.texts[key]
            with self.subTest(page=key):
                self.assertNotIn('Title="مشتری جدید"', text)
                self.assertNotIn("SaveNewParty", text)
                self.assertNotIn("SaveNewCustomer", text)
                self.assertNotIn("newParty.Name", text)
                self.assertNotIn("newCustomer.Name", text)
                self.assertNotIn("savingParty", text)
                self.assertNotIn("@bind=\"newCustomer", text)
                self.assertNotIn("@bind=\"newParty", text)

    def test_two_replaced_forms_match_the_reference_configuration(self):
        for key in ("goods", "repairs"):
            block = self.texts[key].split("<PartyFormModal", 1)[1].split("/>", 1)[0]
            with self.subTest(page=key):
                self.assertIn('Type="PartyType.Customer"', block)
                self.assertIn('AllowReferrerPicker="true"', block)
                self.assertIn("OnSaved=", block)
                self.assertIn("OnCancel=", block)

    def test_created_customer_is_reloaded_and_auto_selected(self):
        goods = self.texts["goods"]
        self.assertIn("OnNewPartySaved(Party created)", goods)
        self.assertIn("await LoadLookups();", goods)
        self.assertIn("OnPartyChanged(created.Id);", goods)
        repairs = self.texts["repairs"]
        self.assertIn("OnNewCustomerSaved(Party created)", repairs)
        self.assertIn("PartySvc.GetLookupsAsync(PartyType.Customer, activeOnly: true)", repairs)
        self.assertIn("form.PartyId = created.Id;", repairs)

    def test_visitor_exception_is_documented_and_preserved(self):
        visit = self.texts["visit"]
        self.assertIn('AllowReferrerPicker="false"', visit,
                      "ویزیتور معرفش را سرور ثابت میکند؛ انتخابگر معرف نباید نمایش داده شود")
        self.assertIn("AllowReferrerPicker=false", self.modal)

    def test_moadian_taxpayer_registry_is_a_separate_entity(self):
        """مودیان «VMCustomer» میسازد (کد ملی/شماره اقتصادی)، نه «Party»؛
        عمداً با فرم طرف حسابها جایگزین نشده است."""
        s = read(PAGES / "Moadian/MoadianTabCustomers.razor")
        self.assertIn("NationalID", s)
        self.assertIn("EconomicNumber", s)
        self.assertNotIn("<PartyFormModal", s)
        self.assertNotIn("PartySvc.SaveAsync", s)

    def test_save_contract_and_zero_stock_revert_are_untouched(self):
        self.assertIn("if (string.IsNullOrWhiteSpace(party.Name))", self.modal)
        self.assertIn("await OnSaved.InvokeAsync(saved);", self.modal)
        self.assertNotIn("zs-row", self.texts["order"])
        self.assertNotIn("IsOutOfStock", self.texts["order"])


class SharedFormPreviewTests(unittest.TestCase):
    """پیشنمایش مستقل: همان فیلدهای فرم مرجع، از هر ۵ نقطه."""

    @classmethod
    def setUpClass(cls):
        cls.demo = read(Path(__file__).parent / "customer-form-unification-preview.html")
        cls.modal = read(MODAL)

    def test_all_five_entry_points_are_listed(self):
        for name in ["طرف حسابها", "فاکتور فروش", "ثبت سفارش ویزیتوری",
                     "حوالهٔ تحویل", "پذیرش تعمیرات"]:
            with self.subTest(entry=name):
                self.assertIn(name, self.demo)

    def test_preview_mirrors_every_field_of_the_reference_form(self):
        for label in ["نام", "موبایل", "تلفن ثابت", "آدرس", "شناسه ملی خریدار",
                      "کد پستی خریدار", "معرف", "یادداشت", "فعال باشد",
                      "انصراف (Esc)", "ذخیره"]:
            with self.subTest(label=label):
                self.assertIn(label, self.demo)
                self.assertIn(label, self.modal)

    def test_preview_is_self_contained(self):
        self.assertNotIn("<link", self.demo)
        self.assertNotIn("src=\"http", self.demo)


if __name__ == "__main__":
    unittest.main()
