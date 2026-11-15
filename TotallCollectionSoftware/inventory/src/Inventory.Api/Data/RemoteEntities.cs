namespace Inventory.Api.Data;

public sealed class RemoteSetting
{
    public int Id { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public int ConsentTimeoutSeconds { get; set; } = 60;
    public int SessionIdleTimeoutMinutes { get; set; } = 10;
    public int FrameIntervalMs { get; set; } = 250;
    public int JpegQuality { get; set; } = 55;
    public int MaxWidth { get; set; } = 1280;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

public sealed class RemoteDevice
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string CompanyCode { get; set; } = "";
    public string DeviceKeyHash { get; set; } = "";
    public string DeviceKeyHint { get; set; } = "";
    public string Os { get; set; } = "";
    public string AgentVersion { get; set; } = "";
    public bool IsOnline { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime? LastSeenAt { get; set; }
    public string? LastIp { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

public sealed class RemoteSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DeviceId { get; set; } = "";
    public int TechnicianUserId { get; set; }
    public string TechnicianName { get; set; } = "";
    /// <summary>Pending | Active | Rejected | Expired | Ended</summary>
    public string Status { get; set; } = "Pending";
    public bool AllowControl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime ExpiresAt { get; set; } = DateTime.Now.AddMinutes(1);
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public DateTime LastActivityAt { get; set; } = DateTime.Now;
    public string? EndReason { get; set; }
}

public sealed class RemoteAuditLog
{
    public int Id { get; set; }
    public string? SessionId { get; set; }
    public string DeviceId { get; set; } = "";
    public int? UserId { get; set; }
    public string Action { get; set; } = "";
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
