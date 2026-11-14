namespace LicenseSigner.Data;

/// <summary>مشتری — یک ردیف به‌ازای هر قرارداد.</summary>
public class Customer
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? NationalId { get; set; }
    public string? Address { get; set; }
    public int? MergedIntoId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public List<ContactPerson> Contacts { get; set; } = new();
    public List<Installation> Installations { get; set; } = new();
    public List<LicenseRequest> Requests { get; set; } = new();
    public List<IssuedLicense> Licenses { get; set; } = new();
}

public class ContactPerson
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Title { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>یک نصب مشخص از نرم‌افزار مشتری — با شناسهٔ نصب (installationId) یکتا.</summary>
public class Installation
{
    public int Id { get; set; }
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string InstallationId { get; set; } = "";
    public string? MachineName { get; set; }
    public string? EngineVersion { get; set; }
    public string? EngineSchema { get; set; }
    public string? BuildDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }

    public List<LicenseRequest> Requests { get; set; } = new();
}

public class LicenseRequest
{
    public int Id { get; set; }
    public string RequestNo { get; set; } = "";
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int? InstallationRefId { get; set; }
    public Installation? InstallationRef { get; set; }
    public string InstallationId { get; set; } = "";
    public RequestType Type { get; set; }
    public RequestStatus Status { get; set; }
    public RequestChannel Channel { get; set; }
    public int RequestVersion { get; set; } = 2;
    public string PayloadJson { get; set; } = "";
    public string PayloadSha256 { get; set; } = "";
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public string? CustomerNote { get; set; }
    public string? InterestedModulesCsv { get; set; }
    public string? Deployment { get; set; }
    public string? MachineName { get; set; }
    public string? EngineVersion { get; set; }
    public string? EngineSchema { get; set; }
    public string? BuildDate { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public string? RequestedAtTehran { get; set; }
    public string? InternalNote { get; set; }
    public int? DuplicateOfRequestId { get; set; }
    public string? WarningsJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public List<RequestEvent> Events { get; set; } = new();
    public List<RequestAttachment> Attachments { get; set; } = new();
    public List<IdentityCheck> IdentityChecks { get; set; } = new();
    public List<LicenseDraft> Drafts { get; set; } = new();
    public List<IssuedLicense> Licenses { get; set; } = new();
}

public class RequestEvent
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public LicenseRequest? Request { get; set; }
    public string Kind { get; set; } = "";
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string Text { get; set; } = "";
    public string? ByName { get; set; }
    public DateTime At { get; set; }
}

/// <summary>پیوست ورودی (فایل درخواست/ایمیل) — هش SHA256 برای تشخیص تکراری و ممیزی.</summary>
public class RequestAttachment
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public LicenseRequest? Request { get; set; }
    public string FileName { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public string? ContentType { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>دروازهٔ پیش از «تأییدشده»: چه کسی، با چه روشی، چه زمانی.</summary>
public class IdentityCheck
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public LicenseRequest? Request { get; set; }
    public IdentityMethod Method { get; set; }
    public string VerifiedWith { get; set; } = "";
    public DateTime VerifiedAt { get; set; }
    public string? Note { get; set; }
    public string? ByName { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>پیش‌نویس امضا — پروفایل JSON که مبنای اجرای LicenseTool است.</summary>
public class LicenseDraft
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public LicenseRequest? Request { get; set; }
    public string Deployment { get; set; } = "OnPrem";
    public string TermMode { get; set; } = "months";
    public int? Months { get; set; } = 12;
    public DateTime? Until { get; set; }
    public DateTime? UpdatesUntil { get; set; }
    public string UserMode { get; set; } = "Named";
    public int UserMax { get; set; }
    public int Exempt { get; set; }
    public string AppliesTo { get; set; } = "Global";
    public string? PresetsCsv { get; set; }
    public string? ModulesCsv { get; set; }
    public string? QuotasJson { get; set; }
    public string? Note { get; set; }
    public string ProfileJson { get; set; } = "";
    public string? ByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>لایسنس صادرشده — <b>تغییرناپذیر</b>؛ اصلاح یعنی سریال تازه + Superseded.</summary>
public class IssuedLicense
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public LicenseRequest? Request { get; set; }
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string InstallationId { get; set; } = "";
    public string Serial { get; set; } = "";
    public string KeyId { get; set; } = "";
    public string Algorithm { get; set; } = "RS256";
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long SizeBytes { get; set; }
    public string? PayloadJson { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public DateTime? UpdatesUntil { get; set; }
    public LicenseStatus Status { get; set; }
    public bool VerifiedByPublicKey { get; set; }
    public bool VerifiedByInstallation { get; set; }
    public int? SupersededByLicenseId { get; set; }
    public string? RevocationNote { get; set; }
    public string? ByName { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<Delivery> Deliveries { get; set; } = new();
}

public class Delivery
{
    public int Id { get; set; }
    public int IssuedLicenseId { get; set; }
    public IssuedLicense? IssuedLicense { get; set; }
    public DeliveryChannel Channel { get; set; }
    public DateTime At { get; set; }
    public string? Recipient { get; set; }
    public string? Subject { get; set; }
    public string? Note { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int? ByUserId { get; set; }
    public string? ByName { get; set; }
}

public class Preset
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Json { get; set; }
    public bool IsActive { get; set; } = true;
}

public class SupportUser
{
    public int Id { get; set; }
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public SupportRole Role { get; set; }
    public string PasswordHash { get; set; } = "";
    public int PasswordIterations { get; set; }
    public bool MustChangePassword { get; set; }
    public int FailedCount { get; set; }
    public DateTime? LockedUntil { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

/// <summary>ممیزی فقط‌افزودی با زنجیرهٔ هش (تغییر/حذف ردیف میانی = زنجیرهٔ شکسته).</summary>
public class AuditLog
{
    public long Id { get; set; }
    public DateTime At { get; set; }
    public int? UserId { get; set; }
    public string UserName { get; set; } = "";
    public string Action { get; set; } = "";
    public string Entity { get; set; } = "";
    public string? EntityId { get; set; }
    public string? DataJson { get; set; }
    public string PrevHash { get; set; } = "";
    public string Hash { get; set; } = "";
}

public class MailTemplate
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
}

public class Setting
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}
