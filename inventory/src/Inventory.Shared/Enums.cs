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
