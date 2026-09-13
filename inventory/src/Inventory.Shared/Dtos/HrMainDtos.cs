namespace Inventory.Shared.Dtos;

// ============================================================
//  منابع انسانی اصلی — مدیریت پایه سازمانی (HrMain) — DTOها
// ============================================================

public class HrMainCompanyDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? LogoPath { get; set; }
    public string? Address { get; set; }
    public string? EconomicCode { get; set; }
    public string? RegistrationNo { get; set; }
    public string? NationalId { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? ManagerName { get; set; }
}

public class HrMainCompanySaveDto
{
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? EconomicCode { get; set; }
    public string? RegistrationNo { get; set; }
    public string? NationalId { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? ManagerName { get; set; }
}

public class HrMainBranchDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Type { get; set; }
    public string? City { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? ManagerName { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public int NodeCount { get; set; }
}

public class HrMainBranchSaveDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Type { get; set; }
    public string? City { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? ManagerName { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public class HrMainOrgNodeDto
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public int Level { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ParentName { get; set; }
    public int? BranchId { get; set; }
    public string? BranchName { get; set; }
    public string? ManagerTitle { get; set; }
    public string? Phone { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public int PositionCount { get; set; }
    public List<HrMainOrgNodeDto> Children { get; set; } = new();
}

public class HrMainOrgNodeSaveDto
{
    public int? ParentId { get; set; }
    public int Level { get; set; } = 1;
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int? BranchId { get; set; }
    public string? ManagerTitle { get; set; }
    public string? Phone { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class HrMainPositionDto
{
    public int Id { get; set; }
    public string? Code { get; set; }
    public string Title { get; set; } = "";
    public int? OrgNodeId { get; set; }
    public string? OrgNodeName { get; set; }
    public string? Grade { get; set; }
    public string? JobDescription { get; set; }
    public string? Requirements { get; set; }
    public int HeadCount { get; set; }
    public bool IsActive { get; set; }
}

public class HrMainPositionSaveDto
{
    public string? Code { get; set; }
    public string Title { get; set; } = "";
    public int? OrgNodeId { get; set; }
    public string? Grade { get; set; }
    public string? JobDescription { get; set; }
    public string? Requirements { get; set; }
    public int HeadCount { get; set; } = 1;
    public bool IsActive { get; set; } = true;
}

public class HrMainLocaleDto
{
    public string Language { get; set; } = "fa";
    public string Calendar { get; set; } = "jalali";
}

public class HrMainRulesDto
{
    public int AnnualLeaveDays { get; set; } = 26;
    public int MaxConsecutiveLeaveDays { get; set; } = 9;
    public int MaxCarryOverDays { get; set; } = 9;
    public bool UnpaidLeaveAllowed { get; set; } = true;
    public int LateGraceMinutes { get; set; } = 10;
    public int MonthlyAllowedLateMinutes { get; set; } = 60;
    public int MaxLateWithoutLeaveMinutes { get; set; } = 120;
    public decimal LateDeductionFactor { get; set; } = 1.0m;
    public decimal AbsenceDailyDeductionFactor { get; set; } = 1.0m;
    public int UnexcusedAbsenceWarningAfter { get; set; } = 3;
    public decimal OvertimeFactor { get; set; } = 1.4m;
    public int MaxMonthlyOvertimeHours { get; set; } = 60;
    public bool OvertimeNeedsApproval { get; set; } = true;
    public int ContractAlertDays { get; set; } = 30;
}

/// <summary>تعطیلات رسمی/شرکتی — نگاشت روی جدول موجود CompanyHoliday</summary>
public class HrMainHolidayDto
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string Name { get; set; } = "";
    public bool IsOfficial { get; set; }
    public string? CreatedByName { get; set; }
}

public class HrMainHolidaySaveDto
{
    public DateTime Date { get; set; } = DateTime.Today;
    public string Name { get; set; } = "";
    public bool IsOfficial { get; set; } = true;
}

/// <summary>نمای کلی منابع انسانی اصلی برای هاب/داشبورد</summary>
public class HrMainOverviewDto
{
    public string? CompanyName { get; set; }
    public string? LogoPath { get; set; }
    public int ActiveBranches { get; set; }
    public int ActiveNodes { get; set; }
    public int ActivePositions { get; set; }
    public int ActiveEmployees { get; set; }
    public int HolidaysThisYear { get; set; }
    public int CurrentJalaliYear { get; set; }
}
