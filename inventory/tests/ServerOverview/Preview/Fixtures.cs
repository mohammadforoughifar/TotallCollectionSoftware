using System.Text.Json;
using Inventory.Client.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;

namespace Preview;

public sealed class FixtureAuth(NavigationManager navigation) : IAuthState
{
    public bool IsLoggedIn => true;
    public bool IsAdmin => false;
    public bool IsOperator => true;
    public bool IsReferrer => false;
    public bool CanOperate => true;
    public string? Token => null;
    public int UserId => 1;
    public string DisplayName => "کاربر آزمایشی";
    public string Role => "Operator";
    public int? ReferrerId => null;
    public bool Has(string permission)
    {
        var query = new Uri(navigation.Uri).Query;
        if (query.Contains("mode=denied")) return false;
        if (query.Contains("mode=viewer")) return permission is "Servers.View" or "WorkOrders.View";
        return permission is "Servers.View" or "Servers.Update" or "Servers.Collect" or "WorkOrders.Create" or "WorkOrders.View";
    }
    public bool HasModule(string module) => Has(module + ".View");
    public bool CanSee(string module) => HasModule(module);
    public event Action? Changed { add { } remove { } }
    public Task InitializeAsync() => Task.CompletedTask;
    public Task SignInAsync(LoginResponse login) => Task.CompletedTask;
    public Task SignOutAsync() => Task.CompletedTask;
}

public sealed class FixtureApi(NavigationManager navigation) : IApiClient
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly List<ServerProfileDto> servers = Enumerable.Range(1, 8).Select(i => new ServerProfileDto
    {
        Id = i, CompanyId = 1, Name = new[] { "ESXi-Production-01", "Database-Primary", "File-Server-01", "Backup-Node-02", "Web-Production", "Archive-Server", "ESXi-Standby", "New-Server-08" }[i - 1],
        Manufacturer = "HPE", Model = i % 2 == 0 ? "ProLiant DL360 Gen10" : "ProLiant DL380 Gen10",
        SerialNumber = "DEMO-SN-" + i.ToString("0000"), AssetTag = "IT-SRV-" + i.ToString("000"), IloAddress = "192.0.2." + (10 + i),
        IloPort = 443, CredentialId = 1, IloVersion = "iLO 5 · 3.06", Role = i == 1 ? "مجازی‌سازی" : "زیرساخت سازمانی", Environment = "Production", Criticality = i <= 2 ? "Critical" : "Normal",
        Status = "Active", Rack = "Rack A", RackUnit = i * 2, OperatingSystem = i == 1 ? "VMware ESXi 8.0" : "Windows Server 2022",
        WarrantyEndDate = DateTime.Today.AddMonths(7), Notes = "اطلاعات آزمایشی برای نمایش امکانات صفحه. این سرور واقعی نیست.",
        MonitoringEnabled = i != 8, MonitoringIntervalMinutes = 15, LastAutomaticPollAt = i == 8 ? null : DateTime.Now.AddMinutes(-4),
        ResponsibleUserId = 1, ResponsibleName = "مسئول نمونه", AlertNotificationsEnabled = true,
        MaintenanceUntil = i == 7 ? DateTime.Now.AddHours(2) : null, MaintenanceNote = i == 7 ? "بررسی برنامه‌ریزی‌شده" : null,
        LastHealthCheckAt = i == 8 ? null : i == 6 ? DateTime.Now.AddHours(-3) : DateTime.Now.AddMinutes(-4),
        LastInventoryAt = i == 8 ? null : DateTime.Now.AddMinutes(-4), LastHardwareDetailsAt = DateTime.Now.AddMinutes(-4),
        LastHealthStatus = i == 3 ? "Warning" : i == 4 ? "Critical" : i == 8 ? null : "OK", LastPowerState = "On",
        LastConnectionError = i == 5 ? "اتصال به کنترلر مدیریتی در مهلت تعیین‌شده برقرار نشد." : null,
        CreatedAt = DateTime.Today.AddMonths(-8), UpdatedAt = DateTime.Now.AddMinutes(-4), UpdatedByName = "سیستم پایش سرورها"
    }).ToList();
    private readonly List<(int ServerId, ServerAlertDto Alert)> alerts = Enumerable.Range(1, 35).Select(i => (3, new ServerAlertDto
    { Id = i, Category = "HardwareEvent", Severity = "Warning", Message = "نمونهٔ رخداد ذخیره‌شدهٔ سخت‌افزار — " + i,
        Status = i == 1 ? "Acknowledged" : "Open", AcknowledgedByName = i == 1 ? "مسئول نمونه" : null,
        OpenedAt = DateTime.Now.AddHours(-i), LastSeenAt = DateTime.Now.AddMinutes(-i), WorkOrderId = i == 1 ? 123 : null })).ToList();
    private ServerDisplayStatus Status(ServerProfileDto s) => ServerStatusPolicy.Evaluate(new ServerStatusInput
    { Id = s.Id, Health = s.LastHealthStatus, LastAttemptAt = s.LastHealthCheckAt, ConnectionError = s.LastConnectionError,
        MonitoringEnabled = s.MonitoringEnabled, IntervalMinutes = s.MonitoringIntervalMinutes, Lifecycle = s.Status, MaintenanceUntil = s.MaintenanceUntil }, DateTime.Now);
    private T Convert<T>(object value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value), Json)!;
    public Task<T> GetAsync<T>(string path)
    {
        var uri = new Uri("https://fixture.invalid/" + path);
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var query = QueryHelpers.ParseQuery(uri.Query);
        int Num(string key, int fallback = 0) => int.TryParse(query[key], out var n) ? n : fallback;
        if (new Uri(navigation.Uri).Query.Contains("fail=all")) throw new Exception("خطای آزمایشی دریافت اطلاعات");
        object result;
        if (path == "api/servers/companies") result = new[] { new { Id = 1, Name = "شرکت نمونه — مرکز اصلی", Count = 8 }, new { Id = 2, Name = "شرکت نمونه — شعبهٔ خالی", Count = 0 } };
        else if (parts.Last() == "responsibles") result = new[] { new { Id = 1, Name = "مسئول نمونه", Username = "demo" } };
        else if (parts.Last() == "credentials") result = Array.Empty<object>();
        else if (parts.Last() == "summary")
        {
            var items = servers.Where(s => s.CompanyId == Num("companyId")).Select(Status).ToList();
            result = new ServerDashboardDto { GeneratedAt = DateTime.Now, Total = items.Count, Servers = items,
                Healthy = items.Count(s => s.State == "Healthy"), Warning = items.Count(s => s.State == "Warning"), Critical = items.Count(s => s.State == "Critical"),
                Disconnected = items.Count(s => s.State == "Disconnected"), Stale = items.Count(s => s.State == "Stale"), Unknown = items.Count(s => s.State == "Unknown"),
                Maintenance = items.Count(s => s.InMaintenance), MonitoringEnabledCount = items.Count > 0 ? 7 : 0, OpenAlertCount = items.Count > 0 ? alerts.Count(a => a.Alert.Status != "Resolved") : 0 };
        }
        else if (parts.Length == 2)
        {
            var q = query["q"].ToString();
            result = servers.Where(s => s.CompanyId == Num("companyId") && (string.IsNullOrWhiteSpace(q) || s.Name.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
        }
        else if (parts.Length == 3 && parts[2] == "alerts") result = Num("companyId") == 1
            ? alerts.Where(a => a.Alert.Status != "Resolved").Select(a => new { a.Alert.Id, ManagedServerId = a.ServerId, ServerName = servers.Single(s => s.Id == a.ServerId).Name,
                a.Alert.Category, a.Alert.Severity, a.Alert.Message, a.Alert.Status, a.Alert.WorkOrderId, a.Alert.LastSeenAt, a.Alert.AcknowledgedByName }).Take(100).ToArray() : Array.Empty<object>();
        else
        {
            var id = int.Parse(parts[2]);
            var s = servers.SingleOrDefault(s => s.Id == id) ?? throw new Exception("سرور پیدا نشد یا به آن دسترسی ندارید.");
            if (parts.Length == 3)
            {
                var points = Enumerable.Range(0, 32).Select(i => new ServerHealthPointDto { Id = i + 1, CapturedAt = DateTime.Now.AddMinutes(-4 - i * 15),
                    OverallHealth = i is >= 8 and <= 11 ? "Warning" : "OK", PowerState = "On", BiosVersion = "U30 v2.90", FirmwareVersion = "3.06", Message = "دریافت خودکار از Redfish — دادهٔ آزمایشی" }).ToList();
                result = new ServerDetailsDto { Server = s, CompanyName = "شرکت نمونه — مرکز اصلی", ResponsibleName = s.ResponsibleName, Status = Status(s), GeneratedAt = DateTime.Now,
                    Snapshots = id == 8 ? new() : points, SnapshotCount = id == 8 ? 0 : 32, LogCount = 8,
                    Logs = Enumerable.Range(0, 8).Select(i => new ServerOperationDto { Id = i + 1, At = DateTime.Now.AddMinutes(-i * 15), Action = i == 7 ? "Update" : "AutoCollect", UserName = "سیستم پایش سرورها", Result = "Success", Details = "دریافت وضعیت سلامت — دادهٔ آزمایشی" }).ToList() };
            }
            else if (parts[3] == "hardware")
            {
                if (new Uri(navigation.Uri).Query.Contains("fail=hardware")) throw new Exception("خطای آزمایشی دریافت قطعات؛ شناسنامه همچنان قابل مشاهده است.");
                result = id == 8 ? new ServerHardwareDto() : new ServerHardwareDto { CapturedAt = s.LastHardwareDetailsAt,
                    Components = new() {
                        new() { Id = 1, Name = "CPU 1", ComponentType = "Processor", Health = "OK", Reading = "24 cores", Model = "Intel Xeon Silver 4314" },
                        new() { Id = 2, Name = "DIMM A1", ComponentType = "Memory", Health = "OK", Reading = "32768 MiB", Model = "DDR4 ECC", SerialNumber = "DEMO-RAM-01" },
                        new() { Id = 3, Name = "Drive Bay 1", ComponentType = "Disk", Health = "OK", Reading = "960 GiB", Model = "HPE SAS SSD", SerialNumber = "DEMO-DISK-01" },
                        new() { Id = 4, Name = "Power Supply 1", ComponentType = "PowerSupply", Health = "OK", Reading = "240 W", Model = "HPE 800W" },
                        new() { Id = 5, Name = "System Fan 1", ComponentType = "Fan", Health = "OK", Reading = "32 Percent" },
                        new() { Id = 6, Name = "Inlet Temperature", ComponentType = "Temperature", Health = "OK", Reading = "24 °C" }
                    }, Events = new() { new() { Id = 1, Severity = "OK", Message = "نمونهٔ رخداد: خودآزمایی سخت‌افزار کامل شد.", Origin = "/redfish/v1/Systems/1", OccurredAt = DateTime.Now.AddHours(-2), LastSeenAt = DateTime.Now.AddMinutes(-4) } } };
            }
            else if (parts[3] == "alerts")
            {
                var include = bool.TryParse(query["includeResolved"], out var inc) && inc;
                var all = alerts.Where(a => a.ServerId == id).Select(a => a.Alert).ToList();
                var filtered = all.Where(a => include || a.Status != "Resolved").OrderBy(a => a.Status == "Resolved").ThenByDescending(a => a.LastSeenAt).ToList();
                result = new ServerAlertPageDto { Total = filtered.Count, OpenCount = all.Count(a => a.Status != "Resolved"), Items = filtered.Skip(Num("skip")).Take(Num("take", 30)).ToList() };
            }
            else throw new NotSupportedException(path);
        }
        return Task.FromResult(Convert<T>(result));
    }
    public Task<T> PostAsync<T>(string path, object? body = null)
    {
        var parts = path.Split('/');
        if (parts[2] == "alerts")
        {
            var item = alerts.Single(a => a.Alert.Id == long.Parse(parts[3])).Alert;
            if (parts[4] == "acknowledge") { item.Status = "Acknowledged"; item.AcknowledgedByName = "کاربر آزمایشی"; item.AcknowledgedAt = DateTime.Now; }
            if (parts[4] == "resolve") { item.Status = "Resolved"; item.ResolvedAt = DateTime.Now; }
            if (parts[4] == "work-order") item.WorkOrderId = 123;
        }
        else if (parts.Last() is "test" or "collect")
        {
            var s = servers.Single(s => s.Id == int.Parse(parts[2])); s.LastHealthCheckAt = DateTime.Now;
            s.LastInventoryAt = DateTime.Now; s.LastConnectionError = null; s.LastHealthStatus = "OK";
        }
        else throw new NotSupportedException("این عملیات در پیش‌نمایش فعال نیست.");
        return Task.FromResult(Convert<T>(new { ok = true, id = 123, number = "WO/DEMO/123" }));
    }
    public Task<T> PutAsync<T>(string path, object? body = null)
    {
        var replacement = Convert<ServerProfileDto>(body!); var old = servers.Single(s => s.Id == replacement.Id);
        old.Name = replacement.Name; old.Notes = replacement.Notes;
        return Task.FromResult(Convert<T>(new { ok = true }));
    }
    public Task<T> GetLongAsync<T>(string path) => GetAsync<T>(path);
    public Task<T> PostLongAsync<T>(string path, object? body = null) => PostAsync<T>(path, body);
    public Task DeleteAsync(string path) => throw new NotSupportedException("حذف در پیش‌نمایش غیرفعال است.");
    public Task<T> DeleteAsync<T>(string path) => throw new NotSupportedException();
    public Task<T> PostFileAsync<T>(string path, Stream stream, string name, string field = "file", string? type = null) => throw new NotSupportedException();
    public string BuildUrl(string path) => "/" + path;
    public Task<(byte[] Data, string FileName, string ContentType)> GetFileAsync(string path) => throw new NotSupportedException();
}
