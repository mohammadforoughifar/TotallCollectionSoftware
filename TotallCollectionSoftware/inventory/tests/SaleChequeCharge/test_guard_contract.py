"""Until corrective accounting is complete, old mutation paths fail closed for charge invoices."""
import unittest
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2] / 'src/Inventory.Api/Services'
class GuardContract(unittest.TestCase):
    def test_legacy_mutations_refuse_charge_invoice(self):
        s = (ROOT / 'Catalog/InventoryService.cs').read_text(encoding='utf8')
        for name in ('UpdateOrderAsync', 'DeleteOrderAsync', 'ClearChequeAsync', 'BounceChequeAsync',
                     'ResetChequeAsync', 'PayInstallmentAsync', 'UnpayInstallmentAsync', 'SettleCreditAsync'):
            section = s[s.index('public async Task' + ('<Order>' if name == 'UpdateOrderAsync' else '') + ' ' + name):]
            section = section[:section.find('\n    }')]
            self.assertIn('RejectUnimplementedChequeChargeMutationAsync(', section, name)
        invoicing = (ROOT / 'Invoicing/InvoicingService.cs').read_text(encoding='utf8')
        for name in ('PostStoreSaleVoucherAsync', 'UnpostStoreSaleVoucherAsync'):
            section = invoicing[invoicing.index(name):]
            self.assertIn('_db.SaleChequeCharges.AnyAsync', section[:500])
    def test_linked_accounting_vouchers_cannot_be_mutated_independently(self):
        s = (ROOT / 'Accounting/AccountingService.cs').read_text(encoding='utf8')
        self.assertIn('SaleChequeChargeEvents.IgnoreQueryFilters().AnyAsync', s)
        for name in ('SaveVoucherAsync', 'UnconfirmVoucherAsync', 'CancelVoucherAsync', 'DeleteVoucherAsync'):
            start = s.index(' ' + name + '(')
            section = s[start:s.find('\n    }', start)]
            self.assertIn('RejectChequeVoucherMutationAsync(', section, name)

if __name__ == '__main__': unittest.main()
