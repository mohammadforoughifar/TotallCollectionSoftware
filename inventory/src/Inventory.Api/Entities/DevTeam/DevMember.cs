using System.ComponentModel.DataAnnotations;
using Inventory.Shared;

namespace Inventory.Api.Data;

/// <summary>
/// عضو تیم توسعه — یک انسان مشخص.
/// <para>
/// چرا این موجودیت لازم است: در تاریخچهٔ git این مخزن ۲۱ هویت نویسندهٔ متفاوت
/// برای چند نفر وجود دارد و بیشتر commitها به نام ایجنت AI ثبت شده‌اند، پس
/// «چه کسی» از روی git قابل تشخیص نیست. این جدول فهرست واقعی افراد است.
/// </para>
/// </summary>
public class DevMember
{
    public int Id { get; set; }

    /// <summary>نام کامل — همان چیزی که در تریلر Requested-by می‌آید.</summary>
    [MaxLength(120)]
    public string FullName { get; set; } = "";

    /// <summary>هندل گیت‌هاب — برای اتصال به CODEOWNERS و PRها.</summary>
    [MaxLength(60)]
    public string? GithubHandle { get; set; }

    /// <summary>ایمیل — باید با git config user.email یکی باشد.</summary>
    [MaxLength(160)]
    public string? Email { get; set; }

    [MaxLength(40)]
    public string? Phone { get; set; }

    public DevRole Role { get; set; } = DevRole.Developer;

    public bool IsActive { get; set; } = true;

    /// <summary>رنگ اختصاصی برای نمایش روی بورد و نمودار بار کاری.</summary>
    [MaxLength(9)]
    public string ColorHex { get; set; } = "#6c757d";

    [MaxLength(500)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
