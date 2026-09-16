using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

// =====================================================================
//  گزارش‌های شخصیِ ساخته‌شده با «گزارش‌ساز»
//    UserReport ← UserReportRoleShare
//
//  هر گزارش یک کوئری (JSON) روی یک دیتاست است. دیتاست‌ها سمت سرور
//  ثبت شده‌اند و هرکدام یک ماژول مجوز دارند؛ بنابراین حتی اگر کاربر
//  QueryJson را دستکاری کند، دادهٔ بدون مجوز برگردانده نمی‌شود.
// =====================================================================

/// <summary>یک گزارش ذخیره‌شده (قابل استفاده به‌عنوان ویجت داشبورد).</summary>
public class UserReport
{
    public int Id { get; set; }

    /// <summary>سازندهٔ گزارش — مالک آن.</summary>
    public int UserId { get; set; }

    [MaxLength(140)] public string Name { get; set; } = "گزارش بدون نام";

    /// <summary>کلید دیتاست (مثل fac-sale-line).</summary>
    [MaxLength(60)] public string DatasetKey { get; set; } = "";

    /// <summary>ماژول مجوزِ دیتاست در زمان ذخیره — برای کنترل دسترسی.</summary>
    [MaxLength(60)] public string Module { get; set; } = "";

    /// <summary>۰ = فقط من، ۱ = من + نقش‌های اشتراک‌گذاری‌شده.</summary>
    public int Visibility { get; set; }

    /// <summary>کوئری گزارش به‌صورت JSON (ReportQueryDto).</summary>
    public string QueryJson { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public List<UserReportRoleShare> RoleShares { get; set; } = new();
}

/// <summary>اشتراک‌گذاری یک گزارش با یک نقش.</summary>
public class UserReportRoleShare
{
    public int Id { get; set; }

    public int ReportId { get; set; }
    public UserReport? Report { get; set; }

    public int RoleId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
