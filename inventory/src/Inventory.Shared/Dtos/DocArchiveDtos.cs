namespace Inventory.Shared.Dtos;

// ============================================================
//  ماژول آرشیو اسناد و مدارک — DTOها
// ============================================================

/// <summary>سطح دسترسی (هم‌ارز DocAccessLevel در بک‌اند)</summary>
public enum DocAccessLevelDto
{
    None = 0,
    View = 1,
    Read = 2,
    Write = 3,
    Full = 4
}

public enum DocVersionStatusDto
{
    Draft = 0,
    InReview = 1,
    Approved = 2,
    Rejected = 3,
    Archived = 4
}

/// <summary>پوشه آرشیو</summary>
public class DocFolderDto
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsPublic { get; set; }
    public bool PublicCanDownload { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>تعداد مدارک مستقیم داخل پوشه</summary>
    public int DocumentCount { get; set; }

    /// <summary>سطح دسترسی کاربر جاری روی این پوشه</summary>
    public DocAccessLevelDto MyLevel { get; set; }
    public bool MyCanDownload { get; set; }

    /// <summary>دسترسی‌های تعریف‌شده (فقط در حالت ویرایش برای مدیر)</summary>
    public List<DocPermissionDto> Permissions { get; set; } = new();
}

/// <summary>یک ردیف دسترسی کاربر</summary>
public class DocPermissionDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public DocAccessLevelDto Level { get; set; } = DocAccessLevelDto.Read;
    public bool CanDownload { get; set; }
}

/// <summary>نفر گردش/تایید</summary>
public class DocApproverDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public int Order { get; set; }
    /// <summary>0=در انتظار 1=تایید 2=رد</summary>
    public int Status { get; set; }
    public string? Comment { get; set; }
    public DateTime? ActedAt { get; set; }
}

/// <summary>ورژن مدرک</summary>
public class DocVersionDto
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public int VersionNo { get; set; }
    public string? Title { get; set; }
    public string? ChangeNote { get; set; }
    public DateTime? ExpireDate { get; set; }
    public DocVersionStatusDto Status { get; set; }
    public bool IsActive { get; set; }
    public bool IsFrozen { get; set; }
    public bool ActivateOnApprove { get; set; } = true;
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public int AttachmentCount { get; set; }

    /// <summary>نفرات گردش این ورژن</summary>
    public List<DocApproverDto> Approvers { get; set; } = new();

    /// <summary>آیا کاربر جاری تاییدکننده در انتظارِ این ورژن است؟</summary>
    public bool IsMyTurn { get; set; }
}

/// <summary>مدرک — خلاصه برای لیست</summary>
public class DocumentListDto
{
    public int Id { get; set; }
    public int FolderId { get; set; }
    public string FolderName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Code { get; set; } = "";

    /// <summary>شماره مدرک مشتری</summary>
    public string? CustomerCode { get; set; }

    public DateTime? ExpireDate { get; set; }
    public bool IsExpired { get; set; }
    public bool AllowMultipleActiveVersions { get; set; }
    public bool IsPublic { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>دلیل و اطلاعات غیرفعال‌سازی</summary>
    public DateTime? DeactivatedAt { get; set; }
    public string? DeactivatedByName { get; set; }
    public string? DeactivateReason { get; set; }

    public int VersionCount { get; set; }
    public int ActiveVersionNo { get; set; }
    public DocVersionStatusDto LastVersionStatus { get; set; }
    public int LinkCount { get; set; }

    public DocAccessLevelDto MyLevel { get; set; }
    public bool MyCanDownload { get; set; }
}

/// <summary>مدرک — کامل (فرم ثبت/ویرایش و صفحه جزئیات)</summary>
public class DocumentDto
{
    public int Id { get; set; }
    public int FolderId { get; set; }
    public string FolderName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Code { get; set; } = "";

    /// <summary>شماره مدرک مشتری</summary>
    public string? CustomerCode { get; set; }

    public string? Description { get; set; }
    public DateTime? ExpireDate { get; set; }
    public bool AllowMultipleActiveVersions { get; set; }
    public bool IsPublic { get; set; }
    public bool PublicCanDownload { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? DeactivatedAt { get; set; }
    public string? DeactivatedByName { get; set; }
    public string? DeactivateReason { get; set; }

    /// <summary>شناسه مدارکی که باید به این مدرک لینک شوند (چندانتخابی در فرم)</summary>
    public List<int> LinkedDocumentIds { get; set; } = new();

    /// <summary>نفرات گردش (الگوی مدرک)</summary>
    public List<DocApproverDto> Approvers { get; set; } = new();

    /// <summary>دسترسی‌های مستقیم مدرک</summary>
    public List<DocPermissionDto> Permissions { get; set; } = new();

    public List<DocVersionDto> Versions { get; set; } = new();
    public List<DocLinkDto> Links { get; set; } = new();
    public List<DocLogDto> Logs { get; set; } = new();

    public DocAccessLevelDto MyLevel { get; set; }
    public bool MyCanDownload { get; set; }

    /// <summary>در ثبت اولیه: آیا ورژن ۱ بلافاصله فعال باشد</summary>
    public bool FirstVersionActive { get; set; } = true;
}

/// <summary>مدرک لینک‌شده</summary>
public class DocLinkDto
{
    public int Id { get; set; }
    public int LinkedDocumentId { get; set; }
    public string Code { get; set; } = "";
    public string? CustomerCode { get; set; }
    public string Title { get; set; } = "";
    public string? Note { get; set; }
    public bool IsActive { get; set; } = true;
    public int ActiveVersionNo { get; set; }
}

/// <summary>لاگ رویداد</summary>
public class DocLogDto
{
    public int Id { get; set; }
    public string Action { get; set; } = "";
    public string? Detail { get; set; }
    public string UserName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int? VersionId { get; set; }
}

/// <summary>ورودی ساخت ورژن جدید</summary>
public class DocVersionCreateDto
{
    public int DocumentId { get; set; }
    public string? Title { get; set; }
    public string? ChangeNote { get; set; }
    public DateTime? ExpireDate { get; set; }

    /// <summary>پس از تایید، این ورژن فعال شود</summary>
    public bool ActivateOnApprove { get; set; } = true;

    /// <summary>نفرات گردش این ورژن — خالی یعنی از الگوی مدرک استفاده شود</summary>
    public List<int> ApproverUserIds { get; set; } = new();

    /// <summary>بلافاصله به گردش برود (فریز شود و در کارتابل بنشیند)</summary>
    public bool SendToFlow { get; set; } = true;
}

/// <summary>اقدام تایید/رد</summary>
public class DocApprovalActionDto
{
    public int VersionId { get; set; }
    public bool Approve { get; set; }
    public string? Comment { get; set; }
}

/// <summary>آیتم کارتابل آرشیو</summary>
public class DocCartableItemDto
{
    public int Id { get; set; }
    public string Kind { get; set; } = "Approval";
    public int DocumentId { get; set; }
    public int? VersionId { get; set; }
    public string DocumentCode { get; set; } = "";
    public string DocumentTitle { get; set; } = "";
    public int VersionNo { get; set; }
    public string? SourceDocumentCode { get; set; }
    public string Title { get; set; } = "";
    public int Status { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>ورودی لینک چندتایی مدارک</summary>
public class DocLinkSaveDto
{
    public int DocumentId { get; set; }
    /// <summary>لیست مدارکی که باید لینک شوند (چندانتخابی)</summary>
    public List<int> LinkedDocumentIds { get; set; } = new();
    public string? Note { get; set; }
}

/// <summary>ورودی تغییر وضعیت فعال/غیرفعال مدرک</summary>
public class DocSetActiveDto
{
    public bool IsActive { get; set; }
    public string? Reason { get; set; }
}

/// <summary>ورودی ذخیره دسترسی‌ها</summary>
public class DocPermissionsSaveDto
{
    public bool IsPublic { get; set; }
    public bool PublicCanDownload { get; set; }
    public List<DocPermissionDto> Items { get; set; } = new();
}

/// <summary>داده‌های کمکی صفحه آرشیو</summary>
public class DocArchiveLookups
{
    public List<LookupItem> Users { get; set; } = new();
    public List<LookupItem> Documents { get; set; } = new();
}
