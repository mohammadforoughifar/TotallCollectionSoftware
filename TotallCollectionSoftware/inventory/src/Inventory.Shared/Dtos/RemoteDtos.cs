namespace Inventory.Shared.Dtos;

public sealed class RemoteDeviceDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string CompanyCode { get; set; } = "";
    public string Os { get; set; } = "";
    public string AgentVersion { get; set; } = "";
    public bool IsOnline { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public string? LastIp { get; set; }
    public string? KeyHint { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class RemoteDeviceCreatedDto
{
    public RemoteDeviceDto Device { get; set; } = new();
    public string DeviceKey { get; set; } = "";
}

public sealed class RemoteSessionDto
{
    public string Id { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public int TechnicianUserId { get; set; }
    public string TechnicianName { get; set; } = "";
    public string Status { get; set; } = "";
    public bool AllowControl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? EndReason { get; set; }
}

public sealed class RemoteSettingsDto
{
    public bool Enabled { get; set; } = true;
    public int ConsentTimeoutSeconds { get; set; } = 60;
    public int SessionIdleTimeoutMinutes { get; set; } = 10;
    public int FrameIntervalMs { get; set; } = 250;
    public int JpegQuality { get; set; } = 55;
    public int MaxWidth { get; set; } = 1280;
    public DateTime UpdatedAt { get; set; }
}

public sealed class RemoteAuditDto
{
    public int Id { get; set; }
    public string? SessionId { get; set; }
    public string DeviceId { get; set; } = "";
    public int? UserId { get; set; }
    public string Action { get; set; } = "";
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class CreateRemoteDeviceRequest
{
    public string Name { get; set; } = "";
    public string CompanyCode { get; set; } = "";
}

public sealed class UpdateRemoteDeviceRequest
{
    public string? Name { get; set; }
    public bool? IsEnabled { get; set; }
}

public sealed class CreateRemoteSessionRequest
{
    public string DeviceId { get; set; } = "";
    public bool ViewOnly { get; set; }
}
