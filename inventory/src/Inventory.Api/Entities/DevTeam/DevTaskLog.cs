using System.ComponentModel.DataAnnotations;
using Inventory.Shared;

namespace Inventory.Api.Data;

/// <summary>
/// یک رویداد در تاریخچهٔ آیتم کاری.
/// <para>
/// این جدول همان چیزی است که پرسش «چه کسی روی چه بخشی کار کرده» را پاسخ می‌دهد —
/// چیزی که git این مخزن به‌تنهایی نمی‌تواند بگوید، چون بیشتر commitها به نام
/// ایجنت AI ثبت شده‌اند و برنچ‌ها به نام افراد بوده‌اند نه به نام کارها.
/// </para>
/// </summary>
public class DevTaskLog
{
    public int Id { get; set; }

    public int TaskId { get; set; }

    /// <summary>انسانِ انجام‌دهنده. تهی یعنی رویداد خودکار سامانه.</summary>
    public int? MemberId { get; set; }

    public DevLogAction Action { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }

    /// <summary>وضعیت پیش از تغییر — فقط برای رویداد تغییر وضعیت.</summary>
    public DevTaskStatus? FromStatus { get; set; }

    /// <summary>وضعیت پس از تغییر — فقط برای رویداد تغییر وضعیت.</summary>
    public DevTaskStatus? ToStatus { get; set; }

    /// <summary>sha کوتاه commit مرتبط، اگر این رویداد از راه git ثبت شده باشد.</summary>
    [MaxLength(60)]
    public string? CommitSha { get; set; }

    public DateTime At { get; set; } = DateTime.Now;
}
