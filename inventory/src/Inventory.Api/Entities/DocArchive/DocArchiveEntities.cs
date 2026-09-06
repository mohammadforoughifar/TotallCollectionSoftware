using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

// ============================================================================
//  ماژول آرشیو اسناد و مدارک (DocArchive)
//  ─ پوشه‌بندی درختی مثل بایگانی
//  ─ دسترسی نفر به نفر روی پوشه و مدرک (مشاهده/خواندن/نوشتن/کامل + دانلود)
//  ─ مدرک با «کد یکتا»، تاریخ انقضا، گردش تایید چندنفره
//  ─ ورژن‌گذاری کامل + پیوست روی هر ورژن
//  ─ لینک مدارک به هم و ایجاد کار در کارتابل هنگام ورژن خوردن
// ============================================================================

/// <summary>سطح دسترسی به پوشه/مدرک — عدد بزرگ‌تر شامل سطوح پایین‌تر است.</summary>
public enum DocAccessLevel
{
    None = 0,
    /// <summary>مشاهده — فقط دیدن عنوان/کد در لیست (بدون باز کردن محتوا)</summary>
    View = 1,
    /// <summary>خواندن — باز کردن مدرک، دیدن ورژن‌ها و پیوست‌ها</summary>
    Read = 2,
    /// <summary>نوشتن — ثبت/ویرایش مدرک و ورژن جدید</summary>
    Write = 3,
    /// <summary>کامل — همه‌چیز + مدیریت دسترسی، حذف، دریافت کار در کارتابل</summary>
    Full = 4
}

/// <summary>وضعیت ورژن مدرک</summary>
public enum DocVersionStatus
{
    /// <summary>پیش‌نویس — هنوز به گردش نرفته</summary>
    Draft = 0,
    /// <summary>در گردش تایید — فریز شده و در کارتابل تاییدکنندگان</summary>
    InReview = 1,
    /// <summary>تایید شده</summary>
    Approved = 2,
    /// <summary>رد شده</summary>
    Rejected = 3,
    /// <summary>باطل/آرشیو شده</summary>
    Archived = 4
}

/// <summary>پوشه آرشیو اسناد — درختی و نامحدود.</summary>
public class DocFolder
{
    public int Id { get; set; }

    public int? ParentId { get; set; }

    [MaxLength(200)]
    public string Name { get; set; } = "";

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>نمایش برای همه — همه کاربران حداقل سطح خواندن دارند.</summary>
    public bool IsPublic { get; set; }

    /// <summary>در حالت نمایش عمومی، دانلود هم آزاد باشد؟</summary>
    public bool PublicCanDownload { get; set; }

    public int CreatedByUserId { get; set; }

    [MaxLength(150)]
    public string CreatedByName { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public bool IsActive { get; set; } = true;
}

/// <summary>دسترسی یک کاربر روی یک پوشه (به زیرپوشه‌ها و مدارک داخل آن ارث می‌رسد).</summary>
public class DocFolderPermission
{
    public int Id { get; set; }
    public int FolderId { get; set; }
    public int UserId { get; set; }

    public DocAccessLevel Level { get; set; } = DocAccessLevel.Read;

    /// <summary>اجازه دانلود فایل پیوست</summary>
    public bool CanDownload { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>مدرک (سند) — کد یکتا، عنوان اجباری، تاریخ انقضا، سیاست ورژن.</summary>
public class ArchiveDocument
{
    public int Id { get; set; }

    public int FolderId { get; set; }

    [MaxLength(250)]
    public string Title { get; set; } = "";

    /// <summary>کد مدرک — اجباری و یکتا در کل سیستم</summary>
    [MaxLength(80)]
    public string Code { get; set; } = "";

    /// <summary>شماره مدرک نزد مشتری/کارفرما (اختیاری — یکتا نیست)</summary>
    [MaxLength(80)]
    public string? CustomerCode { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>تاریخ انقضای مدرک (اختیاری)</summary>
    public DateTime? ExpireDate { get; set; }

    /// <summary>
    /// اگر true: چند ورژن می‌توانند هم‌زمان فعال باشند.
    /// اگر false: فقط یک ورژن فعال است و با فعال شدن ورژن جدید، قبلی‌ها غیرفعال می‌شوند.
    /// </summary>
    public bool AllowMultipleActiveVersions { get; set; }

    /// <summary>نمایش برای همه — همه کاربران سطح خواندن دارند.</summary>
    public bool IsPublic { get; set; }
    public bool PublicCanDownload { get; set; }

    public int CreatedByUserId { get; set; }

    [MaxLength(150)]
    public string CreatedByName { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// وضعیت مدرک: فعال / غیرفعال.
    /// مدرک غیرفعال فقط-خواندنی است — ورژن جدید، ویرایش، لینک و گردش روی آن ممنوع است
    /// و در زیرمنوی «مدارک غیرفعال» نمایش داده می‌شود.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>غیرفعال‌سازی توسط چه کسی و چه زمانی</summary>
    public DateTime? DeactivatedAt { get; set; }

    [MaxLength(150)]
    public string? DeactivatedByName { get; set; }

    [MaxLength(500)]
    public string? DeactivateReason { get; set; }

    /// <summary>حذف نرم (soft delete) — از همه لیست‌ها حذف می‌شود</summary>
    public bool IsDeleted { get; set; }
}

/// <summary>دسترسی مستقیم یک کاربر روی یک مدرک (مکمل دسترسی پوشه — بیشترین سطح برنده است).</summary>
public class DocumentPermission
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public int UserId { get; set; }
    public DocAccessLevel Level { get; set; } = DocAccessLevel.Read;
    public bool CanDownload { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>نفرات گردش/تایید مدرک — الگو در سطح مدرک تعریف می‌شود و برای هر ورژن کپی می‌گردد.</summary>
public class DocumentApprover
{
    public int Id { get; set; }
    public int DocumentId { get; set; }

    /// <summary>null یعنی «الگوی مدرک»؛ مقدار دارد یعنی رکورد گردش همان ورژن.</summary>
    public int? VersionId { get; set; }

    public int UserId { get; set; }

    [MaxLength(150)]
    public string UserName { get; set; } = "";

    /// <summary>ترتیب نمایش (گردش موازی است؛ همه باید تایید کنند)</summary>
    public int Order { get; set; }

    /// <summary>0=در انتظار 1=تایید 2=رد</summary>
    public int Status { get; set; }

    [MaxLength(1000)]
    public string? Comment { get; set; }

    public DateTime? ActedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>ورژن مدرک — محتوا/پیوست‌ها روی ورژن ثبت می‌شوند.</summary>
public class DocumentVersion
{
    public int Id { get; set; }
    public int DocumentId { get; set; }

    /// <summary>شماره ورژن (۱، ۲، ۳ …)</summary>
    public int VersionNo { get; set; }

    [MaxLength(250)]
    public string? Title { get; set; }

    [MaxLength(2000)]
    public string? ChangeNote { get; set; }

    /// <summary>تاریخ انقضای همین ورژن (اختیاری)</summary>
    public DateTime? ExpireDate { get; set; }

    public DocVersionStatus Status { get; set; } = DocVersionStatus.Draft;

    /// <summary>ورژن فعال (قابل استناد)</summary>
    public bool IsActive { get; set; }

    /// <summary>فریز — تا پایان گردش تایید، قابل ویرایش نیست و به گردش عادی نمی‌آید.</summary>
    public bool IsFrozen { get; set; }

    /// <summary>کاربر خواسته این ورژن پس از تایید، فعال شود.</summary>
    public bool ActivateOnApprove { get; set; } = true;

    public int CreatedByUserId { get; set; }

    [MaxLength(150)]
    public string CreatedByName { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? ApprovedAt { get; set; }
}

/// <summary>لینک دو مدرک به هم (دوطرفه ذخیره می‌شود).</summary>
public class DocumentLink
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public int LinkedDocumentId { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }

    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// کار کارتابل آرشیو — دو نوع:
/// Approval  : تایید ورژن (برای نفرات گردش)
/// RelatedUpdate : بررسی/به‌روزرسانی مدرک مرتبط پس از ورژن خوردن مدرک لینک‌شده
/// </summary>
public class DocCartableTask
{
    public int Id { get; set; }

    [MaxLength(30)]
    public string Kind { get; set; } = "Approval";

    public int UserId { get; set; }
    public int DocumentId { get; set; }
    public int? VersionId { get; set; }

    /// <summary>مدرکی که باعث ایجاد این کار شده (در نوع RelatedUpdate)</summary>
    public int? SourceDocumentId { get; set; }

    [MaxLength(300)]
    public string Title { get; set; } = "";

    /// <summary>0=باز 1=انجام شده 2=رد شده</summary>
    public int Status { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? DoneAt { get; set; }
}

/// <summary>لاگ رویدادهای مدرک</summary>
public class DocumentLog
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public int? VersionId { get; set; }

    [MaxLength(50)]
    public string Action { get; set; } = "";

    [MaxLength(600)]
    public string? Detail { get; set; }

    public int UserId { get; set; }

    [MaxLength(150)]
    public string UserName { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
