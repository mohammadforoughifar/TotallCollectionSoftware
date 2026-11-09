"""Source contracts for shop print entry points (not a .NET or Razor build)."""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CLIENT = ROOT / 'src/Inventory.Client'


def read(relative):
    return (CLIENT / relative).read_text(encoding='utf-8')


class ShopPrintPreviewContracts(unittest.TestCase):
    def test_invoice_issue_repair_and_customer_statement_use_shop_html_preview(self):
        files = {
            'Pages/Orders/Orders.razor': 1,
            'Pages/Orders/OrderForm.razor': 1,
            'Pages/Catalog/GoodsIssues.razor': 1,
            'Pages/Repairs/Repairs.razor': 2,
            'Pages/Operations/OperationsOrderReport.razor': 1,
        }
        for path, count in files.items():
            with self.subTest(path=path):
                source = read(path)
                self.assertEqual(source.count('"shopPrintPreview.openHtml"'), count)
                self.assertNotIn('"giPrint"', source)
        self.assertIn('SaleInvoicePrint.Build(order, companyProfile, printPaperSize)', read('Pages/Orders/Orders.razor'))
        self.assertIn('SaleInvoicePrint.Build(order, companyProfile, printPaperSize)', read('Pages/Orders/OrderForm.razor'))
        self.assertIn('CustomerStatementPrint.Build(statement, companyProfile, start, end)', read('Pages/Operations/OperationsOrderReport.razor'))

    def test_save_and_print_still_saves_first_and_preview_does_not_close_issue_details(self):
        invoice = read('Pages/Orders/OrderForm.razor')
        save = invoice.index('saved = await OrderSvc.CreateAsync(cmd);')
        preview = invoice.index('if (printAfterSave) await PrintOrderAsync(saved.Id);')
        callback = invoice.index('await OnSaved.InvokeAsync();', preview)
        self.assertLess(save, preview)
        self.assertLess(preview, callback)
        self.assertIn('data-hotkey="f4"', invoice)
        issue = read('Pages/Catalog/GoodsIssues.razor')
        self.assertIn('private async Task PrintCurrent() => await PrintDoc(form);', issue)
        self.assertIn('data-hotkey="f4"', issue)

    def test_all_shop_report_print_buttons_have_an_explicit_report_root(self):
        files = [
            'Pages/Operations/OperationsDashboard.razor',
            'Pages/Operations/OperationsOrderReport.razor',
            'Pages/Operations/PartyStatementsReport.razor',
            'Pages/Operations/ChequeIncomeReport.razor',
            'Pages/Reports/Kardex.razor',
            'Pages/Warehousing/InvKardex.razor',
            'Pages/Treasury/TrsVoucherForm.razor',
        ]
        for path in files:
            with self.subTest(path=path):
                source = read(path)
                self.assertIn('@ref="printRoot"', source)
                self.assertIn('private ElementReference printRoot;', source)
                self.assertIn('"shopPrintPreview.openElement", printRoot', source)
        rial = read('Pages/Warehousing/InvKardex.razor')
        self.assertIn('operations/reports/kardex-rial', rial)
        self.assertIn('await JS.InvokeVoidAsync("window.print");', rial)  # non-shop /inv/kardex

    def test_shop_guide_business_card_and_product_pdf_opt_in_only(self):
        self.assertIn('UsePrintPreview="true"', read('Pages/Shop/ShopGuide.razor'))
        self.assertIn('UsePrintPreview="true"', read('Pages/Catalog/Referrers.razor'))
        self.assertIn('PreviewPdf="true"', read('Pages/Catalog/Products.razor'))
        self.assertIn('public bool UsePrintPreview { get; set; }', read('Pages/Office/Guides/OfficeGuide.razor'))
        self.assertIn('officeGuides.print', read('Pages/Office/Guides/OfficeGuide.razor'))
        self.assertIn('printBusinessCard', read('Shared/BusinessCard.razor'))
        exports = read('Shared/ExportButtons.razor')
        self.assertIn('public bool PreviewPdf { get; set; }', exports)
        self.assertIn('format == "pdf" && PreviewPdf', exports)
        self.assertIn('"shopPrintPreview.openPdf", data, fileName', exports)
        self.assertIn('"downloadBlob"', exports)

    def test_letter_print_styles_do_not_hide_unrelated_documents(self):
        css = read('wwwroot/css/letters.css')
        self.assertNotRegex(css, r'body\s+\*\s*\{\s*visibility:\s*hidden')
        self.assertIn('body:where(:has(.ltx)) * { visibility:hidden !important; }', css)
        self.assertIn('.ltx .paper, .ltx .paper * { visibility:visible !important; }', css)
        self.assertIn('css/letters.css?v=13', read('wwwroot/index.html'))

    def test_preview_is_loaded_and_has_only_one_explicit_print_call(self):
        index = read('wwwroot/index.html')
        self.assertIn('css/shop-print-preview.css', index)
        self.assertIn('js/shop-print-preview.js', index)
        self.assertLess(index.index('js/file-preview.js'), index.index('js/shop-print-preview.js'))
        script = read('wwwroot/js/shop-print-preview.js')
        self.assertEqual(script.count('target.print();'), 1)
        self.assertNotRegex(script, r'setTimeout\s*\(\s*(?:\(\s*\)\s*=>|function\s*\([^)]*\))\s*\{?\s*[^}]*\.print\s*\(')
        self.assertIn('allow-same-origin allow-modals', script)
        self.assertNotIn('allow-scripts', script)
        self.assertIn("mode === 'guide'", script)
        self.assertIn('clone.setAttribute(\'inert\'', script)
        self.assertIn('window.giPrint = function', read('wwwroot/js/app-shell.js'))


if __name__ == '__main__':
    unittest.main()
