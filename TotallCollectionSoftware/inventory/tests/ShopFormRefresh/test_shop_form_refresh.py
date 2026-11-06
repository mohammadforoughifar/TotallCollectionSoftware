"""Source contracts for integration; numeric helpers have separate executable C# tests."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / 'src/Inventory.Client'
def read(path):
    return (SRC / path).read_text(encoding='utf-8')

FORM = read('Pages/Orders/OrderForm.razor')
ORDERS = read('Pages/Orders/Orders.razor')
SCHEDULE = read('Pages/Orders/CalculatedChequeSchedule.cs')
PRICE = read('Pages/Orders/PurchaseSalePriceInput.cs')
INDEX = read('wwwroot/index.html')
JS = read('wwwroot/js/order-entry.js')


def section(start, end):
    return FORM.split(start, 1)[1].split(end, 1)[0]


class ChequeProvenanceContracts(unittest.TestCase):
    def test_only_generated_rows_in_current_component_get_provenance(self):
        self.assertIn('CalculatedChequeSchedule? calculatedChequeSchedule;', FORM)
        self.assertEqual(FORM.count('new CalculatedChequeSchedule('), 1)
        apply = section('private void ApplyCalc()', 'private void AddCheque()')
        self.assertLess(apply.index('if (calcInterestOnly)'), apply.index('new CalculatedChequeSchedule('))
        self.assertIn('calculatedChequeSchedule = null;', apply.split('if (calcInterestOnly)', 1)[1])
        self.assertIn('cheques = newCheques; chequeAmounts = newAmounts; chequeDates = newDates;', apply)
        self.assertIn('new CalculatedChequeSchedule(calculation, goodsTotal, CashPart, InstSum()', apply)
        load = section('if (IsEdit)\n            {\n                // پر کردن فرم', 'else\n            {\n                date = DateTime.Now;')
        self.assertNotIn('calculatedChequeSchedule =', load)
        self.assertIn('@if (showForm)\n{\n    <OrderForm', ORDERS)
        self.assertNotIn('DbContext', SCHEDULE)
        self.assertNotIn('localStorage', SCHEDULE)

    def test_manual_or_structural_changes_invalidate_generated_state(self):
        change = section('private void OnChequeAmountInput', 'private void OnInstAmountInput')
        self.assertIn('if (value != chequeAmounts[i]) calculatedChequeSchedule = null;', change)
        self.assertIn('chequeAmounts[i] = value;', change)
        for start, end in [('private void AddCheque()', 'private void RemoveCheque'),
                           ('private void RemoveCheque(', 'private void')]:
            self.assertIn('calculatedChequeSchedule = null;', section(start, end))
        self.assertIn('currentAmounts[i] != expected[i]', SCHEDULE)
        self.assertIn('cashAmount != CashAmount', SCHEDULE)
        self.assertIn('installmentAmount != InstallmentAmount', SCHEDULE)
        self.assertIn('currentAmounts.Count != chequeCount', SCHEDULE)
        self.assertIn('chequeCount != Calculation.Months', SCHEDULE)

    def test_goods_delta_reuses_original_rate_and_duration(self):
        self.assertIn('Calculation.BaseAmount + (newGoodsTotal - GoodsTotal)', SCHEDULE)
        self.assertIn('InvoiceInstallmentCalculation.Create(nextBase, Calculation.MonthlyRatePercent, Calculation.Months)', SCHEDULE)
        self.assertIn('this with { Calculation = next, GoodsTotal = newGoodsTotal }', SCHEDULE)
        refresh = section('private bool TryRefreshCalculatedCheques()', 'private void GoToPayment()')
        self.assertLess(refresh.index('schedule.Recalculate('), refresh.index('chequeAmounts = nextAmounts;'))
        self.assertLess(refresh.index('CreateSaleChequeCharge('), refresh.index('chequeAmounts = nextAmounts;'))
        self.assertIn('refreshed.AdjustmentRatePercent, refreshed.AdjustmentPeriodDays', refresh)
        self.assertIn('saleChequeIncome = nextCharge is null ? 0m : refreshed.Calculation.Interest;', refresh)
        self.assertIn('chargeInvoiceBase = nextCharge is null ? 0m : goodsTotal;', refresh)
        self.assertIn('removedCalculatedChequeIncome = nextCharge is null && (hadCharge || removedCalculatedChequeIncome);', refresh)
        self.assertIn('EditOrder?.HasChequeCharge == true && nextCharge is null', refresh)
        self.assertIn('EditOrder?.HasChequeCharge != true && nextCharge is not null', refresh)
        self.assertNotIn('chequeDates =', refresh)
        self.assertNotIn('cheques =', refresh)
        self.assertNotRegex(refresh, r'cheques\[.*?\]\.(Number|BankName|DueDate|TrsAccountId)\s*=')

    def test_failure_clears_amounts_but_preserves_history_and_metadata(self):
        pay = section('private void GoToPayment()', 'private void BackToInvoice()')
        self.assertIn('KnownLaterChequeActivity', pay)
        self.assertIn('order.Cheques.Any(c => c.IsCleared || c.Status != ChequeStatus.InHand)', pay)
        self.assertIn('paymentStepGoodsTotal.Value != goodsTotal && !hasKnownHistory', pay)
        self.assertIn('var refreshed = TryRefreshCalculatedCheques();', pay)
        fallback = pay.split('if (!refreshed)', 1)[1].split('else InvoiceInfo', 1)[0]
        self.assertIn('chequeAmounts[i] = "";', fallback)
        self.assertIn('calculatedChequeSchedule = null;', fallback)
        self.assertNotIn('cheques[i]', fallback)
        self.assertNotIn('chequeDates[i]', fallback)
        self.assertIn('instAmounts[i] = "";', pay)
        self.assertIn('شماره، بانک و تاریخ چک‌ها حفظ شده‌اند', FORM)
        save = section('private async Task SaveInternal(bool printAfterSave)', 'saving = true;')
        self.assertIn('chargeInvoiceBase != goodsTotal', save)
        self.assertIn('جمع پرداخت‌ها بیش از مبلغ فاکتور', save)


class PriceAndDeclutterContracts(unittest.TestCase):
    def test_new_sale_price_is_live_formatted_and_validated_in_purchase_only(self):
        table = FORM.split('<tr @key="l.Key" data-order-line="@l.Key">', 1)[1].split('</tr>', 1)[0]
        self.assertIn('Type == TransactionType.Purchase && canManagePricing', table)
        self.assertIn('value="@l.NewSalePriceText"', table)
        self.assertIn('PurchaseSalePriceInput.Format(e.Value?.ToString())', table)
        self.assertIn('قیمت اختیاری از تاریخ خرید اعمال می‌شود', table)
        self.assertIn('=> PurchaseSalePriceInput.TryParse(text, out value);', FORM)
        self.assertIn('!string.IsNullOrWhiteSpace(l.NewSalePriceText)', FORM)
        self.assertIn('!TryNewSalePrice(l.NewSalePriceText, out _)', FORM)
        self.assertIn('NewSalePrice = Type == TransactionType.Purchase', FORM)
        self.assertIn('Fa.ToEn(value)', PRICE)
        self.assertIn('if (raw.Length == 0) return typed;', PRICE)
        self.assertIn('!DigitsOnly(whole) || !DigitsOnly(fraction)', PRICE)
        self.assertIn('NumberStyles.AllowDecimalPoint', PRICE)
        self.assertIn('decimal.Round(result, 2) == result', PRICE)

    def test_redundant_hints_removed_but_warnings_and_description_fields_remain(self):
        opening = read('Pages/Shop/OpeningBalanceWizard.razor')
        pricing = read('Pages/Shop/Pricing.razor')
        products = read('Pages/Catalog/Products.razor')
        referrers = read('Pages/Catalog/Referrers.razor')
        goods = read('Pages/Catalog/GoodsIssues.razor')
        self.assertNotIn('<p class="op-form-desc">', opening)
        self.assertNotIn('<div class="op-segment-desc">', opening)
        self.assertNotIn('order-keyboard-hint', FORM)
        self.assertIn('aria-label="روش‌های پرداخت"', FORM)
        self.assertIn('مبلغ ردیف‌های خالی را دوباره وارد کنید', FORM)
        self.assertIn('فاکتور دارای گردش بعدی', FORM)
        self.assertIn('سود همین محاسبه، یک‌بار', FORM)
        self.assertIn('۱۰٬۰۰۰ ریال', FORM)
        self.assertIn('بالاتر از ۱۰۰ مبلغ ریالی است', products)
        self.assertIn('بر مبنای <b>سود</b>', referrers)
        self.assertIn('بر مبنای <b>کل مبلغ</b>', referrers)
        self.assertIn('مبلغ این چک در زمان وصول از موجودی این حساب بانکی کسر', opening)
        self.assertIn('بدون خرید معتبر، فروش کالا متوقف می‌شود', pricing)
        self.assertIn('<details class="pricing-note">', pricing)
        self.assertIn('@bind="form.Description"', goods)
        self.assertIn('@bind="description"', FORM)

    def test_shortcut_asset_is_updated_and_scoped(self):
        self.assertIn('js/order-entry.js?v=7', INDEX)
        self.assertIn('Inventory.Client.styles.css?v=9', INDEX)
        self.assertIn("event.key === 'F7' && isInvoice", JS)
        self.assertIn("event.key === 'F7' && !modal", JS)
        self.assertIn("target.closest('[data-order-line]')", JS)
        self.assertIn("row.querySelector('[data-order-last-purchase]')", JS)
        self.assertIn('Type == TransactionType.Sale && CanSeeProfit && l.ProductId > 0 && !IsServiceProduct(l.ProductId)', FORM)
        self.assertIn('F7 یا F8', FORM)
        self.assertIn('data-order-copy-context', ORDERS)
        self.assertIn('data-hotkey="f7"', ORDERS)


if __name__ == '__main__':
    unittest.main()
