namespace Inventory.Shared.Dtos;

// =====================================================================
// ماژول مدیریت برنامه‌نویسان (DevTeam)
//
// چرا این ماژول: تا پیش از این، پاسخ «چه کسی روی چه ماژولی کار کرده و کدام
// آیتم در جریان است» فقط از راه نام برنچ‌های شخصی و ممیزی دستی git ممکن بود.
// این ماژول همان سه پرسش را داخل خود برنامه پاسخ می‌دهد:
//   ۱) چه کسی روی چه بخشی کار کرده  → DevTaskLog + DevModule.OwnerId
//   ۲) کدام آیتم در حال انجام است    → DevTask.Status (ستون‌های بورد)
//   ۳) چه کسی مالک هر ماژول است      → DevModule + نقشهٔ مالکیت
// =====================================================================

/// <summary>عضو تیم توسعه — یک انسان مشخص.</summary>
public class DevMemberDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string? GithubHandle { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public DevRole Role { get; set; } = DevRole.Developer;

    /// <summary>
    /// عنوان فارسی نقش — محاسبه‌شده از <see cref="Role"/> تا هر صفحهٔ Razor مجبور
    /// نباشد خودش <c>DevTeamLabels.Role(...)</c> را صدا بزند و متن‌ها واگرا نشوند.
    /// فقط خواندنی است، پس در JSON ورودی نادیده گرفته می‌شود.
    /// </summary>
    public string RoleTitle => DevTeamLabels.Role(Role);

    public bool IsActive { get; set; } = true;
    public string ColorHex { get; set; } = "#6c757d";
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>تعداد ماژول‌هایی که این فرد مالکشان است (محاسبه‌شده در سرور).</summary>
    public int OwnedModules { get; set; }

    /// <summary>آیتم‌های بازِ این فرد (محاسبه‌شده در سرور).</summary>
    public int OpenTasks { get; set; }
}

/// <summary>یک ماژول نرم‌افزاری و مالک آن.</summary>
public class DevModuleDto
{
    public int Id { get; set; }

    /// <summary>کلید یکتا، هم‌نام با پوشهٔ کد (Hr، Office، DocArchive).</summary>
    public string Key { get; set; } = "";

    /// <summary>نام فارسی برای نمایش.</summary>
    public string Title { get; set; } = "";

    public int? OwnerId { get; set; }

    /// <summary>نام مالک (محاسبه‌شده در سرور).</summary>
    public string? OwnerName { get; set; }

    /// <summary>نقش مالک (محاسبه‌شده در سرور) — برای دیدن اینکه مالک ارشد است یا ایجنت.</summary>
    public string? OwnerRoleTitle { get; set; }

    public string? Icon { get; set; }
    public string ColorHex { get; set; } = "#0d6efd";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>مسیرهای کد این ماژول، جداشده با «;».</summary>
    public string? RepoPaths { get; set; }

    /// <summary>حجم کد سمت سرور — برای اولویت‌بندی تعیین مالک.</summary>
    public int ServiceLines { get; set; }

    /// <summary>تعداد صفحه‌های کلاینت.</summary>
    public int PageCount { get; set; }

    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>آیتم‌های باز این ماژول (محاسبه‌شده در سرور).</summary>
    public int OpenTasks { get; set; }

    /// <summary>آیتم‌های در حال انجام (محاسبه‌شده در سرور) — همان چیزی که «جاری» بودن ماژول را نشان می‌دهد.</summary>
    public int InProgress { get; set; }

    /// <summary>آیتم‌های مسدود (محاسبه‌شده در سرور).</summary>
    public int Blocked { get; set; }

    /// <summary>آیتم‌های در صف (محاسبه‌شده در سرور).</summary>
    public int Backlog { get; set; }
}

/// <summary>یک آیتم کاری — واحد «چه چیزی در حال انجام است».</summary>
public class DevTaskDto
{
    public int Id { get; set; }

    /// <summary>کلید نمایشی مثل HR-42. همان کلیدی که در نام برنچ می‌آید.</summary>
    public string Number { get; set; } = "";

    public string Title { get; set; } = "";
    public string? Description { get; set; }

    public int ModuleId { get; set; }
    public string? ModuleKey { get; set; }
    public string? ModuleTitle { get; set; }
    public string? ModuleColorHex { get; set; }

    public int? AssigneeId { get; set; }
    public string? AssigneeName { get; set; }
    public string? AssigneeColorHex { get; set; }

    public DevTaskStatus Status { get; set; } = DevTaskStatus.Backlog;
    public DevPriority Priority { get; set; } = DevPriority.Normal;
    public DevTaskSize Size { get; set; } = DevTaskSize.Medium;

    /// <summary>نام برنچ git مطابق قرارداد &lt;ماژول&gt;/&lt;کلید&gt;-&lt;توضیح&gt;.</summary>
    public string? BranchName { get; set; }

    public string? PullRequestUrl { get; set; }

    /// <summary>آیا این کار را ایجنت AI انجام داده؟ برای سخت‌گیری بیشتر در بازبینی.</summary>
    public bool AgentAssisted { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? CompletedAt { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>روزهای مانده به سررسید؛ منفی یعنی عقب‌افتاده.</summary>
    public int? DaysToDue { get; set; }
}

/// <summary>یک رویداد در تاریخچهٔ آیتم — مبنای «کی روی چه چیزی کار کرد».</summary>
public class DevTaskLogDto
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public string? TaskNumber { get; set; }
    public string? TaskTitle { get; set; }
    public int? MemberId { get; set; }
    public string? MemberName { get; set; }
    public DevLogAction Action { get; set; }
    public string? Note { get; set; }
    public DevTaskStatus? FromStatus { get; set; }
    public DevTaskStatus? ToStatus { get; set; }
    public string? CommitSha { get; set; }
    public DateTime At { get; set; }
}

/// <summary>یک ستون بورد.</summary>
public class DevBoardColumnDto
{
    public DevTaskStatus Status { get; set; }
    public string Title { get; set; } = "";
    public List<DevTaskDto> Tasks { get; set; } = new();
}

/// <summary>بار کاری هر عضو — چه کسی چند آیتم در هر وضعیت دارد.</summary>
public class DevWorkloadDto
{
    public int MemberId { get; set; }
    public string FullName { get; set; } = "";
    public string ColorHex { get; set; } = "#6c757d";
    public string? RoleTitle { get; set; }
    public int Backlog { get; set; }
    public int InProgress { get; set; }
    public int InReview { get; set; }
    public int Blocked { get; set; }
    public int Done { get; set; }
    public int Total => Backlog + InProgress + InReview + Blocked + Done;
    public int OwnedModules { get; set; }
}

/// <summary>خلاصهٔ بار یک ماژول.</summary>
public class DevModuleLoadDto
{
    public int ModuleId { get; set; }
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string ColorHex { get; set; } = "#0d6efd";
    public string? OwnerName { get; set; }
    public int OpenTasks { get; set; }
    public int InProgress { get; set; }
    public int Blocked { get; set; }
    public int DoneTasks { get; set; }
    public int ServiceLines { get; set; }
}

/// <summary>شاخص‌های کلی تیم برای بالای بورد.</summary>
public class DevStatsDto
{
    public int ActiveMembers { get; set; }
    public int TotalModules { get; set; }
    public int ModulesWithOwner { get; set; }
    public int OpenTasks { get; set; }
    public int InProgress { get; set; }
    public int InReview { get; set; }
    public int Blocked { get; set; }
    public int DoneThisWeek { get; set; }
    public int Overdue { get; set; }

    /// <summary>سهم آیتم‌هایی که ایجنت AI در آنها نقش داشته — از کل آیتم‌های بسته‌شده.</summary>
    public int AgentAssistedShare { get; set; }

    /// <summary>روزهای میانهٔ انجام یک آیتم (از شروع تا پایان).</summary>
    public double MedianDaysToComplete { get; set; }
}

/// <summary>پاسخ کامل بورد در یک درخواست — تا صفحه برای هر بخش جداگانه به سرور نرود.</summary>
public class DevBoardDto
{
    public List<DevBoardColumnDto> Columns { get; set; } = new();
    public List<DevWorkloadDto> Workload { get; set; } = new();
    public List<DevModuleLoadDto> ModuleLoad { get; set; } = new();
    public DevStatsDto Stats { get; set; } = new();
}

/// <summary>درخواست تغییر وضعیت یک آیتم — دلیل تغییر هم در تاریخچه ثبت می‌شود.</summary>
public class DevTaskStatusRequest
{
    public DevTaskStatus Status { get; set; }
    public string? Note { get; set; }
    public string? CommitSha { get; set; }
}

/// <summary>درخواست ثبت یک رویداد در تاریخچهٔ آیتم.</summary>
public class DevTaskLogRequest
{
    public DevLogAction Action { get; set; } = DevLogAction.Progress;
    public string? Note { get; set; }
    public string? CommitSha { get; set; }
    public int? MemberId { get; set; }
}
