using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// ================== ماژول هسته پرسنلی (کارگزینی) — HrCore ==================
/// کاملاً مستقل از دو ماژول موجود (RadisHr با کانتکست جدا + جداول Hr کانتکست اصلی).
/// نام جدول‌ها با پیشوند Hr در دیتابیس مشترک یکتا هستند و هیچ تداخلی ندارند:
/// HrOrgUnits، HrEmployees، HrContracts، HrDecrees.
/// مدارک پرسنلی در جدول عمومی پیوست‌ها با Module="HrEmployee" ذخیره می‌شود.
/// </summary>

/// <summary>نوع واحد سازمانی</summary>
public enum HrOrgUnitType
{
    Company = 0,
    Branch = 1,
    Department = 2,
    CostCenter = 3
}

/// <summary>نوع استخدام / قرارداد</summary>
public enum HrEmploymentType
{
    Rasmi = 0,
    Gharardadi = 1,
    Peymani = 2,
    Saati = 3,
    Mashaverei = 4
}

/// <summary>وضعیت پرسنل</summary>
public enum HrEmployeeStatus
{
    Active = 0,
    OnLeave = 1,
    Suspended = 2,
    Terminated = 3,
    Retired = 4
}

/// <summary>نوع حکم کارگزینی</summary>
public enum HrDecreeType
{
    Estekhdam = 0,
    Erteqa = 1,
    Enteghal = 2,
    TaghirHoghugh = 3,
    Tashvigh = 4,
    Tanbih = 5,
    GhatHamkari = 6,
    Bazneshastegi = 7
}

/// <summary>واحد سازمانی — درخت شرکت/شعبه/دپارتمان/مرکز هزینه</summary>
public class HrOrgUnit
{
    public int Id { get; set; }

    public int? ParentId { get; set; }

    public HrOrgUnitType Type { get; set; } = HrOrgUnitType.Department;

    [MaxLength(20)]
    public string Code { get; set; } = "";

    [MaxLength(150)]
    public string Name { get; set; } = "";

    /// <summary>مدیر واحد (ارجاع به پرسنل) — اختیاری</summary>
    public int? ManagerEmployeeId { get; set; }

    [MaxLength(30)]
    public string? Phone { get; set; }

    [MaxLength(300)]
    public string? Address { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>پرونده پرسنل — یک ردیف به‌ازای هر نفر</summary>
public class HrEmployee
{
    public int Id { get; set; }

    /// <summary>کد پرسنلی یکتا — خودکار (۱۰۰۱، ۱۰۰۲، …) یا دستی</summary>
    [MaxLength(20)]
    public string Code { get; set; } = "";

    [MaxLength(80)]
    public string FirstName { get; set; } = "";

    [MaxLength(80)]
    public string LastName { get; set; } = "";

    /// <summary>کد ملی یکتا (۱۰ رقم)</summary>
    [MaxLength(10)]
    public string NationalCode { get; set; } = "";

    public DateTime? BirthDate { get; set; }

    /// <summary>0=مرد 1=زن</summary>
    public int Gender { get; set; }

    /// <summary>0=مجرد 1=متاهل</summary>
    public int MaritalStatus { get; set; }

    [MaxLength(20)]
    public string? Mobile { get; set; }

    [MaxLength(150)]
    public string? Email { get; set; }

    [MaxLength(300)]
    public string? Address { get; set; }

    public DateTime HireDate { get; set; } = DateTime.Today;

    public int? OrgUnitId { get; set; }

    [MaxLength(150)]
    public string? PostTitle { get; set; }

    /// <summary>مدیر مستقیم (خودارجاع) — برای چارت و گردش تأییدها</summary>
    public int? ManagerId { get; set; }

    public HrEmploymentType EmploymentType { get; set; } = HrEmploymentType.Gharardadi;

    public HrEmployeeStatus Status { get; set; } = HrEmployeeStatus.Active;

    /// <summary>حساب کاربری ورود به سیستم (اختیاری)</summary>
    public int? SystemUserId { get; set; }

    /// <summary>حقوق پایه حکمی فعلی (ریال)</summary>
    public decimal BaseSalary { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>قرارداد پرسنل — هر نفر می‌تواند چند قرارداد دوره‌ای داشته باشد</summary>
public class HrContract
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    [MaxLength(30)]
    public string ContractNo { get; set; } = "";

    public HrEmploymentType Type { get; set; } = HrEmploymentType.Gharardadi;

    public DateTime StartDate { get; set; } = DateTime.Today;

    /// <summary>خالی = دائمی/رسمی بدون پایان</summary>
    public DateTime? EndDate { get; set; }

    public decimal BaseSalary { get; set; }

    [MaxLength(150)]
    public string? JobTitle { get; set; }

    public int? OrgUnitId { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>حکم کارگزینی — تغییرات رسمی وضعیت/پست/حقوق/واحد پرسنل با قابلیت «اجرا»</summary>
public class HrDecree
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    [MaxLength(30)]
    public string DecreeNo { get; set; } = "";

    public HrDecreeType Type { get; set; } = HrDecreeType.Estekhdam;

    public DateTime EffectiveDate { get; set; } = DateTime.Today;

    [MaxLength(150)]
    public string? NewPostTitle { get; set; }

    public int? NewOrgUnitId { get; set; }

    public decimal? NewBaseSalary { get; set; }

    public HrEmployeeStatus? NewStatus { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>آیا روی پرونده پرسنل اعمال شده است؟ (یک‌بار)</summary>
    public bool IsApplied { get; set; }

    public DateTime? AppliedAt { get; set; }

    public int? CreatedByUserId { get; set; }

    [MaxLength(150)]
    public string? CreatedByName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
