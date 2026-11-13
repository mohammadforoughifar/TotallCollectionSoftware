"""Static checks for purchase and sale; not a substitute for the Blazor build/browser tests."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
FORM = (ROOT / 'src/Inventory.Client/Pages/Orders/OrderForm.razor').read_text(encoding='utf-8')
CSS = (ROOT / 'src/Inventory.Client/Pages/Orders/OrderForm.razor.css').read_text(encoding='utf-8')
MODAL = (ROOT / 'src/Inventory.Client/Shared/Modal.razor').read_text(encoding='utf-8')
SEARCH = (ROOT / 'src/Inventory.Client/Shared/SearchSelect.razor').read_text(encoding='utf-8')
INDEX = (ROOT / 'src/Inventory.Client/wwwroot/index.html').read_text(encoding='utf-8')
PARTY = (ROOT / 'src/Inventory.Client/Shared/PartyFormModal.razor').read_text(encoding='utf-8')
BANK = (ROOT / 'src/Inventory.Client/Shared/QuickBankModal.razor').read_text(encoding='utf-8')


def method(name, following):
    return FORM.split(name, 1)[1].split(following, 1)[0]


class PurchaseWizardContract(unittest.TestCase):
    def test_purchase_and_sales_use_same_step_gate(self):
        self.assertIn('Type is TransactionType.Purchase or TransactionType.Sale', FORM)
        self.assertIn('!UseInvoiceWizard || invoiceStep == 1', FORM)
        self.assertIn('UseInvoiceWizard && invoiceStep == 2', FORM)
        self.assertIn('private int invoiceStep = 1;', FORM)
        self.assertIn('if (!saving && ValidateInvoice())', FORM)
        self.assertIn('invoiceStep = 2;', FORM)
        self.assertIn('if (!saving)', FORM)
        self.assertIn('invoiceStep = 1;', FORM)

    def test_save_and_print_share_validation_and_cannot_skip_payment(self):
        self.assertIn('private Task Save() => SaveInternal(printAfterSave: false);', FORM)
        self.assertIn('private void SaveAndPrint()', FORM)
        self.assertIn('await SaveInternal(printAfterSave: true);', FORM)
        save = method('private async Task SaveInternal(bool printAfterSave)', 'var cmd = new OrderCommand')
        self.assertIn('UseInvoiceWizard && invoiceStep != 2', save)
        self.assertIn('!ValidateInvoice()', save)
        self.assertIn('invoiceStep = 1', save)
        validation = method('private bool ValidateInvoice()', 'private async Task SaveInternal')
        for expression in ('warehouseId == 0', 'partyId == 0', '!date.HasValue', 'validLines.Count == 0', 'decimal.Round(l.Qty, 2'):
            self.assertIn(expression, validation)

    def test_payment_selection_and_sums(self):
        for component in ('cash', 'cheque', 'installment', 'credit'):
            self.assertIn(f'data-pay-comp="{component}"', FORM)
        for callback, following in (('ToggleCheque()', 'private void ToggleInstallment()'),
                                    ('ToggleInstallment()', 'private void ToggleCredit()'),
                                    ('ToggleCredit()', 'private void EnsureInvoicePaymentMethod()')):
            body = method('private void ' + callback, following)
            self.assertIn('EnsureInvoicePaymentMethod();', body)
        self.assertIn('UseInvoiceWizard && !useCash', FORM)
        self.assertIn('UseInvoiceWizard && !useCash && !HasNonCash', FORM)
        self.assertIn('lines.Where(l => l.ProductId > 0 && l.Qty > 0)', FORM)
        self.assertIn('private decimal goodsTotal => TotalLines.Sum(l => l.Total);', FORM)
        self.assertIn('private decimal grandTotal => goodsTotal + saleChequeIncome;', FORM)
        for value in ('grandTotal.Money()', 'CashPart.Money()', 'ChequeSum().Money()', 'InstSum().Money()', 'Math.Abs(FinalRemaining).Money()'):
            self.assertIn(value, FORM.split('class="purchase-summary"', 1)[1].split('</section>', 1)[0])

    def test_sale_features_are_preserved_in_first_step(self):
        self.assertIn('Type == TransactionType.Sale ? "اطلاعات فروش" : "اطلاعات خرید"', FORM)
        self.assertIn('Type == TransactionType.Sale ? "مشتری" : "تأمین‌کننده"', FORM)
        self.assertIn('@onclick="BackToInvoice" disabled="@saving"><i class="bi bi-arrow-right"></i> بازگشت (F3)', FORM)
        self.assertIn('Type == TransactionType.Sale && invoiceStep == 1 && CanSeeProfit', FORM)
        self.assertIn('data-label="سود"', FORM)
        self.assertIn('data-label="پورسانت"', FORM)
        self.assertIn('CanSeeProfit && referrerId > 0', FORM)
        self.assertIn('CanSeeProfit', FORM)
        self.assertIn('LineProfit(l)', FORM)
        self.assertIn('LineCommission(l)', FORM)
        self.assertIn('ShowLastPurchase(l.ProductId)', FORM)
        self.assertIn('ReferrerId = Type == TransactionType.Sale && referrerId > 0 ? referrerId : null', FORM)
        self.assertIn('جمع مقدار اقلام', FORM)
        self.assertIn('purchase-items sale-items', FORM)
        self.assertIn('.purchase-items tbody td:first-child', CSS)

    def test_line_keyboard_contract_and_purchase_payment_title(self):
        self.assertIn('data-order-line="@l.Key"', FORM)
        self.assertIn('data-order-last-field', FORM)
        self.assertIn('AdvanceOnEnter="UseInvoiceWizard"', FORM)
        self.assertIn('pendingLineFocus = line.Key;', FORM)
        self.assertIn('JS.InvokeVoidAsync("orderEntry.focusLine", formKey, key.ToString())', FORM)
        self.assertIn('if (lines.Count == 0) AddLine();', FORM)
        self.assertIn('tabindex="-1" title="حذف سطر"', FORM)
        self.assertIn('title="آخرین فاکتور خرید این کالا (F8)"', FORM)
        self.assertIn('data-order-add-line', FORM)
        self.assertIn('data-order-add-line data-hotkey="f2"', FORM)
        self.assertIn('Type == TransactionType.Purchase ? "اعلامیه پرداخت" : "اقساطی"', FORM)
        self.assertIn('data-order-initial', FORM)
        self.assertIn('pendingSectionFocus = "[data-order-payment]"', FORM)
        self.assertIn('pendingSectionFocus = "[data-pay-cheque-box]"', FORM)
        self.assertIn('pendingSectionFocus = "[data-pay-inst-box]"', FORM)
        self.assertIn('pendingSectionFocus = "[data-pay-credit-box]"', FORM)
        self.assertIn('data-order-calculator="@formKey"', FORM)
        self.assertIn('InvoiceFormKey="@(UseInvoiceWizard ? formKey : null)"', FORM)
        self.assertIn('data-order-child="@InvoiceFormKey"', PARTY)
        self.assertIn('data-order-child="@InvoiceFormKey"', BANK)
        self.assertIn('Shift+Enter', PARTY)
        self.assertIn('<SearchSelect Items="whLookup" ShowAllOnFocus="UseInvoiceWizard"', FORM)
        self.assertIn('<SearchSelect Items="partyLookup" ShowAllOnFocus="UseInvoiceWizard"', FORM)
        self.assertIn('if (AdvanceOnEnter)', SEARCH)
        self.assertIn('await JS.InvokeVoidAsync("orderEntry.focusNextField", _input)', SEARCH)
        self.assertLess(INDEX.index('js/order-entry.js?v='), INDEX.index("document.addEventListener('keydown'"))

    def test_invoice_row_controls_and_values_share_alignment(self):
        items_section = FORM.split('<div data-order-entry', 1)[1]
        thead = items_section.split('<thead>', 1)[1].split('</thead>', 1)[0]
        row = items_section.split('<tr @key="l.Key" data-order-line="@l.Key">', 1)[1].split('</tr>', 1)[0]
        # bootstrap.min.css is LTR (.text-start = left), which breaks RTL header/cell alignment against flex values.
        self.assertNotIn('text-start', thead)
        self.assertNotIn('text-start', row)
        self.assertIn('CssClass="order-line-control"', row)
        self.assertEqual(row.count('order-line-control'), 6)  # sixth is purchase-only optional sale price
        self.assertIn('Type == TransactionType.Purchase && canManagePricing', row)
        self.assertEqual(row.count('order-line-value'), 5)
        self.assertIn('class="order-line-actions"', row)
        desktop_css = CSS.split('/* Desktop:', 1)[1].split('@media (max-width: 991px)', 1)[0]
        self.assertIn('table-layout: fixed', desktop_css)
        self.assertIn('vertical-align: top', desktop_css)
        self.assertIn('text-align: right', desktop_css)
        self.assertNotIn('display: grid', desktop_css)
        self.assertIn('@media (max-width: 991px)', CSS)
        self.assertIn('InvoiceColumnWidth(22, 20)', FORM)
        self.assertIn('InvoiceActionsColumnWidth', FORM)
        self.assertNotIn('overflow-x: auto', CSS)
        self.assertNotIn('min-width: 1120px', CSS)
        self.assertIn('height: 38px; min-height: 38px;', CSS)
        self.assertIn('min-height: 2.6em;', CSS)
        self.assertIn('.purchase-items ::deep .order-line-control', CSS)
        self.assertEqual(row.count('input-group input-group-sm order-line-rate'), 2)
        self.assertIn('display: block; position: relative; width: 100%; max-width: 5.25rem; }', CSS)
        self.assertIn('position: absolute; z-index: 1; top: 1px; bottom: 1px; left: 1px;', CSS)
        self.assertIn('padding-left: 36px;', CSS)
        self.assertEqual(row.count('input-group-text order-line-unit'), 2)
        self.assertNotIn('l.DiscountPercent.ToString', row)
        self.assertIn('padding-inline-end: 1rem;', CSS)
        self.assertIn('.purchase-items table thead th {', CSS)
        self.assertIn('overflow-wrap: normal; word-break: normal;', CSS)

    def test_sale_column_widths_cover_all_optional_column_combinations(self):
        items = FORM.split('<div data-order-entry', 1)[1]
        colgroup = items.split('<colgroup>', 1)[1].split('</colgroup>', 1)[0]
        thead = items.split('<thead>', 1)[1].split('</thead>', 1)[0]
        row = items.split('<tr @key="l.Key" data-order-line="@l.Key">', 1)[1].split('</tr>', 1)[0]
        shared_cols = colgroup.split('@if (Type == TransactionType.Sale && CanSeeProfit)', 1)[0]
        shared_cols = re.sub(r'@if \(Type == TransactionType.Purchase && canManagePricing\) \{ <col[^\n]+\}', '', shared_cols)
        weights = re.findall(r'InvoiceColumnWidth\((\d+)(?:,\s*(\d+))?\)', shared_cols)
        self.assertEqual(len(weights), 8)
        purchase_weights = [int(purchase) for purchase, _ in weights]
        sale_weights = [int(sale or purchase) for purchase, sale in weights]
        base = int(re.search(r'SaleBaseColumnWeight = (\d+)', FORM).group(1))
        extra = int(re.search(r'SaleExtraColumnWeight = (\d+)', FORM).group(1))
        self.assertEqual(sum(purchase_weights) + 5, 100)
        self.assertIn('canManagePricing ? 111m : 100m', FORM)
        self.assertIn('InvoiceColumnWidth(11)', colgroup)
        self.assertEqual(sum(sale_weights), base)
        self.assertEqual(colgroup.count('InvoiceColumnWidth(SaleExtraColumnWeight)'), 2)
        self.assertIn('<col style="width:@InvoiceActionsColumnWidth" />', colgroup)
        self.assertIn('SaleActionsColumnRem = 11m', FORM)
        self.assertIn('SaleBaseColumnWeight + SaleExtraColumnWeight * extraColumns', FORM)
        self.assertIn('(CanSeeProfit && referrerId > 0 ? 1 : 0)', FORM)
        self.assertIn('100m * ratio:0.######', FORM)
        self.assertIn('SaleActionsColumnRem * ratio:0.######', FORM)
        self.assertIn('if (Type != TransactionType.Sale)', FORM)
        self.assertIn(': InvoiceColumnWidth(5)', FORM)

        for condition, label in (('CanSeeProfit', 'سود'), ('CanSeeProfit && referrerId > 0', 'پورسانت')):
            self.assertIn(f'@if (Type == TransactionType.Sale && {condition})', colgroup)
            guard = re.escape(f'@if ({condition})')
            self.assertRegex(thead, guard + r'\s*\{\s*<th><span>' + label + r'</span>')
            self.assertRegex(row, guard + r'\s*\{\s*<td data-label="' + label + '"')
        for profit, commission in ((0, 0), (1, 0), (1, 1)):
            denominator = base + extra * (profit + commission)
            visible = sale_weights + [extra] * (profit + commission)
            self.assertEqual(sum(visible), denominator)
            self.assertAlmostEqual(sum(100 * w / denominator for w in visible), 100)
            # Percentage columns reserve their proportional share of the action width.
            for width in (992, 1280, 1568):
                available = width - 11 * 16
                self.assertAlmostEqual(sum(available * w / denominator for w in visible) + 11 * 16, width)

        desktop_sale = CSS.split('@media (min-width: 992px)', 1)[1].split('}', 1)[0]
        self.assertIn('.sale-items table thead th, .sale-items tbody td', desktop_sale)
        self.assertIn('padding-inline: .3rem', desktop_sale)
        self.assertIn('.sale-items tbody .order-line-rate > input.form-control { padding-left: 24px;', CSS)
        self.assertIn('.sale-items tbody .order-line-rate > .order-line-unit { width: 20px;', CSS)
        self.assertRegex(INDEX, r'Inventory\.Client\.styles\.css\?v=(?:9|[1-9][0-9]+)')

    def test_invoice_totals_show_after_discount_and_after_vat(self):
        totals = FORM.split('<div class="purchase-totals">', 1)[1].split('</div>\n                </div>', 1)[0]
        self.assertIn('مبلغ پس از تخفیف', totals)
        self.assertIn('TotalLines.Sum(l => l.AfterDiscount).MoneyExact()', totals)
        self.assertIn('مبلغ نهایی پس از ارزش افزوده', totals)
        self.assertIn('@ExactMoney(goodsTotal)', totals)
        self.assertIn('TotalLines.Sum(l => l.VatAmount).MoneyExact()', totals)

    def test_bank_and_focus_contract(self):
        self.assertIn('OpenOnFocus="UseInvoiceWizard"', FORM)
        self.assertIn('ConstrainToModal="UseInvoiceWizard"', FORM)
        self.assertIn('OpenChequeBank(i)', FORM)
        self.assertIn('new TrsAccount { Kind = TreasuryAccountKind.Bank, Name = name, BankName = name', FORM)
        self.assertIn('customChequeBanks.Append(name)', FORM)
        self.assertIn('showChequeBank = false', FORM)
        for key in ('(F1)', '(F2)', '(F3)', '(F4)', '(Esc)'):
            self.assertIn(key, FORM)
        self.assertNotIn('Alt+P', FORM)
        self.assertNotIn('Alt+U', FORM)
        payment_choices = FORM.split('<div class="pay-tabs"', 1)[1].split('</div>', 1)[0]
        for key in ('F4', 'F6', 'F7', 'F8', 'Alt+'):
            self.assertNotIn(key, payment_choices)
        self.assertNotIn('data-order-shortcut="new-cheque-bank"', FORM)
        self.assertNotIn('data-order-shortcut="last-purchase"', FORM)

    def test_responsive_and_header_scope(self):
        self.assertIn('@media (max-width: 600px)', CSS)
        self.assertIn('.purchase-items tbody tr', CSS)
        self.assertIn('.purchase-payment .pay-tabs', CSS)
        self.assertIn('HeaderClass="@(UseInvoiceWizard', FORM)
        self.assertIn('[Parameter] public string HeaderClass', MODAL)
        self.assertIn('@HeaderClass', MODAL)


if __name__ == '__main__':
    unittest.main()
