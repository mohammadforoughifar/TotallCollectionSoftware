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

    def test_mixed_cash_uses_one_linked_treasury_receipt(self):
        s = (ROOT / 'Inventory.Api/Services/Invoicing/SaleChequeChargeRegistration.cs').read_text(encoding='utf8')
        for part in ('SystemCompanies.CountAsync() > 1', 'sale.TrsAccountId is not > 0',
                     'SaleChequeChargePosting.PostAsync', 'db.TrsVouchers.Add(cashReceipt)',
                     'charge.InitialCashTrsVoucher = cashReceipt', 'TotalAmount = cash', 'Status = TreasuryStatus.Confirmed'):
            self.assertIn(part, s)
        statement = (ROOT / 'Inventory.Api/Services/Catalog/InventoryService.cs').read_text(encoding='utf8')
        integrity = (ROOT / 'Inventory.Api/Services/Invoicing/SaleChequeShopLedgerIntegrity.cs').read_text(encoding='utf8')
        self.assertIn('دریافت نقدی اولیه با گردش فروشگاهی صندوق/بانک مطابقت ندارد', integrity)

    def test_registration_is_enabled_without_erp_or_experimental_flag(self):
        s = (ROOT / 'Inventory.Api/Services/Catalog/InventoryService.cs').read_text(encoding='utf8')
        start = s.index('public async Task<Order> CreateOrderAsync')
        end = s.index('public async Task<Order?> GetOrderAsync', start)
        section = s[start:end]
        self.assertNotIn('SaleChequeCharge:ExperimentalEnabled', s)
        for part in ('cmd.ChequeCharge is not null',
                     'IsolationLevel.Serializable', 'SaleChequeChargeRegistration.RegisterAsync',
                     'ApplyPayment(txn, cmd, paymentTotal ?? ShopInvoiceLineMath.Total(cmd.Lines, wholeRials))',
                     'var goodsTotal = ShopInvoiceLineMath.Total(cmd.Lines, wholeRials)',
                     'if (ownsTransaction) await ownedTransaction!.CommitAsync()'):
            self.assertIn(part, section)
        self.assertLess(section.index('SaleChequeChargeRegistration.RegisterAsync'), section.index('if (ownsTransaction) await ownedTransaction!.CommitAsync()'))

if __name__ == '__main__': unittest.main()
