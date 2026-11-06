"""Source-level regressions only; executable .NET/EF and browser probes are separate."""
from pathlib import Path
import sqlite3
import unittest

ROOT = Path(__file__).resolve().parents[2] / 'src'
def read(path):
    return (ROOT / path).read_text(encoding='utf-8')

class InvoiceEnhancements(unittest.TestCase):
    def test_f8_is_focused_row_and_permission_scoped(self):
        form = read('Inventory.Client/Pages/Orders/OrderForm.razor')
        js = read('Inventory.Client/wwwroot/js/order-entry.js')
        self.assertIn('Type == TransactionType.Sale && CanSeeProfit && l.ProductId > 0 && !IsServiceProduct(l.ProductId)', form)
        self.assertIn('data-order-last-purchase', form)
        self.assertIn("target.closest('[data-order-line]')", js)
        self.assertIn("row.querySelector('[data-order-last-purchase]')", js)
        self.assertIn('row && modal.contains(row)', js)
        self.assertIn('if (lastPurchase) actions = [lastPurchase]', js)

    def test_manual_cheque_interest_and_amount_reset(self):
        form = read('Inventory.Client/Pages/Orders/OrderForm.razor')
        shared = read('Inventory.Shared/InvoiceInstallmentCalculation.cs')
        registration = read('Inventory.Api/Services/Invoicing/SaleChequeChargeRegistration.cs')
        self.assertIn('CreateInterestOnly', shared)
        self.assertIn('SaleChequeInitialCalculation.Calculate', shared)
        self.assertIn('OpenCalc(true, interestOnly: true)', form)
        self.assertIn('if (!calcInterestOnly)', form)
        self.assertIn('paymentStepGoodsTotal.Value != goodsTotal', form)
        self.assertIn('chequeAmounts[i] = ""', form)
        self.assertIn('instAmounts[i] = ""', form)
        self.assertIn('cheques <= PaymentMix.Tolerance / 2m', registration)
        self.assertIn('parts > amounts.CustomerDebit', registration)
        self.assertIn('input.ConfirmedCreditDueDate', registration)

    def test_rate_snapshots_upgrade_without_backfilling_old_sales(self):
        schema = read('Inventory.Api/Data/ShopReferrerCommissionSchemaV1.cs')
        for col in ('ReferrerGoodsPercentSnapshot', 'ReferrerServicePercentSnapshot'):
            self.assertIn(col, schema)
            self.assertIn('COL_LENGTH', schema)
        self.assertIn('decimal(18,6) NULL', schema)
        self.assertNotIn('UPDATE [dbo].[Transactions]', schema)
        self.assertEqual(read('Inventory.Api/Data/DbInitializer.cs').count('await ShopReferrerCommissionSchemaV1.EnsureAsync(db)'), 2)
        # SQLite syntax/idempotency semantics: legacy rows remain NULL after two upgrades.
        db = sqlite3.connect(':memory:')
        db.executescript('CREATE TABLE Transactions (Id INTEGER PRIMARY KEY); INSERT INTO Transactions VALUES (1);')
        for _ in range(2):
            present = {row[1] for row in db.execute("PRAGMA table_info('Transactions')")}
            for name in ('ReferrerGoodsPercentSnapshot', 'ReferrerServicePercentSnapshot'):
                if name not in present:
                    db.execute(f'ALTER TABLE "Transactions" ADD COLUMN "{name}" TEXT NULL')
        self.assertEqual(db.execute('SELECT ReferrerGoodsPercentSnapshot, ReferrerServicePercentSnapshot FROM Transactions').fetchone(), (None, None))

    def test_commission_safety_and_permissions(self):
        service = read('Inventory.Api/Services/Catalog/InventoryService.cs')
        helper = read('Inventory.Shared/SaleReferrerCommission.cs')
        controller = read('Inventory.Api/Controllers/Sales/OrdersController.cs')
        self.assertIn('Math.Max(0m, lineProfit) * percent', helper)
        self.assertIn('Math.Max(0m, lineAfterDiscount) * percent', helper)
        self.assertIn('SaleReferrerCommission.Service(remainingAfterDiscount, serviceRate)', service)
        self.assertIn('!order.UsesLegacyCommissionPolicy', service)
        self.assertIn('hasAnyCommissionRateSnapshot', service)
        self.assertIn('ReliableSaleCostLines', service)
        self.assertIn('order.CommissionRatesAreHistorical && order.HasEstimatedSaleCost', service)
        self.assertIn('GetReferrerWalletAsync(referrerId)', read('Inventory.Api/Services/Core/AuthService.cs'))
        self.assertIn('goodsCostBasisChanged', service)
        for sensitive in ('ReferrerGoodsCommissionPercent', 'ReferrerServiceCommissionPercent', 'CommissionAmount', 'NetProfit'):
            self.assertIn('order.' + sensitive + ' = null', controller)
        for name in ('Catalog/InventoryService.cs', 'Warehousing/WarehousingService.cs'):
            self.assertIn('ShopCommissionProductKindGuard.EnsureChangeAllowedAsync', read('Inventory.Api/Services/' + name))

    def test_statement_is_customer_ledger_not_profit_report(self):
        api = read('Inventory.Api/Services/Catalog/InventoryService.cs')
        printer = read('Inventory.Client/Pages/Operations/CustomerStatementPrint.cs')
        page = read('Inventory.Client/Pages/Operations/OperationsOrderReport.razor')
        controller = read('Inventory.Api/Controllers/Catalog/PartiesController.cs')
        self.assertIn('.Include(t => t.Lines)', api.split('GetPartyStatementDetailAsync', 1)[1])
        self.assertIn('SaleIssuedDiscountTotal += saleDiscount', api)
        self.assertIn('SaleChequeIncomeTotal += income', api)
        self.assertIn('GoodsAmount + r.VatAmount', api)
        self.assertIn('statementRange = "all"', page)
        self.assertIn('GetPartyStatementDetailAsync(selectedParty, start, end)', page)
        self.assertIn('GetInvoicePartiesAsync(selectedType)', page)
        self.assertIn('type == PartyType.Customer && perms.Contains("Shop.OrderReports.View")', controller)
        self.assertIn('تخفیف فاکتور', printer)
        self.assertIn('تاخیر تادیه چک', printer)
        self.assertNotIn('پورسانت', printer)
        self.assertNotIn('TotalProfit', printer)

    def test_paper_choices_and_stock_warning_are_visible(self):
        invoice = read('Inventory.Client/Pages/Orders/SaleInvoicePrint.cs')
        form = read('Inventory.Client/Pages/Orders/OrderForm.razor')
        orders = read('Inventory.Client/Pages/Orders/Orders.razor')
        for text in ('A4 portrait', 'A5 portrait', 'compact-items', 'payment-schedule', 'این فاکتور ابطال شده است'):
            self.assertIn(text, invoice)
        self.assertIn('data-invoice-paper-size', form)
        self.assertIn('data-invoice-paper-size', orders)
        self.assertIn('موجودی صفر یا منفی', form)
        self.assertIn('max-width: 5.25rem', read('Inventory.Client/Pages/Orders/OrderForm.razor.css'))
        self.assertIn('مدیریت اقساط و خزانه داری', read('Inventory.Client/Layout/NavMenu.razor'))

if __name__ == '__main__':
    unittest.main()
