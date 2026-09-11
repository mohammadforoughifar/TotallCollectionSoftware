namespace Inventory.Shared;

/// <summary>نوع طرف حساب (مشتری / تأمین‌کننده)</summary>
public enum PartyType
{
    Customer = 0,
    Supplier = 1
}

/// <summary>نوع سند انبار</summary>
public enum TransactionType
{
    /// <summary>موجودی اول دوره</summary>
    Initial = 0,

    /// <summary>رسید خرید</summary>
    Purchase = 1,

    /// <summary>حواله فروش</summary>
    Sale = 2,

    /// <summary>اصلاح / شمارش موجودی</summary>
    Adjustment = 3
}

/// <summary>وضعیت پذیرش تعمیر</summary>
public enum RepairStatus
{
    /// <summary>پذیرش شده (وارد مجموعه شد)</summary>
    Received = 0,

    /// <summary>در حال تعمیر</summary>
    InProgress = 1,

    /// <summary>آماده تحویل</summary>
    Ready = 2,

    /// <summary>تحویل شده (خارج شد)</summary>
    Delivered = 3,

    /// <summary>انصراف / مرجوع بدون تعمیر</summary>
    Cancelled = 4
}

/// <summary>روش پرداخت سند فروش</summary>
public enum PaymentMethod
{
    /// <summary>نقدی (نقد / کارت‌خوان / کارت به کارت)</summary>
    Cash = 0,

    /// <summary>نسیه با تاریخ سررسید</summary>
    Credit = 1,

    /// <summary>چک</summary>
    Cheque = 2,

    /// <summary>اقساطی با دفترچه اقساط</summary>
    Installment = 3
}

/// <summary>نوع دریافت نقدی</summary>
public enum CashType
{
    /// <summary>وجه نقد</summary>
    Cash = 0,

    /// <summary>کارت‌خوان (POS)</summary>
    CardReader = 1,

    /// <summary>کارت به کارت</summary>
    CardTransfer = 2
}

// =====================================================================
// ماژول انبارداری (انبار، رسید و حواله، کاردکس)
// =====================================================================

/// <summary>روش قیمت‌گذاری (ارزیابی) خروج کالا از انبار</summary>
public enum ValuationMethod
{
    /// <summary>میانگین موزون</summary>
    Average = 0,

    /// <summary>اولین صادره از اولین وارده (FIFO)</summary>
    Fifo = 1,

    /// <summary>اولین صادره از آخرین وارده (LIFO)</summary>
    Lifo = 2
}

/// <summary>ماهیت نوع رسید/حواله نسبت به موجودی انبار</summary>
public enum StockNature
{
    /// <summary>افزایشی — رسید (ورود کالا به انبار)</summary>
    Increase = 0,

    /// <summary>کاهشی — حواله (خروج کالا از انبار)</summary>
    Decrease = 1,

    /// <summary>خنثی — بدون تاثیر بر مانده (انتقال بین انبار یا سند یادداشتی)</summary>
    Neutral = 2
}

/// <summary>وضعیت سند انبار</summary>
public enum InvDocStatus
{
    /// <summary>پیش‌نویس — بدون تاثیر بر موجودی</summary>
    Draft = 0,

    /// <summary>قطعی شده — روی موجودی و کاردکس اثر گذاشته است</summary>
    Confirmed = 1,

    /// <summary>ابطال شده</summary>
    Cancelled = 2
}

/// <summary>نوع مقدار ویژگی کالا</summary>
public enum AttrValueType
{
    /// <summary>متنی</summary>
    Text = 0,

    /// <summary>عددی</summary>
    Number = 1,

    /// <summary>بله / خیر</summary>
    Boolean = 2,

    /// <summary>تاریخ</summary>
    Date = 3,

    /// <summary>انتخاب از فهرست</summary>
    List = 4
}

/// <summary>نوع (ماهیت) انبار</summary>
public enum WarehouseKind
{
    /// <summary>انبار اصلی</summary>
    Main = 0,

    /// <summary>انبار فرعی</summary>
    Sub = 1,

    /// <summary>انبار امانی</summary>
    Consignment = 2,

    /// <summary>انبار ضایعات</summary>
    Scrap = 3,

    /// <summary>انبار تولید / خط تولید</summary>
    Production = 4,

    /// <summary>انبار در راه</summary>
    Transit = 5
}

// =====================================================================
// ماژول حسابداری
// =====================================================================

/// <summary>سطح حساب در کدینگ (درخت حساب‌ها)</summary>
public enum AccountLevel
{
    /// <summary>گروه حساب (سطح ۱)</summary>
    Group = 0,

    /// <summary>حساب کل (سطح ۲)</summary>
    General = 1,

    /// <summary>حساب معین (سطح ۳)</summary>
    Subsidiary = 2,

    /// <summary>حساب تفصیلی (سطح ۴)</summary>
    Detail = 3
}

/// <summary>نوع حساب در صورت‌های مالی</summary>
public enum AccountType
{
    /// <summary>دارایی</summary>
    Asset = 0,

    /// <summary>بدهی</summary>
    Liability = 1,

    /// <summary>سرمایه (حقوق صاحبان سهام)</summary>
    Equity = 2,

    /// <summary>درآمد</summary>
    Income = 3,

    /// <summary>هزینه</summary>
    Expense = 4
}

/// <summary>ماهیت حساب</summary>
public enum AccountNature
{
    /// <summary>بدهکار</summary>
    Debit = 0,

    /// <summary>بستانکار</summary>
    Credit = 1,

    /// <summary>دوطرفه</summary>
    Both = 2
}

/// <summary>وضعیت سند حسابداری</summary>
public enum VoucherStatus
{
    /// <summary>پیش‌نویس — در دفاتر منظور نمی‌شود</summary>
    Draft = 0,

    /// <summary>قطعی — در دفاتر و تراز اثر دارد</summary>
    Confirmed = 1,

    /// <summary>ابطال شده</summary>
    Cancelled = 2
}

/// <summary>منشأ صدور سند حسابداری</summary>
public enum VoucherSource
{
    /// <summary>ثبت دستی</summary>
    Manual = 0,

    /// <summary>خودکار از سند انبار (رسید/حواله)</summary>
    InventoryDoc = 1,

    /// <summary>خودکار از فاکتور خرید/فروش</summary>
    Invoice = 2,

    /// <summary>سند افتتاحیه</summary>
    Opening = 3,

    /// <summary>سند اختتامیه</summary>
    Closing = 4,

    /// <summary>خودکار از حقوق و دستمزد</summary>
    Payroll = 5
}

// ===================== ابعاد تحلیلی، دارایی ثابت، بودجه =====================

/// <summary>نوع بُعد تحلیلی (مرکز هزینه / شعبه — پروژه از ماژول پروژه‌ها است)</summary>
public enum AccDimensionType
{
    /// <summary>مرکز هزینه</summary>
    CostCenter = 0,

    /// <summary>شعبه / واحد سازمانی</summary>
    Branch = 1
}

/// <summary>وضعیت دارایی ثابت</summary>
public enum FixedAssetStatus
{
    /// <summary>در حال بهره‌برداری</summary>
    Active = 0,

    /// <summary>متوقف / انبارش</summary>
    Idle = 1,

    /// <summary>فروخته شده</summary>
    Sold = 2,

    /// <summary>امحاء / اسقاط شده</summary>
    Disposed = 3
}

/// <summary>روش استهلاک دارایی ثابت</summary>
public enum DepreciationMethod
{
    /// <summary>مستقیم (خط مستقیم)</summary>
    StraightLine = 0,

    /// <summary>نزولی (مانده نزولی)</summary>
    DecliningBalance = 1
}

/// <summary>نوع رویداد بودجه</summary>
public enum BudgetTransactionType
{
    /// <summary>مصرف (از سند قطعی حسابداری)</summary>
    Actual = 0,

    /// <summary>تعهد (سفارش/قرارداد)</summary>
    Commitment = 1,

    /// <summary>آزادسازی تعهد</summary>
    Release = 2
}

// ===================== سامانه مودیان (فاکتور الکترونیکی) =====================

/// <summary>نوع فاکتور الکترونیکی (InvoicePattern سامانه مودیان)</summary>
public enum MoadianInvoiceKind
{
    /// <summary>فروش</summary>
    Sale = 1,

    /// <summary>فروش برگشتی</summary>
    SaleReturn = 2,

    /// <summary>خرید</summary>
    Purchase = 3,

    /// <summary>خرید برگشتی</summary>
    PurchaseReturn = 4,

    /// <summary>پیش‌فاکتور</summary>
    Proforma = 5
}

/// <summary>وضعیت فاکتور الکترونیکی در چرخه‌ی مودیان</summary>
public enum MoadianInvoiceStatus
{
    /// <summary>پیش‌نویس — هنوز در صف ارسال نیست</summary>
    Draft = 0,

    /// <summary>در صف ارسال به سامانه</summary>
    Queued = 1,

    /// <summary>در حال ارسال</summary>
    Sending = 2,

    /// <summary>به سامانه ارسال شد (در انتظار استعلام/تایید)</summary>
    Sent = 3,

    /// <summary>ارسال ناموفق — با پیام خطای سامانه</summary>
    Failed = 4,

    /// <summary>برگشت از سامانه (مرجع/خطای فنی) — نیاز به اصلاح و ارسال مجدد</summary>
    Returned = 5,

    /// <summary>ابطال شده</summary>
    Voided = 6
}

/// <summary>نوع رویداد/لاگ مودیان</summary>
public enum MoadianLogAction
{
    /// <summary>ساخته شدن فاکتور</summary>
    Created = 0,

    /// <summary>قرار گرفتن در صف ارسال</summary>
    Enqueued = 1,

    /// <summary>ارسال به سامانه</summary>
    Sent = 2,

    /// <summary>خطا در ارسال</summary>
    Failed = 3,

    /// <summary>برگشت از سامانه (مرجع)</summary>
    Returned = 4,

    /// <summary>ابطال / اصلاح</summary>
    Voided = 5
}

// ===================== ماژول فاکتور =====================

/// <summary>نوع فاکتور</summary>
public enum InvoiceKind
{
    /// <summary>فاکتور خرید — کالا وارد انبار می‌شود</summary>
    Purchase = 0,

    /// <summary>فاکتور فروش — کالا از انبار خارج می‌شود</summary>
    Sale = 1,

    /// <summary>برگشت از خرید — کالا از انبار خارج می‌شود</summary>
    PurchaseReturn = 2,

    /// <summary>برگشت از فروش — کالا وارد انبار می‌شود</summary>
    SaleReturn = 3
}

/// <summary>وضعیت فاکتور</summary>
public enum InvoiceStatus
{
    /// <summary>پیش‌نویس — روی انبار و دفاتر اثری ندارد</summary>
    Draft = 0,

    /// <summary>قطعی — سند انبار و سند حسابداری صادر شده است</summary>
    Confirmed = 1,

    /// <summary>ابطال شده</summary>
    Cancelled = 2
}

/// <summary>نحوه تسویه فاکتور</summary>
public enum SettlementType
{
    /// <summary>نسیه — بدهی/طلب روی حساب طرف حساب می‌نشیند</summary>
    Credit = 0,

    /// <summary>نقدی — مبلغ مستقیم به صندوق/بانک منظور می‌شود</summary>
    Cash = 1
}

// =====================================================================
// ماژول خزانه‌داری
// =====================================================================

/// <summary>نوع حساب خزانه (صندوق، بانک، کارتخوان، تنخواه)</summary>
public enum TreasuryAccountKind
{
    /// <summary>صندوق نقدی</summary>
    Cash = 0,

    /// <summary>حساب بانکی</summary>
    Bank = 1,

    /// <summary>دستگاه کارتخوان (POS)</summary>
    Pos = 2,

    /// <summary>تنخواه‌گردان</summary>
    PettyCash = 3
}

/// <summary>نوع سند خزانه</summary>
public enum TreasuryKind
{
    /// <summary>دریافت — پول یا چک وارد خزانه می‌شود</summary>
    Receipt = 0,

    /// <summary>پرداخت — پول یا چک از خزانه خارج می‌شود</summary>
    Payment = 1,

    /// <summary>انتقال بین حساب‌های خزانه</summary>
    Transfer = 2
}

/// <summary>وضعیت سند خزانه</summary>
public enum TreasuryStatus
{
    /// <summary>پیش‌نویس — روی موجودی و دفاتر اثری ندارد</summary>
    Draft = 0,

    /// <summary>قطعی — سند حسابداری صادر شده است</summary>
    Confirmed = 1,

    /// <summary>ابطال شده</summary>
    Cancelled = 2
}

/// <summary>ابزار دریافت/پرداخت در هر سطر سند خزانه</summary>
public enum PayMethod
{
    /// <summary>نقد — صندوق</summary>
    Cash = 0,

    /// <summary>کارتخوان / کارت به کارت</summary>
    Card = 1,

    /// <summary>حواله و انتقال بانکی (پایا، ساتنا، اینترنتی)</summary>
    Transfer = 2,

    /// <summary>چک</summary>
    Cheque = 3,

    /// <summary>تخفیف و کسورات نقدی</summary>
    Discount = 4
}

/// <summary>نوع چک: دریافتی از مشتری یا صادرشده توسط ما</summary>
public enum ChequeKind
{
    /// <summary>چک دریافتی (اسناد دریافتنی)</summary>
    Received = 0,

    /// <summary>چک پرداختی / صادره (اسناد پرداختنی)</summary>
    Issued = 1
}

/// <summary>وضعیت (چرخه عمر) چک</summary>
public enum ChequeStatus
{
    /// <summary>نزد ما — چک دریافتی در صندوق / چک صادره تحویل طرف حساب شده</summary>
    InHand = 0,

    /// <summary>واگذار شده به بانک — در جریان وصول</summary>
    InCollection = 1,

    /// <summary>وصول شده / پاس شده</summary>
    Cleared = 2,

    /// <summary>برگشت خورده</summary>
    Bounced = 3,

    /// <summary>خرج شده — به شخص دیگری واگذار شده است</summary>
    Endorsed = 4,

    /// <summary>ابطال شده</summary>
    Cancelled = 5
}

// =====================================================================
// ماژول انبارگردانی و بارکد
// =====================================================================

/// <summary>وضعیت دوره انبارگردانی</summary>
public enum StocktakeStatus
{
    /// <summary>پیش‌نویس — هنوز لیست شمارش ساخته نشده</summary>
    Draft = 0,

    /// <summary>در حال شمارش — موجودی سیستم قفل (اسنپ‌شات) شده است</summary>
    Counting = 1,

    /// <summary>بررسی مغایرت — شمارش تمام شده، منتظر تأیید</summary>
    Review = 2,

    /// <summary>اعمال شده — اسناد اصلاح موجودی صادر و قطعی شده‌اند</summary>
    Applied = 3,

    /// <summary>لغو شده</summary>
    Cancelled = 4
}

/// <summary>دامنه‌ی کالاهای دوره انبارگردانی</summary>
public enum StocktakeScope
{
    /// <summary>فقط کالاهایی که در این انبار موجودی دارند</summary>
    InStock = 0,

    /// <summary>همه کالاهای فعال (حتی با موجودی صفر)</summary>
    AllProducts = 1,

    /// <summary>فقط کالاهای یک گروه کالا</summary>
    ByCategory = 2,

    /// <summary>فقط کالاهایی که در حین شمارش اسکن می‌شوند</summary>
    ScanOnly = 3
}

/// <summary>استاندارد بارکد</summary>
public enum BarcodeType
{
    /// <summary>Code 128 — طول متغیر، حروف و رقم</summary>
    Code128 = 0,

    /// <summary>EAN-13 — سیزده رقم با رقم کنترلی</summary>
    Ean13 = 1,

    /// <summary>EAN-8 — هشت رقم</summary>
    Ean8 = 2,

    /// <summary>کد داخلی سازمان (بدون استاندارد جهانی)</summary>
    Internal = 3
}
