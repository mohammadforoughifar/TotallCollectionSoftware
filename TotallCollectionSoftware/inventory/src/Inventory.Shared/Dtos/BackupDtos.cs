namespace Inventory.Shared.Dtos;

public class BackupSettingsDto
{
    public bool Enabled { get; set; }
    public string? CentralServerUrl { get; set; }
    public string? CompanyCode { get; set; }
    public string? CompanyName { get; set; }
    public string? ApiKey { get; set; }
    public bool ClearKey { get; set; }
    public bool HasKey { get; set; }
    public bool TriggerOnLastSession { get; set; } = true;
    public bool TriggerDaily { get; set; } = true;
    public string DailyTime { get; set; } = "23:00";
    public int LastSessionGraceSeconds { get; set; } = 90;
    public int CooldownMinutes { get; set; } = 15;
    public string? LocalBackupPath { get; set; }
    public int LocalKeepCount { get; set; } = 5;
}

public class BackupJobDto
{
    public string Id { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "";
    public string DatabaseName { get; set; } = "";
    public string? FileName { get; set; }
    public long SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public string? SqlVersion { get; set; }
    public string? SqlEdition { get; set; }
    public string? Compression { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

public class BackupStatusDto
{
    public BackupSettingsDto Settings { get; set; } = new();
    public int ActiveSessions { get; set; }
    public int PendingJobs { get; set; }
    public int FailedJobs { get; set; }
    public BackupJobDto? LastJob { get; set; }
    public BackupJobDto? LastSentJob { get; set; }
    public string? Provider { get; set; }
    public string? SqlVersion { get; set; }
    public string? SqlEdition { get; set; }
    public bool NativeCompressionSupported { get; set; }
}

public class BackupSessionHeartbeatDto
{
    public string SessionId { get; set; } = "";
}

public class CentralBackupCompanyDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int RetentionDays { get; set; } = 30;
    public long MaxStorageMb { get; set; } = 10240;
    public string? Note { get; set; }
    public string? ApiKey { get; set; }
    public string? ApiKeyHint { get; set; }
    public DateTime? LastSeenAt { get; set; }
}

public class CentralBackupReceiptDto
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string CompanyCode { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string DatabaseName { get; set; } = "";
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? SqlVersion { get; set; }
    public string? SqlEdition { get; set; }
    public DateTime ReceivedAt { get; set; }
}
