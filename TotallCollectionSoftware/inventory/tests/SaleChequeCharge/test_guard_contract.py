"""Until corrective accounting is complete, old mutation paths fail closed for charge invoices."""
import unittest
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2] / 'src/Inventory.Api/Services'
class GuardContract(unittest.TestCase):
    def test_legacy_mutations_refuse_charge_invoice(self):
        s = (ROOT / 'Catalog/InventoryService.cs').read_text(encoding='utf8')
        for name in ('UpdateOrderAsync', 'DeleteOrderAsync'):
            section = s[s.index('public async Task' + ('<Order>' if name == 'UpdateOrderAsync' else '') + ' ' + name):]
            section = section[:section.find('\n    }')]
            self.assertIn('RejectUnimplementedChequeChargeMutationAsync(', section, name)
        for name in ('PayInstallmentAsync', 'UnpayInstallmentAsync', 'SettleCreditAsync'):
            section = s[s.index('public Task ' + name):]
            section = section[:section.find('\n    }')]
            self.assertIn('throw new InvalidOperationException', section, name)
        clear = s[s.index('public Task ClearChequeAsync'):s.index('public async Task BounceChequeAsync')]
        self.assertIn('throw new InvalidOperationException', clear)
        for name in ('BounceChequeAsync', 'ResetChequeAsync'):
            section = s[s.index('public async Task ' + name):]
            section = section[:section.find('\n    }')]
            self.assertIn('SaleChequeCharges.AnyAsync', section, name)
            self.assertIn('SaleChequeReceipts.AnyAsync', section, name)
            self.assertIn('IsolationLevel.Serializable', section, name)
        section = s[s.index('public async Task ChangeSaleChequeStatusAsync'):]
        section = section[:section.find('\n    }')]
        for guard in ('IsolationLevel.Serializable', 'SaleChequeReceipts.AnyAsync'):
            self.assertIn(guard, section)
        self.assertNotIn('SaleChequeChargeEvents.Add', section)
        invoicing = (ROOT / 'Invoicing/InvoicingService.cs').read_text(encoding='utf8')
        for name in ('PostStoreSaleVoucherAsync', 'UnpostStoreSaleVoucherAsync'):
            section = invoicing[invoicing.index(name):]
            self.assertIn('_db.SaleChequeCharges.IgnoreQueryFilters().AnyAsync', section[:500])
    def test_linked_accounting_vouchers_cannot_be_mutated_independently(self):
        s = (ROOT / 'Accounting/AccountingService.cs').read_text(encoding='utf8')
        self.assertIn('SaleChequeChargeEvents.IgnoreQueryFilters().AnyAsync', s)
        for name in ('SaveVoucherAsync', 'UnconfirmVoucherAsync', 'CancelVoucherAsync', 'DeleteVoucherAsync'):
            start = s.index(' ' + name + '(')
            section = s[start:s.find('\n    }', start)]
            self.assertIn('RejectChequeVoucherMutationAsync(', section, name)
        treasury = (ROOT / 'Treasury/TreasuryService.cs').read_text(encoding='utf8')
        reverse = treasury[treasury.index('private async Task ReverseAsync'):]
        reverse = reverse[:reverse.find('\n    }')]
        self.assertIn('SaleChequeChargeEvents.IgnoreQueryFilters()', reverse)
        self.assertIn('SaleChequeReceipts.IgnoreQueryFilters()', reverse)
        save = treasury[treasury.index('public async Task<TrsAccount> SaveAccountAsync'):]
        save = save[:save.find('\n    }')]
        self.assertIn('BeginTransactionAsync(IsolationLevel.Serializable)', save)
        self.assertIn('mappingTx.CommitAsync()', save)

if __name__ == '__main__': unittest.main()
