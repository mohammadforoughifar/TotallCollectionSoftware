using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Services.DevTeam;

/// <summary>
/// قرارداد سرویس مدیریت برنامه‌نویسان.
/// <para>سه پرسشی که این سرویس پاسخ می‌دهد:</para>
/// <list type="number">
///   <item>چه کسی روی چه بخشی کار کرده؟ → <see cref="GetActivityAsync"/> و <see cref="GetTaskLogsAsync"/></item>
///   <item>کدام آیتم در حال انجام است؟ → <see cref="GetBoardAsync"/></item>
///   <item>مالک هر ماژول کیست؟ → <see cref="GetModulesAsync"/></item>
/// </list>
/// </summary>
public interface IDevTeamService
{
    // ---------------- اعضای تیم ----------------
    Task<List<DevMemberDto>> GetMembersAsync(bool activeOnly = false);
    Task<DevMemberDto> SaveMemberAsync(DevMemberDto dto);
    Task DeleteMemberAsync(int id);

    // ---------------- ماژول‌ها و مالکیت ----------------
    Task<List<DevModuleDto>> GetModulesAsync(bool activeOnly = false);
    Task<DevModuleDto> SaveModuleAsync(DevModuleDto dto);
    Task DeleteModuleAsync(int id);

    /// <summary>تعیین یا برداشتن مالک یک ماژول. این همان چیزی است که CODEOWNERS از آن ساخته می‌شود.</summary>
    Task<DevModuleDto> SetModuleOwnerAsync(int moduleId, int? ownerId);

    /// <summary>
    /// هم‌زمان‌سازی فهرست ماژول‌ها با ساختار واقعی مخزن.
    /// ورودی از <c>tools/ownership-map.csv</c> خوانده می‌شود و فقط ماژول‌های تازه را
    /// اضافه می‌کند؛ مالک‌های تعیین‌شدهٔ دستی دست‌نخورده می‌مانند.
    /// </summary>
    Task<int> SyncModulesFromRepoAsync(string ownershipCsvPath);

    // ---------------- آیتم‌های کاری ----------------
    Task<PagedResult<DevTaskDto>> GetTasksAsync(string? search, DevTaskStatus? status, int? moduleId, int? assigneeId, int page, int pageSize);
    Task<DevTaskDto?> GetTaskAsync(int id);
    Task<DevTaskDto> SaveTaskAsync(DevTaskDto dto);
    Task DeleteTaskAsync(int id);

    /// <summary>تغییر وضعیت + ثبت خودکار رویداد در تاریخچه.</summary>
    Task<DevTaskDto> SetStatusAsync(int id, DevTaskStatusRequest request);

    /// <summary>واگذاری آیتم به یک فرد + ثبت خودکار رویداد.</summary>
    Task<DevTaskDto> AssignAsync(int id, int? memberId, string? note = null);

    // ---------------- تاریخچهٔ کار ----------------
    Task<List<DevTaskLogDto>> GetTaskLogsAsync(int taskId);

    /// <summary>ثبت یک رویداد دستی در تاریخچهٔ آیتم (پیشرفت، کامیت، بازبینی، یادداشت).</summary>
    Task<DevTaskLogDto> AddLogAsync(int taskId, DevTaskLogRequest request);

    /// <summary>
    /// رویدادهای اخیر کل تیم — پاسخ مستقیم «کی روی چه چیزی کار کرده».
    /// </summary>
    Task<List<DevTaskLogDto>> GetActivityAsync(int days = 14, int take = 200);

    // ---------------- بورد و شاخص‌ها ----------------
    Task<DevBoardDto> GetBoardAsync(int? moduleId = null, int? assigneeId = null);
}
