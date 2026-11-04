"""Static checks for purchase and sale; not a substitute for the Blazor build/browser tests."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
FORM = (ROOT / 'src/Inventory.Client/Pages/Orders/OrderForm.razor').read_text(encoding='utf-8')
CSS = (ROOT / 'src/Inventory.Client/Pages/Orders/OrderForm.razor.css').read_text(encoding='utf-8')
MODAL = (ROOT / 'src/Inventory.Client/Shared/Modal.razor').read_text(encoding='utf-8')
SEARCH = (ROOT / 'src/Inventory.Client/Shared/SearchSelect.razor').read_text(encoding='utf-8')
INDEX = (ROOT / 'src/Inventory.Client/wwwroot/index.html').read_text(encoding='utf-8')
PARTY = (ROOT / 'src/Inventory.Client/Shared/PartyFormModal.razor').read_text(encoding='utf-8')
BANK = (ROOT / 'src/Inventory.Client/Shared/QuickBankModal.razor').read_text(encoding='utf-8')


def method(name, following):
    return FORM.split(name, 1)[1].split(following, 1)[0]


class PurchaseWizardContract(unittest.TestCase):
    def test_purchase_and_sales_use_same_step_gate(self):
        self.assertIn('Type is TransactionType.Purchase or TransactionType.Sale', FORM)
        self.assertIn('!UseInvoiceWizard || invoiceStep == 1', FORM)
        self.assertIn('UseInvoiceWizard && invoiceStep == 2', FORM)
        self.assertIn('private int invoiceStep = 1;', FORM)
        self.assertIn('if (!saving && ValidateInvoice())', FORM)
        self.assertIn('invoiceStep = 2;', FORM)
        self.assertIn('if (!saving)', FORM)
        self.assertIn('invoiceStep = 1;', FORM)

    def test_save_and_print_share_validation_and_cannot_skip_payment(self):
        self.assertIn('private Task Save() => SaveInternal(printAfterSave: false);', FORM)
        self.assertIn('private Task SaveAndPrint() => SaveInternal(printAfterSave: true);', FORM)
        save = method('private async Task SaveInternal(bool printAfterSave)', 'var cmd = new OrderCommand')
        self.assertIn('UseInvoiceWizard && invoiceStep != 2', save)
        self.assertIn('!ValidateInvoice()', save)
        self.assertIn('invoiceStep = 1', save)
        validation = method('private bool ValidateInvoice()', 'private async Task SaveInternal')
        for expression in ('warehouseId == 0', 'partyId == 0', '!date.HasValue', 'validLines.Count == 0', 'decimal.Round(l.Qty, 2'):
            self.assertIn(expression, validation)

    def test_payment_selection_and_sums(self):
        for component in ('cash', 'cheque', 'installment', 'credit'):
            self.assertIn(f'data-pay-comp="{component}"', FORM)
        for callback in ('ToggleCheque()', 'ToggleInstallment()', 'ToggleCredit()'):
            self.assertRegex(FORM, re.escape(callback) + r'\s*\{[^}]*EnsureInvoicePaymentMethod\(\);')
        self.assertIn('UseInvoiceWizard && !useCash', FORM)
        self.assertIn('UseInvoiceWizard && !useCash && !HasNonCash', FORM)
        self.assertIn('lines.Where(l => l.ProductId > 0 && l.Qty > 0)', FORM)
        self.assertIn('private decimal grandTotal => TotalLines.Sum(l => l.Total);', FORM)
        for value in ('grandTotal.Money()', 'CashPart.Money()', 'ChequeSum().Money()', 'InstSum().Money()', 'Math.Abs(FinalRemaining).Money()'):
            self.assertIn(value, FORM.split('class="purchase-summary"', 1)[1].split('</section>', 1)[0])

    def test_sale_features_are_preserved_in_first_step(self):
        self.assertIn('Type == TransactionType.Sale ? "اطلاعات فروش" : "اطلاعات خرید"', FORM)
        self.assertIn('Type == TransactionType.Sale ? "مشتری" : "تأمین‌کننده"', FORM)
        self.assertIn('Type == TransactionType.Sale ? "بازگشت به اطلاعات فروش"', FORM)
        self.assertIn('Type == TransactionType.Sale && invoiceStep == 1 && (CanSeeProfit || referrerId > 0)', FORM)
        self.assertIn('data-label="سود"', FORM)
        self.assertIn('data-label="پورسانت"', FORM)
        self.assertIn('CanSeeProfit', FORM)
        self.assertIn('LineProfit(l)', FORM)
        self.assertIn('LineCommission(l)', FORM)
        self.assertIn('ShowLastPurchase(l.ProductId)', FORM)
        self.assertIn('ReferrerId = Type == TransactionType.Sale && referrerId > 0 ? referrerId : null', FORM)
        self.assertIn('جمع مقدار اقلام', FORM)
        self.assertIn('purchase-items sale-items', FORM)
        self.assertIn('.sale-items tbody td:first-child', CSS)

    def test_line_keyboard_contract_and_purchase_payment_title(self):
        self.assertIn('data-order-line="@l.Key"', FORM)
        self.assertIn('data-order-last-field', FORM)
        self.assertIn('AdvanceOnEnter="UseInvoiceWizard"', FORM)
        self.assertIn('pendingLineFocus = line.Key;', FORM)
        self.assertIn('JS.InvokeVoidAsync("orderEntry.focusLine", formKey, key.ToString())', FORM)
        self.assertIn('if (lines.Count == 0) AddLine();', FORM)
        self.assertIn('tabindex="-1" title="حذف سطر"', FORM)
        self.assertIn('tabindex="-1" title="آخرین فاکتور خرید این کالا"', FORM)
        self.assertIn('Type == TransactionType.Purchase ? "اعلامیه پرداخت" : "اقساطی"', FORM)
        self.assertIn('data-order-initial', FORM)
        self.assertIn('pendingSectionFocus = "[data-order-payment]"', FORM)
        self.assertIn('pendingSectionFocus = "[data-pay-cheque-box]"', FORM)
        self.assertIn('pendingSectionFocus = "[data-pay-inst-box]"', FORM)
        self.assertIn('pendingSectionFocus = "[data-pay-credit-box]"', FORM)
        self.assertIn('data-order-calculator="@formKey"', FORM)
        self.assertIn('InvoiceFormKey="@(UseInvoiceWizard ? formKey : null)"', FORM)
        self.assertIn('data-order-child="@InvoiceFormKey"', PARTY)
        self.assertIn('data-order-child="@InvoiceFormKey"', BANK)
        self.assertIn('Shift+Enter', PARTY)
        self.assertIn('<SearchSelect Items="whLookup" AdvanceOnEnter="UseInvoiceWizard"', FORM)
        self.assertIn('<SearchSelect Items="partyLookup" AdvanceOnEnter="UseInvoiceWizard"', FORM)
        self.assertIn('if (AdvanceOnEnter)', SEARCH)
        self.assertIn('await JS.InvokeVoidAsync("orderEntry.focusNextField", _input)', SEARCH)
        self.assertLess(INDEX.index('js/order-entry.js?v=2'), INDEX.index("document.addEventListener('keydown'"))

    def test_responsive_and_header_scope(self):
        self.assertIn('@media (max-width: 600px)', CSS)
        self.assertIn('.purchase-items tbody tr', CSS)
        self.assertIn('.purchase-payment .pay-tabs', CSS)
        self.assertIn('HeaderClass="@(UseInvoiceWizard', FORM)
        self.assertIn('[Parameter] public string HeaderClass', MODAL)
        self.assertIn('@HeaderClass', MODAL)


if __name__ == '__main__':
    unittest.main()
