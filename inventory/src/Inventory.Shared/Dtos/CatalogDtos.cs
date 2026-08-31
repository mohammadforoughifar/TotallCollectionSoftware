namespace Inventory.Shared.Dtos;

/// <summary>تنظیمات برنامه.</summary>
public class AppSettings
{
    /// <summary>روش قیمت‌گذاری خروج انبار: Average | FIFO | LIFO</summary>
    public string CostingMethod { get; set; } = "Average";

    /// <summary>اجازه منفی شدن موجودی (پیش‌فرض: غیرمجاز)</summary>
    public bool AllowNegativeStock { get; set; } = false;

    /// <summary>آدرس سرور مرکزی درخواست‌های IT — خالی یعنی همین سرور، مرکزی است</summary>
    public string? ItServerUrl { get; set; }

    /// <summary>نام این شرکت (هنگام ارسال درخواست به سرور مرکزی همراه درخواست می‌رود)</summary>
    public string? ItCompanyName { get; set; }

    /// <summary>توکن ربات بله</summary>
    public string? BaleBotToken { get; set; }

    /// <summary>توکن ایتایار</summary>
    public string? EitaaToken { get; set; }

    /// <summary>شماره معرف سامانه در پیام‌ها</summary>
    public string? MessengerSenderNumber { get; set; } = "09111189771";
}

/// <summary>معرف (بازاریاب).</summary>
public class Referrer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>نام مجموعه / شرکت</summary>
    public string? CompanyName { get; set; }

    public string? Phone { get; set; }

    /// <summary>درصد پورسانت فروش کالا — بر مبنای «سود» سطرهای کالا</summary>
    public decimal GoodsCommissionPercent { get; set; }

    /// <summary>درصد پورسانت فروش خدمات — بر مبنای «کل مبلغ» سطرهای خدمات</summary>
    public decimal ServiceCommissionPercent { get; set; }

    /// <summary>شماره کارت بانکی (برای واریز پورسانت)</summary>
    public string? CardNumber { get; set; }

    /// <summary>شماره شبا (IBAN)</summary>
    public string? Iban { get; set; }

    /// <summary>اجازه مشاهده کالاهای موجود در پنل معرف</summary>
    public bool CanViewProducts { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    /// <summary>تعداد اسناد فروش این معرف (پر شده توسط سرور)</summary>
    public int OrderCount { get; set; }

    /// <summary>جمع پورسانت‌های محاسبه‌شده (پر شده توسط سرور)</summary>
    public decimal TotalCommission { get; set; }

    /// <summary>جمع پرداختی‌ها (پر شده توسط سرور)</summary>
    public decimal TotalPaid { get; set; }

    /// <summary>مانده کیف پول = پورسانت − پرداختی (پر شده توسط سرور)</summary>
    public decimal WalletBalance { get; set; }
}

/// <summary>سند پرداخت پورسانت به معرف.</summary>
public class ReferrerPayment
{
    public int Id { get; set; }
    public int ReferrerId { get; set; }
    public string? ReferrerName { get; set; }
    public string Number { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>گروه / دسته‌بندی کالا (درختی)</summary>
public class ProductCategory
{
    public int Id { get; set; }

    /// <summary>نام گروه</summary>
    public string Name { get; set; } = "";

    /// <summary>شناسه گروه والد (null = گروه ریشه)</summary>
    public int? ParentId { get; set; }

    /// <summary>شرح</summary>
    public string? Description { get; set; }

    /// <summary>فعال / غیرفعال</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>زمان ایجاد</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>تعداد کالاهای این گروه (پر شده توسط سرور)</summary>
    public int ProductCount { get; set; }

    /// <summary>عمق در درخت (پر شده توسط سرور — برای نمایش تورفتگی)</summary>
    public int Depth { get; set; }

    /// <summary>مسیر کامل: والد ← فرزند (پر شده توسط سرور)</summary>
    public string FullPath { get; set; } = "";
}

/// <summary>واحد شمارش کالا</summary>
public class MeasureUnit
{
    public int Id { get; set; }

    /// <summary>نام واحد (عدد، کیلوگرم، لیتر، ...)</summary>
    public string Name { get; set; } = "";

    /// <summary>فعال / غیرفعال</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>زمان ایجاد</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>تعداد کالاهای دارای این واحد (پر شده توسط سرور)</summary>
    public int ProductCount { get; set; }
}

/// <summary>نتیجه ورود گروهی کالا از اکسل</summary>
public class ExcelImportResult
{
    /// <summary>تعداد سطرهای ثبت‌شده</summary>
    public int Imported { get; set; }

    /// <summary>تعداد سطرهای رد شده</summary>
    public int Failed { get; set; }

    /// <summary>خطاهای هر سطر (شماره سطر + پیام)</summary>
    public List<string> Errors { get; set; } = new();
}

/// <summary>کالا / محصول</summary>
public class Product
{
    public int Id { get; set; }

    /// <summary>کد کالا (اختیاری، یکتا)</summary>
    public string Code { get; set; } = "";

    /// <summary>نام کالا</summary>
    public string Name { get; set; } = "";

    /// <summary>واحد شمارش (عدد، کیلوگرم، متر، ...)</summary>
    public string Unit { get; set; } = "عدد";

    /// <summary>گروه / دسته‌بندی کالا</summary>
    public string? Category { get; set; }

    /// <summary>بارکد</summary>
    public string? Barcode { get; set; }

    /// <summary>قیمت فروش (ریال)</summary>
    public decimal SalePrice { get; set; }

    /// <summary>قیمت خرید (ریال)</summary>
    public decimal PurchasePrice { get; set; }

    /// <summary>نقطه سفارش (حداقل موجودی مجاز)</summary>
    public decimal ReorderPoint { get; set; }

    /// <summary>حداکثر موجودی مطلوب</summary>
    public decimal MaxStock { get; set; }

    /// <summary>شرح</summary>
    public string? Description { get; set; }

    /// <summary>فعال / غیرفعال</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>خدمات است؟ (بدون موجودی انبار — فقط در فروش)</summary>
    public bool IsService { get; set; }

    /// <summary>انبار اختصاصی کالا (null = همه انبارها)</summary>
    public int? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }

    /// <summary>زمان ورود (ثبت) کالا</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>مجموع موجودی در همه انبارها (پر شده توسط سرور)</summary>
    public decimal TotalStock { get; set; }

    /// <summary>قیمت میانگین موزون خرید (پر شده توسط سرور)</summary>
    public decimal AvgCost { get; set; }

    /// <summary>آیا موجودی زیر نقطه سفارش است؟</summary>
    public bool BelowReorder => ReorderPoint > 0 && TotalStock <= ReorderPoint;
}

/// <summary>انبار</summary>
public class Warehouse
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Note { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>تعداد اقلام موجود در این انبار (پر شده توسط سرور)</summary>
    public int ItemCount { get; set; }
}

/// <summary>طرف حساب (مشتری / تأمین‌کننده)</summary>
public class Party
{
    public int Id { get; set; }
    public PartyType Type { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Mobile { get; set; }
    public string? Address { get; set; }
    public string? Note { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>معرف پیش‌فرض مشتری (اختیاری — null = بدون معرف)</summary>
    public int? ReferrerId { get; set; }
    public string? ReferrerName { get; set; }

    /// <summary>مانده حساب (پر شده توسط سرور)</summary>
    public decimal Balance { get; set; }
}

/// <summary>نتیجه فیلتر/صفحه‌بندی اقلام پایه</summary>
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
}

/// <summary>آیتم سبک برای انتخاب در فرم‌ها</summary>
public class LookupItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>آدرس آواتار (اختیاری) — نمایش تصویر دایره‌ای در جستجو/انتخاب</summary>
    public string? AvatarUrl { get; set; }
}
