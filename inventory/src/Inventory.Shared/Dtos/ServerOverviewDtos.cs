namespace Inventory.Shared.Dtos;

/// <summary>Read-only presentation contracts. Never include Vault secrets or raw Redfish payloads.</summary>
public sealed class ServerStatusInput
{
    public int Id { get; set; }
    public string? Health { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public string? ConnectionError { get; set; }
    public bool ConnectionAlert { get; set; }
    public bool CriticalAlert { get; set; }
    public bool WarningAlert { get; set; }
    public bool MonitoringEnabled { get; set; }
    public int IntervalMinutes { get; set; } = 15;
    public string Lifecycle { get; set; } = "Active";
    public DateTime? MaintenanceUntil { get; set; }
}

public sealed class ServerDisplayStatus
{
    public int Id { get; set; }
    // Healthy | Warning | Critical | Disconnected | Stale | Unknown
    public string State { get; set; } = "Unknown";
    // Reachable = last recorded attempt; never a live availability guarantee.
    public string Connection { get; set; } = "Unknown";
    public string Freshness { get; set; } = "Unknown";
    public string LastKnownHealth { get; set; } = "Unknown";
    public bool InMaintenance { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public int StaleAfterMinutes { get; set; }
}

public static class ServerStatusPolicy
{
    public static ServerDisplayStatus Evaluate(ServerStatusInput input, DateTime now)
    {
        var minutes = input.MonitoringEnabled && input.Lifecycle == "Active"
            ? Math.Clamp(input.IntervalMinutes, 5, 1440) * 2 : 1440;
        var disconnected = !string.IsNullOrWhiteSpace(input.ConnectionError) || input.ConnectionAlert;
        var hasAttempt = input.LastAttemptAt.HasValue;
        // Existing DB timestamps are local wall-clock values. Evaluate only on the API,
        // with its clock; clients must not compare them against the browser timezone.
        var stale = hasAttempt && (now - input.LastAttemptAt!.Value >= TimeSpan.FromMinutes(minutes)
            || input.LastAttemptAt.Value > now.AddMinutes(5));
        var health = input.Health?.ToUpperInvariant();
        var state = disconnected ? "Disconnected" : !hasAttempt ? "Unknown" : stale ? "Stale"
            : input.CriticalAlert || health == "CRITICAL" ? "Critical"
            : input.WarningAlert || health == "WARNING" ? "Warning"
            : health == "OK" ? "Healthy" : "Unknown";
        return new ServerDisplayStatus
        {
            Id = input.Id, State = state,
            Connection = disconnected ? "Disconnected" : !hasAttempt || stale ? "Unknown" : "Reachable",
            Freshness = !hasAttempt ? "Unknown" : stale ? "Stale" : "Fresh",
            LastKnownHealth = input.Health ?? "Unknown", LastAttemptAt = input.LastAttemptAt,
            InMaintenance = input.Lifecycle == "Maintenance" || input.MaintenanceUntil > now,
            StaleAfterMinutes = minutes
        };
    }
}

public sealed class ServerDashboardDto
{
    public DateTime GeneratedAt { get; set; }
    public int Total { get; set; }
    public int Healthy { get; set; }
    public int Warning { get; set; }
    public int Critical { get; set; }
    public int Disconnected { get; set; }
    public int Stale { get; set; }
    public int Unknown { get; set; }
    // Orthogonal to health; maintenance servers still belong to one health bucket.
    public int Maintenance { get; set; }
    public int OpenAlertCount { get; set; }
    public int MonitoringEnabledCount { get; set; }
    public List<ServerDisplayStatus> Servers { get; set; } = new();
}

public class ServerProfileDto
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string Name { get; set; } = "";
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? AssetTag { get; set; }
    public string IloAddress { get; set; } = "";
    public int IloPort { get; set; }
    public int? CredentialId { get; set; }
    public string? IloVersion { get; set; }
    public string? Role { get; set; }
    public string? Environment { get; set; }
    public string Criticality { get; set; } = "Normal";
    public string Status { get; set; } = "Active";
    public string? Rack { get; set; }
    public int? RackUnit { get; set; }
    public string? OperatingSystem { get; set; }
    public DateTime? WarrantyEndDate { get; set; }
    public string? Notes { get; set; }
    public bool MonitoringEnabled { get; set; }
    public int MonitoringIntervalMinutes { get; set; }
    public DateTime? LastAutomaticPollAt { get; set; }
    public int? ResponsibleUserId { get; set; }
    public string? ResponsibleName { get; set; }
    public bool AlertNotificationsEnabled { get; set; }
    public DateTime? MaintenanceUntil { get; set; }
    public string? MaintenanceNote { get; set; }
    public DateTime? LastHealthCheckAt { get; set; }
    public DateTime? LastInventoryAt { get; set; }
    public DateTime? LastHardwareDetailsAt { get; set; }
    public string? LastHealthStatus { get; set; }
    public string? LastPowerState { get; set; }
    public string? LastHealthMessage { get; set; }
    public string? LastConnectionError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByName { get; set; }
    public int OpenAlertCount { get; set; }
}

public sealed class ServerDetailsDto
{
    public ServerProfileDto Server { get; set; } = new();
    public string CompanyName { get; set; } = "";
    public string? ResponsibleName { get; set; }
    public ServerDisplayStatus Status { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
    public List<ServerHealthPointDto> Snapshots { get; set; } = new();
    public List<ServerOperationDto> Logs { get; set; } = new();
    public int SnapshotCount { get; set; }
    public int LogCount { get; set; }
}

public sealed class ServerHealthPointDto
{
    public long Id { get; set; }
    public DateTime CapturedAt { get; set; }
    public string OverallHealth { get; set; } = "Unknown";
    public string? PowerState { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? BiosVersion { get; set; }
    public string? Message { get; set; }
}

public sealed class ServerOperationDto
{
    public long Id { get; set; }
    public DateTime At { get; set; }
    public string? UserName { get; set; }
    public string Action { get; set; } = "";
    public string Result { get; set; } = "";
    public string? Details { get; set; }
    public string? ClientIp { get; set; }
}

public sealed class ServerHardwareDto
{
    public DateTime? CapturedAt { get; set; }
    public List<ServerComponentDto> Components { get; set; } = new();
    public List<ServerEventDto> Events { get; set; } = new();
}
public sealed class ServerComponentDto
{
    public long Id { get; set; }
    public string ComponentType { get; set; } = "";
    public string ComponentKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string Health { get; set; } = "Unknown";
    public string? State { get; set; }
    public string? Reading { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? Message { get; set; }
}
public sealed class ServerEventDto
{
    public long Id { get; set; }
    public string Severity { get; set; } = "Unknown";
    public string Message { get; set; } = "";
    public string? Origin { get; set; }
    public DateTime? OccurredAt { get; set; }
    public DateTime LastSeenAt { get; set; }
}
public sealed class ServerAlertDto
{
    public long Id { get; set; }
    public string Category { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
    public string Status { get; set; } = "";
    public int? WorkOrderId { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public string? AcknowledgedByName { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
public sealed class ServerAlertPageDto
{
    public int Total { get; set; }
    public int OpenCount { get; set; }
    public List<ServerAlertDto> Items { get; set; } = new();
}
