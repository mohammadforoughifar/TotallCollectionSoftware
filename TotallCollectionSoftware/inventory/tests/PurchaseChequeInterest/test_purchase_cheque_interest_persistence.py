"""Contracts for *persisting* purchase-invoice cheque interest.

User requirement (Persian): «در رابطه با نکته ای که گفتی باید در گزارش خرید بیاد که
فلان مبلغ بابت سود چک بود و فلان قدر مبلغ کالاها بوده. پس لازمه که در دیتابیس ذخیره
بشه که چقدر بابت سود چک ها دادیم.»

So the interest must be stored, and the purchase report must show the goods amount and
the cheque interest as two separate figures.

These are static source contracts plus an arithmetic mirror; they do not compile the
Razor files or run the API (no .NET SDK in this environment).
"""
import re
import unittest
from decimal import Decimal
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'src'
FORM = (ROOT / 'Inventory.Client/Pages/Orders/OrderForm.razor').read_text(encoding='utf-8')
ENTITY = (ROOT / 'Inventory.Api/Entities/Sales/PurchaseChequeInterest.cs').read_text(encoding='utf-8')
SCHEMA = (ROOT / 'Inventory.Api/Data/PurchaseChequeInterestSchemaV1.cs').read_text(encoding='utf-8')
CONTEXT = (ROOT / 'Inventory.Api/Data/AppDbContext.cs').read_text(encoding='utf-8')
INIT = (ROOT / 'Inventory.Api/Data/DbInitializer.cs').read_text(encoding='utf-8')
SERVICE = (ROOT / 'Inventory.Api/Services/Catalog/InventoryService.cs').read_text(encoding='utf-8')
REGISTRATION = (ROOT / 'Inventory.Api/Services/Invoicing/PurchaseChequeInterestRegistration.cs').read_text(encoding='utf-8')
REPORT_SVC = (ROOT / 'Inventory.Api/Services/Reports/OperationsReportsService.cs').read_text(encoding='utf-8')
REPORT_PAGE = (ROOT / 'Inventory.Client/Pages/Operations/OperationsOrderReport.razor').read_text(encoding='utf-8')
DIGEST_DTOS = (ROOT / 'Inventory.Shared/Dtos/OperationDtos.cs').read_text(encoding='utf-8')
REPORT_DTOS = (ROOT / 'Inventory.Shared/Dtos/OperationsReportDtos.cs').read_text(encoding='utf-8')
MATH = (ROOT / 'Inventory.Shared/PurchaseChequeInterestMath.cs').read_text(encoding='utf-8')
MIGRATION = next((ROOT / 'Inventory.Api/Migrations').glob('*_AddPurchaseChequeInterest.cs')).read_text(encoding='utf-8')
SNAPSHOT = (ROOT / 'Inventory.Api/Migrations/AppDbContextModelSnapshot.cs').read_text(encoding='utf-8')


def block(text, start, end):
    return text.split(start, 1)[1].split(end, 1)[0]


class PurchaseInterestIsPersisted(unittest.TestCase):
    def test_entity_has_its_own_table_and_company_scope(self):
        self.assertIn('public class PurchaseChequeInterest : ICompanyOwned', ENTITY)
        self.assertIn('public int TransactionId { get; set; }', ENTITY)
        self.assertIn('public int? CompanyId { get; set; }', ENTITY)
        self.assertIn('public decimal Amount { get; set; }', ENTITY)

    def test_table_is_created_for_both_providers(self):
        self.assertIn('CREATE TABLE [dbo].[PurchaseChequeInterests]', SCHEMA)
        self.assertIn('CREATE TABLE IF NOT EXISTS ""PurchaseChequeInterests""', SCHEMA)
        self.assertIn('IX_PurchaseChequeInterests_TransactionId', SCHEMA)
        # additive only: an existing database must never be rewritten by the upgrade
        self.assertNotIn('DROP TABLE', SCHEMA)
        self.assertNotIn('UPDATE [dbo].[Transactions]', SCHEMA)

    def test_context_registers_the_set_filter_and_precision(self):
        self.assertIn('DbSet<PurchaseChequeInterest> PurchaseChequeInterests', CONTEXT)
        self.assertIn('mb.Entity<PurchaseChequeInterest>().HasIndex(c => c.TransactionId).IsUnique();', CONTEXT)
        self.assertIn('mb.Entity<PurchaseChequeInterest>().HasQueryFilter(x => ActiveCompanyId == null', CONTEXT)
        self.assertIn('mb.Entity<PurchaseChequeInterest>().Property(c => c.Amount).HasPrecision(18, 2);', CONTEXT)

    def test_startup_repairs_sqlite_and_sqlserver(self):
        self.assertEqual(INIT.count('PurchaseChequeInterestSchemaV1.EnsureAsync(db)'), 2)
        self.assertIn('PurchaseChequeInterestSchemaV1.Sqlite', MIGRATION)
        self.assertIn('Inventory.Api.Data.PurchaseChequeInterest', SNAPSHOT)

    def test_registration_recomputes_the_amount_on_the_server(self):
        self.assertIn('public static async Task<PurchaseChequeInterest> RegisterAsync', REGISTRATION)
        self.assertIn('PurchaseChequeInterestMath.Validate', REGISTRATION)
        # the client never sends the money amount
        self.assertNotIn('input.Amount', REGISTRATION)
        self.assertIn('public sealed record PurchaseChequeInterestRegistrationInput', REGISTRATION)

    def test_edit_syncs_and_can_remove_the_row(self):
        self.assertIn('public static async Task SyncAsync', REGISTRATION)
        self.assertIn('db.PurchaseChequeInterests.Remove(row);', REGISTRATION)
        self.assertIn('PurchaseChequeInterestRegistration.SyncAsync(_db, txn,', SERVICE)

    def test_invoice_amount_still_excludes_the_interest(self):
        # txn.Amount is built from lines only; the interest must never be added to it.
        self.assertIn('paymentTotal = PurchaseChequeInterestMath.PayableTotal(goodsOnlyTotal, purchaseInterest)', SERVICE)
        self.assertNotIn('txn.Amount += purchaseInterest', SERVICE)
        self.assertNotIn('txn.Amount = goodsOnlyTotal + purchaseInterest', SERVICE)

    def test_payments_cannot_exceed_goods_plus_interest(self):
        self.assertEqual(SERVICE.count('PurchaseChequeInterestMath.EnsurePaymentsWithinPayable'), 2)
        self.assertIn('public static void EnsurePaymentsWithinPayable', MATH)


class PurchaseInterestIsWrittenByTheForm(unittest.TestCase):
    def test_form_sends_calculator_inputs_not_the_amount(self):
        self.assertIn('cmd.PurchaseChequeInterest = purchaseChequeInterestTerms;', FORM)
        self.assertIn('private PurchaseChequeInterestInput? purchaseChequeInterestTerms;', FORM)
        self.assertNotIn('cmd.PurchaseChequeInterest = new PurchaseChequeInterestInput { CalculatorBaseAmount = purchaseChequeInterest', FORM)

    def test_saved_interest_is_reloaded_when_editing(self):
        self.assertIn('o.PurchaseChequeInterestAmount', FORM)
        self.assertIn('o.PurchaseChequeInterestSnapshot', FORM)

    def test_clearing_the_interest_clears_the_terms_too(self):
        clear = block(FORM, 'private void ClearPurchaseChequeInterest()', 'InvoiceInfo("سود چک حذف شد')
        self.assertIn('purchaseChequeInterestTerms = null;', clear)

    def test_stale_base_is_rejected_before_save(self):
        self.assertIn('مبلغ کالا از مبنای محاسبهٔ سود چک کمتر شده است', FORM)
        self.assertIn('CalculatorBaseAmount > goodsTotal', block(FORM, 'private async Task SaveInternal', 'private void '))

    def test_wording_no_longer_claims_the_interest_is_display_only(self):
        # the note must no longer claim the figure is only displayed
        note = block(FORM, 'data-purchase-cheque-interest-only-note>', '</div></div>')
        self.assertIn('ذخیره می‌شود', note)
        self.assertIn('دیتابیس ذخیره', FORM)


class StoredInterestIsNotSilentlyLost(unittest.TestCase):
    """Editing an invoice must never erase or corrupt the stored amount by accident."""

    def test_sync_keeps_the_row_unless_removal_is_explicit(self):
        self.assertIn('bool clear = false', REGISTRATION)
        self.assertIn('if (clear && row is not null) db.PurchaseChequeInterests.Remove(row);', REGISTRATION)
        # the old unconditional removal is gone
        self.assertNotIn('if (row is not null) db.PurchaseChequeInterests.Remove(row);\n            return;\n        }\n        var goodsTotal', REGISTRATION)

    def test_command_has_an_explicit_clear_flag(self):
        self.assertIn('public bool ClearPurchaseChequeInterest { get; set; }', DIGEST_DTOS)

    def test_edit_falls_back_to_the_stored_amount(self):
        self.assertIn('cmd.ClearPurchaseChequeInterest ? 0m : storedInterestRow?.Amount ?? 0m', SERVICE)
        self.assertIn('ثبت و حذف سود چک خرید هم‌زمان ممکن نیست', SERVICE)

    def test_form_announces_removal_only_when_a_value_was_stored(self):
        save = block(FORM, 'if (Type == TransactionType.Purchase)\n            {\n                var storedInterest', 'cmd.PurchaseChequeInterest = purchaseChequeInterestTerms;')
        self.assertIn('cmd.ClearPurchaseChequeInterest = true;', save)
        self.assertIn('EditOrder?.PurchaseChequeInterestAmount', save)

    def test_stored_base_must_still_fit_the_new_goods_amount(self):
        self.assertIn('مبلغ کالا از مبنای محاسبهٔ سود چک کمتر است؛ سود چک را دوباره محاسبه کنید.', SERVICE)

    def test_no_unused_helper_left_in_the_form(self):
        self.assertNotIn('HasPurchaseChequeInterest =>', FORM)


class PurchaseReportShowsBothFigures(unittest.TestCase):
    def test_report_dto_carries_the_two_amounts(self):
        self.assertIn('public decimal PurchaseChequeInterestAmount { get; set; }', REPORT_DTOS)
        self.assertIn('public decimal GoodsAmount { get; set; }', REPORT_DTOS)

    def test_service_sums_the_stored_rows(self):
        self.assertIn('_db.PurchaseChequeInterests.AsNoTracking()', REPORT_SVC)
        self.assertIn('PurchaseChequeInterestAmount = purchaseInterestTotal', REPORT_SVC)
        self.assertIn('type == TransactionType.Purchase', REPORT_SVC)

    def test_page_has_explicit_goods_and_interest_columns(self):
        self.assertIn('مبلغ کالا و ارزش افزوده (ریال)', REPORT_PAGE)
        self.assertIn('سود چک (ریال)', REPORT_PAGE)
        self.assertIn('جمع تعهد خرید (ریال)', REPORT_PAGE)
        self.assertIn('data-purchase-cheque-interest-kpi', REPORT_PAGE)
        self.assertIn('summary.PurchaseChequeInterestAmount', REPORT_PAGE)

    def test_order_dto_exposes_the_stored_amount_for_editing(self):
        self.assertIn('public decimal PurchaseChequeInterestAmount { get; set; }', DIGEST_DTOS)
        self.assertIn('public PurchaseChequeInterestInput? PurchaseChequeInterestSnapshot { get; set; }', DIGEST_DTOS)
        self.assertIn('PurchaseChequeInterestAmount = purchaseInterestRow?.Amount ?? 0m', SERVICE)

    def test_party_statement_includes_purchase_interest_as_credit(self):
        self.assertIn('LoadPurchaseChequeInterestAsync', SERVICE)
        self.assertEqual(SERVICE.count('t.Amount + purchaseInterestByTxn.GetValueOrDefault(t.Id)'), 2)


class MissingDataIsZero(unittest.TestCase):
    def test_old_purchases_without_a_row_report_zero(self):
        self.assertIn('PurchaseChequeInterestAmount = purchaseInterestRow?.Amount ?? 0m', SERVICE)
        self.assertIn('purchaseInterestRow is null', SERVICE)


# ---------------------------------------------------------------- arithmetic mirror
class PayableArithmetic(unittest.TestCase):
    """Mirror of PurchaseChequeInterestMath / SaleChequeInitialCalculation."""

    @staticmethod
    def validate(base, rate, months, goods):
        if base > goods:
            raise ValueError('base exceeds goods')
        interest = round(Decimal(base) * Decimal(rate) / Decimal(100) * Decimal(months))
        if interest <= 0:
            raise ValueError('zero interest')
        return interest

    def test_interest_is_rounded_to_whole_rials_like_sales(self):
        self.assertEqual(self.validate(10_000_000, 3, 3, 10_000_000), Decimal(900_000))
        self.assertEqual(self.validate(2_550_000, 2, 3, 2_550_000), Decimal(153_000))

    def test_base_above_goods_is_rejected(self):
        with self.assertRaises(ValueError):
            self.validate(11_000_000, 3, 3, 10_000_000)

    def test_payable_is_goods_plus_interest_and_stays_out_of_the_invoice(self):
        goods, interest = Decimal(10_000_000), Decimal(900_000)
        self.assertEqual(goods + interest, Decimal(10_900_000))
        # invoice amount recorded in Transactions.Amount stays the goods amount
        self.assertEqual(goods, Decimal(10_000_000))


if __name__ == '__main__':
    unittest.main()
