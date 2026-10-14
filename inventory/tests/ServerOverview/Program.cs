using System.Security.Claims;
using System.Text.Json;
using Inventory.Api.Controllers;
using Inventory.Api.Data;
using Inventory.Api.Services.Core;
using Inventory.Api.Services.ItAssets;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

// Real production controller, EF model, SQLite provider, status policy and permissions.
// No outbound Redfish, Vault or notification operations are invoked by these read-only tests.
var count = 0;
void Check(bool ok, string message)
{
    if (!ok) throw new Exception("FAIL: " + message);
    count++; Console.WriteLine("PASS: " + message);
}
var now = DateTime.Now;
ServerStatusInput Fresh(string health = "OK") => new()
{ Id = 1, Health = health, LastAttemptAt = now.AddMinutes(-1), MonitoringEnabled = true, IntervalMinutes = 15 };
string State(ServerStatusInput input) => ServerStatusPolicy.Evaluate(input, now).State;
Check(State(Fresh()) == "Healthy", "Fresh OK is healthy");
Check(State(Fresh("Warning")) == "Warning", "Warning is not healthy");
Check(State(Fresh("Critical")) == "Critical", "Critical remains critical");
Check(State(Fresh("Unknown")) == "Unknown", "Unknown stays unknown");
var input = Fresh(); input.ConnectionError = "TLS failure";
Check(State(input) == "Disconnected", "Connection failure overrides historical OK");
input = Fresh(); input.ConnectionAlert = true;
Check(State(input) == "Disconnected", "Open connection alert overrides historical OK");
input = Fresh(); input.LastAttemptAt = now.AddMinutes(-30);
Check(State(input) == "Stale", "Exactly two intervals is stale");
input = Fresh(); input.LastAttemptAt = now.AddMinutes(-29);
Check(State(input) == "Healthy", "Less than two intervals is fresh");
input = Fresh(); input.LastAttemptAt = now.AddMinutes(6);
Check(State(input) == "Stale", "Future clock anomaly is not healthy");
input = Fresh(); input.LastAttemptAt = null;
Check(State(input) == "Unknown", "No observation is unknown, even with stored OK");
input = Fresh(); input.CriticalAlert = true;
Check(State(input) == "Critical", "Unresolved critical hardware alert overrides OK");
input = Fresh("Critical"); input.WarningAlert = true;
Check(State(input) == "Critical", "Warning alert cannot downgrade critical health");
input = Fresh(); input.MaintenanceUntil = now.AddHours(1);
var display = ServerStatusPolicy.Evaluate(input, now);
Check(display.InMaintenance && display.State == "Healthy", "Maintenance is orthogonal to health");
input.MaintenanceUntil = now;
Check(!ServerStatusPolicy.Evaluate(input, now).InMaintenance, "Expired maintenance is not counted");
input.Lifecycle = "Maintenance";
Check(ServerStatusPolicy.Evaluate(input, now).InMaintenance, "Lifecycle maintenance is counted");
input = Fresh(); input.MonitoringEnabled = false; input.LastAttemptAt = now.AddHours(-23);
Check(State(input) == "Healthy", "Manual-only freshness uses explicit 24-hour threshold");
input.LastAttemptAt = now.AddHours(-24);
Check(State(input) == "Stale", "Manual-only observations expire at 24 hours");
input = Fresh(); input.IntervalMinutes = -20;
Check(ServerStatusPolicy.Evaluate(input, now).StaleAfterMinutes == 10, "Invalid low interval is clamped");
input.IntervalMinutes = int.MaxValue;
Check(ServerStatusPolicy.Evaluate(input, now).StaleAfterMinutes == 2880, "Invalid high interval cannot overflow");

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
await db.Database.EnsureCreatedAsync();
db.Users.Add(new User { Id = 71, Username = "server-reader", IsActive = true, Role = "Operator" });
db.SystemCompanies.AddRange(new SystemCompany { Id = 11, Name = "Company A" }, new SystemCompany { Id = 12, Name = "Company B" }, new SystemCompany { Id = 13, Name = "Empty company" });
db.UserCompanyAccesses.AddRange(new UserCompanyAccess { UserId = 71, CompanyId = 11 }, new UserCompanyAccess { UserId = 71, CompanyId = 13 });
for (var i = 1; i <= 7; i++)
    db.ManagedServers.Add(new ManagedServer { Id = i, CompanyId = 11, Name = "server-" + i, IloAddress = "ilo-" + i + ".example.invalid",
        LastHealthCheckAt = now.AddMinutes(-1), LastHealthStatus = "OK", MonitoringEnabled = true });
db.ManagedServers.Add(new ManagedServer { Id = 88, CompanyId = 12, Name = "Hidden-company-server", IloAddress = "hidden.example.invalid" });
await db.SaveChangesAsync();
var servers = await db.ManagedServers.Where(s => s.CompanyId == 11).OrderBy(s => s.Id).ToListAsync();
servers[1].LastHealthStatus = "Warning";
servers[2].LastHealthStatus = "Critical";
servers[3].LastConnectionError = "Connection failed";
servers[4].LastHealthCheckAt = now.AddHours(-2);
servers[5].LastHealthCheckAt = null;
servers[6].MaintenanceUntil = now.AddHours(2);
for (var i = 0; i < 135; i++) db.ServerAlerts.Add(new ServerAlert
{ ManagedServerId = 2, Category = "Health", Severity = "Warning", Message = "warning " + i, OpenedAt = now.AddMinutes(-i), LastSeenAt = now.AddMinutes(-i) });
db.ServerAlerts.Add(new ServerAlert { ManagedServerId = 2, Category = "Health", Severity = "Warning", Status = "Resolved", Message = "resolved" });
db.ServerAlerts.Add(new ServerAlert { ManagedServerId = 88, Category = "Connection", Severity = "Critical", Message = "PRIVATE" });
for (var i = 0; i < 70; i++) db.ServerHealthSnapshots.Add(new ServerHealthSnapshot
{ ManagedServerId = 1, CapturedAt = now.AddMinutes(-i), OverallHealth = "OK" });
for (var i = 0; i < 110; i++) db.ServerOperationLogs.Add(new ServerOperationLog
{ ManagedServerId = 1, At = now.AddMinutes(-i), Action = "AutoCollect" });
db.ServerHardwareComponentSnapshots.Add(new ServerHardwareComponentSnapshot
{ ManagedServerId = 1, CapturedAt = now.AddHours(-1), ComponentType = "Disk", ComponentKey = "old", Name = "old" });
db.ServerHardwareComponentSnapshots.Add(new ServerHardwareComponentSnapshot
{ ManagedServerId = 1, CapturedAt = now, ComponentType = "Disk", ComponentKey = "latest", Name = "latest" });
await db.SaveChangesAsync();
ServersController Controller(bool allowed = true)
{
    var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "71"), new(ClaimTypes.Name, "server-reader"), new(ClaimTypes.Role, "Operator") };
    if (allowed) claims.Add(new Claim("permission", "Servers.View"));
    var controller = new ServersController(db, new EffectivePermissions(db), null!, null!, null!, null!);
    controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
    { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) } };
    return controller;
}
T Read<T>(IActionResult result)
{
    if (result is not OkObjectResult ok) throw new Exception("Expected OK: " + result.GetType().Name);
    return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(ok.Value), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
}
var controller = Controller();
var summary = Read<ServerDashboardDto>(await controller.Summary(11));
Check(summary.Total == 7, "Dashboard is scoped to allowed company");
Check(summary.Healthy == 2 && summary.Warning == 1 && summary.Critical == 1 && summary.Disconnected == 1 && summary.Stale == 1 && summary.Unknown == 1, "All six status buckets aggregate correctly in SQLite");
Check(summary.Healthy + summary.Warning + summary.Critical + summary.Disconnected + summary.Stale + summary.Unknown == summary.Total, "Health buckets partition total");
Check(summary.Maintenance == 1, "Maintenance count includes active suppression window");
Check(summary.OpenAlertCount == 135, "Alert total is not capped at 100 or contaminated by another company");
Check(summary.Servers.All(s => s.Id != 88), "Status metadata never contains another company's server");
Check(Read<ServerDashboardDto>(await controller.Summary(13)).Total == 0, "Empty allowed company returns zero counts");
var search = (OkObjectResult)await controller.List(11, "server-1");
Check(JsonSerializer.SerializeToElement(search.Value).GetArrayLength() == 1, "List search returns one matching row");
Check(Read<ServerDashboardDto>(await controller.Summary(11)).Total == 7, "Search does not affect company dashboard");
Check(await controller.Summary(12) is ForbidResult, "Foreign company summary denied");
Check(await Controller(false).Summary(11) is ForbidResult, "Summary without Servers.View denied");
Check(await controller.Get(88) is NotFoundObjectResult, "Foreign server detail denied");
Check(await Controller(false).Get(1) is ForbidResult, "Detail without Servers.View denied");
Check(await controller.Get(9999) is NotFoundObjectResult, "Missing server is 404");
var detail = Read<ServerDetailsDto>(await controller.Get(1));
Check(detail.Server.Id == 1 && detail.CompanyName == "Company A", "Detail profile and company deserialize into shared contract");
Check(detail.SnapshotCount == 70 && detail.Snapshots.Count == 60, "Health history limit and full count are explicit");
Check(detail.LogCount == 110 && detail.Logs.Count == 100, "Operation history limit and full count are explicit");
Check(detail.Status.State == "Healthy", "Detail and dashboard use same status policy");
var parts = Read<ServerHardwareDto>(await controller.Hardware(1));
Check(parts.Components.Count == 1 && parts.Components[0].Name == "latest", "Hardware contract reads latest stored component set");
var page = Read<ServerAlertPageDto>(await controller.ServerAlerts(2));
Check(page.Total == 135 && page.OpenCount == 135 && page.Items.Count == 30, "Server alerts are paginated with exact counts");
var next = Read<ServerAlertPageDto>(await controller.ServerAlerts(2, skip: 30));
Check(!next.Items.Select(a => a.Id).Intersect(page.Items.Select(a => a.Id)).Any(), "Stable adjacent alert pages do not overlap");
var allAlerts = Read<ServerAlertPageDto>(await controller.ServerAlerts(2, includeResolved: true, take: 5000));
Check(allAlerts.Total == 136 && allAlerts.Items.Count == 100 && allAlerts.OpenCount == 135, "Include resolved and maximum page size work independently");
Check(await controller.ServerAlerts(88) is NotFoundObjectResult, "Foreign server alerts denied");
Check(await Controller(false).ServerAlerts(2) is ForbidResult, "Server alerts without Servers.View denied");
Check(Read<ServerAlertPageDto>(await controller.ServerAlerts(2, skip: -10, take: 0)).Items.Count == 1, "Negative skip and zero take are clamped");
Check(await controller.Hardware(88) is NotFoundObjectResult, "Foreign hardware endpoint denied");
var empty = Read<ServerDetailsDto>(await controller.Get(6));
Check(empty.Status.State == "Unknown" && empty.Snapshots.Count == 0, "Never-polled server detail remains unknown");
Console.WriteLine($"SUCCESS: {count} assertions passed. Production controllers + real SQLite; no live iLO or SQL Server test.");
