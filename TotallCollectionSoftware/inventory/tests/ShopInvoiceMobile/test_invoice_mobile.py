"""Razor/source contracts; browser fixtures separately verify production CSS geometry."""
from pathlib import Path
import unittest

SRC = Path(__file__).resolve().parents[2] / 'src'
FORM = (SRC / 'Inventory.Client/Pages/Orders/OrderForm.razor').read_text()
CSS = (SRC / 'Inventory.Client/Pages/Orders/OrderForm.razor.css').read_text()
BANK = (SRC / 'Inventory.Client/Shared/QuickBankModal.razor').read_text()

class ShopInvoiceMobileTests(unittest.TestCase):
    def test_bank_is_selected_once_and_stale_selection_is_cleared(self):
        self.assertNotIn('@bind="form.BankName"', BANK)
        self.assertIn('form.BankName = picked;', BANK)
        self.assertIn('selectedBank = "";', BANK)
        self.assertIn('form.BankName = null;', BANK)
        self.assertIn('OpenBankManager', BANK)
        self.assertIn('TreasuryAccountKind.Bank or TreasuryAccountKind.Pos', BANK)

    def test_card_and_iban_show_live_errors_without_truncating_input(self):
        self.assertIn('@bind="form.CardNumber" @bind:event="oninput"', BANK)
        self.assertIn('@bind="form.Iban" @bind:event="oninput"', BANK)
        self.assertIn('ShopIdentifierRules.ValidateBank(new TrsAccount { CardNumber = form.CardNumber })', BANK)
        self.assertIn('ShopIdentifierRules.ValidateBank(new TrsAccount { Iban = form.Iban })', BANK)
        self.assertIn('aria-invalid=', BANK)
        self.assertIn('ShopIdentifierRules.ValidateBank(form)', BANK)

    def test_footer_has_no_size_selector_and_uses_equal_grid_buttons(self):
        footer = FORM.split('<Footer>', 1)[1].split('</Footer>', 1)[0]
        self.assertIn('invoice-footer-actions', footer)
        self.assertNotIn('printPaperSize', footer)
        self.assertNotIn('<select', footer)
        self.assertNotIn('me-auto', footer)
        self.assertIn('مرحله قبل (F3)', footer)
        self.assertIn('repeat(2,minmax(0,1fr))', CSS)
        self.assertIn('min-height: 48px', CSS)

    def test_print_selection_precedes_save_and_confirmation_reuses_validation(self):
        open_dialog = FORM.split('private Task SaveAndPrint()', 1)[1].split('private void ClosePrintOptions', 1)[0]
        self.assertIn('showPrintOptions = true', open_dialog)
        self.assertNotIn('SaveInternal(', open_dialog)
        self.assertNotIn('OrderSvc.', open_dialog)
        confirm = FORM.split('private async Task ConfirmSaveAndPrint()', 1)[1].split('/// <summary>', 1)[0]
        self.assertIn('if (saving || !showPrintOptions) return;', confirm)
        self.assertIn('await SaveInternal(printAfterSave: true);', confirm)
        self.assertLess(confirm.index('showPrintOptions = false;'), confirm.index('SaveInternal('))
        self.assertIn('private void ClosePrintOptions() => showPrintOptions = false;', FORM)
        for size in ('default', 'A4', 'A5'):
            self.assertIn(f'("{size}",', FORM)
        self.assertIn('data-order-child="@formKey" data-invoice-paper-size', FORM)
        self.assertIn('if (printAfterSave) await PrintOrderAsync(saved.Id);', FORM)

    def test_amounts_are_explicitly_bold(self):
        self.assertIn('font-weight: 900', CSS.split('.purchase-summary-main .purchase-summary-total strong', 1)[1].split('}', 1)[0])
        self.assertIn('font-weight: 800', CSS.split('.purchase-summary-pay strong', 1)[1].split('}', 1)[0])

if __name__ == '__main__': unittest.main()
