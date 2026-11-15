"""Source contracts only; a .NET build remains required for runtime validation."""
import pathlib
import unittest

SOURCE = pathlib.Path(__file__).resolve().parents[2] / 'src/Inventory.Client/Pages/Orders/SaleInvoicePrint.cs'

class PurchaseHeader(unittest.TestCase):
    def test_purchase_header_is_guarded(self):
        source = SOURCE.read_text()
        start = source.index('        if (!isSale)')
        end = source.index('        if (order.IsCancelled)', start)
        block = source[start:end]
        self.assertIn('class=\\"purchase-header', block)
        self.assertIn('Meta(sb, "شماره فاکتور تأمین‌کننده", "-")', block)
        self.assertIn('Meta(sb, "تاریخ", order.Date.ToFa().FaDigits())', block)
        self.assertIn('else', block)
        self.assertIn('class=\\"brand-center', block)

    def test_symmetric_header_and_left_metadata(self):
        source = SOURCE.read_text()
        self.assertIn('grid-template-columns:minmax(0,1fr) minmax(0,1.1fr) minmax(0,1fr)', source)
        self.assertIn('.purchase-meta .meta-item { display:flex; justify-content:flex-end', source)
        self.assertIn('isSale ? "فاکتور فروش" : "فاکتور خرید"', source)
