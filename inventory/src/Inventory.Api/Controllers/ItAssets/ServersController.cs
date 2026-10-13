using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.Core;
using Inventory.Api.Services.ItAssets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>
/// شناسنامه، سلامت و دریافت Inventory سرورهای دارای iLO/Redfish.
/// رمز iLO فقط از گاوصندوق رمزها و فقط در سمت سرور خوانده می‌شود.
/// مجوزها: Servers.View / Create / Update / Delete / Collect.
/// </summary>
[ApiController]
[Route("api/servers")]
public sealed class ServersController : ControllerBase
{
    private static readonly HashSet<string> Criticalities = new(StringComparer.OrdinalIgnoreCase)
        { "Low", "Normal", "High", "Critical" };
    private static readonly HashSet<string> Statuses = new(StringComparer.OrdinalIgnoreCase)
        { "Active", "Maintenance", "Standby", "Retired" };

    private readonly AppDbContext _db;
    private readonly IEffectivePermissions _perms;
    private readonly CredentialVaultCrypto _crypto;
    private readonly HpeRedfishClient _redfish;

    public ServersController(AppDbContext db, IEffectivePermissions perms, CredentialVaultCrypto crypto, HpeRedfishClient redfish)
    {
        _db = db;
        _perms = perms;
        _crypto = crypto;
        _redfish = redfish;
    }

    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    private bool IsAdmin => User.IsInRole("Admin");
    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
    private HashSet<string>? _permCache;

    private async Task<bool> HasAsync(string action)
    {
        _permCache ??= await _perms.GetAsync(User);
        return _permCache.Contains("Servers." + action);
    }

    private async Task<bool> HasVaultViewAsync()
    {
        _permCache ??= await _perms.GetAsync(User);
        return _permCache.Contains("ItVault.View");
    }

    private async Task<List<int>> AllowedCompanyIdsAsync()
    {
        var ids = await _db.UserCompanyAccesses.AsNoTracking()
            .Where(x => x.UserId == UserId && x.Company.IsActive)
            .Select(x => x.CompanyId).ToListAsync();
        if (ids.Count > 0) return ids;

        var username = User.FindFirstValue(ClaimTypes.Name) ?? "";
        var legacyCompany = await _db.SystemUsers.AsNoTracking()
            .Where(x => x.Username == username && x.CompanyId != null)
            .Select(x => x.CompanyId!.Value).FirstOrDefaultAsync();
        if (legacyCompany > 0 && await _db.SystemCompanies.AnyAsync(x => x.Id == legacyCompany && x.IsActive))
            return new List<int> { legacyCompany };

        return IsAdmin
            ? await _db.SystemCompanies.AsNoTracking().Where(x => x.IsActive).Select(x => x.Id).ToListAsync()
            : new List<int>();
    }

    private async Task<string> MyNameAsync()
    {
        var me = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == UserId);
        return me is null ? (User.FindFirstValue(ClaimTypes.Name) ?? "") : UserDisplay.Name(me);
    }

    private async Task<ManagedServer?> FindAllowedAsync(int id)
    {
        var server = await _db.ManagedServers.FirstOrDefaultAsync(x => x.Id == id);
        return server is not null && (await AllowedCompanyIdsAsync()).Contains(server.CompanyId) ? server : null;
    }

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length <= max ? value : value[..max];
    }

    private async Task WriteLogAsync(ManagedServer server, string action, string result, string? details = null)
    {
        _db.ServerOperationLogs.Add(new ServerOperationLog
        {
            ManagedServerId = server.Id,
            UserId = UserId > 0 ? UserId : null,
            UserName = await MyNameAsync(),
            Action = action,
            Result = result,
            Details = Clean(details, 1000),
            ClientIp = ClientIp,
            At = DateTime.Now
        });
    }

    // ================================================================ شرکت و Credentialهای قابل انتخاب

    [HttpGet("companies")]
    public async Task<IActionResult> Companies()
    {
        if (!await HasAsync("View")) return Forbid();
        var ids = await AllowedCompanyIdsAsync();
        var rows = await _db.SystemCompanies.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, Count = _db.ManagedServers.Count(s => s.CompanyId == c.Id) })
            .ToListAsync();
        return Ok(rows);
    }

    /// <summary>Credentialهای فعال نوع Server؛ فقط مشخصات غیرمحرمانه برای اتصال به سرور.</summary>
    [HttpGet("credentials")]
    public async Task<IActionResult> Credentials([FromQuery] int companyId)
    {
        // نام کاربری/عنوان Credential هم اطلاعات حساس عملیاتی هستند؛ فقط کسی که هم
        // سرور را ثبت/ویرایش می‌کند و هم مجوز دیدن Vault دارد، می‌تواند آن را انتخاب کند.
        if (!await HasAsync("Create") && !await HasAsync("Update")) return Forbid();
        if (!await HasVaultViewAsync()) return Forbid();
        if (!(await AllowedCompanyIdsAsync()).Contains(companyId))
            return BadRequest(new { message = "به این شرکت دسترسی ندارید یا شرکت انتخاب نشده است." });

        var rows = await _db.ItCredentials.AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.IsActive && c.Category == "Server")
            .OrderBy(c => c.Title)
            .Select(c => new
            {
                c.Id, c.Title, c.Host, c.Port, c.Username,
                HasPassword = c.PasswordCipher != null
            })
            .ToListAsync();
        return Ok(rows);
    }

    // ================================================================ CRUD

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int companyId, [FromQuery] string? q)
    {
        if (!await HasAsync("View")) return Forbid();
        if (!(await AllowedCompanyIdsAsync()).Contains(companyId))
            return BadRequest(new { message = "به این شرکت دسترسی ندارید یا شرکت انتخاب نشده است." });

        var query = _db.ManagedServers.AsNoTracking().Where(s => s.CompanyId == companyId);
        if (!string.IsNullOrWhiteSpace(q))
        {
            q = q.Trim();
            query = query.Where(s => s.Name.Contains(q)
                || (s.Model != null && s.Model.Contains(q))
                || (s.SerialNumber != null && s.SerialNumber.Contains(q))
                || s.IloAddress.Contains(q)
                || (s.Role != null && s.Role.Contains(q)));
        }

        var rows = await query.OrderBy(s => s.Name).Select(s => new
        {
            s.Id, s.CompanyId, s.Name, s.Manufacturer, s.Model, s.SerialNumber, s.AssetTag,
            s.IloAddress, s.IloPort, s.CredentialId, s.IloVersion, s.Role, s.Environment,
            s.Criticality, s.Status, s.Rack, s.RackUnit, s.OperatingSystem, s.WarrantyEndDate, s.Notes,
            s.MonitoringEnabled, s.MonitoringIntervalMinutes, s.LastAutomaticPollAt,
            s.LastHealthCheckAt, s.LastHealthStatus, s.LastPowerState, s.LastHealthMessage,
            s.LastConnectionError, s.LastInventoryAt, s.CreatedAt, s.UpdatedAt, s.UpdatedByName,
            OpenAlertCount = _db.ServerAlerts.Count(a => a.ManagedServerId == s.Id && a.Status != "Resolved")
        }).ToListAsync();
        return Ok(rows);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        if (!await HasAsync("View")) return Forbid();
        var server = await FindAllowedAsync(id);
        if (server is null) return NotFound(new { message = "سرور پیدا نشد." });

        var snapshots = await _db.ServerHealthSnapshots.AsNoTracking()
            .Where(x => x.ManagedServerId == id).OrderByDescending(x => x.CapturedAt).Take(30).ToListAsync();
        var logs = await _db.ServerOperationLogs.AsNoTracking()
            .Where(x => x.ManagedServerId == id).OrderByDescending(x => x.At).Take(50).ToListAsync();
        return Ok(new { server, snapshots, logs });
    }

    // ================================================================ هشدارها

    [HttpGet("alerts")]
    public async Task<IActionResult> Alerts([FromQuery] int companyId, [FromQuery] bool includeResolved = false, [FromQuery] int take = 100)
    {
        if (!await HasAsync("View")) return Forbid();
        if (!(await AllowedCompanyIdsAsync()).Contains(companyId))
            return BadRequest(new { message = "به این شرکت دسترسی ندارید یا شرکت انتخاب نشده است." });
        take = Math.Clamp(take, 1, 500);

        var query = from alert in _db.ServerAlerts.AsNoTracking()
                    join server in _db.ManagedServers.AsNoTracking() on alert.ManagedServerId equals server.Id
                    where server.CompanyId == companyId && (includeResolved || alert.Status != "Resolved")
                    orderby alert.Severity == "Critical" descending, alert.LastSeenAt descending
                    select new
                    {
                        alert.Id, alert.ManagedServerId, ServerName = server.Name, server.Model,
                        alert.Category, alert.Severity, alert.Message, alert.Status,
                        alert.OpenedAt, alert.LastSeenAt, alert.AcknowledgedAt, alert.AcknowledgedByName, alert.ResolvedAt
                    };
        return Ok(await query.Take(take).ToListAsync());
    }

    private async Task<(ServerAlert? Alert, ManagedServer? Server)> FindAllowedAlertAsync(long alertId)
    {
        var alert = await _db.ServerAlerts.FirstOrDefaultAsync(a => a.Id == alertId);
        if (alert is null) return (null, null);
        var server = await FindAllowedAsync(alert.ManagedServerId);
        return server is null ? (null, null) : (alert, server);
    }

    [HttpPost("alerts/{id:long}/acknowledge")]
    public async Task<IActionResult> AcknowledgeAlert(long id)
    {
        if (!await HasAsync("Update")) return Forbid();
        var (alert, server) = await FindAllowedAlertAsync(id);
        if (alert is null || server is null) return NotFound(new { message = "هشدار پیدا نشد." });
        if (alert.Status == "Resolved") return BadRequest(new { message = "این هشدار قبلاً رفع شده است." });

        alert.Status = "Acknowledged";
        alert.AcknowledgedAt = DateTime.Now;
        alert.AcknowledgedByUserId = UserId > 0 ? UserId : null;
        alert.AcknowledgedByName = await MyNameAsync();
        await WriteLogAsync(server, "AlertAcknowledge", "Success", $"{alert.Category}: {alert.Message}");
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPost("alerts/{id:long}/resolve")]
    public async Task<IActionResult> ResolveAlert(long id)
    {
        if (!await HasAsync("Update")) return Forbid();
        var (alert, server) = await FindAllowedAlertAsync(id);
        if (alert is null || server is null) return NotFound(new { message = "هشدار پیدا نشد." });
        if (alert.Status == "Resolved") return Ok(new { ok = true });

        alert.Status = "Resolved";
        alert.ResolvedAt = DateTime.Now;
        alert.LastSeenAt = alert.ResolvedAt.Value;
        await WriteLogAsync(server, "AlertResolve", "Success", $"{alert.Category}: {alert.Message}");
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    public sealed class SaveDto
    {
        public int CompanyId { get; set; }
        public string Name { get; set; } = "";
        public string? Manufacturer { get; set; }
        public string? Model { get; set; }
        public string? SerialNumber { get; set; }
        public string? AssetTag { get; set; }
        public string IloAddress { get; set; } = "";
        public int IloPort { get; set; } = 443;
        public int? CredentialId { get; set; }
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
        public int MonitoringIntervalMinutes { get; set; } = 15;
    }

    private async Task<string?> ValidateAsync(SaveDto d, int? currentId)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return "نام سرور را وارد کنید.";
        if (string.IsNullOrWhiteSpace(d.IloAddress)) return "آدرس iLO را وارد کنید.";
        if (d.IloAddress.Trim().StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return "برای امنیت، آدرس iLO باید با HTTPS باشد.";
        if (d.IloPort is < 1 or > 65535) return "پورت iLO باید بین ۱ تا ۶۵۵۳۵ باشد.";
        if (!Criticalities.Contains(d.Criticality)) return "درجهٔ اهمیت نامعتبر است.";
        if (!Statuses.Contains(d.Status)) return "وضعیت چرخهٔ عمر نامعتبر است.";
        if (d.RackUnit is < 1 or > 100) return "شمارهٔ U رک باید بین ۱ تا ۱۰۰ باشد.";
        if (d.MonitoringIntervalMinutes is < 5 or > 1440) return "فاصلهٔ پایش باید بین ۵ تا ۱۴۴۰ دقیقه باشد.";
        if (d.MonitoringEnabled && d.CredentialId is not > 0) return "برای فعال‌سازی پایش خودکار، Credential iLO را انتخاب کنید.";
        if (!(await AllowedCompanyIdsAsync()).Contains(d.CompanyId)) return "به شرکت انتخاب‌شده دسترسی ندارید.";

        if (d.CredentialId is > 0)
        {
            var credentialOk = await _db.ItCredentials.AsNoTracking().AnyAsync(c => c.Id == d.CredentialId
                && c.CompanyId == d.CompanyId && c.Category == "Server" && c.IsActive);
            if (!credentialOk) return "Credential انتخاب‌شده معتبر نیست یا به همین شرکت تعلق ندارد.";
        }
        return null;
    }

    private static void Apply(ManagedServer target, SaveDto d)
    {
        target.CompanyId = d.CompanyId;
        target.Name = Clean(d.Name, 150)!;
        target.Manufacturer = Clean(d.Manufacturer, 50) ?? "HPE";
        target.Model = Clean(d.Model, 150);
        target.SerialNumber = Clean(d.SerialNumber, 100);
        target.AssetTag = Clean(d.AssetTag, 100);
        target.IloAddress = Clean(d.IloAddress, 250)!;
        target.IloPort = d.IloPort;
        target.CredentialId = d.CredentialId is > 0 ? d.CredentialId : null;
        target.Role = Clean(d.Role, 100);
        target.Environment = Clean(d.Environment, 100);
        target.Criticality = d.Criticality.Trim();
        target.Status = d.Status.Trim();
        target.Rack = Clean(d.Rack, 100);
        target.RackUnit = d.RackUnit;
        target.OperatingSystem = Clean(d.OperatingSystem, 200);
        target.WarrantyEndDate = d.WarrantyEndDate?.Date;
        target.Notes = Clean(d.Notes, 2000);
        target.MonitoringEnabled = d.MonitoringEnabled;
        target.MonitoringIntervalMinutes = Math.Clamp(d.MonitoringIntervalMinutes, 5, 1440);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveDto d)
    {
        if (!await HasAsync("Create")) return Forbid();
        if (d.CredentialId is > 0 && !await HasVaultViewAsync())
            return BadRequest(new { message = "برای متصل کردن Credential به سرور، مجوز مشاهدهٔ گاوصندوق لازم است." });
        var error = await ValidateAsync(d, null);
        if (error is not null) return BadRequest(new { message = error });

        var now = DateTime.Now;
        var server = new ManagedServer { CreatedAt = now, CreatedByUserId = UserId > 0 ? UserId : null, UpdatedAt = now, UpdatedByUserId = UserId > 0 ? UserId : null, UpdatedByName = await MyNameAsync() };
        Apply(server, d);
        _db.ManagedServers.Add(server);
        await _db.SaveChangesAsync();
        await WriteLogAsync(server, "Create", "Success", "ثبت شناسنامه سرور");
        await _db.SaveChangesAsync();
        return Ok(new { id = server.Id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SaveDto d)
    {
        if (!await HasAsync("Update")) return Forbid();
        var server = await FindAllowedAsync(id);
        if (server is null) return NotFound(new { message = "سرور پیدا نشد." });
        // بدون مجوز Vault، کاربر فقط می‌تواند شناسنامهٔ غیراتصالی را ویرایش کند؛
        // تغییر مقصد iLO یا Credential در این حالت می‌تواند رمز ذخیره‌شده را به مقصد نادرست بفرستد.
        var requestedAddress = Clean(d.IloAddress, 250);
        if (!await HasVaultViewAsync() && (d.CredentialId != server.CredentialId
            || (server.CredentialId is > 0 && (!string.Equals(requestedAddress, server.IloAddress, StringComparison.OrdinalIgnoreCase)
                                                || d.IloPort != server.IloPort))))
            return BadRequest(new { message = "برای تغییر Credential یا مقصد iLO، مجوز مشاهدهٔ گاوصندوق رمزها لازم است." });
        var error = await ValidateAsync(d, id);
        if (error is not null) return BadRequest(new { message = error });

        Apply(server, d);
        server.UpdatedAt = DateTime.Now;
        server.UpdatedByUserId = UserId > 0 ? UserId : null;
        server.UpdatedByName = await MyNameAsync();
        await WriteLogAsync(server, "Update", "Success", "ویرایش شناسنامه سرور");
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!await HasAsync("Delete")) return Forbid();
        var server = await FindAllowedAsync(id);
        if (server is null) return NotFound(new { message = "سرور پیدا نشد." });

        // همهٔ تاریخچهٔ وابسته همراه شناسنامه پاک می‌شود؛ ثبت لاگ Delete روی رکوردی که پاک می‌شود
        // باعث ایجاد لاگ یتیم خواهد شد، بنابراین این عملیات در لاگ سرور نگه‌داری نمی‌شود.
        _db.ServerHealthSnapshots.RemoveRange(await _db.ServerHealthSnapshots.Where(x => x.ManagedServerId == id).ToListAsync());
        _db.ServerAlerts.RemoveRange(await _db.ServerAlerts.Where(x => x.ManagedServerId == id).ToListAsync());
        _db.ServerOperationLogs.RemoveRange(await _db.ServerOperationLogs.Where(x => x.ManagedServerId == id).ToListAsync());
        _db.ManagedServers.Remove(server);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ================================================================ Redfish (فقط خواندنی)

    private async Task<(RedfishCredentials? Credentials, string? Error)> LoadCredentialsAsync(ManagedServer server)
    {
        if (server.CredentialId is not > 0) return (null, "برای این سرور Credential iLO انتخاب نشده است.");
        var c = await _db.ItCredentials.AsNoTracking().FirstOrDefaultAsync(x => x.Id == server.CredentialId
            && x.CompanyId == server.CompanyId && x.Category == "Server" && x.IsActive);
        if (c is null) return (null, "Credential انتخاب‌شده پیدا نشد یا غیرفعال است.");
        if (string.IsNullOrWhiteSpace(c.Username) || string.IsNullOrWhiteSpace(c.PasswordCipher))
            return (null, "Credential انتخاب‌شده نام کاربری یا رمز ذخیره‌شده ندارد.");
        try
        {
            var password = _crypto.Decrypt(c.PasswordCipher, c.CompanyId);
            if (string.IsNullOrEmpty(password)) return (null, "رمز Credential قابل خواندن نیست.");
            return (new RedfishCredentials(c.Username, password), null);
        }
        catch
        {
            return (null, "رمز Credential قابل رمزگشایی نیست؛ وضعیت کلید گاوصندوق را بررسی کنید.");
        }
    }

    private static async Task ReconcileAlertsAsync(AppDbContext db, ManagedServer server, RedfishSnapshot snapshot, DateTime now, CancellationToken ct = default)
    {
        await ServerAlertRules.ResolveAsync(db, server.Id, "Connection", now, ct);
        if (string.Equals(snapshot.Health, "OK", StringComparison.OrdinalIgnoreCase))
        {
            await ServerAlertRules.ResolveAsync(db, server.Id, "Health", now, ct);
            return;
        }
        var severity = string.Equals(snapshot.Health, "Critical", StringComparison.OrdinalIgnoreCase) ? "Critical" : "Warning";
        await ServerAlertRules.RaiseAsync(db, server, "Health", severity,
            $"وضعیت سلامت سخت‌افزار Redfish: {snapshot.Health}", now, ct);
    }

    [HttpPost("{id:int}/test")]
    public async Task<IActionResult> TestConnection(int id)
    {
        if (!await HasAsync("Collect")) return Forbid();
        var server = await FindAllowedAsync(id);
        if (server is null) return NotFound(new { message = "سرور پیدا نشد." });
        var (credentials, credentialError) = await LoadCredentialsAsync(server);
        if (credentials is null)
        {
            await ServerAlertRules.RaiseAsync(_db, server, "Connection", "Critical", credentialError ?? "Credential iLO در دسترس نیست.", DateTime.Now, HttpContext.RequestAborted);
            await WriteLogAsync(server, "TestConnection", "Failed", credentialError);
            await _db.SaveChangesAsync();
            return BadRequest(new { message = credentialError });
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var snapshot = await _redfish.GetSnapshotAsync(server.IloAddress, server.IloPort, credentials, timeout.Token);
        server.LastHealthCheckAt = DateTime.Now;
        if (!snapshot.Success)
        {
            server.LastConnectionError = Clean(snapshot.Error, 1000);
            await ServerAlertRules.RaiseAsync(_db, server, "Connection", "Critical", snapshot.Error ?? "اتصال iLO برقرار نشد.", DateTime.Now, HttpContext.RequestAborted);
            await WriteLogAsync(server, "TestConnection", "Failed", snapshot.Error);
            await _db.SaveChangesAsync();
            return BadRequest(new { message = snapshot.Error });
        }

        server.LastHealthStatus = snapshot.Health;
        server.LastPowerState = Clean(snapshot.PowerState, 30);
        server.LastHealthMessage = "ارتباط Redfish برقرار است.";
        server.LastConnectionError = null;
        await ReconcileAlertsAsync(_db, server, snapshot, DateTime.Now, HttpContext.RequestAborted);
        await WriteLogAsync(server, "TestConnection", "Success", $"Health: {snapshot.Health}; Power: {snapshot.PowerState ?? "Unknown"}");
        await _db.SaveChangesAsync();
        return Ok(new { ok = true, snapshot });
    }

    [HttpPost("{id:int}/collect")]
    public async Task<IActionResult> CollectInventory(int id)
    {
        if (!await HasAsync("Collect")) return Forbid();
        var server = await FindAllowedAsync(id);
        if (server is null) return NotFound(new { message = "سرور پیدا نشد." });
        var (credentials, credentialError) = await LoadCredentialsAsync(server);
        if (credentials is null)
        {
            await ServerAlertRules.RaiseAsync(_db, server, "Connection", "Critical", credentialError ?? "Credential iLO در دسترس نیست.", DateTime.Now, HttpContext.RequestAborted);
            await WriteLogAsync(server, "CollectInventory", "Failed", credentialError);
            await _db.SaveChangesAsync();
            return BadRequest(new { message = credentialError });
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var snapshot = await _redfish.GetSnapshotAsync(server.IloAddress, server.IloPort, credentials, timeout.Token);
        var now = DateTime.Now;
        server.LastHealthCheckAt = now;
        if (!snapshot.Success)
        {
            server.LastConnectionError = Clean(snapshot.Error, 1000);
            await ServerAlertRules.RaiseAsync(_db, server, "Connection", "Critical", snapshot.Error ?? "اتصال iLO برقرار نشد.", now, HttpContext.RequestAborted);
            await WriteLogAsync(server, "CollectInventory", "Failed", snapshot.Error);
            await _db.SaveChangesAsync();
            return BadRequest(new { message = snapshot.Error });
        }

        server.Manufacturer = Clean(snapshot.Manufacturer, 50) ?? server.Manufacturer;
        server.Model = Clean(snapshot.Model, 150) ?? server.Model;
        server.SerialNumber = Clean(snapshot.SerialNumber, 100) ?? server.SerialNumber;
        server.AssetTag = Clean(snapshot.AssetTag, 100) ?? server.AssetTag;
        server.IloVersion = Clean(snapshot.IloVersion, 80) ?? server.IloVersion;
        server.LastHealthStatus = snapshot.Health;
        server.LastPowerState = Clean(snapshot.PowerState, 30);
        server.LastHealthMessage = "Inventory و وضعیت سلامت از Redfish خوانده شد.";
        server.LastConnectionError = null;
        await ReconcileAlertsAsync(_db, server, snapshot, now, HttpContext.RequestAborted);
        server.LastInventoryAt = now;
        server.UpdatedAt = now;
        server.UpdatedByUserId = UserId > 0 ? UserId : null;
        server.UpdatedByName = await MyNameAsync();

        _db.ServerHealthSnapshots.Add(new ServerHealthSnapshot
        {
            ManagedServerId = server.Id,
            CapturedAt = now,
            OverallHealth = snapshot.Health,
            PowerState = Clean(snapshot.PowerState, 30),
            FirmwareVersion = Clean(snapshot.FirmwareVersion, 100),
            BiosVersion = Clean(snapshot.BiosVersion, 100),
            Model = Clean(snapshot.Model, 150),
            SerialNumber = Clean(snapshot.SerialNumber, 100),
            Message = "دریافت دستی از Redfish"
        });
        await WriteLogAsync(server, "CollectInventory", "Success", $"Health: {snapshot.Health}; Power: {snapshot.PowerState ?? "Unknown"}");
        await _db.SaveChangesAsync();
        return Ok(new { ok = true, snapshot });
    }
}
