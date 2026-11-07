using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>تنظیمات بک‌آپ خودکار سمت مشتری — تک‌ردیفی با Id=1.</summary>
public class BackupSetting
{
    public int Id { get; set; } = 1;
    public bool Enabled { get; set; }
    [MaxLength(300)] public string? CentralServerUrl { get; set; }
    [MaxLength(50)] public string? CompanyCode { get; set; }
    [MaxLength(200)] public string? CompanyName { get; set; }
    /// <summary>کلید API محافظت‌شده با DataProtection — هرگز به کلاینت برگردانده نمی‌شود.</summary>
    [MaxLength(2000)] public string? ApiKeyProtected { get; set; }
    public bool TriggerOnLastSession { get; set; } = true;
    public bool TriggerDaily { get; set; } = true;
    [MaxLength(5)] public string DailyTime { get; set; } = "23:00";
    public int LastSessionGraceSeconds { get; set; } = 90;
    public int CooldownMinutes { get; set; } = 15;
    [MaxLength(500)] public string? LocalBackupPath { get; set; }
    public int LocalKeepCount { get; set; } = 5;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public DateTime? LastDailyRunDate { get; set; }
    public DateTime? LastAutoTriggerAt { get; set; }
}

/// <summary>نشست مرورگر برای تشخیص «آخرین کاربر فعال».</summary>
public class BackupBrowserSession
{
    [MaxLength(32)] public string Id { get; set; } = "";
    public int UserId { get; set; }
    [MaxLength(150)] public string UserName { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime LastHeartbeatAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    [MaxLength(30)] public string? CloseReason { get; set; }
    [MaxLength(300)] public string? UserAgent { get; set; }
}

/// <summary>صف بک‌آپ محلی — هر ردیف یک بک‌آپ و تلاش‌های ارسال آن.</summary>
public class BackupJob
{
    [MaxLength(32)] public string Id { get; set; } = "";
    [MaxLength(32)] public string ExternalId { get; set; } = "";
    [MaxLength(20)] public string Reason { get; set; } = "Manual";
    [MaxLength(20)] public string Status { get; set; } = "Pending";
    [MaxLength(128)] public string DatabaseName { get; set; } = "";
    [MaxLength(260)] public string? FileName { get; set; }
    [MaxLength(1000)] public string? FilePath { get; set; }
    public long SizeBytes { get; set; }
    [MaxLength(64)] public string? Sha256 { get; set; }
    [MaxLength(100)] public string? SqlVersion { get; set; }
    [MaxLength(100)] public string? SqlEdition { get; set; }
    [MaxLength(20)] public string? Compression { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public int Attempts { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    [MaxLength(2000)] public string? LastError { get; set; }
}

/// <summary>شرکت مشتری در سرور مرکزیِ دریافت بک‌آپ.</summary>
public class CentralBackupCompany
{
    public int Id { get; set; }
    [MaxLength(50)] public string Code { get; set; } = "";
    [MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(100)] public string ApiKeyHash { get; set; } = "";
    [MaxLength(20)] public string? ApiKeyHint { get; set; }
    public bool IsActive { get; set; } = true;
    public int RetentionDays { get; set; } = 30;
    public long MaxStorageMb { get; set; } = 10240;
    [MaxLength(500)] public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? KeyRotatedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
}

/// <summary>رسید بک‌آپ دریافت‌شده در سرور مرکزی.</summary>
public class CentralBackupReceipt
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    [MaxLength(32)] public string ExternalId { get; set; } = "";
    [MaxLength(128)] public string DatabaseName { get; set; } = "";
    [MaxLength(260)] public string FileName { get; set; } = "";
    [MaxLength(1000)] public string StoredPath { get; set; } = "";
    public long SizeBytes { get; set; }
    [MaxLength(64)] public string Sha256 { get; set; } = "";
    [MaxLength(20)] public string Reason { get; set; } = "";
    [MaxLength(100)] public string? SqlVersion { get; set; }
    [MaxLength(100)] public string? SqlEdition { get; set; }
    public DateTime ReceivedAt { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "Stored";
}
