namespace Inventory.Api.Data;

/// <summary>
/// سود (هزینهٔ مالی) چک‌های پرداختی در سند <b>خرید</b>.
/// <para>
/// در خرید، پرداخت چک به تأمین‌کننده درآمد ما نیست؛ سود فقط هزینهٔ مالی است و
/// مبلغ فاکتور (ارزش کالا و ارزش افزوده) را بالا نمی‌برد. چون گزارش خرید باید
/// تفکیک «مبلغ کالا» و «سود چک» را نشان دهد، این مبلغ در ردیف مستقل خودش
/// نگه‌داری می‌شود و در <c>Transactions.Amount</c> جمع نمی‌شود.
/// </para>
/// <para>
/// تعهد کل پرداخت به تأمین‌کننده = <c>Transactions.Amount + Amount</c>؛ همان
/// قاعده‌ای که اجزای چک/نقد/اقساط با آن مقایسه می‌شوند.
/// </para>
/// نبود ردیف برای فاکتورهای قدیمی یعنی صفر.
/// </summary>
public class PurchaseChequeInterest : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public int TransactionId { get; set; }
    public Transaction Transaction { get; set; } = null!;

    /// <summary>مبنای محاسبه (مبلغ کالا و ارزش افزوده) به ریال.</summary>
    public decimal BaseAmount { get; set; }
    /// <summary>درصد سود ماهانهٔ واردشده در ماشین‌حساب.</summary>
    public decimal RatePercent { get; set; }
    /// <summary>تعداد ماه‌های محاسبه.</summary>
    public int Months { get; set; }
    /// <summary>ورودی‌های ماشین‌حساب برای بارگذاری دوباره در فرم ویرایش.</summary>
    public decimal CalculatorBaseAmount { get; set; }
    public decimal CalculatorRatePercent { get; set; }
    public int CalculatorMonths { get; set; }
    /// <summary>سود محاسبه‌شده؛ تنها مبلغ مالی این ردیف.</summary>
    public decimal Amount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string? CreatedBy { get; set; }
}
