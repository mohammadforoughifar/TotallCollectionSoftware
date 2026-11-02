"""Static regression checks; they do not replace the Blazor build/browser tests."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
FORM = (ROOT / 'src/Inventory.Client/Pages/Orders/OrderForm.razor').read_text(encoding='utf-8')
CSS = (ROOT / 'src/Inventory.Client/Pages/Orders/OrderForm.razor.css').read_text(encoding='utf-8')
MODAL = (ROOT / 'src/Inventory.Client/Shared/Modal.razor').read_text(encoding='utf-8')


def method(name, following):
    return FORM.split(name, 1)[1].split(following, 1)[0]


class PurchaseWizardContract(unittest.TestCase):
    def test_purchase_steps_do_not_change_sales_gate(self):
        self.assertIn('Type != TransactionType.Purchase || purchaseStep == 1', FORM)
        self.assertIn('Type == TransactionType.Sale || (Type == TransactionType.Purchase && purchaseStep == 2)', FORM)
        self.assertIn('private int purchaseStep = 1;', FORM)
        self.assertIn('if (!saving && ValidateInvoice()) purchaseStep = 2;', FORM)
        self.assertIn('if (!saving) purchaseStep = 1;', FORM)

    def test_save_and_print_share_validation_and_cannot_skip_payment(self):
        self.assertIn('private Task Save() => SaveInternal(printAfterSave: false);', FORM)
        self.assertIn('private Task SaveAndPrint() => SaveInternal(printAfterSave: true);', FORM)
        save = method('private async Task SaveInternal(bool printAfterSave)', 'var cmd = new OrderCommand')
        self.assertIn('Type == TransactionType.Purchase && purchaseStep != 2', save)
        self.assertIn('!ValidateInvoice()', save)
        self.assertIn('purchaseStep = 1', save)
        validation = method('private bool ValidateInvoice()', 'private async Task SaveInternal')
        for expression in ('warehouseId == 0', 'partyId == 0', '!date.HasValue', 'validLines.Count == 0', 'decimal.Round(l.Qty, 2'):
            self.assertIn(expression, validation)

    def test_payment_selection_and_sums(self):
        for component in ('cash', 'cheque', 'installment', 'credit'):
            self.assertIn(f'data-pay-comp="{component}"', FORM)
        for callback in ('ToggleCheque()', 'ToggleInstallment()', 'ToggleCredit()'):
            self.assertRegex(FORM, re.escape(callback) + r'\s*\{[^}]*EnsurePurchasePaymentMethod\(\);')
        self.assertIn('Type == TransactionType.Purchase && !useCash', FORM)
        self.assertIn('Type == TransactionType.Purchase && !useCash && !HasNonCash', FORM)
        self.assertIn('lines.Where(l => l.ProductId > 0 && l.Qty > 0)', FORM)
        self.assertIn('private decimal grandTotal => TotalLines.Sum(l => l.Total);', FORM)
        for value in ('grandTotal.Money()', 'CashPart.Money()', 'ChequeSum().Money()', 'InstSum().Money()', 'Math.Abs(FinalRemaining).Money()'):
            self.assertIn(value, FORM.split('class="purchase-summary"', 1)[1].split('</section>', 1)[0])

    def test_responsive_and_header_scope(self):
        self.assertIn('@media (max-width: 600px)', CSS)
        self.assertIn('.purchase-items tbody tr', CSS)
        self.assertIn('.purchase-payment .pay-tabs', CSS)
        self.assertIn('HeaderClass="@(Type == TransactionType.Purchase', FORM)
        self.assertIn('[Parameter] public string HeaderClass', MODAL)
        self.assertIn('@HeaderClass', MODAL)


if __name__ == '__main__':
    unittest.main()
