using System.ComponentModel.DataAnnotations;
using Inventory.Shared;

namespace Inventory.Api.Data;

/// <summary>
/// یک آیتم کاری — واحد پاسخ به «کدام آیتم در حال انجام است».
/// <para>
/// ستون <c>Status</c> همان ستون بورد است. با تغییر وضعیت، یک رویداد هم در
/// <see cref="DevTaskLog"/> ثبت می‌شود تا تاریخچهٔ «کی، چه زمانی، چه کرد» بماند.
/// </para>
/// </summary>
public class DevTask
{
    public int Id { get; set; }

    /// <summary>کلید نمایشی مثل HR-42 — همان کلیدی که در نام برنچ می‌آید.</summary>
    [MaxLength(30)]
    public string Number { get; set; } = "";

    [MaxLength(200)]
    public string Title { get; set; } = "";

    [MaxLength(2000)]
    public string? Description { get; set; }

    public int ModuleId { get; set; }

    /// <summary>فرد مسئول. تهی یعنی هنوز به کسی واگذار نشده.</summary>
    public int? AssigneeId { get; set; }

    public DevTaskStatus Status { get; set; } = DevTaskStatus.Backlog;

    public DevPriority Priority { get; set; } = DevPriority.Normal;

    public DevTaskSize Size { get; set; } = DevTaskSize.Medium;

    /// <summary>نام برنچ git مطابق قرارداد &lt;ماژول&gt;/&lt;کلید&gt;-&lt;توضیح&gt;.</summary>
    [MaxLength(160)]
    public string? BranchName { get; set; }

    [MaxLength(300)]
    public string? PullRequestUrl { get; set; }

    /// <summary>آیا ایجنت AI در انجام این کار نقش داشته؟ نشانهٔ لزوم بازبینی دقیق‌تر.</summary>
    public bool AgentAssisted { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? DueDate { get; set; }

    public DateTime? CompletedAt { get; set; }

    [MaxLength(120)]
    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public List<DevTaskLog> Logs { get; set; } = new();
}
