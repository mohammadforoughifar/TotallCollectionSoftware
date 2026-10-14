using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.ItAssets;

/// <summary>Read-only, company-scoped dashboard. No network polling or secret decryption.</summary>
public static class ServerOverviewReader
{
    public static async Task<List<ServerStatusInput>> ReadInputsAsync(AppDbContext db, int companyId,
        int? serverId = null, CancellationToken ct = default)
    {
        return await db.ManagedServers.AsNoTracking()
            .Where(s => s.CompanyId == companyId && (!serverId.HasValue || s.Id == serverId.Value))
            .OrderBy(s => s.Id)
            .Select(s => new ServerStatusInput
            {
                Id = s.Id, Health = s.LastHealthStatus, LastAttemptAt = s.LastHealthCheckAt,
                ConnectionError = s.LastConnectionError,
                MonitoringEnabled = s.MonitoringEnabled, IntervalMinutes = s.MonitoringIntervalMinutes,
                Lifecycle = s.Status, MaintenanceUntil = s.MaintenanceUntil,
                ConnectionAlert = db.ServerAlerts.Any(a => a.ManagedServerId == s.Id && a.Status != "Resolved" && a.Category == "Connection"),
                CriticalAlert = db.ServerAlerts.Any(a => a.ManagedServerId == s.Id && a.Status != "Resolved" && a.Category != "Connection" && a.Severity == "Critical"),
                WarningAlert = db.ServerAlerts.Any(a => a.ManagedServerId == s.Id && a.Status != "Resolved" && a.Category != "Connection" && a.Severity == "Warning")
            }).ToListAsync(ct);
    }

    public static async Task<ServerDashboardDto> DashboardAsync(AppDbContext db, int companyId, CancellationToken ct)
    {
        var inputs = await ReadInputsAsync(db, companyId, ct: ct);
        var now = DateTime.Now;
        var states = inputs.Select(s => ServerStatusPolicy.Evaluate(s, now)).ToList();
        var openCount = await (from a in db.ServerAlerts.AsNoTracking()
                               join s in db.ManagedServers.AsNoTracking() on a.ManagedServerId equals s.Id
                               where s.CompanyId == companyId && a.Status != "Resolved"
                               select a.Id).CountAsync(ct);
        return new ServerDashboardDto
        {
            GeneratedAt = now, Total = states.Count, Servers = states,
            Healthy = states.Count(s => s.State == "Healthy"), Warning = states.Count(s => s.State == "Warning"),
            Critical = states.Count(s => s.State == "Critical"), Disconnected = states.Count(s => s.State == "Disconnected"),
            Stale = states.Count(s => s.State == "Stale"), Unknown = states.Count(s => s.State == "Unknown"),
            Maintenance = states.Count(s => s.InMaintenance), OpenAlertCount = openCount,
            MonitoringEnabledCount = inputs.Count(s => s.MonitoringEnabled && s.Lifecycle == "Active")
        };
    }
}
