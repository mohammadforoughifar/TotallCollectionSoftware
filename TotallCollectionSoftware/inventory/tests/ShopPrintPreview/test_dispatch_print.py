"""Source contracts; not runtime C# / database tests."""
import pathlib
import unittest
ROOT=pathlib.Path(__file__).resolve().parents[2]/'src/Inventory.Client'
class DispatchPrintContracts(unittest.TestCase):
    def test_saved_dispatch_only(self):
        source=(ROOT/'Pages/Catalog/GoodsIssues.razor').read_text()
        self.assertIn('await Print(form);',source)
        self.assertIn('await IssueSvc.GetAsync(g.Id)',source)
        self.assertIn('"shopPrintPreview.choosePaper"',source)
        self.assertIn('GoodsIssuesPrint.Build(d, companyName, selectedPaper, companyProfile.LogoDataUrl)',source)
    def test_persisted_print_fields(self):
        source=(ROOT/'Pages/Catalog/GoodsIssuesPrint.cs').read_text()
        for field in ['d.Number','d.Date','d.PartyName','d.TotalQuantity','line.ProductName','line.Quantity','line.Note','d.Description']:
            self.assertIn(field,source)
        for field in ['Carrier','Warehouse','Vehicle','InvoiceNumber','Phone','Address','Price']:
            self.assertNotIn(field,source)
        self.assertIn('a5 ? "A5" : "A4"',source)
        self.assertIn('WebUtility.HtmlEncode',source)
    def test_paper_choice_is_at_print_entry_only(self):
        for file in ['Pages/Orders/Orders.razor','Pages/Orders/OrderForm.razor']:
            source=(ROOT/file).read_text()
            self.assertNotIn('data-invoice-paper-size',source)
            if file.endswith('OrderForm.razor'):
                self.assertIn('showPrintPaperChooser',source)
                self.assertIn('ConfirmPrintPaper',source)
            else:
                self.assertIn('"shopPrintPreview.choosePaper"',source)
                self.assertIn('if (selectedPaper is null) return;',source)
        script=(ROOT/'wwwroot/js/shop-print-preview.js').read_text()
        self.assertIn('dialog.showModal()',script)
        self.assertIn('resolve(value)',script)
