using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// نشست فعال کاربر — پایهٔ سیاست «کاربر هم‌زمان» و همچنین اعتبارسنجی توکن.
///
/// <para>
/// از مرحلهٔ ۳ توکن JWT یک claim به نام <c>sid</c> می‌گیرد و Gate فقط توکنی را می‌پذیرد
/// که ردیف نشست متناظرش <c>Active</c> باشد. بنابراین خروج از سیستم واقعاً صندلی را
/// آزاد می‌کند و «۱۲ ساعت اعتبار توکن» دیگر به معنی «۱۲ ساعت اشغال صندلی» نیست.
/// </para>
/// </summary>
public class LicenseSession
{
    public int Id { get; set; }

    /// <summary>شناسهٔ نشست — همان claim «sid» در توکن.</summary>
    [MaxLength(64)]
    public string SessionId { get; set; } = "";

    public int UserId { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.Now;

    /// <summary>آخرین فعالیت — با هر درخواست (حداکثر یک‌بار در دقیقه) به‌روز می‌شود.</summary>
    public DateTime LastSeen { get; set; } = DateTime.Now;

    [MaxLength(100)]
    public string? MachineName { get; set; }

    [MaxLength(64)]
    public string? Ip { get; set; }

    /// <summary>Active | Closed | Evicted</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "Active";

    public DateTime? EndedAt { get; set; }

    [MaxLength(60)]
    public string? EndReason { get; set; }
}
