"""Source-level terminology check; this is not a .NET runtime test."""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

class TermsContract(unittest.TestCase):
    def test_visible_label_and_errors_use_requested_name(self):
        form = (ROOT / 'src/Inventory.Client/Pages/Invoicing/FacRules.razor').read_text(encoding='utf-8')
        service = (ROOT / 'src/Inventory.Api/Services/Invoicing/InvoicingService.cs').read_text(encoding='utf-8')
        self.assertIn('حساب درآمد تاخیر تادیه چک', form)
        self.assertIn('حساب درآمد تاخیر تادیه چک', service)
        self.assertNotIn('تأخیر و تعدیه', form)
        self.assertNotIn('تأخیر و تعدیه', service)

    def test_sale_cheque_calculator_and_income_article_are_unified(self):
        form = (ROOT / 'src/Inventory.Client/Pages/Orders/OrderForm.razor').read_text(encoding='utf-8')
        cheque = form.split('@if (useCheque)', 1)[1].split('@* ---------- جزء اقساط', 1)[0]
        self.assertIn('OpenCalc(true)', cheque)
        self.assertIn('data-sale-cheque-income-article', cheque)
        self.assertLess(cheque.index('@for (var ci = 0;'), cheque.index('data-sale-cheque-income-article'))
        for text in ('<strong>تاخیر تادیه چک</strong>', 'بدهکار مشتری', 'بستانکار فروشگاه', 'بدون ارزش افزوده'):
            self.assertIn(text, cheque)
        self.assertNotIn('OpenSaleChequeCharge', form)
        self.assertNotIn('showSaleChequeCharge', form)
        self.assertNotIn('محاسبه سود</button>', cheque)
        self.assertNotIn('disabled="@(saleChequeCharge is not null)"', cheque)
        self.assertNotIn('به تنظیمات سرور وابسته است', cheque)

if __name__ == '__main__':
    unittest.main()
