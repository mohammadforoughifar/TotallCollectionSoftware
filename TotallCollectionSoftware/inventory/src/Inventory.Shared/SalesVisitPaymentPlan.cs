namespace Inventory.Shared;

/// <summary>
/// «نوع پرداخت ترکیبی» سفارش ویزیتور — یک قاعدهٔ مشترک بین کلاینت و سرور.
/// <para>
/// دقیقاً همان سه جزء فاکتور خرید و فروش: <b>نقد + چک + اقساط + نسیه (باقی‌مانده)</b>.
/// ویزیتور در فرم ثبت سفارش روش را انتخاب می‌کند، مالی در کارتابل مالی همین اجزا را
/// می‌بیند/تأیید می‌کند و هنگام ثبت فروش، سرور روش مؤثر را از اجزای واقعی می‌سازد
/// (<see cref="PaymentMix.Derive"/>).
/// </para>
/// </summary>
public static class SalesVisitPaymentPlan
{
    /// <summary>عنوان فارسی جزء پرداخت سفارش ویزیتور.</summary>
    public static string PartTitle(VisPaymentPart part) => part switch
    {
        VisPaymentPart.Cash => "نقدی",
        VisPaymentPart.Cheque => "چک",
        VisPaymentPart.Installment => "اقساطی",
        VisPaymentPart.Credit => "نسیه",
        _ => "—"
    };

    /// <summary>
    /// روش پرداخت اعلامی سفارش. اگر بیش از یک جزء مبلغ داشته باشد، «ترکیبی» است؛
    /// نبودِ هیچ مبلغی (سفارش بدون مبلغ) هم بر اساس جزء فعال تعیین می‌شود.
    /// </summary>
    public static PaymentMethod MethodOf(bool useCash, bool useCheque, bool useInstallment, bool useCredit)
    {
        var parts = (useCash ? 1 : 0) + (useCheque ? 1 : 0) + (useInstallment ? 1 : 0) + (useCredit ? 1 : 0);
        if (parts > 1) return PaymentMethod.Mixed;
        if (useCheque) return PaymentMethod.Cheque;
        if (useInstallment) return PaymentMethod.Installment;
        if (useCredit) return PaymentMethod.Credit;
        return PaymentMethod.Cash;
    }

    /// <summary>روش مؤثر از مبلغ اجزای واقعی (برای بازخوانی ذخیره‌شده در فرم و نمایش فهرست).</summary>
    public static PaymentMethod MethodOfAmounts(decimal cash, decimal chequeSum, decimal installmentSum, decimal creditRemainder)
        => PaymentMix.Derive(cash, chequeSum, installmentSum, creditRemainder);

    /// <summary>آیا اجزای ثبت‌شده بیش از یک جزء مبلغ دارند؟ (نمایش «ترکیبی» در فهرست‌ها)</summary>
    public static bool IsMixed(decimal cash, decimal chequeSum, decimal installmentSum, decimal creditRemainder)
        => MethodOfAmounts(cash, chequeSum, installmentSum, creditRemainder) == PaymentMethod.Mixed;

    /// <summary>خلاصهٔ یک‌خطی پرداخت سفارش برای فهرست‌ها و گردش سفارش.</summary>
    public static string Describe(PaymentMethod method, decimal cash, decimal chequeSum, decimal installmentSum, decimal creditRemainder)
        => PaymentMix.Describe(method, cash, chequeSum, installmentSum, creditRemainder);

    /// <summary>
    /// اعتبارسنجی اجزا پیش از ثبت سفارش: جمع پرداخت‌ها (نقد + چک + اقساط) نمی‌تواند
    /// از مبلغ سفارش بیشتر شود. باقی‌ماندهٔ مثبت همان «نسیه» است.
    /// </summary>
    public static decimal RemainingAfterPayments(decimal total, decimal cash, decimal chequeSum, decimal installmentSum)
        => total - cash - chequeSum - installmentSum;

    /// <summary>
    /// آیا این روش پرداخت باید چک داشته باشد؟ (مالی نمی‌تواند اجزای چک ویزیتور را حذف کند)
    /// </summary>
    public static bool RequiresCheque(bool useCheque) => useCheque;

    /// <summary>
    /// آیا سفارش با نوع پرداخت چکی، اجزای چک کامل دارد؟ خالی = ایراد ندارد.
    /// </summary>
    public static string? ValidateCheques(bool useCheque, IReadOnlyList<ChequeDto>? cheques)
    {
        if (!useCheque) return null;
        if (cheques is null || cheques.Count == 0)
            return "نوع پرداخت چکی است؛ حداقل یک چک با شماره، مبلغ و سررسید ثبت کنید.";
        for (var i = 0; i < cheques.Count; i++)
        {
            var c = cheques[i];
            if (string.IsNullOrWhiteSpace(c.Number))
                return $"شماره چک ردیف {i + 1} را وارد کنید.";
            if (c.Amount <= 0)
                return $"مبلغ چک ردیف {i + 1} را وارد کنید.";
            if (c.DueDate == default)
                return $"سررسید چک ردیف {i + 1} را وارد کنید.";
        }
        return null;
    }

    /// <summary>
    /// آیا سفارش با نوع پرداخت اقساطی، دفترچهٔ اقساط کامل دارد؟ خالی = ایراد ندارد.
    /// </summary>
    public static string? ValidateInstallments(bool useInstallment, IReadOnlyList<InstallmentDto>? installments)
    {
        if (!useInstallment) return null;
        if (installments is null || installments.Count == 0)
            return "نوع پرداخت اقساطی است؛ دفترچهٔ اقساط را بسازید.";
        for (var i = 0; i < installments.Count; i++)
        {
            var inst = installments[i];
            if (inst.Amount <= 0)
                return $"مبلغ قسط {i + 1} را وارد کنید.";
            if (inst.DueDate == default)
                return $"سررسید قسط {i + 1} را وارد کنید.";
        }
        return null;
    }

    /// <summary>
    /// قاعدهٔ مشترک «ماندهٔ نهایی»: اگر جمع پرداخت‌ها کمتر از مبلغ سفارش باشد و جزء
    /// نسیه فعال نباشد، ثبت متوقف می‌شود. خالی = ایراد ندارد.
    /// </summary>
    public static string? ValidateRemainder(decimal total, decimal cash, decimal chequeSum, decimal installmentSum,
        bool useCredit, DateTime? creditDueDate, bool exact = false)
    {
        if (chequeSum + installmentSum + cash > total + PaymentMix.Tolerance)
            return "جمع پرداخت‌ها از مبلغ سفارش بیشتر است؛ مبلغ اجزا را اصلاح کنید.";
        var remaining = RemainingAfterPayments(total, cash, chequeSum, installmentSum);
        var threshold = exact ? 0m : PaymentMix.Tolerance;
        if (remaining > threshold && !useCredit)
            return $"جمع پرداخت‌ها ({Fa.Money(total - remaining)}) کمتر از مبلغ سفارش ({Fa.Money(total)}) است؛ " +
                   "باقی‌ماندهٔ " + Fa.Money(remaining) + " ریال به نسیه می‌رود — جزء «نسیه» را فعال و تاریخ سررسید را وارد کنید.";
        if (remaining > threshold && useCredit && !creditDueDate.HasValue)
            return "تاریخ سررسید نسیه را وارد کنید.";
        return null;
    }

    /// <summary>
    /// گزارش کوتاه اجزای پرداخت سفارش برای گردش سفارش و کارتابل مالی.
    /// </summary>
    public static string DescribeParts(decimal cash, decimal chequeSum, decimal installmentSum, decimal creditRemainder)
    {
        var parts = new List<string>();
        if (cash > PaymentMix.Tolerance / 2) parts.Add($"نقد {Fa.Money(cash)}");
        if (chequeSum > PaymentMix.Tolerance / 2) parts.Add($"چک {Fa.Money(chequeSum)}");
        if (installmentSum > PaymentMix.Tolerance / 2) parts.Add($"اقساط {Fa.Money(installmentSum)}");
        if (creditRemainder > PaymentMix.Tolerance / 2) parts.Add($"نسیه {Fa.Money(creditRemainder)}");
        return parts.Count == 0 ? "بدون جزء پرداخت" : string.Join(" + ", parts);
    }
}

/// <summary>جزء پرداخت سفارش ویزیتور — برای انتخاب/نمایش در فرم و مالی.</summary>
public enum VisPaymentPart
{
    Cash = 0,
    Cheque = 1,
    Installment = 2,
    Credit = 3
}
