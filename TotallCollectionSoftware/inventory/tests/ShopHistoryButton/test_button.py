from pathlib import Path
import unittest
ROOT = Path(__file__).resolve().parents[2] / 'src/Inventory.Client'
PAGE = (ROOT / 'Pages/Orders/OrderForm.razor').read_text()
CSS = (ROOT / 'Pages/Orders/OrderForm.razor.css').read_text()
class HistoryButtonTests(unittest.TestCase):
    def test_button_and_delete_share_action_row(self):
        actions = PAGE.split('<div class="order-line-actions">')[1].split('</div>')[0]
        self.assertIn('data-order-last-purchase', actions)
        self.assertIn('RemoveLine(l)', actions)
        self.assertIn('SaleActionsColumnRem = 11m', PAGE)
        self.assertIn('flex-wrap: nowrap', CSS)
    def test_footer_is_compact_and_local(self):
        self.assertIn('class="invoice-footer-actions"', PAGE)
        footer = PAGE.split('class="invoice-footer-actions"')[1].split('</Footer>')[0]
        self.assertNotIn('me-auto', footer)
        self.assertIn('.invoice-footer-actions > .btn { flex: 0 0 auto; width: auto;', CSS)
        self.assertIn('min-height: 40px', CSS)
    def test_label_and_shortcut_are_separate_and_readable(self):
        self.assertIn('<span>آخرین خرید</span><kbd dir="ltr">F8</kbd>', PAGE)
        self.assertIn('width: max-content', CSS)
        self.assertIn('white-space: nowrap', CSS)
        self.assertIn('overflow-wrap: normal', CSS)
    def test_keyboard_and_permissions(self):
        self.assertIn('Type == TransactionType.Sale && CanSeeProfit && l.ProductId > 0 && !IsServiceProduct(l.ProductId)', PAGE)
        js=(ROOT/'wwwroot/js/order-entry.js').read_text()
        self.assertNotIn("event.key === 'F7' && isInvoice", js)
        self.assertIn("event.key === 'F7' && !modal", js)
if __name__ == '__main__': unittest.main()
