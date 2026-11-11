"""Source regressions; live Blazor rendering and database actions need .NET/integration tests."""
from pathlib import Path
import unittest

SRC = Path(__file__).resolve().parents[2] / 'src'
FORM = (SRC/'Inventory.Client/Pages/Orders/OrderForm.razor').read_text()
JS = (SRC/'Inventory.Client/wwwroot/js/order-entry.js').read_text()

class SalesFollowupTests(unittest.TestCase):
    def test_short_action_label_precedes_the_single_f8_shortcut(self):
        button = FORM.split('data-order-last-purchase',1)[1].split('</button>',1)[0]
        self.assertIn('<span>آخرین خرید</span><kbd>F8</kbd>', button)
        self.assertNotIn('F7', button)
        self.assertNotIn("event.key === 'F7' && isInvoice", JS)
        self.assertIn("if (event.key === 'F8')", JS)
        self.assertIn("event.key === 'F7' && !modal", JS)  # list copy remains unchanged

    def test_both_purchase_numbers_open_real_purchase_form_without_navigation(self):
        self.assertIn('OpenPurchaseForm(basis.PurchaseId.Value)', FORM)
        self.assertIn('OpenPurchaseForm(lastPurchase.Id)', FORM)
        self.assertIn('Type="TransactionType.Purchase" EditOrder="purchaseFormOrder"', FORM)
        method = FORM.split('private async Task OpenPurchaseForm(int purchaseId)',1)[1].split('private void ClosePurchaseForm',1)[0]
        self.assertIn('await OrderSvc.GetAsync(purchaseId)', method)
        self.assertIn('order.Type != TransactionType.Purchase', method)
        self.assertNotIn('NavigateTo', method)
        self.assertIn('OnSaved="OnPurchaseFormSaved" OnCancel="ClosePurchaseForm"', FORM)
        callback = FORM.split('private async Task OnPurchaseFormSaved()',1)[1].split('private void CloseLastPurchase',1)[0]
        self.assertNotIn('lines =', callback)
        self.assertNotIn('cheques =', callback)
        self.assertIn('ShowLastPurchase(lastPurchaseProductId)', callback)

    def test_manual_calculator_has_no_optional_rescheduling_section(self):
        optional = FORM.split('شرایط اختیاری تعدیل سررسید',1)[0]
        self.assertIn('@if (!calcInterestOnly)', optional[-230:])
        # Existing financial snapshots/defaults are preserved, not deleted as part of a UI cleanup.
        self.assertIn('calcAdjustmentDays = saleChequeCharge.AdjustmentPeriodDays', FORM)
        self.assertIn('calculation.CreateSaleChequeCharge(goodsTotal, adjustmentRate, calcAdjustmentDays)', FORM)

    def test_repair_percentage_formats_the_whole_razor_expression(self):
        repairs = (SRC/'Inventory.Client/Pages/Repairs/Repairs.razor').read_text()
        self.assertIn('@((bucket.Count * 100 / openTotal).Num())', repairs)
        self.assertNotIn('@((bucket.Count * 100 / openTotal)).Num()', repairs)

if __name__ == '__main__': unittest.main()
