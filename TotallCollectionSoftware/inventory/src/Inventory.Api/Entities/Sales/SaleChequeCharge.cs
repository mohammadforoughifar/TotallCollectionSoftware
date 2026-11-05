namespace Inventory.Api.Data;

/// <summary>
/// ردیف مالی تاخیر تادیه چک در سند فروش؛ عمداً فاقد ProductId است و گردش انبار ندارد.
/// نبود ردیف برای فاکتورهای قدیمی به معنی مبلغ صفر است.
/// </summary>
public class SaleChequeCharge : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public int TransactionId { get; set; }
    public Transaction Transaction { get; set; } = null!;

    /// <summary>مبنای محاسبه همراه با ارزش افزوده، به ریال.</summary>
    public decimal BaseAmount { get; set; }
    /// <summary>درصد سود برای بازهٔ واردشده.</summary>
    public decimal RatePercent { get; set; }
    public int PeriodDays { get; set; }
    /// <summary>ورودی‌های ماشین‌حساب اولیه؛ مقدار null یعنی اطلاعات تاریخی قابل احراز نیست.</summary>
    public decimal? CalculatorBaseAmount { get; set; }
    public decimal? CalculatorRatePercent { get; set; }
    public int? CalculatorMonths { get; set; }
    /// <summary>حساب‌های سند قطعی در لحظهٔ ثبت؛ null یعنی انتساب تاریخی احراز نشده است.</summary>
    public int? IncomeAccountId { get; set; }
    public int? PartyAccountId { get; set; }
    /// <summary>رسید نقدی اولیه فروشگاه؛ برای پیشگیری از تغییر مستقل و محاسبه مانده صندوق.</summary>
    public int? InitialCashTrsVoucherId { get; set; }
    public TrsVoucher? InitialCashTrsVoucher { get; set; }
    /// <summary>سود اولیهٔ ماشین‌حساب و رقم جاری ردیف مالی.</summary>
    public decimal InitialAmount { get; set; }
    public decimal Amount { get; set; }
    /// <summary>مازاد کاهش سود نسبت به سود اولیه؛ طلب مشتری، نه درآمد منفیِ کالا.</summary>
    public decimal CustomerCredit { get; set; }
}
