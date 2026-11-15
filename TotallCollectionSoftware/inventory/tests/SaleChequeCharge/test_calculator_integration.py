"""Arithmetic mirrors and source contracts; run Program.cs for actual .NET/EF execution."""
import unittest
from decimal import Decimal, ROUND_DOWN, ROUND_HALF_EVEN
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / 'src'
D = Decimal


def split(basis, rate, months):
    """Mirror monthly interest and the exact 10,000-rial distribution."""
    if months < 1 or months > 60:
        raise ValueError('invalid number of rows')
    interest = (basis * rate / 100 * months).quantize(D('1'), rounding=ROUND_HALF_EVEN)
    total = basis + interest
    if total <= D('.5'):
        raise ValueError('below the payment classifier threshold')
    unit = D('10000')
    whole_units = (total / unit).quantize(D('1'), rounding=ROUND_DOWN)
    base_units = (whole_units / months).quantize(D('1'), rounding=ROUND_DOWN)
    extra_units = int(whole_units % months)
    remainder = total - whole_units * unit
    amounts = [base_units * unit for _ in range(months)]
    upper_end = months - 1 if remainder > 0 else months
    for i in range(upper_end - extra_units, upper_end):
        amounts[i] += unit
    amounts[-1] += remainder
    if any(amount <= 0 for amount in amounts):
        raise ValueError('insufficient amount for positive rounded rows')
    return interest, amounts


def read(path):
    return (ROOT / path).read_text(encoding='utf8')


class CalculatorIntegration(unittest.TestCase):
    def test_example_and_collection_are_balanced(self):
        interest, cheques = split(D(2000000), D(5), 5)
        self.assertEqual((interest, cheques), (D(500000), [D(500000)] * 5))
        # Before actual collection, handed-over cheques are still receivable.
        self.assertEqual(D(2000000) + interest, D(2500000))
        self.assertEqual(D(2000000) + interest - sum(cheques), D(0))

    def test_mixed_payments_and_recalculation_exclude_previous_interest(self):
        goods, cash, installments = D(2000000), D(500000), D(200000)
        basis = goods - cash - installments
        interest, cheques = split(basis, D(5), 2)
        self.assertEqual((basis, interest, cheques),
                         (D(1300000), D(130000), [D(710000), D(720000)]))
        self.assertEqual(cash + installments + sum(cheques), goods + interest)
        # A second invocation uses principal 1,300,000, not the interest-bearing total.
        self.assertEqual(split(goods - cash - installments, D(5), 2), (interest, cheques))

    def test_zero_rate_and_fractional_amounts(self):
        interest, cheques = split(D('50000.01'), D(0), 3)
        self.assertEqual(interest, 0)
        self.assertEqual(cheques, [D(20000), D(20000), D('10000.01')])
        self.assertEqual(sum(cheques), D('50000.01'))
        self.assertEqual(split(D('.51'), D(0), 1)[1], [D('.51')])
        with self.assertRaises(ValueError):
            split(D('.50'), D(0), 1)
        with self.assertRaises(ValueError):
            split(D('19999'), D(0), 3)
        # The server rounds a half-rial to even; no positive event may be created.
        self.assertEqual(split(D(100), D('.5'), 1)[0], 0)

    def test_balanced_rounding_and_last_row_remainder(self):
        self.assertEqual(split(D('1000000'), D(0), 3)[1],
                         [D('330000'), D('330000'), D('340000')])
        amounts = split(D('1000001.23'), D(0), 3)[1]
        self.assertEqual(amounts, [D('330000'), D('340000'), D('330001.23')])
        self.assertEqual(sum(amounts), D('1000001.23'))
        self.assertTrue(all(amount % D(10000) == 0 for amount in amounts[:-1]))

    def test_shared_helper_controls_terms_and_zero_event(self):
        calc = read('Inventory.Shared/InvoiceInstallmentCalculation.cs')
        for term in ('SaleChequeInitialCalculation.Calculate(baseAmount, monthlyRatePercent, months)',
                     'RoundedInstallmentDistribution.Split(total, months)', 'public decimal[] Amounts',
                     'total <= PaymentMix.Tolerance / 2m', 'if (Interest == 0) return null',
                     'CalculatorBaseAmount = BaseAmount', 'CalculatorRatePercent = MonthlyRatePercent',
                     'CalculatorMonths = Months', 'AdjustmentRatePercent = adjustmentRatePercent', 'SaleChequeVoucherAmounts.Create(goodsTotal, 0m, Interest)'):
            self.assertIn(term, calc)
        registration = read('Inventory.Api/Services/Invoicing/SaleChequeChargeRegistration.cs')
        self.assertIn('SaleChequeInitialCalculation.Calculate(input.CalculatorBaseAmount', registration)
        self.assertIn('if (input.CalculatorBaseAmount > baseAmount || income <= 0)', registration)

    def test_ui_validates_before_replacing_rows_and_retains_fractional_output(self):
        form = read('Inventory.Client/Pages/Orders/OrderForm.razor')
        apply = form[form.index('private void ApplyCalc()'):form.index('private List<ChequeDto> cheques')]
        replace = apply.index('cheques = newCheques')
        for guard in ('KnownLaterChequeActivity', 'calculation.CreateSaleChequeCharge(goodsTotal',
                      'calculatedCharge is not null', 'calcStart.Value.Date.AddDays',
                      'calculation.Amounts', 'rowAmounts[i]', 'Fa.FormatTyping(ExactDecimal(amount))'):
            self.assertLess(apply.index(guard), replace)
        self.assertIn('saleChequeCharge = calculatedCharge', apply)
        self.assertIn('saleChequeIncome = calculatedCharge is null ? 0m : calculation.Interest', apply)
        self.assertIn('goodsTotal - CashPart - InstSum()', form)
        self.assertIn('chargeCashBase == CashPart && chargeInstallmentBase == InstSum()', form)
        self.assertIn('chequeAmounts = o.Cheques.Select(c => Fa.FormatTyping(ExactDecimal(c.Amount)))', form)
        self.assertIn('(saleChequeCharge is not null || removedCalculatedChequeIncome) && remainder < 0', form)
        self.assertIn('useCheque && (saleChequeCharge is not null ||', form)
        self.assertIn('جزئیات مبنا و نرخ اولیه در سابقه این فاکتور موجود نیست', form)
        self.assertIn('saleChequeIncome = o.ChequeIncomeAmount;', form)
        self.assertIn('IsEdit && EditOrder?.HasChequeCharge == true && saleChequeCharge is null', form)
        self.assertIn('ردیف‌های چک فعلی جایگزین می‌شوند', form)
        self.assertIn('پیش‌فرض: نرخ ماهانه، هر ۳۰ روز', form)
        self.assertIn('!TryCalculatorPercent(calcRateText', form)
        for field in ('calcMonths', 'calcIntervalDays', 'calcAdjustmentDays'):
            self.assertIn(f'@bind="{field}" @bind:event="oninput"', form)
        formatting = read('Inventory.Shared/PersianText.cs')
        self.assertIn('.Replace("٫", ".")', formatting)
        self.assertIn('public static string MoneyExact(decimal amount)', formatting)

    def test_reports_explain_invoice_and_do_not_guess_legacy_terms(self):
        svc = read('Inventory.Api/Services/Catalog/InventoryService.cs')
        desc = svc[svc.index('private static string DescribeChequeIncome'):svc.index('private sealed record ShopCashStatementEvent')]
        self.assertIn('فاکتور {saleNumber}', desc)
        self.assertIn('جدا از کالا و بدون ارزش افزوده', desc)
        self.assertIn('history?.CalculatorBaseAmount is decimal amount', desc)
        self.assertIn('history.CalculatorRatePercent is decimal rate', desc)
        self.assertIn('history.CalculatorMonths is int months', desc)
        self.assertIn('Description = DescribeChequeIncome(saleHistory, t.Number)', svc)
        self.assertIn('data-sale-cheque-income-detail', read('Inventory.Client/Pages/Orders/Orders.razor'))
        self.assertIn('viewing.GoodsAmount.MoneyExact()', read('Inventory.Client/Pages/Orders/Orders.razor'))
        printed = read('Inventory.Client/Pages/Orders/SaleInvoicePrint.cs')
        self.assertIn('تاخیر تادیه چک (بدون ارزش افزوده)', printed)
        self.assertIn('order.GoodsAmount.MoneyExact()', printed)
        self.assertIn('order.TotalAmount.MoneyExact()', printed)
        self.assertIn('نه صرفاً هنگام تحویل چک', read('Inventory.Client/Pages/Operations/PartyStatementsReport.razor'))
        self.assertIn('بستانکاری درآمد فروشگاه', read('Inventory.Client/Pages/Operations/ChequeIncomeReport.razor'))


if __name__ == '__main__':
    unittest.main()
