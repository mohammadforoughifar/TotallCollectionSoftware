"""Static checks; execute Program.cs for the actual C# and EF integration tests."""
import unittest
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2] / 'src'
class InitialChargeContract(unittest.TestCase):
    def test_original_monthly_calculator_keeps_same_formula_and_rounding(self):
        existing = (ROOT / 'Inventory.Client/Pages/Orders/OrderForm.razor').read_text(encoding='utf8')
        calc = (ROOT / 'Inventory.Shared/SaleChequeInitialCalculation.cs').read_text(encoding='utf8')
        shared = (ROOT / 'Inventory.Shared/InvoiceInstallmentCalculation.cs').read_text(encoding='utf8')
        self.assertIn('InvoiceInstallmentCalculation.Create(CalcBase, rate, calcMonths)', existing)
        self.assertIn('SaleChequeInitialCalculation.Calculate(baseAmount, monthlyRatePercent, months)', shared)
        self.assertIn('Math.Round(calculatorBase * monthlyRatePercent / 100m * months, 0)', calc)
        self.assertIn('months > 60', calc)
        self.assertNotIn('Math.Round(CalcBase * CalcRate', existing)

    def test_posting_blocks_undecided_payment_or_unknown_original_date(self):
        s = (ROOT / 'Inventory.Api/Services/Invoicing/SaleChequeChargePosting.cs').read_text(encoding='utf8')
        for part in ('charge.BaseAmount != goods + vat', 'charge.InitialAmount != charge.Amount',
                     '!c.OriginalDueDate.HasValue', 'c.OriginalDueDate.Value.Date != c.DueDate.Date',
                     'parts > sale.Amount || (parts < sale.Amount && !sale.DueDate.HasValue)',
                     'sale.Installments.Any(i => i.IsPaid || i.Amount <= 0'):
            self.assertIn(part, s)
        self.assertNotIn('PaymentMix.Tolerance', s)

if __name__ == '__main__': unittest.main()
