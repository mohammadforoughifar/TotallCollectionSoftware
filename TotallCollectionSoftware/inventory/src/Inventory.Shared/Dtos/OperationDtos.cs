using Inventory.Shared;
using System.Text.Json.Serialization;

namespace Inventory.Shared.Dtos;

/// <summary>سطر سند (خرید/فروش)</summary>
public class OrderLine
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public bool DiscountIsAmount { get; set; }
    public decimal? GrossAmount { get; set; }
    public decimal VatValue { get; set; }
    public decimal VatAmount { get; set; }
    public decimal Gross => GrossAmount ?? (Quantity * Price);
    public decimal AfterDiscount => Math.Max(0, Gross - DiscountAmount);
    public decimal Total => AfterDiscount + VatAmount;

    /// <summary>خدمات است؟</summary>
    public bool IsService { get; set; }

    /// <summary>مقدار قبلاً برگشت‌داده‌شده از این سطر فروش.</summary>
    public decimal ReturnedQuantity { get; set; }
    public decimal RemainingQuantity => Math.Max(0, Quantity - ReturnedQuantity);

    /// <summary>بهای تمام‌شدهٔ واحد؛ از snapshot ثبت‌شده یا روش قیمت‌گذاری انبار، پرشده توسط سرور.</summary>
    public decimal? UnitCost { get; set; }

    /// <summary>سود سطر فروش = (قیمت فروش − بهای تمام‌شده) × مقدار — برای کالا و خدمات دارای هزینهٔ ثبت‌شده.</summary>
    public decimal? Profit { get; set; }

    /// <summary>پورسانت معرف این سطر (پر شده توسط سرور — فقط فروش با معرف)</summary>
    public decimal? Commission { get; set; }
}

/// <summary>سند خرید/فروش</summary>
public class Order
{
    public int Id { get; set; }
    public int WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public int PartyId { get; set; }
    public string? PartyName { get; set; }
    public string? PartyPhone { get; set; }
    public string? PartyAddress { get; set; }
    public string? PartyTaxId { get; set; }
    public TransactionType Type { get; set; }
    public string Number { get; set; } = "";
    public DateTime Date { get; set; }
    public string Description { get; set; } = "";
    public List<OrderLine> Lines { get; set; } = new();
    public decimal TotalAmount { get; set; }
    /// <summary>ردیف مستقل درآمد تاخیر تادیه چک؛ فاکتورهای قدیمی صفر دارند.</summary>
    public decimal ChequeIncomeAmount { get; set; }
    /// <summary>آیا ردیف تاخیر تادیه چک روی این سند وجود دارد (حتی اگر مبلغ جاری پس از اصلاح صفر شده باشد).</summary>
    public bool HasChequeCharge { get; set; }
    /// <summary>جمع کالا و مالیات برگشت‌داده‌شده از این فاکتور.</summary>
    public decimal ReturnedGoodsAmount { get; set; }
    /// <summary>جمع درآمد چک کسرشده بابت برگشت کالا.</summary>
    public decimal ReturnedChequeIncomeAmount { get; set; }
    /// <summary>بستانکاری ثبت‌شده مشتری بابت کاهش سود چک.</summary>
    public decimal CustomerCreditAmount { get; set; }
    /// <summary>بستانکاری قابل پرداخت یا قابل مصرف جاری مشتری در این فاکتور.</summary>
    public decimal AvailableCustomerCredit { get; set; }
    /// <summary>آیا فاکتور با سند معکوس ابطال شده است؟</summary>
    public bool IsCancelled { get; set; }
    /// <summary>مقادیر ثبت‌شده ماشین‌حساب و نرخ تعدیل برای بارگذاری در ویرایش فاکتور اولیه.</summary>
    public SaleChequeChargeInput? ChequeChargeSnapshot { get; set; }
    public List<SaleChequeReturnSummaryDto> ChequeReturns { get; set; } = new();
    /// <summary>کالا و خدمات شامل ارزش افزوده، بدون ردیف چک.</summary>
    public decimal GoodsAmount => TotalAmount - ChequeIncomeAmount;

    /// <summary>معرف (بازاریاب) سند فروش</summary>
    public int? ReferrerId { get; set; }
    public string? ReferrerName { get; set; }

    /// <summary>درصدهای ثبت‌شده در زمان فروش؛ برای سند قدیمی نرخ جاری به‌صورت تخمینی نمایش داده می‌شود.</summary>
    public decimal? ReferrerGoodsCommissionPercent { get; set; }
    public decimal? ReferrerServiceCommissionPercent { get; set; }
    public bool CommissionRatesAreHistorical { get; set; }
    /// <summary>در سندهای بدون نرخ تاریخی، مبنای خدمات قدیمی حفظ شده است.</summary>
    public bool UsesLegacyCommissionPolicy { get; set; }
    /// <summary>بهای همه سطرهای کالا در زمان فروش تثبیت نشده است؛ سود و پورسانت برآوردی‌اند.</summary>
    public bool HasEstimatedSaleCost { get; set; }

    /// <summary>مجموع سود سند (فقط فروش — پر شده توسط سرور)</summary>
    public decimal? TotalProfit { get; set; }

    /// <summary>مبلغ پورسانت معرف (پر شده توسط سرور)</summary>
    public decimal? CommissionAmount { get; set; }

    /// <summary>سود خالص = سود کل − پورسانت معرف (پر شده توسط سرور)</summary>
    public decimal? NetProfit { get; set; }

    // ---------- پرداخت (فقط فروش) ----------
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public CashType? CashType { get; set; }
    public string? CashTrackingNumber { get; set; }

    /// <summary>پیش‌دریافت نقدی در پرداخت ترکیبی</summary>
    public decimal CashAmount { get; set; }

    /// <summary>آیا گردش نقدی فاکتور در دفتر فروشگاهی ثبت می‌شود؟</summary>
    public bool ShopCashLedgerEnabled { get; set; }

    /// <summary>شناسه بانک / حساب خزانه انتخابی برای تسویه</summary>
    public int? TrsAccountId { get; set; }
    public string? TrsAccountName { get; set; }

    public DateTime? DueDate { get; set; }
    public decimal SettledAmount { get; set; }
    public List<ChequeDto> Cheques { get; set; } = new();
    public List<InstallmentDto> Installments { get; set; } = new();

    public bool IsPosted { get; set; } = true;
    public bool CanDelete { get; set; } = true;

    public bool IsPurchase => Type == TransactionType.Purchase;
    public bool IsSale => Type == TransactionType.Sale;
}

/// <summary>تطبیق صریح نقد فاکتور قدیمی؛ بدون این تأیید سابقه به‌طور خودکار ساخته نمی‌شود.</summary>
public class ShopCashReconciliationCommand
{
    public Guid OperationId { get; set; }
    public bool Confirmed { get; set; }
    public decimal ExpectedCashAmount { get; set; }
    public int? TrsAccountId { get; set; }
    public CashType? CashType { get; set; }
    /// <summary>شماره سند دستی قطعی هم‌مبلغ؛ null یعنی کاربر نبود گردش قبلی را تأیید کرده است.</summary>
    public int? ExistingVoucherNumber { get; set; }
}

/// <summary>وضعیت و تاریخچه تسویه نسیه، جدا از اقساط و چک‌های پرداخت‌نشده.</summary>
public class ShopOrderCreditState
{
    public decimal Outstanding { get; set; }
    public decimal ReservedForCheques { get; set; }
    public decimal ReservedForInstallments { get; set; }
    public decimal AvailableCredit { get; set; }
    public bool IsCancelled { get; set; }
    public List<ShopOrderCreditReceiptDto> Receipts { get; set; } = new();
}

public class ShopOrderCreditReceiptDto
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public int TrsAccountId { get; set; }
    public string AccountName { get; set; } = "";
    public DateTime Date { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ReversalDate { get; set; }
    public string? ReversedBy { get; set; }
}

public class ShopOrderCreditReceiptCommand
{
    public Guid OperationId { get; set; }
    public int TrsAccountId { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
}

public class ShopOrderCreditReversalCommand
{
    public Guid OperationId { get; set; }
    public DateTime Date { get; set; }
}

/// <summary>درخواست ثبت/ویرایش سند</summary>
public class OrderCommand
{
    public int Id { get; set; }
    public int WarehouseId { get; set; }
    public int PartyId { get; set; }
    public TransactionType Type { get; set; }
    public DateTime Date { get; set; }
    public string? Description { get; set; }

    /// <summary>معرف (بازاریاب) — اختیاری، فقط فروش</summary>
    public int? ReferrerId { get; set; }

    // ---------- پرداخت (فقط فروش) ----------
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public CashType? CashType { get; set; }
    public string? CashTrackingNumber { get; set; }

    /// <summary>پیش‌دریافت نقدی در پرداخت ترکیبی (نسیه/چک/اقساط)</summary>
    public decimal CashAmount { get; set; }

    /// <summary>شناسه بانک / حساب خزانه انتخابی برای تسویه</summary>
    public int? TrsAccountId { get; set; }

    public DateTime? DueDate { get; set; }
    /// <summary>درخواست صریح ثبت ردیف تاخیر تادیه چک؛ مبلغ سود در سرور محاسبه می‌شود.</summary>
    public SaleChequeChargeInput? ChequeCharge { get; set; }
    public List<ChequeDto> Cheques { get; set; } = new();
    public List<InstallmentDto> Installments { get; set; } = new();

    public List<OrderLineInput> Lines { get; set; } = new();
}

/// <summary>ورودی ماشین‌حساب و شرایط تعدیل سررسید؛ مبلغ ردیف از کلاینت دریافت نمی‌شود.</summary>
public class SaleChequeChargeInput
{
    public decimal CalculatorBaseAmount { get; set; }
    public decimal CalculatorRatePercent { get; set; }
    public int CalculatorMonths { get; set; }
    public decimal AdjustmentRatePercent { get; set; }
    public int AdjustmentPeriodDays { get; set; }
    /// <summary>ماندهٔ نسیه صریحاً تأیید شده و تاریخ سررسید آن تعیین شده است.</summary>
    public DateTime? ConfirmedCreditDueDate { get; set; }
}

public class SaleChequeRescheduleCommand
{
    public Guid OperationId { get; set; }
    public DateTime ExpectedDueDate { get; set; }
    public DateTime NewDueDate { get; set; }
    public DateTime OperationDate { get; set; }
    public decimal ConfirmedIncomeDelta { get; set; }
}

public class SaleChequeReschedulePreview
{
    public decimal BaseAmount { get; set; }
    public decimal RatePercent { get; set; }
    public int PeriodDays { get; set; }
    public decimal IncomeDelta { get; set; }
}

public class SaleChequeReceiptCommand
{
    public Guid OperationId { get; set; }
    public int TrsAccountId { get; set; }
    public DateTime Date { get; set; }
}

public class SaleChequeReceiptReversalCommand
{
    public Guid OperationId { get; set; }
    public DateTime Date { get; set; }
}

public class SaleChequeStatusCommand
{
    public Guid OperationId { get; set; }
    public bool Bounced { get; set; }
    public string? Reason { get; set; }
}

public class SaleChequeStatusHistoryItem
{
    public DateTime CreatedAt { get; set; }
    public ChequeStatus PreviousStatus { get; set; }
    public ChequeStatus NewStatus { get; set; }
    public string? CreatedBy { get; set; }
    public string? Reason { get; set; }
}

public class SaleChequeReturnLineDto
{
    public int OriginalLineId { get; set; }
    public decimal Quantity { get; set; }
}

public class SaleChequeReturnPreviewRequest
{
    public DateTime Date { get; set; }
    public List<SaleChequeReturnLineDto> Lines { get; set; } = new();
}

public class SaleChequeReturnPreviewDto
{
    public decimal GoodsBeforeVat { get; set; }
    public decimal Vat { get; set; }
    public decimal Income { get; set; }
    public decimal TotalCredit { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal BalanceAfter { get; set; }
    public decimal UncollectedChequeExcess { get; set; }
}

public class SaleChequeReturnCommand
{
    public Guid OperationId { get; set; }
    public DateTime Date { get; set; }
    public List<SaleChequeReturnLineDto> Lines { get; set; } = new();
    public SaleChequeReturnDecision Decision { get; set; }
    public decimal? ConfirmedTotalCredit { get; set; }
}

public class SaleChequeReturnSummaryDto
{
    public int Id { get; set; }
    public string ReturnNumber { get; set; } = "";
    public DateTime Date { get; set; }
    public decimal GoodsAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal IncomeAmount { get; set; }
    public decimal TotalCredit => GoodsAmount + VatAmount + IncomeAmount;
    public decimal BalanceAfter { get; set; }
    public decimal UncollectedChequeExcess { get; set; }
    public SaleChequeReturnDecision Decision { get; set; }
}

public class SaleChequeReplaceCommand
{
    public Guid OperationId { get; set; }
    public DateTime OperationDate { get; set; }
    public string Number { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime DueDate { get; set; }
    public string? BankName { get; set; }
    public string? AccountInfo { get; set; }
    public string? OwnerName { get; set; }
    public string? Note { get; set; }
    public string? Reason { get; set; }
    public DateTime? ConfirmedCreditDueDate { get; set; }
}

public class SaleChequeCreditRefundCommand
{
    public Guid OperationId { get; set; }
    public int TrsAccountId { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string? Note { get; set; }
}

public class SaleChequeCreditAllocationCommand
{
    public Guid OperationId { get; set; }
    public int TargetSaleId { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string? Note { get; set; }
}

public class SaleChequeInstallmentReceiptCommand
{
    public Guid OperationId { get; set; }
    public int TrsAccountId { get; set; }
    public DateTime Date { get; set; }
}

public class SaleChequeInstallmentReversalCommand
{
    public Guid OperationId { get; set; }
    public DateTime Date { get; set; }
}

public class SaleChequeCancelCommand
{
    public Guid OperationId { get; set; }
    public DateTime Date { get; set; }
    public string? Reason { get; set; }
}

/// <summary>چک دریافتی.</summary>
public class ChequeDto
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public int? TrsAccountId { get; set; }
    public string? BankName { get; set; }
    public string? AccountInfo { get; set; }
    public string? OwnerName { get; set; }
    public decimal Amount { get; set; }
    public DateTime DueDate { get; set; }
    public bool IsCleared { get; set; }
    public DateTime? ClearedAt { get; set; }
    public ChequeStatus Status { get; set; } = ChequeStatus.InHand;
    public DateTime? BouncedAt { get; set; }
    public string? BounceReason { get; set; }
    public string? Note { get; set; }
}

/// <summary>قسط دفترچه اقساط.</summary>
public class InstallmentDto
{
    public int Id { get; set; }
    public int No { get; set; }
    public decimal Amount { get; set; }
    public DateTime DueDate { get; set; }
    public bool IsPaid { get; set; }
    public DateTime? PaidAt { get; set; }
}

public class OrderLineInput
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    /// <summary>Optional new list price, only for purchase; effective on invoice date.</summary>
    public decimal? NewSalePrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public bool DiscountIsAmount { get; set; }
    /// <summary>خالی در ویرایش توسط کلاینت قدیمی، snapshot فعلی را حفظ می‌کند.</summary>
    public decimal? VatValue { get; set; }

    /// <summary>هزینهٔ واحدِ ارسالی فقط از سرویس داخلی تعمیرات؛ از درخواست‌های عمومی JSON پذیرفته نمی‌شود.</summary>
    [JsonIgnore]
    public decimal? UnitCostSnapshot { get; set; }
}

/// <summary>درخواست اصلاح موجودی</summary>
public class AdjustmentCommand
{
    public int WarehouseId { get; set; }
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
    public DateTime Date { get; set; }
    public string? Description { get; set; }
}

/// <summary>موجودی تعدادیِ لازم برای انتخاب کالا در فاکتور؛ بدون بهای تمام‌شده.</summary>
public class InvoiceStockItem
{
    public int WarehouseId { get; set; }
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
}

/// <summary>موجودی فعلی یک کالا در یک انبار</summary>
public class StockItem
{
    public int WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal AvgCost { get; set; }
    public decimal LastSalePrice { get; set; }
    public decimal ReorderPoint { get; set; }
    public decimal MaxStock { get; set; }
    public string? Category { get; set; }
    public bool BelowReorder => ReorderPoint > 0 && Quantity <= ReorderPoint;
}

/// <summary>یک سطر کاردکس کالا</summary>
public class KardexRow
{
    public DateTime Date { get; set; }
    public TransactionType Type { get; set; }
    public string DocumentNo { get; set; } = "";
    public string PartyName { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal InQty { get; set; }
    public decimal OutQty { get; set; }
    public decimal Balance { get; set; }
    public decimal UnitPrice { get; set; }
}

/// <summary>آیتم گزارش نقطه سفارش</summary>
public class ReorderItem
{
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Unit { get; set; } = "";
    public string? Category { get; set; }
    public decimal ReorderPoint { get; set; }
    public decimal MaxStock { get; set; }
    public decimal TotalStock { get; set; }

    /// <summary>مقدار پیشنهادی برای خرید (تا رسیدن به حداکثر)</summary>
    public decimal Suggested => (MaxStock > TotalStock ? MaxStock : ReorderPoint * 2) - TotalStock;

    public decimal Shortage => ReorderPoint - TotalStock;
}

/// <summary>خلاصه داشبورد</summary>
public class DashboardSummary
{
    public int ProductCount { get; set; }
    public int WarehouseCount { get; set; }
    public int CustomerCount { get; set; }
    public int SupplierCount { get; set; }
    public int BelowReorderCount { get; set; }
    public decimal InventoryValue { get; set; }
    public decimal TodaySales { get; set; }
    public decimal TodayPurchases { get; set; }
    public decimal MonthSales { get; set; }
    public decimal MonthPurchases { get; set; }
    public int LowStockThreshold { get; set; }
}

/// <summary>داشبورد مدیریتی: فروش/سود دوره‌ای + بدهکاران سررسیدشده + چک‌های امروز (فقط ادمین).</summary>
public class AdminDashboard
{
    // فروش دوره‌ای (شمسی)
    public decimal SalesToday { get; set; }
    public decimal SalesWeek { get; set; }
    public decimal SalesMonth { get; set; }
    public decimal SalesQuarter { get; set; }

    // سود دوره‌ای (شمسی)
    public decimal ProfitToday { get; set; }
    public decimal ProfitWeek { get; set; }
    public decimal ProfitMonth { get; set; }
    public decimal ProfitQuarter { get; set; }

    /// <summary>بدهکاران دارای سررسید گذشته/امروز</summary>
    public List<DebtorItem> OverdueDebtors { get; set; } = new();

    /// <summary>چک‌هایی که امروز باید پاس شوند</summary>
    public List<ChequeAlertItem> TodayCheques { get; set; } = new();

    /// <summary>چک‌های پرداختی ما (صادره به تأمین‌کننده) که سررسید شده یا امروز سررسید دارند</summary>
    public List<ChequeAlertItem> IssuedCheques { get; set; } = new();

    /// <summary>روند فروش/سود ۳۰ روز اخیر (برای نمودار)</summary>
    public List<TrendPoint> DailyTrend { get; set; } = new();

    /// <summary>فروش/سود ۶ ماه شمسی اخیر (برای نمودار میله‌ای)</summary>
    public List<TrendPoint> MonthlyTrend { get; set; } = new();

    /// <summary>تفکیک فروش بر اساس روش پرداخت — فصل جاری (برای نمودار دونات)</summary>
    public decimal PayCash { get; set; }
    public decimal PayCredit { get; set; }
    public decimal PayCheque { get; set; }
    public decimal PayInstallment { get; set; }
}

/// <summary>نقطه نمودار روند.</summary>
public class TrendPoint
{
    public string Label { get; set; } = "";
    public decimal Sales { get; set; }
    public decimal Profit { get; set; }
}

/// <summary>بدهکار سررسیدشده.</summary>
public class DebtorItem
{
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public string? Mobile { get; set; }
    public string Number { get; set; } = "";
    public int TransactionId { get; set; }

    /// <summary>نسیه یا قسط</summary>
    public string Kind { get; set; } = "";

    public decimal Amount { get; set; }
    public DateTime DueDate { get; set; }

    /// <summary>چند روز از سررسید گذشته (۰ = امروز)</summary>
    public int DaysOverdue { get; set; }
}

/// <summary>چک قابل وصول امروز.</summary>
public class ChequeAlertItem
{
    public int ChequeId { get; set; }
    public string Number { get; set; } = "";
    public string? BankName { get; set; }
    public string? OwnerName { get; set; }
    public string PartyName { get; set; } = "";
    public string OrderNumber { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime DueDate { get; set; }
    public ChequeStatus Status { get; set; } = ChequeStatus.InHand;
    public string? BounceReason { get; set; }
}

/// <summary>آیتم فعالیت اخیر برای داشبورد</summary>
public class RecentActivity
{
    public DateTime Date { get; set; }
    public string Type { get; set; } = "";
    public string Number { get; set; } = "";
    public string PartyName { get; set; } = "";
    public decimal Amount { get; set; }
}


/// <summary>کالای موجود — نمای محدود برای پنل معرف.</summary>
public class ReferrerProductItem
{
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string Unit { get; set; } = "";
    public string? Category { get; set; }
    public bool InStock { get; set; }
}

/// <summary>دسته هزینه.</summary>
public class ExpenseCategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    /// <summary>تعداد اسناد این دسته (پر شده توسط سرور)</summary>
    public int ExpenseCount { get; set; }

    /// <summary>جمع مبلغ اسناد این دسته (پر شده توسط سرور)</summary>
    public decimal TotalAmount { get; set; }
}

/// <summary>سند هزینه.</summary>
public class ExpenseDto
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public int CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }

    /// <summary>نحوه پرداخت (نقد/کارت‌خوان/کارت به کارت)</summary>
    public CashType PayType { get; set; } = CashType.Cash;

    /// <summary>بانک / حساب خزانهٔ انتخابی برای نحوهٔ پرداخت</summary>
    public int? TrsAccountId { get; set; }
    public string? TrsAccountName { get; set; }

    /// <summary>دریافت‌کننده / طرف هزینه</summary>
    public string? Payee { get; set; }

    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>سوابق چک پرداخت هزینه؛ خالی برای پرداخت‌های غیرچکی و اسناد قدیمی.</summary>
    public List<ExpenseChequeDto> Cheques { get; set; } = new();
}

/// <summary>گزارش هزینه؛ جمع و جزئیات از تمام اسناد فیلترشده هستند، نه فقط صفحه جاری.</summary>
public class ExpenseReportDto
{
    public int Count { get; set; }
    public decimal TotalAmount { get; set; }
    public List<ExpenseReportCategoryDto> Categories { get; set; } = new();
    public List<ExpenseDto> Items { get; set; } = new();
}

public class ExpenseReportCategoryDto
{
    public int CategoryId { get; set; }
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>مدل مدیریت چک در سامانه فروشگاهی (دریافتی و پرداختی)</summary>
public class ShopChequeItem
{
    public int Id { get; set; }
    public int TransactionId { get; set; }
    public bool HasChequeCharge { get; set; }
    public string OrderNumber { get; set; } = "";
    public TransactionType TransactionType { get; set; }
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public string? PartyMobile { get; set; }
    /// <summary>تلفن ثابت طرف حساب — برای جستجوی مشتری با شماره تلفن.</summary>
    public string? PartyPhone { get; set; }
    public string Number { get; set; } = "";
    public int? TrsAccountId { get; set; }
    public string? BankName { get; set; }
    public string? AccountInfo { get; set; }
    public string? OwnerName { get; set; }
    public decimal Amount { get; set; }
    public DateTime DueDate { get; set; }
    public bool IsCleared { get; set; }
    public DateTime? ClearedAt { get; set; }
    public ChequeStatus Status { get; set; } = ChequeStatus.InHand;
    public DateTime? BouncedAt { get; set; }
    public string? BounceReason { get; set; }
    public string? Note { get; set; }
    public int DaysOverdue { get; set; }
}

/// <summary>مدل مدیریت اقساط در سامانه فروشگاهی</summary>
public class ShopInstallmentItem
{
    public int Id { get; set; }
    public int TransactionId { get; set; }
    public bool HasChequeCharge { get; set; }
    public TransactionType TransactionType { get; set; }
    public bool HasTrackedReceipt { get; set; }
    public string OrderNumber { get; set; } = "";
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public string? PartyMobile { get; set; }
    /// <summary>تلفن ثابت طرف حساب — برای جستجوی مشتری با شماره تلفن.</summary>
    public string? PartyPhone { get; set; }
    public int No { get; set; }
    public decimal Amount { get; set; }
    public DateTime DueDate { get; set; }
    public bool IsPaid { get; set; }
    public DateTime? PaidAt { get; set; }
    public int DaysOverdue { get; set; }
}

/// <summary>آمار و خلاصه وضعیت چک‌ها و اقساط فروشگاهی</summary>
public class ShopCreditSummary
{
    public int InHandChequesCount { get; set; }
    public decimal InHandChequesTotal { get; set; }
    public int ClearedChequesCount { get; set; }
    public decimal ClearedChequesTotal { get; set; }
    public int BouncedChequesCount { get; set; }
    public decimal BouncedChequesTotal { get; set; }
    public int PendingInstallmentsCount { get; set; }
    public decimal PendingInstallmentsTotal { get; set; }
    public int PaidInstallmentsCount { get; set; }
    public decimal PaidInstallmentsTotal { get; set; }
    public int OverdueInstallmentsCount { get; set; }
    public decimal OverdueInstallmentsTotal { get; set; }
}

// ==================== گزارش گردش حساب و مانده اشخاص (طلب و بدهی) ====================
public class PartyBalanceSummaryDto
{
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public string? Mobile { get; set; }
    public string? NationalCode { get; set; }
    public PartyType PartyType { get; set; }
    public PartyType Type => PartyType;
    public decimal OpeningDebit { get; set; }
    public decimal OpeningCredit { get; set; }
    public decimal OpeningBalance => OpeningDebit - OpeningCredit;
    public decimal PeriodDebit { get; set; }
    public decimal PeriodCredit { get; set; }
    public decimal TotalDebit => OpeningDebit + PeriodDebit;
    public decimal TotalCredit => OpeningCredit + PeriodCredit;
    public decimal RemainingBalance => TotalDebit - TotalCredit; // مثبت = بدهکار (طلب ما)، منفی = بستانکار (بدهی ما)
    public decimal ClosingBalance => RemainingBalance;
    public bool IsDebtor => RemainingBalance > 0;
    public bool IsCreditor => RemainingBalance < 0;
    public bool IsSettled => RemainingBalance == 0;
}

public class PartyStatementReportDto
{
    public decimal TotalReceivable { get; set; }
    public decimal TotalPayable { get; set; }
    public decimal NetBalance => TotalReceivable - TotalPayable;
    public int DebtorCount { get; set; }
    public int CreditorCount { get; set; }
    public List<PartyBalanceSummaryDto> Items { get; set; } = new();
}

public class PartyStatementRowDto
{
    public DateTime Date { get; set; }
    public int? TransactionId { get; set; }
    public string? DocumentNumber { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsOpening { get; set; }
    /// <summary>تخفیف فاکتور فروشِ همان ردیف؛ صرفاً اطلاعاتی و قبلاً در بدهکار لحاظ شده است.</summary>
    public decimal? SaleInvoiceDiscount { get; set; }
    public decimal Debit { get; set; }  // بدهکار (افزایش طلب ما از شخص)
    public decimal Credit { get; set; } // بستانکار (کاهش طلب ما یا افزایش بدهی ما به شخص)
    public decimal Balance { get; set; } // مانده پس از این سطر
}

public class PartyStatementDetailDto
{
    public int PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public string? Mobile { get; set; }
    public string? NationalCode { get; set; }
    public PartyType PartyType { get; set; }
    public PartyType Type => PartyType;
    public decimal OpeningBalance { get; set; } // مثبت = بدهکار اولیه، منفی = بستانکار اولیه
    /// <summary>جمع فروش کالا و خدمات دوره با ارزش افزوده، بدون درآمد چک.</summary>
    public decimal SaleGoodsTotal { get; set; }
    /// <summary>تخفیف فاکتورهای فروش صادرشده در دوره، پیش از برگشت‌های بعدی.</summary>
    public decimal SaleIssuedDiscountTotal { get; set; }
    /// <summary>جمع ردیف تاخیر تادیه چک برای فاکتورهای فروش دوره.</summary>
    public decimal SaleChequeIncomeTotal { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public decimal ClosingBalance { get; set; }
    public List<PartyStatementRowDto> Rows { get; set; } = new();
}
