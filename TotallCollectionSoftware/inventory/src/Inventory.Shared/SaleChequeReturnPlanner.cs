using System.Numerics;

namespace Inventory.Shared;

/// <summary>
/// محاسبهٔ غیرعملیاتی پیش‌نمایش برگشت؛ این کلاس سند، موجودی یا چک را تغییر نمی‌دهد.
/// مقدارها باید از ردیف اصلی و تاریخچهٔ معتبر برگشت در سرور خوانده شوند.
/// </summary>
public static class SaleChequeReturnPlanner
{
    public readonly record struct Line(decimal GoodsBeforeVat, decimal Vat)
    {
        public decimal BaseWithVat => GoodsBeforeVat + Vat;
    }

    public readonly record struct Preview(decimal GoodsBeforeVat, decimal Vat, decimal Income,
        decimal BalanceBefore, decimal BalanceAfter, decimal UncollectedChequeExcess)
    {
        public decimal TotalCredit => GoodsBeforeVat + Vat + Income;
    }

    private const decimal MaxMoney = 9999999999999999.99m;

    /// <summary>
    /// سهم این نوبت از یک ردیف: تفاضل دو سهم تجمعی گرد شده. آخرین برگشت
    /// دقیقاً کل مبالغ اصلی را تکمیل می‌کند و برگشت‌های پیاپی اختلاف گردکردن ندارند.
    /// </summary>
    public static Line PlanLine(decimal originalQuantity, decimal previouslyReturnedQuantity,
        decimal quantity, decimal originalGoodsBeforeVat, decimal originalVat)
    {
        if (originalQuantity <= 0 || decimal.Round(originalQuantity, 2) != originalQuantity ||
            previouslyReturnedQuantity < 0 || decimal.Round(previouslyReturnedQuantity, 2) != previouslyReturnedQuantity ||
            quantity <= 0 || decimal.Round(quantity, 2) != quantity ||
            previouslyReturnedQuantity > originalQuantity || quantity > originalQuantity - previouslyReturnedQuantity)
            throw new ArgumentOutOfRangeException(nameof(quantity), "مقدار برگشتی از مانده ردیف بیشتر یا نامعتبر است.");
        ValidateMoney(originalGoodsBeforeVat, nameof(originalGoodsBeforeVat));
        ValidateMoney(originalVat, nameof(originalVat));
        var end = previouslyReturnedQuantity + quantity;
        return new Line(
            Allocated(originalGoodsBeforeVat, end, originalQuantity) - Allocated(originalGoodsBeforeVat, previouslyReturnedQuantity, originalQuantity),
            Allocated(originalVat, end, originalQuantity) - Allocated(originalVat, previouslyReturnedQuantity, originalQuantity));
    }

    /// <summary>
    /// مانده مثبت یعنی بدهی مشتری، منفی یعنی طلب مشتری. چک وصول‌شده فقط در settledAmount
    /// حضور دارد؛ مبلغ آن بار دوم کسر نمی‌شود. مازاد چک صرفاً هشدار است، نه دستور تغییر آن.
    /// برای برگشت‌های متوالی، snapshot دورهٔ پس از آخرین تعدیل سررسید و مبلغ برگشتی قبلی الزامی است؛
    /// حذف snapshot فقط برای نخستین برگشت آن دوره مجاز است و صحت آن را فراخواننده بررسی می‌کند.
    /// </summary>
    public static Preview Plan(decimal remainingBase, decimal remainingIncome, decimal existingCustomerCredit,
        decimal settledAmount, decimal uncollectedCheques, IEnumerable<Line> lines,
        decimal? epochBase = null, decimal? epochIncome = null, decimal previouslyReturnedBaseInEpoch = 0)
    {
        ValidateMoney(remainingBase, nameof(remainingBase));
        ValidateMoney(remainingIncome, nameof(remainingIncome));
        ValidateMoney(existingCustomerCredit, nameof(existingCustomerCredit));
        ValidateMoney(settledAmount, nameof(settledAmount));
        ValidateMoney(uncollectedCheques, nameof(uncollectedCheques));
        if (remainingBase == 0 || (remainingIncome > 0 && existingCustomerCredit > 0))
            throw new ArgumentException("مبنای مانده یا وضعیت سود و بستانکاری نامعتبر است.");
        ArgumentNullException.ThrowIfNull(lines);
        var items = lines.ToList();
        if (items.Count == 0) throw new ArgumentException("حداقل یک سطر برگشتی لازم است.", nameof(lines));
        foreach (var line in items)
        {
            ValidateMoney(line.GoodsBeforeVat, nameof(line.GoodsBeforeVat));
            ValidateMoney(line.Vat, nameof(line.Vat));
        }
        var goods = items.Sum(x => x.GoodsBeforeVat);
        var vat = items.Sum(x => x.Vat);
        var returnedBase = goods + vat;
        if (returnedBase > remainingBase)
            throw new ArgumentException("جمع برگشت از ماندهٔ مبنای کالا بیشتر است.", nameof(lines));
        ValidateMoney(returnedBase, nameof(lines));
        // هر تغییر سررسید یک دورهٔ جدید ایجاد می‌کند؛ بین دو تغییر، سهم‌ها تجمعی گرد می‌شوند.
        if (epochBase.HasValue != epochIncome.HasValue || (!epochBase.HasValue && previouslyReturnedBaseInEpoch != 0))
            throw new ArgumentException("مبنای دورهٔ برگشت ناقص است.");
        var baseAtEpoch = epochBase ?? remainingBase;
        var incomeAtEpoch = epochIncome ?? remainingIncome;
        ValidateMoney(baseAtEpoch, nameof(epochBase));
        ValidateMoney(incomeAtEpoch, nameof(epochIncome));
        ValidateMoney(previouslyReturnedBaseInEpoch, nameof(previouslyReturnedBaseInEpoch));
        if (baseAtEpoch <= 0 || baseAtEpoch - previouslyReturnedBaseInEpoch != remainingBase)
            throw new ArgumentException("ماندهٔ پایه با تاریخچهٔ دورهٔ برگشت مطابقت ندارد.");
        var priorIncome = SaleChequeChargeMath.ProportionalReturn(baseAtEpoch, incomeAtEpoch, 0, previouslyReturnedBaseInEpoch);
        if (incomeAtEpoch - priorIncome != remainingIncome)
            throw new ArgumentException("ماندهٔ سود با تاریخچهٔ دورهٔ برگشت مطابقت ندارد.");
        var income = SaleChequeChargeMath.ProportionalReturn(baseAtEpoch, incomeAtEpoch, previouslyReturnedBaseInEpoch, returnedBase);
        ValidateMoney(returnedBase + income, nameof(lines));
        var before = remainingBase + remainingIncome - existingCustomerCredit - settledAmount;
        var after = before - returnedBase - income;
        if (decimal.Abs(before) > MaxMoney || decimal.Abs(after) > MaxMoney)
            throw new ArgumentOutOfRangeException(nameof(lines), "مانده از محدودهٔ حسابداری بیشتر است.");
        var excess = Math.Max(0, uncollectedCheques - Math.Max(0, after));
        return new Preview(goods, vat, income, before, after, excess);
    }

    private static void ValidateMoney(decimal value, string name)
    {
        if (value < 0 || value > MaxMoney || decimal.Round(value, 2) != value)
            throw new ArgumentOutOfRangeException(name, "مبلغ باید غیرمنفی و با حداکثر دو رقم اعشار باشد.");
    }

    private static decimal Allocated(decimal amount, decimal part, decimal total)
    {
        static (BigInteger Integer, int Scale) Parts(decimal value)
        {
            var bits = decimal.GetBits(value);
            var n = ((BigInteger)(uint)bits[2] << 64) | ((BigInteger)(uint)bits[1] << 32) | (uint)bits[0];
            return (n, (bits[3] >> 16) & 0xff);
        }
        var (a, aScale) = Parts(amount);
        var (p, pScale) = Parts(part);
        var (t, tScale) = Parts(total);
        var numerator = a * p * BigInteger.Pow(10, tScale + 2);
        var denominator = t * BigInteger.Pow(10, aScale + pScale);
        var cents = BigInteger.DivRem(numerator, denominator, out var remainder);
        var cmp = (remainder * 2).CompareTo(denominator);
        if (cmp > 0 || (cmp == 0 && !cents.IsEven)) cents++;
        return (decimal)cents / 100m;
    }
}
