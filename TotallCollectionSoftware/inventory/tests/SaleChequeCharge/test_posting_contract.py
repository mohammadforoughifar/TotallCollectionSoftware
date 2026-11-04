"""Structural safeguards only; Program.cs exercises posting through real EF when .NET is available."""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'src'
class PostingContract(unittest.TestCase):
    def test_posting_requires_caller_transaction_and_separate_accounts(self):
        s = (ROOT / 'Inventory.Api/Services/Invoicing/SaleChequeChargePosting.cs').read_text(encoding='utf8')
        for part in ('db.Database.CurrentTransaction', 'IsolationLevel.Serializable', 'VoucherSource.StoreSale',
                     'ChequeIncomeAccountId', 'rule.VatAccountId', 'rule.PartyAccountId', 'rule.MainAccountId',
                     'amounts.CustomerDebit != sale.Amount', 'db.AccVouchers.AnyAsync',
                     'voucher.TotalDebit != voucher.TotalCredit', 'year', 'IsClosed', 'db.SaveChangesAsync()'):
            self.assertIn(part, s)
        self.assertNotIn('CommitAsync', s)
        self.assertNotIn('RecalcStock', s)
        self.assertNotIn('ProductId =', s)

    def test_no_posting_hook_until_transaction_and_returns_are_completed(self):
        # Keep partial work from being enabled in the production invoice path.
        for path in ('Inventory.Api/Services/Catalog/InventoryService.cs', 'Inventory.Api/Controllers/Sales/OrdersController.cs'):
            self.assertNotIn('SaleChequeChargePosting.PostAsync', (ROOT / path).read_text(encoding='utf8'))

if __name__ == '__main__': unittest.main()
