"""Structural safeguards only; Program.cs exercises posting through real EF when .NET is available."""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'src'
class PostingContract(unittest.TestCase):
    def test_posting_requires_atomic_store_event_not_erp_setup(self):
        s = (ROOT / 'Inventory.Api/Services/Invoicing/SaleChequeChargePosting.cs').read_text(encoding='utf8')
        for part in ('db.Database.CurrentTransaction', 'IsolationLevel.Serializable',
                     'sale.CompanyId != charge.CompanyId', 'db.SaleChequeChargeEvents.AnyAsync',
                     'SaleChequeChargeEventKind.Initial', 'db.SaleChequeChargeEvents.Add(entry)',
                     'await db.SaveChangesAsync()'):
            self.assertIn(part, s)
        for forbidden in ('db.FacRules', 'db.AccAccounts', 'db.AccFiscalYears', 'db.AccVouchers',
                          'CommitAsync', 'RecalcStock', 'ProductId ='):
            self.assertNotIn(forbidden, s)

    def test_no_posting_hook_until_transaction_and_returns_are_completed(self):
        # Keep partial work from being enabled in the production invoice path.
        for path in ('Inventory.Api/Services/Catalog/InventoryService.cs', 'Inventory.Api/Controllers/Sales/OrdersController.cs'):
            self.assertNotIn('SaleChequeChargePosting.PostAsync', (ROOT / path).read_text(encoding='utf8'))

if __name__ == '__main__': unittest.main()
