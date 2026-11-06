"""Arithmetic mirrors and source contracts. The .NET probe executes the C# helper."""
import random
import unittest
from decimal import Decimal, ROUND_DOWN
from pathlib import Path

D = Decimal
UNIT = D('10000')
MAX_AMOUNT = D('9999999999999999.99')
ROOT = Path(__file__).resolve().parents[2] / 'src'


def read(path):
    return (ROOT / path).read_text(encoding='utf-8-sig')


def distribute(total, count):
    """Independent Decimal mirror for invariant/property testing, not C# execution."""
    if total <= 0 or total > MAX_AMOUNT or total.quantize(D('.01')) != total:
        raise ValueError('invalid total')
    if count < 1 or count > 60:
        raise ValueError('invalid count')
    whole_units = (total / UNIT).quantize(D('1'), rounding=ROUND_DOWN)
    base_units = (whole_units / count).quantize(D('1'), rounding=ROUND_DOWN)
    extra_units = int(whole_units % count)
    residual = total - whole_units * UNIT
    amounts = [base_units * UNIT] * count
    upper_end = count - 1 if residual > 0 else count
    for i in range(upper_end - extra_units, upper_end):
        amounts[i] += UNIT
    amounts[-1] += residual
    if any(a <= 0 for a in amounts):
        raise ValueError('no positive distribution is possible')
    return amounts


class RoundingArithmetic(unittest.TestCase):
    def assert_invariants(self, total, count):
        amounts = distribute(total, count)
        self.assertEqual(len(amounts), count)
        self.assertEqual(sum(amounts), total)
        self.assertTrue(all(amount > 0 for amount in amounts))
        self.assertTrue(all(amount % UNIT == 0 for amount in amounts[:-1]))
        self.assertEqual(amounts[-1] % UNIT, total % UNIT)
        self.assertLessEqual(max(amounts) - min(amounts), UNIT)

    def test_even_distribution_and_multiple_rounded_values(self):
        self.assertEqual(distribute(D('1000000'), 3), [D(330000), D(330000), D(340000)])
        self.assertEqual(distribute(D('1030000'), 4), [D(250000), D(260000), D(260000), D(260000)])
        self.assertEqual(distribute(D('600000'), 60), [D(10000)] * 60)

    def test_exact_remainder_only_in_last_row(self):
        amounts = distribute(D('1000001.23'), 3)
        self.assertEqual(amounts, [D(330000), D(340000), D('330001.23')])
        self.assertEqual(sum(amounts), D('1000001.23'))
        self.assertEqual(distribute(D('1030001'), 4), [D(260000), D(260000), D(260000), D(250001)])
        self.assertEqual(distribute(D('50000.01'), 3), [D(20000), D(20000), D('10000.01')])

    def test_single_and_smallest_feasible_total(self):
        self.assertEqual(distribute(D('.51'), 1), [D('.51')])
        self.assertEqual(distribute(D('20000.01'), 3), [D(10000), D(10000), D('.01')])
        with self.assertRaises(ValueError):
            distribute(D('20000'), 3)
        with self.assertRaises(ValueError):
            distribute(D('19999.99'), 3)

    def test_invalid_precision_range_and_count(self):
        for total, count in ((D(0), 1), (D(-1), 1), (D('100000.001'), 2),
                             (D('10000000000000000'), 1), (D(10000), 0), (D(10000), 61)):
            with self.subTest(total=total, count=count), self.assertRaises(ValueError):
                distribute(total, count)

    def test_small_value_feasibility_boundaries(self):
        for count in range(1, 61):
            for whole_units in range(count + 2):
                for residual in (D(0), D('.01'), D(1), D('9999.99')):
                    total = whole_units * UNIT + residual
                    possible = total > 0 and whole_units >= count - (1 if residual > 0 else 0)
                    if possible:
                        self.assert_invariants(total, count)
                    else:
                        with self.assertRaises(ValueError):
                            distribute(total, count)

    def test_randomized_exactness_and_fair_distribution(self):
        rng = random.Random(29041)
        for count in range(1, 61):
            for _ in range(150):
                whole_rials = rng.randrange(count * int(UNIT), 10_000_000_000)
                total = D(whole_rials) + D(rng.randrange(100)) / 100
                self.assert_invariants(total, count)
        self.assert_invariants(MAX_AMOUNT, 60)


class RoundingIntegrationContracts(unittest.TestCase):
    def test_csharp_algorithm_has_fixed_unit_exact_remainder_and_positive_guard(self):
        helper = read('Inventory.Shared/RoundedInstallmentDistribution.cs')
        for term in ('RoundingUnit = 10_000m', 'MaximumCount = 60', 'decimal.Round(total, 2) != total',
                     'decimal.Floor(total / RoundingUnit)', 'wholeUnits % count',
                     'total - wholeUnits * RoundingUnit', 'amounts[count - 1] += remainder',
                     'amounts.Any(amount => amount <= 0m)'):
            self.assertIn(term, helper)
        self.assertNotIn('Math.Round(total', helper)

    def test_both_calculator_modes_use_shared_rows_without_changing_interest(self):
        calc = read('Inventory.Shared/InvoiceInstallmentCalculation.cs')
        self.assertIn('SaleChequeInitialCalculation.Calculate(baseAmount, monthlyRatePercent, months)', calc)
        self.assertIn('RoundedInstallmentDistribution.Split(total, months)', calc)
        self.assertIn('public decimal[] Amounts => RoundedInstallmentDistribution.Split(Total, Months)', calc)
        form = read('Inventory.Client/Pages/Orders/OrderForm.razor')
        apply = form[form.index('private void ApplyCalc()'):form.index('private List<ChequeDto> cheques')]
        self.assertEqual(apply.count('var amount = rowAmounts[i];'), 2)
        self.assertLess(apply.index('calculation.Amounts'), apply.index('cheques = newCheques'))
        self.assertLess(apply.index('calculation.Amounts'), apply.index('installments = newInstallments'))
        self.assertIn('saleChequeIncome = calculatedCharge is null ? 0m : calculation.Interest', apply)
        self.assertIn('saleChequeCharge = calculatedCharge', apply)

    def test_booklet_does_not_replace_existing_rows_on_invalid_amount_or_date(self):
        form = read('Inventory.Client/Pages/Orders/OrderForm.razor')
        build = form[form.index('private void BuildInstallments('):form.index('private void RemoveInstallment(')]
        replacement = build.index('installments = newInstallments')
        self.assertLess(build.index('totalDue <= PaymentMix.Tolerance / 2m'), replacement)
        self.assertLess(build.index('RoundedInstallmentDistribution.Split(totalDue, instCount)'), replacement)
        self.assertLess(build.index('instFirstDue.Value.AddDays'), replacement)
        self.assertLess(build.index('catch (ArgumentOutOfRangeException)'), replacement)
        self.assertNotIn('installments.Clear()', build)
        self.assertNotIn('instAmounts.Clear()', build)
        self.assertNotIn('instDates.Clear()', build)
        self.assertIn('Fa.FormatTyping(ExactDecimal(amounts[i]))', build)

    def test_manual_payment_edits_and_past_cheques_are_not_auto_redistributed(self):
        form = read('Inventory.Client/Pages/Orders/OrderForm.razor')
        manual = form[form.index('private void OnChequeAmountInput('):form.index('// ---------------- فعال/غیرفعال کردن')]
        self.assertIn('var value = Fa.FormatTyping(e.Value?.ToString());', manual)
        self.assertIn('if (value != chequeAmounts[i]) calculatedChequeSchedule = null;', manual)
        self.assertIn('chequeAmounts[i] = value;', manual)
        self.assertIn('instAmounts[i] = Fa.FormatTyping', manual)
        self.assertNotIn('RoundedInstallmentDistribution', manual)
        pay = form[form.index('private void GoToPayment()'):form.index('private void BackToInvoice()')]
        self.assertIn('!hasKnownHistory', pay)
        self.assertIn('if (!refreshed)', pay)
        self.assertIn('chequeAmounts[i] = ""', pay.split('if (!refreshed)', 1)[1])
        self.assertIn('KnownLaterChequeActivity', form)
        self.assertIn('ردیف‌های چک فعلی جایگزین می‌شوند', form)

    def test_preview_and_downstream_displays_show_exact_amounts(self):
        form = read('Inventory.Client/Pages/Orders/OrderForm.razor')
        for text in ('CalculatorAmountRange(rowAmounts)', 'rowAmounts.GroupBy(amount => amount)',
                     'rowAmounts[^1]', 'RoundingUnit != 0m', 'ExactMoney(InstSum())',
                     'private bool ExactPaymentDisplay', 'grandTotal % 1m != 0m', 'CashPart % 1m != 0m',
                     'FinalRemaining < 0 ? "مازاد پرداخت"', 'اختلاف در محدوده تلورانس'):
            self.assertIn(text, form)
        management = read('Inventory.Client/Pages/Shop/ShopCreditManagement.razor')
        for text in ('c.Amount.MoneyExact()', 'i.Amount.MoneyExact()',
                     'receiptTarget.Amount.MoneyExact()', 'instReceiptTarget.Amount.MoneyExact()'):
            self.assertIn(text, management)
        detail = read('Inventory.Client/Pages/Orders/Orders.razor')
        self.assertIn('UseExactAmounts(viewing) ? viewing.TotalAmount.MoneyExact()', detail)
        self.assertIn('order.TotalAmount % 1m != 0m', detail)
        printed = read('Inventory.Client/Pages/Orders/SaleInvoicePrint.cs')
        self.assertIn('var exactPayments = order.HasChequeCharge || order.Cheques.Count > 0 ||', printed)
        self.assertIn('order.Installments.Count > 0 || order.TotalAmount % 1m != 0m', printed)
        self.assertIn('exactPayments ? order.TotalAmount.MoneyExact()', printed)


if __name__ == '__main__':
    unittest.main()
