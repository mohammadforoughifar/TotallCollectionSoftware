namespace Inventory.Shared.Dtos;

// ============================================================
//  ماژول هسته پرسنلی (کارگزینی) — HrCore — DTOها
// ============================================================

public class HrEmployeeDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string FullName => $"{FirstName} {LastName}".Trim();
    public string NationalCode { get; set; } = "";
    public DateTime? BirthDate { get; set; }
    public int Gender { get; set; }
    public int MaritalStatus { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public DateTime HireDate { get; set; }
    public int? OrgUnitId { get; set; }
    public string? OrgUnitName { get; set; }
    public string? PostTitle { get; set; }
    public int? ManagerId { get; set; }
    public string? ManagerName { get; set; }
    public int EmploymentType { get; set; }
    public int Status { get; set; }
    public int? SystemUserId { get; set; }
    public string? SystemUserName { get; set; }
    public decimal BaseSalary { get; set; }
    public bool IsActive { get; set; }
    /// <summary>قرارداد فعال جاری (اگر داشته باشد)</summary>
    public string? ActiveContractNo { get; set; }
    public DateTime? ActiveContractEnd { get; set; }
    public int ContractsCount { get; set; }
    public int DecreesCount { get; set; }
}

public class HrEmployeeSaveDto
{
    public string? Code { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string NationalCode { get; set; } = "";
    public DateTime? BirthDate { get; set; }
    public int Gender { get; set; }
    public int MaritalStatus { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public DateTime HireDate { get; set; } = DateTime.Today;
    public int? OrgUnitId { get; set; }
    public string? PostTitle { get; set; }
    public int? ManagerId { get; set; }
    public int EmploymentType { get; set; } = 1;
    public int Status { get; set; }
    public int? SystemUserId { get; set; }
    public decimal BaseSalary { get; set; }
    public bool IsActive { get; set; } = true;
}

public class HrOrgUnitDto
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public int Type { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ParentName { get; set; }
    public int? ManagerEmployeeId { get; set; }
    public string? ManagerName { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public int EmployeeCount { get; set; }
    public List<HrOrgUnitDto> Children { get; set; } = new();
}

public class HrOrgUnitSaveDto
{
    public int? ParentId { get; set; }
    public int Type { get; set; } = 2;
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int? ManagerEmployeeId { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public class HrContractDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public string ContractNo { get; set; } = "";
    public int Type { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    /// <summary>روزهای مانده تا پایان (null = دائمی)</summary>
    public int? DaysToEnd { get; set; }
    public decimal BaseSalary { get; set; }
    public string? JobTitle { get; set; }
    public int? OrgUnitId { get; set; }
    public string? OrgUnitName { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
}

public class HrContractSaveDto
{
    public int EmployeeId { get; set; }
    public string ContractNo { get; set; } = "";
    public int Type { get; set; } = 1;
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime? EndDate { get; set; }
    public decimal BaseSalary { get; set; }
    public string? JobTitle { get; set; }
    public int? OrgUnitId { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public class HrDecreeDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public string DecreeNo { get; set; } = "";
    public int Type { get; set; }
    public DateTime EffectiveDate { get; set; }
    public string? NewPostTitle { get; set; }
    public int? NewOrgUnitId { get; set; }
    public string? NewOrgUnitName { get; set; }
    public decimal? NewBaseSalary { get; set; }
    public int? NewStatus { get; set; }
    public string? Description { get; set; }
    public bool IsApplied { get; set; }
    public DateTime? AppliedAt { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class HrDecreeSaveDto
{
    public int EmployeeId { get; set; }
    public string DecreeNo { get; set; } = "";
    public int Type { get; set; }
    public DateTime EffectiveDate { get; set; } = DateTime.Today;
    public string? NewPostTitle { get; set; }
    public int? NewOrgUnitId { get; set; }
    public decimal? NewBaseSalary { get; set; }
    public int? NewStatus { get; set; }
    public string? Description { get; set; }
}

/// <summary>ویجت داشبورد کارگزینی</summary>
public class HrDashboardDto
{
    public int ActiveEmployees { get; set; }
    public int OrgUnits { get; set; }
    public int ExpiringContracts { get; set; }
    public int PendingDecrees { get; set; }
    public List<HrContractDto> ExpiringList { get; set; } = new();
    public List<HrDecreeDto> RecentDecrees { get; set; } = new();
    public Dictionary<string, int> ByEmploymentType { get; set; } = new();
}
