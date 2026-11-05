"""Safety contracts for store-only permissions and operational balances.

These source checks complement, but cannot replace, the .NET EF/HTTP probe.
"""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'src'


def read(path):
    return (ROOT / path).read_text(encoding='utf8')


class IndependentStoreContract(unittest.TestCase):
    def test_financial_writes_do_not_create_or_require_erp_records(self):
        for name in ('SaleChequeChargePosting', 'SaleChequeChargeRegistration', 'SaleChequeRescheduling',
                     'SaleChequeReturnService', 'SaleChequeReceiptPosting', 'SaleChequeReceiptReversal',
                     'SaleChequeInstallmentPosting', 'SaleChequeCreditSettlementService', 'SaleChequeCancellationService'):
            source = read(f'Inventory.Api/Services/Invoicing/{name}.cs')
            for forbidden in ('db.FacRules', 'db.AccFiscalYears', 'db.AccAccounts', 'db.AccVouchers.Add',
                              'new AccVoucher', 'ExperimentalEnabled'):
                self.assertNotIn(forbidden, source, (name, forbidden))
            self.assertIn('IsolationLevel.Serializable', source, name)
        registration = read('Inventory.Api/Services/Invoicing/SaleChequeChargeRegistration.cs')
        self.assertIn('InitialCashTrsVoucher = cashReceipt', registration)
        self.assertIn('db.Database.CurrentTransaction', registration)  # caller owns atomic transaction
        self.assertIn('db.ChangeTracker.Clear()', registration)

    def test_profit_and_purchase_lookups_are_separately_scoped(self):
        auth = read('Inventory.Api/Infrastructure/ShopPermissionAuthorizer.cs')
        for token in ('TransactionType.Sale or TransactionType.SaleReturn', 'TransactionType.Purchase',
                      'Section(permissions, "Sale", write)', 'Section(permissions, "Purchase", write)',
                      'InvoiceProducts', 'InvoiceWarehouses', 'InvoiceParties', 'OrderType(permissions, requestedType, write)',
                      'action.Equals("ViewProfit"'):
            self.assertIn(token, auth)
        controller = read('Inventory.Api/Controllers/Sales/OrdersController.cs')
        for token in ('PurchasePrice = showCost ? p.PurchasePrice : 0', 'AvgCost = showCost ? p.AvgCost : 0',
                      'type == TransactionType.Purchase || await CanSeeProfitAsync()',
                      'order.TotalProfit = null', 'order.NetProfit = null', 'order.CommissionAmount = null',
                      'line.UnitCost = null', 'line.Profit = null', 'line.Commission = null'):
            self.assertIn(token, controller)
        sections = read('Inventory.Shared/ShopSections.cs')
        self.assertIn('section.Writable && granted.Equals(section.ManagePermission', sections)
        self.assertIn('IsViewLike(action) || kardexRead', sections)
        self.assertNotIn('IsViewLike(action) || !section.Writable', sections)
        self.assertIn('action.Equals("ViewProfit"', sections)

    def test_bank_history_and_company_checks_are_explicit(self):
        guard = read('Inventory.Api/Services/Invoicing/SaleChequeTreasuryGuard.cs')
        for token in ('activeCompanyId == 0', 'account.CompanyId != saleCompanyId',
                      'saleCompanyId != db.ActiveCompanyId', 'SystemCompanies.CountAsync() > 1'):
            self.assertIn(token, guard)
        context = read('Inventory.Api/Data/AppDbContext.cs')
        self.assertIn('if (active == 0)', context)
        self.assertIn('entry.Entity.CompanyId != active', context)
        company_schema = read('Inventory.Api/Data/SalesCompanySchemaV1.cs')
        self.assertEqual(company_schema.count('(SELECT COUNT(*) FROM [SystemCompanies]) = 1'), 2)
        self.assertEqual(company_schema.count('(SELECT COUNT(*) FROM [dbo].[SystemCompanies]) = 1'), 2)
        middleware = read('Inventory.Api/Infrastructure/ActiveCompanyMiddleware.cs')
        self.assertIn('ctx.Items[ItemKey] = 0', middleware)
        self.assertIn('if (userId <= 0) return Task.FromResult<(int?, string?)>((0, null))', middleware)
        treasury = read('Inventory.Api/Services/Treasury/TreasuryService.cs')
        for token in ('VisibleAccounts()', 'a.CompanyId == null && _db.SystemCompanies.Count() <= 1',
                      'await TreasuryBalanceCalculator.CalculateAsync(_db, new[] { trsAccountId })',
                      'SaleChequeReceipts.IgnoreQueryFilters().AsNoTracking()',
                      'SaleChequeInstallmentReceipts.IgnoreQueryFilters().AsNoTracking()',
                      'EnsureNotShopManagedVoucherAsync'):
            self.assertIn(token, treasury)
        calc = read('Inventory.Api/Services/Treasury/TreasuryBalanceCalculator.cs')
        for token in ('!Matches(owner, r.CompanyId)', 'r.CompanyId != r.SaleCompany',
                      'r.ChequeTransaction != r.TransactionId', 'r.InstallmentTransaction != r.TransactionId',
                      'myReceipts.Where(r => r.SaleType == TransactionType.Sale)',
                      'myReceipts.Where(r => r.SaleType == TransactionType.Purchase',
                      'myInstallments.Where(r => r.SaleType == TransactionType.Sale)',
                      'myInstallments.Where(r => r.SaleType == TransactionType.Purchase'):
            self.assertIn(token, calc)

    def test_summary_and_detail_preserve_cash_before_cancellation(self):
        source = read('Inventory.Api/Services/Catalog/InventoryService.cs')
        summary = source[source.index('public async Task<PartyStatementReportDto> GetPartyStatementSummaryReportAsync'):
                         source.index('public async Task<PartyStatementDetailDto> GetPartyStatementDetailAsync')]
        detail = source[source.index('public async Task<PartyStatementDetailDto> GetPartyStatementDetailAsync'):]
        self.assertIn('chequeHistory.ContainsKey(t.Id) ? t.CashAmount :', summary)
        self.assertIn('saleHistory is not null ? t.CashAmount :', detail)
        self.assertIn('activeOrdinaryCheques.GetValueOrDefault(t.Id)', detail)
        integrity = read('Inventory.Api/Services/Invoicing/SaleChequeShopLedgerIntegrity.cs')
        for token in ('allowUnpostedLegacy', 'events.Sum(e => e.IncomeDelta) != charge.Amount - charge.CustomerCredit',
                      'Matches(r.SaleChequeChargeEventId', 'MatchesPayment', 'InitialCashTrsVoucherId'):
            self.assertIn(token, integrity)


if __name__ == '__main__':
    unittest.main()
