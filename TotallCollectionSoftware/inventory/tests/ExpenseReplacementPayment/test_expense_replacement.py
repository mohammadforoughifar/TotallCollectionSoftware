"""Source contracts; live .NET, concurrent API and treasury integration tests are still required."""
import unittest
from pathlib import Path
S = Path(__file__).resolve().parents[2] / 'src'
def read(path): return (S / path).read_text(encoding='utf-8')
SERVICE = read('Inventory.Api/Services/Finance/ShopExpenseChequeService.cs')
PAGE = read('Inventory.Client/Pages/Finance/Expenses.razor')
TREASURY = read('Inventory.Api/Services/Treasury/TreasuryService.cs')
SCHEMA = read('Inventory.Api/Data/ShopExpenseChequeSchemaV1.cs')
CTRL = read('Inventory.Api/Controllers/Finance/ExpensesController.cs')

class ReplacementPaymentTests(unittest.TestCase):
    def test_settlement_checks_state_date_company_balance_and_serializes(self):
        block = SERVICE.split('public async Task SettleReturnedAsync', 1)[1].split('public async Task<int> CreateAsync', 1)[0]
        for part in ('IsolationLevel.Serializable', 'cheques.Count == 0', 'c.State != ExpenseChequeState.Returned',
                     'c.SettledAt!.Value.Date > cmd.Date.Date', 'ShopExpenseChequeIntegrity.Validate',
                     'VerifyCompanyOwnershipAsync', 'EnsureAvailableOnAndAfterAsync', 'expense.ReplacementVoucher = voucher',
                     'expense.ReplacementOperationId = cmd.OperationId', 'await transaction.CommitAsync()'):
            self.assertIn(part, block)
        self.assertIn('expense.ReplacementVoucherId.HasValue || expense.CompanyId', SERVICE)
        self.assertIn('e.ReplacementVoucherId == id', TREASURY)

    def test_ui_supports_methods_and_preserves_cheque_history(self):
        for part in ('SelectOrdinary(CashType.Cash)', 'SelectOrdinary(CashType.CardReader)',
                     'SelectOrdinary(CashType.CardTransfer)', 'SettleReturnedExpenseAsync',
                     'chequeDetail.ReplacementVoucherId.HasValue', 'تاریخ پرداخت جایگزین'):
            self.assertIn(part, PAGE)
        self.assertIn('[HttpPost("{id:int}/replacement-payment")]', CTRL)
        self.assertIn('ReplacementOperationId', SCHEMA)
        self.assertIn('ReplacementVoucherId', SCHEMA)

if __name__ == '__main__': unittest.main()
