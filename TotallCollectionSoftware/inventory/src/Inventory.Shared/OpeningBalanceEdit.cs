namespace Inventory.Shared;

/// <summary>چهار بخش ماژول افتتاحیه؛ سند هر بخش فرم ویرایش خودش را دارد.</summary>
public enum OpeningBalanceSection
{
    /// <summary>سند افتتاحیه نیست.</summary>
    None = 0,
    /// <summary>طلب/بدهی طرف حساب ([OPENING:PARTY]).</summary>
    Party = 1,
    /// <summary>چک اول دوره (دفتر خزانه، سند خرید/فروش نیست).</summary>
    Cheque = 2,
    /// <summary>دفترچهٔ اقساط ([OPENING:INSTALLMENT]).</summary>
    Installment = 3,
    /// <summary>موجودی اولیهٔ کالا ([OPENING:STOCK]).</summary>
    Stock = 4
}

/// <summary>
/// قواعد مشترک «ویرایش مانده‌های اول دوره» برای هر چهار بخش ماژول
/// «افتتاح حساب و ثبت مانده‌های اولیه»: طرف حساب، چک، اقساط و موجودی کالا.
///
/// دو قاعدهٔ قطعی که کلاینت و سرور هر دو از همین فایل می‌خوانند:
/// <list type="number">
/// <item>سند افتتاحیه فقط تا وقتی گردش تسویه/وصول/پرداخت قسط ندارد ویرایش می‌شود؛
/// پس از آن دکمهٔ ویرایش غیرفعال است و سرور هم تغییر را رد می‌کند.</item>
/// <item>ماهیت مانده (بدهکار/بستانکار) و نوع چک (دریافتی/پرداختی) پس از ثبت
/// قابل تغییر نیست؛ چون یعنی تبدیل سند فروش به خرید که سابقه را دگرگون می‌کند.</item>
/// </list>
/// </summary>
public static class OpeningBalanceEdit
{
    /// <summary>پیشوند ثبتی هر بخش؛ همان چیزی که سند را «افتتاحیه» می‌کند.</summary>
    public const string PartyPrefix = "[OPENING:PARTY]";

    public const string InstallmentPrefix = "[OPENING:INSTALLMENT]";

    public const string StockPrefix = "[OPENING:STOCK]";

    /// <summary>شرح‌های قدیمی که پیش از افزودن پیشوند ثبت شده‌اند.</summary>
    public const string LegacyInstallmentDescription = "مانده اقساط بدهی باز اول دوره";

    public const string LegacyStockDescription = "موجودی اولیه اول دوره انبار";

    /// <summary>نام و کد دو کالای ساختگی که سند افتتاحیه را از فاکتور واقعی جدا می‌کند.</summary>
    public const string ServiceProductName = "مانده افتتاح حساب (خدمات)";

    public const string GoodsProductName = "مانده بستانکاری افتتاحیه (کالا)";

    public const string ServiceProductCode = "OPN-SVC";

    public const string GoodsProductCode = "OPN-GDS";

    public const string LockedAfterSettlementMessage =
        "این مانده اول دوره گردش تسویه، دریافت یا وصول دارد؛ ویرایش آن غیرفعال است. " +
        "ابتدا گردش را برگشت یا اصلاح کنید.";

    public const string LockedCancelledMessage =
        "این سند افتتاحیه ابطال شده است و ویرایش ندارد.";

    public const string NatureLockedMessage =
        "ماهیت مانده (بدهکار/بستانکار) پس از ثبت قابل تغییر نیست؛ مبلغ، تاریخ و شرح قابل ویرایش‌اند.";

    public const string ChequeKindLockedMessage =
        "نوع چک (دریافتی/پرداختی) پس از ثبت قابل تغییر نیست؛ مبلغ، سررسید و مشخصات قابل ویرایش‌اند.";

    public const string ChequeStatusLockedMessage =
        "ویرایش چک پس از وصول، برگشت، خرج یا ابطال غیرفعال است.";

    public const string NotEditableTitle = "این ردیف قابل ویرایش نیست";

    public const string EditActionTitle = "ویرایش این ردیف";

    /// <summary>وضعیت گردش یک سند افتتاحیه؛ منبع تصمیم «ویرایش آزاد است یا غیرفعال».</summary>
    /// <param name="IsCancelled">سند با سند معکوس ابطال شده است.</param>
    /// <param name="HasCashHistory">گردش نقدی روی سند ثبت شده است.</param>
    /// <param name="HasCreditReceipts">دریافت/پرداخت نسیه روی سند ثبت شده است.</param>
    /// <param name="HasChequeHistory">چکی وصول/خرج/برگشت خورده است.</param>
    /// <param name="HasInstallmentHistory">قسطی پرداخت شده است.</param>
    public readonly record struct SettlementState(
        bool IsCancelled,
        bool HasCashHistory,
        bool HasCreditReceipts,
        bool HasChequeHistory,
        bool HasInstallmentHistory)
    {
        /// <summary>هیچ گردش مالی‌ای روی سند رخ نداده است.</summary>
        public bool IsUntouched =>
            !HasCashHistory && !HasCreditReceipts && !HasChequeHistory && !HasInstallmentHistory;

        /// <summary>علت غیرفعال بودن ویرایش؛ null یعنی ویرایش آزاد است.</summary>
        public string? BlockedReason => IsCancelled ? LockedCancelledMessage
            : !IsUntouched ? LockedAfterSettlementMessage
            : null;
    }

    /// <summary>ویرایش آزاد است؟</summary>
    public static bool CanEdit(SettlementState state) => state.BlockedReason is null;

    /// <summary>علت غیرفعال بودن ویرایش یا null.</summary>
    public static string? BlockedReason(SettlementState state) => state.BlockedReason;

    /// <summary>آیا این شرح به یک سند افتتاحیه تعلق دارد؟ (با شرح‌های قدیمی بدون پیشوند هم می‌خواند)</summary>
    public static bool IsOpeningDocument(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return false;
        return description.StartsWith("[OPENING:", StringComparison.Ordinal) ||
               description.Contains("افتتاح", StringComparison.Ordinal) ||
               description == LegacyInstallmentDescription ||
               description == LegacyStockDescription;
    }

    /// <summary>سند «موجودی اولیه کالا» است؟</summary>
    public static bool IsOpeningStock(string? description) =>
        description is not null &&
        (description.StartsWith(StockPrefix, StringComparison.Ordinal) ||
         description == LegacyStockDescription);

    /// <summary>
    /// بخش سند از روی شرح و اقلام. عیناً همان ترتیبی که فهرست سابقهٔ ماژول ردیف‌ها را
    /// دسته می‌کند، تا تصمیم «این ردیف در کدام تب است» یک‌جا و یکسان گرفته شود.
    /// </summary>
    /// <param name="description">شرح سند.</param>
    /// <param name="paymentMethod">روش پرداخت سند.</param>
    /// <param name="hasOpeningProductLine">سند یکی از دو کالای ساختگی افتتاحیه را دارد.</param>
    public static OpeningBalanceSection SectionOf(string? description, PaymentMethod paymentMethod,
        bool hasOpeningProductLine)
    {
        if (string.IsNullOrWhiteSpace(description) && !hasOpeningProductLine) return OpeningBalanceSection.None;
        if (description is not null && description.StartsWith(InstallmentPrefix, StringComparison.Ordinal))
            return OpeningBalanceSection.Installment;
        if (hasOpeningProductLine && paymentMethod == PaymentMethod.Installment)
            return OpeningBalanceSection.Installment;
        if (description == LegacyInstallmentDescription) return OpeningBalanceSection.Installment;
        if (IsOpeningStock(description)) return OpeningBalanceSection.Stock;
        if (description is not null && description.StartsWith(PartyPrefix, StringComparison.Ordinal))
            return OpeningBalanceSection.Party;
        if (hasOpeningProductLine) return OpeningBalanceSection.Party;
        if (paymentMethod == PaymentMethod.Credit && description is not null &&
            description.Contains("افتتاح", StringComparison.Ordinal))
            return OpeningBalanceSection.Party;
        return IsOpeningDocument(description) ? OpeningBalanceSection.Stock : OpeningBalanceSection.None;
    }

    /// <summary>
    /// همان وضعیت، از خلاصهٔ سند در فهرست سابقه. کلاینت با این متد بدون فراخوانی
    /// اضافه تصمیم می‌گیرد دکمهٔ ویرایش فعال باشد؛ سرور با جدول‌های واقعی گردش
    /// همان قاعده را دوباره بررسی می‌کند و حرف آخر را می‌زند.
    /// </summary>
    /// <param name="isCancelled">سند ابطال شده است.</param>
    /// <param name="description">شرح سند.</param>
    /// <param name="hasCashEntry">گردش نقدی (بهای تمام‌شده یا پیش‌پرداخت) دارد.</param>
    /// <param name="cashAmount">جزء نقدی سند.</param>
    /// <param name="settledAmount">جمع تسویه‌شدهٔ سند.</param>
    /// <param name="chequesNotInHand">تعداد چک‌هایی که دیگر «نزد ما» نیستند.</param>
    /// <param name="anyInstallmentPaid">قسطی پرداخت شده است.</param>
    public static SettlementState FromOrderSummary(bool isCancelled, string? description,
        bool hasCashEntry, decimal cashAmount, decimal settledAmount,
        int chequesNotInHand, bool anyInstallmentPaid)
        => State(isCancelled, description,
            hasCashEntry,
            // هر تسویهٔ اضافه بر نقد، یعنی دریافت نسیه یا وصول: سابقه دارد.
            settledAmount > cashAmount,
            chequesNotInHand > 0,
            anyInstallmentPaid);

    /// <summary>
    /// وضعیت گردش یک سند؛ ورودی خام سرور را به «علت غیرفعال بودن ویرایش» تبدیل می‌کند.
    /// قاعدهٔ «گردش نقدی مانع ویرایش است» برای سند موجودی اولیه اعمال نمی‌شود، چون جزء
    /// نقدی آن همان بهای تمام‌شدهٔ کالاست و تسویه‌ای رخ نداده است.
    /// </summary>
    public static SettlementState State(bool isCancelled, string? description,
        bool hasCashHistory, bool hasCreditReceipts, bool hasChequeHistory, bool hasInstallmentHistory)
        => new(isCancelled,
            hasCashHistory && CashLedgerBlocksEdit(description),
            hasCreditReceipts, hasChequeHistory, hasInstallmentHistory);

    /// <summary>
    /// گردش نقدی فقط برای مانده‌های «طلب/بدهی» و «اقساط» نشانهٔ تسویه است. در سند
    /// موجودی اولیه، جزء نقدی همان بهای تمام‌شدهٔ کالاست و تسویه‌ای رخ نداده است؛
    /// بنابراین نباید ویرایش موجودی اولیه را قفل کند.
    /// </summary>
    public static bool CashLedgerBlocksEdit(string? description) => !IsOpeningStock(description);

    /// <summary>وضعیت چک اجازهٔ ویرایش می‌دهد؟ (همان قاعدهٔ سرور)</summary>
    public static bool ChequeStatusIsEditable(ChequeStatus status) =>
        status is ChequeStatus.InHand or ChequeStatus.InCollection;

    /// <summary>علت غیرفعال بودن ویرایش چک یا null.</summary>
    public static string? ChequeBlockedReason(ChequeStatus status) =>
        ChequeStatusIsEditable(status) ? null : ChequeStatusLockedMessage;

    /// <summary>
    /// متن کاربر از شرح سند: پیشوند ثبتی حذف می‌شود تا در فرم ویرایش دوباره
    /// به متن اضافه نشود و شرح دوبار پیشوند نگیرد.
    /// </summary>
    public static string StripMarker(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return "";
        var text = description.Trim();
        if (!text.StartsWith("[OPENING:", StringComparison.Ordinal)) return text;
        var close = text.IndexOf(']');
        if (close < 0) return text;
        return text[(close + 1)..].TrimStart();
    }

    /// <summary>
    /// شرح کامل سند برای ذخیره: پیشوند بخش + متن کاربر. متن خالی یعنی شرح پیش‌فرض
    /// همان بخش تا سند همچنان به‌عنوان افتتاحیه شناخته شود.
    /// </summary>
    public static string Compose(string prefix, string? text, string fallback)
    {
        var body = string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
        if (body.StartsWith("[OPENING:", StringComparison.Ordinal)) body = StripMarker(body);
        return $"{prefix} {body}";
    }

    /// <summary>
    /// تقسیم مبلغ دفترچهٔ اقساط اول دوره به تعداد اقساط؛ همان قاعدهٔ ثبت اولیه:
    /// هر قسط گرد شده و اختلاف در قسط آخر می‌نشیند تا جمع اقساط دقیقاً برابر مبلغ باشد.
    /// </summary>
    public static decimal[] SplitInstallments(decimal total, int count)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count), "تعداد اقساط باید حداقل ۱ قسط باشد.");
        if (total <= 0m)
            throw new ArgumentOutOfRangeException(nameof(total), "مبلغ اقساط باید بزرگ‌تر از صفر باشد.");
        var each = decimal.Round(total / count, 0, MidpointRounding.AwayFromZero);
        var amounts = new decimal[count];
        for (var i = 0; i < count; i++)
            amounts[i] = i == count - 1 ? total - each * (count - 1) : each;
        return amounts;
    }

    /// <summary>
    /// فاصلهٔ ماهانهٔ اقساط از روی اولین دو سررسید؛ برای بازسازی همان تاریخ‌ها در
    /// فرم ویرایش. مقدار نامعتبر یعنی یک ماه.
    /// </summary>
    public static int DeriveIntervalMonths(DateTime firstDueDate, DateTime secondDueDate)
    {
        var months = (secondDueDate.Year - firstDueDate.Year) * 12 + (secondDueDate.Month - firstDueDate.Month);
        return months is >= 1 and <= 12 ? months : 1;
    }
}
