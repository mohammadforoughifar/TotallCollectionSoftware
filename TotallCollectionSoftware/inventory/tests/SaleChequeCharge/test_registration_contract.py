"""Source-level checks for the unconnected registration path; Program.cs covers EF execution."""
import unittest
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2] / 'src'
class RegistrationContract(unittest.TestCase):
    def test_initial_charge_is_server_calculated_and_rollback_is_mandatory(self):
        s = (ROOT / 'Inventory.Api/Services/Invoicing/SaleChequeChargeRegistration.cs').read_text(encoding='utf8')
        for part in ('db.Database.CurrentTransaction', 'IsolationLevel.Serializable', 'await transaction.RollbackAsync()',
                     'SaleChequeInitialCalculation.Calculate(', 'baseAmount = sale.Lines.Sum(',
                     'input.CalculatorBaseAmount > baseAmount', 'income <= 0', 'parts > amounts.CustomerDebit',
                     'credit > 0 && !input.ConfirmedCreditDueDate.HasValue', 'sale.Amount = amounts.CustomerDebit',
                     'db.SaleChequeCharges.Add(', 'await db.SaveChangesAsync()', 'SaleChequeChargePosting.PostAsync',
                     'db.ChangeTracker.Clear()'):
            self.assertIn(part, s)
        self.assertNotIn('CommitAsync', s)
        self.assertNotIn('ProductId =', s)
        self.assertNotIn('cheque.Amount =', s)

    def test_no_api_hook_until_corrections_are_ready(self):
        for name in ('Inventory.Api/Services/Catalog/InventoryService.cs', 'Inventory.Api/Controllers/Sales/OrdersController.cs'):
            self.assertNotIn('SaleChequeChargeRegistration.RegisterAsync', (ROOT / name).read_text(encoding='utf8'))

if __name__ == '__main__': unittest.main()
