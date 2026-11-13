from pathlib import Path
import unittest
PAGE = (Path(__file__).resolve().parents[2] / 'src/Inventory.Client/Pages/Orders/OrderForm.razor').read_text()
class PrintChooserTests(unittest.TestCase):
    def test_main_footer_has_no_paper_controls(self):
        footer = PAGE.split('<Footer>',1)[1].split('</Footer>',1)[0]
        self.assertNotIn('<select', footer)
        self.assertIn('@onclick="SaveAndPrint"',footer)
    def test_click_opens_chooser_without_saving(self):
        method=PAGE.split('private void SaveAndPrint()',1)[1].split('private void ClosePrintPaperChooser',1)[0]
        self.assertIn('showPrintPaperChooser = true', method)
        self.assertNotIn('SaveInternal(',method)
        self.assertIn('saving || loading || showPrintPaperChooser',method)
    def test_cancel_does_not_save_and_confirm_reuses_validation(self):
        self.assertIn('private void ClosePrintPaperChooser() => showPrintPaperChooser = false;',PAGE)
        confirm=PAGE.split('private async Task ConfirmPrintPaper()',1)[1].split('/// <summary>',1)[0]
        self.assertIn('!showPrintPaperChooser || saving',confirm)
        self.assertIn('await SaveInternal(printAfterSave: true);',confirm)
        self.assertLess(confirm.index('showPrintPaperChooser = false'),confirm.index('await SaveInternal'))
        self.assertIn('if (!ValidateInvoice())',PAGE)
    def test_only_a4_a5_in_chooser_and_used_for_print(self):
        modal=PAGE.split('<Modal Visible="showPrintPaperChooser"',1)[1].split('</Modal>',1)[0]
        self.assertIn('data-order-child="print-paper"',modal)
        self.assertIn('<option value="A4">A4</option>',modal)
        self.assertIn('<option value="A5">A5</option>',modal)
        self.assertNotIn('value="default"',modal)
        self.assertIn('SaleInvoicePrint.Build(order, companyProfile, printPaperSize)',PAGE)
if __name__=='__main__':unittest.main()
