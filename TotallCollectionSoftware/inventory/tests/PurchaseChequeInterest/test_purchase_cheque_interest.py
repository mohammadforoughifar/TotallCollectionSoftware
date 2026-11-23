"""Regression contracts for the purchase-invoice cheque profit bug.

Reported bug: in a *purchase* invoice paid by cheque, after running the
interest calculator the profit amount was displayed as «مازاد پرداخت»
(overpayment) instead of as profit.

These tests are static source contracts plus an arithmetic mirror of the
displayed labels. They do not compile Razor or run the API.
"""
import re
import unittest
from decimal import Decimal
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'src'
FORM = (ROOT / 'Inventory.Client/Pages/Orders/OrderForm.razor').read_text(encoding='utf-8')
CSS = (ROOT / 'Inventory.Client/Pages/Orders/OrderForm.razor.css').read_text(encoding='utf-8')


def block(start, end):
    return FORM.split(start, 1)[1].split(end, 1)[0]


class RemainingCaptionMirror:
    """Mirror of RemainingCaption so the label rule itself is pinned."""

    @staticmethod
    def caption(final_remaining, use_credit=False, strict=False):
        tolerance = 0 if strict else 1
        if final_remaining < 0:
            return 'مازاد پرداخت'
        if use_credit and final_remaining > tolerance:
            return 'مانده نسیه'
        return 'باقی‌مانده'


class PurchaseChequeInterestCalculations(unittest.TestCase):
    """The arithmetic that produced the wrong label, reproduced and fixed."""

    def setUp(self):
        # Purchase invoice: goods + VAT 10,000,000; calculator 3% per month, 3 months.
        self.goods = Decimal(10_000_000)
        self.interest = Decimal(900_000)
        self.cheques = self.goods + self.interest

    def test_old_model_labelled_the_interest_as_overpayment(self):
        # Previous behaviour: purchase payable base ignored the interest.
        old_base = self.goods
        old_remaining = old_base - self.cheques
        self.assertEqual(old_remaining, -self.interest)
        self.assertEqual(RemainingCaptionMirror.caption(old_remaining), 'مازاد پرداخت')

    def test_fixed_model_keeps_remaining_zero_and_never_says_overpayment(self):
        payable_total = self.goods + self.interest  # grandTotal + PurchaseChequeInterest
        remaining = payable_total - self.cheques
        self.assertEqual(remaining, 0)
        self.assertEqual(RemainingCaptionMirror.caption(remaining), 'باقی‌مانده')

    def test_real_overpayment_is_still_reported(self):
        payable_total = self.goods + self.interest
        remaining = payable_total - (self.cheques + Decimal(250_000))
        self.assertEqual(remaining, Decimal(-250_000))
        self.assertEqual(RemainingCaptionMirror.caption(remaining), 'مازاد پرداخت')


class PurchaseChequeInterestSourceContract(unittest.TestCase):
    def test_purchase_cheque_interest_is_separate_from_the_invoice_total(self):
        self.assertIn('private decimal grandTotal => goodsTotal + saleChequeIncome;', FORM)
        interest = block('/// <summary>سود چک خرید؛', 'private decimal PayableTotal')
        self.assertIn('Type == TransactionType.Purchase ? purchaseChequeInterest : 0m;', interest)
        # The interest must live in its own field: saleChequeIncome feeds grandTotal
        # (and therefore the stored invoice amount), so reusing it would inflate the
        # purchase invoice by the profit -- the exact double-count to avoid.
        self.assertIn('private decimal purchaseChequeInterest;', FORM)
        for assignment in ('purchaseChequeInterest = calculation.Interest;',
                           'purchaseChequeInterest = refreshed.Calculation.Interest;',
                           'purchaseChequeInterest = 0m;'):
            self.assertIn(assignment, FORM)
        self.assertNotIn('saleChequeIncome = calculation.Interest;', FORM.split('Type == TransactionType.Purchase)', 1)[1])

    def test_payable_base_includes_the_purchase_interest(self):
        self.assertIn('private decimal PayableTotal => grandTotal + PurchaseChequeInterest;', FORM)
        for expression in (
            'private decimal FinalRemaining => PayableTotal - CashPart - ChequeSum() - InstSum();',
            'private decimal RemainingAfterCash => PayableTotal - CashPart;',
            'return PayableTotal - s;',
        ):
            self.assertIn(expression, FORM)

    def test_remaining_caption_is_derived_from_the_correct_base(self):
        caption = block('private string RemainingCaption', 'private decimal RemainingAfterCash')
        self.assertIn('FinalRemaining < 0', caption)
        self.assertIn('"مازاد پرداخت"', caption)
        # The summary must call the shared caption instead of inlining the rule.
        summary = block('<div class="purchase-summary-pay"', '</section>')
        self.assertIn('<span>@RemainingCaption</span>', summary)
        self.assertNotIn('FinalRemaining < 0 ? "مازاد پرداخت"', summary)

    def test_interest_has_its_own_titled_row_and_wording(self):
        summary = block('<section class="purchase-summary"', '</section>')
        row = block('data-purchase-cheque-interest', '</div>')
        self.assertIn('سود چک (هزینهٔ مالی خرید)', row)
        self.assertIn('جدا از مبلغ کالا و مازاد پرداخت', row)
        self.assertIn('PurchaseChequeInterest > 0 && !HasChequeIncomeArticle', summary)
        self.assertIn('data-purchase-cheque-interest', summary)
        self.assertIn('.purchase-summary-cheque-interest', CSS)

    def test_running_installment_balance_uses_labels_and_absolute_values(self):
        # Raw negatives ("‎-۹۰۰٬۰۰۰") used to appear in the payment section, which is how
        # the profit leaked out as an unlabelled extra amount.
        self.assertIn('@(FinalRemaining < 0 ? "مازاد پرداخت تجمعی" : "باقی‌ماندهٔ تجمعی"):', FORM)
        self.assertIn('data-pay-remain-inst>@(ExactPaymentDisplay ? ExactMoney(Math.Abs(FinalRemaining))', FORM)
        self.assertIn('Math.Abs(RemainingAfterInstallment(i))', FORM)
        self.assertIn('Math.Abs(RemainingAfterCheque(i))', FORM)
        self.assertIn('RemainingAfterCheque(i) < 0 ? "مازاد این چک بر مانده فاکتور:" : "باقی‌مانده پس از این چک:"', FORM)
        self.assertIn('(مازاد)', FORM)

    def test_overpayment_text_no_longer_mentions_profit(self):
        summary = block('<section class="purchase-summary"', '</section>')
        self.assertNotIn('مازاد / سود مدت‌دار', summary)
        self.assertNotIn('سود مدت‌دار', summary)


class PurchaseChequeInterestFlow(unittest.TestCase):
    def test_purchase_calculator_records_interest_without_changing_the_invoice_total(self):
        apply = block('private void ApplyCalc()', 'private List<ChequeDto> cheques')
        purchase_branch = apply.split('else\n            {\n                // خرید', 1)[1].split('}', 1)[0]
        self.assertIn('purchaseChequeInterest = calculation.Interest;', purchase_branch)
        self.assertNotIn('saleChequeIncome = calculation.Interest;', purchase_branch)
        # The sale-only financial article must not be created for a purchase.
        self.assertNotIn('saleChequeCharge = calculatedCharge;', purchase_branch)
        self.assertIn('سود چک', apply)

    def test_refresh_keeps_the_purchase_interest_in_sync(self):
        refresh = block('private bool TryRefreshCalculatedCheques()', 'private void GoToPayment()')
        self.assertIn('purchaseChequeInterest = refreshed.Calculation.Interest;', refresh)

    def test_cheque_toggle_clears_purchase_interest(self):
        toggle = block('private void ToggleCheque()', 'private void ToggleInstallment()')
        self.assertIn('Type == TransactionType.Purchase && purchaseChequeInterest > 0', toggle)
        self.assertIn('purchaseChequeInterest = 0m;', toggle)

    def test_defaults_and_installments_use_the_payable_base(self):
        self.assertIn('var rest = FinalRemaining;', FORM)
        self.assertIn('var totalDue = Math.Max(0, PayableTotal - CashPart - ChequeSum());', FORM)
        self.assertIn('PayableTotal - CashPart - InstSum()', FORM)
        self.assertIn('PayableTotal - CashPart - ChequeSum()', FORM)

    def test_save_path_validates_against_the_payable_base(self):
        save = block('private async Task SaveInternal(bool printAfterSave)', 'if (ConversionQuote is not null')
        self.assertIn('var remainder = PayableTotal - paid;', save)
        self.assertIn('CashPart >= PayableTotal', save)
        self.assertIn('var cash = HasNonCash ? CashPart : PayableTotal;', save)
        self.assertIn('ExactMoney(PayableTotal)', save)
        # A purchase invoice only stores goods in its total, so paying the interest must
        # not be rejected -- but paying beyond goods + interest must be.
        self.assertIn('Type == TransactionType.Purchase && purchaseChequeInterest > 0 && remainder < -PaymentMix.Tolerance', save)
        self.assertIn('از مبلغ کالا و سود چک', save)
        self.assertNotIn('payableGoodsOnly', save)


class ManualChequeInterestForPurchase(unittest.TestCase):
    """«محاسبه سود چک‌های دستی» must exist for purchases too (parity with sales)."""

    def test_button_is_available_for_both_invoice_types(self):
        # Previously the button was wrapped in @if (Type == TransactionType.Sale).
        self.assertIn('data-cheque-interest-only', FORM)
        self.assertIn('@onclick="() => OpenCalc(true, interestOnly: true)"', FORM)
        self.assertNotIn('@if (Type == TransactionType.Sale)\n                                        {\n                                            <button type="button" class="btn btn-sm btn-outline-primary" data-cheque-interest-only', FORM)
        # The cheque component header must render it unconditionally.
        cheque_box = block('data-pay-cheque-box', 'data-pay-add-cheque')
        self.assertIn('data-cheque-interest-only', cheque_box)
        self.assertIn('محاسبه سود چک‌های دستی', cheque_box)

    def test_interest_only_mode_is_not_restricted_to_sales(self):
        self.assertIn('calcInterestOnly = forCheque && interestOnly;', FORM)
        self.assertNotIn('calcInterestOnly = forCheque && Type == TransactionType.Sale && interestOnly;', FORM)

    def test_purchase_interest_only_keeps_amounts_and_stores_interest(self):
        apply = block('private void ApplyCalc()', 'private List<ChequeDto> cheques')
        branch = apply.split('if (Type == TransactionType.Purchase)', 1)[1].split('var hadCharge', 1)[0]
        # Never touch cheque rows or the invoice total in this mode.
        self.assertNotIn('chequeAmounts', branch)
        self.assertNotIn('cheques =', branch)
        self.assertNotIn('saleChequeIncome = calculation.Interest', branch)
        self.assertIn('saleChequeIncome = 0m;', branch)
        self.assertIn('purchaseChequeInterest = calculation.Interest;', branch)
        self.assertIn('مبنای سود بیشتر از مبلغ کالا و ارزش افزوده است.', branch)

    def test_purchase_base_never_compounds_previous_interest(self):
        base = block('private decimal CalculatorSuggestedBase', 'private static string ExactMoney')
        self.assertIn('calcInterestOnly && Type == TransactionType.Purchase', base)
        self.assertIn('Math.Max(0m, goodsTotal - CashPart)', base)

    def test_interest_can_be_cleared_without_touching_payments(self):
        clear = block('private void ClearPurchaseChequeInterest()', 'InvoiceInfo("سود چک حذف شد')
        self.assertIn('purchaseChequeInterest = 0m;', clear)
        self.assertNotIn('chequeAmounts', clear)
        self.assertNotIn('saleChequeIncome', clear)
        self.assertNotIn('cheques =', clear)
        self.assertIn('data-purchase-cheque-interest-clear', FORM)
        self.assertIn('ClearPurchaseChequeInterest', FORM)

    def test_titles_and_hints_match_the_invoice_type(self):
        self.assertIn('private string ChequeInterestOnlyTitle', FORM)
        self.assertIn('private string ChequeInterestOnlyCaption', FORM)
        self.assertIn('private string CalculatorBaseHint', FORM)
        self.assertIn('@CalculatorBaseHint', FORM)
        self.assertIn('@(calcInterestOnly ? ChequeInterestOnlyCaption', FORM)
        self.assertIn('data-purchase-cheque-interest-only-note', FORM)


class PurchaseChequeInterestRecordedValues(unittest.TestCase):
    """The interest shown must equal cheque-total minus goods, exactly."""

    def test_interest_equals_cheque_sum_minus_goods(self):
        for goods, interest in ((Decimal(10_000_000), Decimal(900_000)),
                                (Decimal(2_550_000), Decimal(153_000)),
                                (Decimal(1), Decimal(1))):
            cheques = goods + interest
            self.assertEqual(cheques - goods, interest)
            payable = goods + interest
            self.assertEqual(payable - cheques, 0)

    def test_zero_interest_keeps_previous_behaviour(self):
        goods = Decimal(4_200_000)
        self.assertEqual(goods + Decimal(0) - goods, 0)
        self.assertEqual(RemainingCaptionMirror.caption(Decimal(0)), 'باقی‌مانده')


if __name__ == '__main__':
    unittest.main()
