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

if __name__ == '__main__':
    unittest.main()
