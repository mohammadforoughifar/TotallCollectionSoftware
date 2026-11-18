using System.Data;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Api.Services.ItAssets;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// Run with: dotnet run --project tests/WorkOrderSms -c Release
// Real AppDbContext/settings editor, staging transaction and worker. No provider traffic.
static void Check(bool ok, string message)
{
    if (!ok) throw new InvalidOperationException(message);
}

using var installation = new TemporaryDatabase();
var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(new SqliteConnectionStringBuilder {
    DataSource = installation.Path, Pooling = false
}.ToString()).Options;
await using var admin = new AppDbContext(dbOptions);
await admin.Database.EnsureCreatedAsync();
await admin.Database.OpenConnectionAsync();
var editor = new InventoryService(admin, NullLogger<InventoryService>.Instance);
var saved = await editor.SaveSettingsAsync(new AppSettings { KavenegarApiKey = "test-db-only", KavenegarSdkSendEnabled = true });
Check(saved.KavenegarApiKey is null && saved.KavenegarApiKeyHint?.EndsWith("only") == true, "The key must be write-only.");
// Simulate a legacy extra settings row, then reverse SQLite's unordered scans.
admin.AppSettings.Add(new AppSetting { KavenegarSdkSendEnabled = false });
admin.SystemCompanies.Add(new SystemCompany { Id = 1, Name = "شرکت آزمون" });
admin.CompanySmsSettings.Add(new CompanySmsSetting { CompanyId = 1, ModuleKey = SmsModuleCatalog.WorkOrders, Enabled = true });
for (int i = 1; i <= 3; i++)
    admin.Users.Add(new User { Id = i, Username = $"user{i}", Role = "Admin", Mobile = $"0912000000{i}" });
admin.WorkOrders.Add(new WorkOrder { Id = 1, CompanyId = 1, OwnerUserId = 1, Number = "WO/1405/0001", Title = "تست پیامک", DueAt = new DateTime(2026, 11, 20) });
await admin.SaveChangesAsync();
await admin.Database.ExecuteSqlRawAsync("PRAGMA reverse_unordered_selects=ON");
Check((await editor.GetSettingsAsync()).KavenegarSdkSendEnabled, "Form must still see its saved row.");

var file = Options.Create(new KavenegarSmsOptions { WorkOrderReplyTemplate = "ApprovedReply" }); // no file key, SDK off
KavenegarSmsGatewayFactory Factory(AppDbContext db) => new(db, file, NullLogger<KavenegarSmsGatewayFactory>.Instance);
var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
http.HttpContext.Items["ActiveCompanyId"] = 1;
async Task<int> Stage(WorkOrderSmsEventType kind, string action, int recipient, bool addAssignee = false)
{
    // A fresh request after the settings-save request, just like each controller action.
    await using var db = new AppDbContext(dbOptions, http);
    var service = new WorkOrderSmsService(db, Factory(db), file, NullLogger<WorkOrderSmsService>.Instance);
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
    var order = await db.WorkOrders.SingleAsync();
    if (addAssignee) db.WorkOrderAssignees.Add(new WorkOrderAssignee { OrderId = order.Id, UserId = recipient });
    var log = new WorkOrderLog { OrderId = order.Id, Action = action };
    db.WorkOrderLogs.Add(log);
    await db.SaveChangesAsync();
    Check(await service.TryStageAsync(tx, order, log, new[] { recipient }, kind, 1), "Could not stage the business event.");
    await tx.CommitAsync();
    return log.Id;
}

await using var workerDb = new AppDbContext(dbOptions); // no HTTP context; reuse across jobs, like a batch
await workerDb.Database.OpenConnectionAsync();
await workerDb.Database.ExecuteSqlRawAsync("PRAGMA reverse_unordered_selects=ON");
var recording = new RecordingFactory(Factory(workerDb));
var dispatcher = new WorkOrderSmsDispatcher(workerDb, recording, file);
Task<WorkOrderSmsNotification> Job(int logId) => workerDb.WorkOrderSmsNotifications.AsNoTracking().SingleAsync(n => n.EventLogId == logId);
var registration = await Stage(WorkOrderSmsEventType.Registration, "Created", 2, addAssignee: true);
Check(await dispatcher.ProcessOneAsync() && (await Job(registration)).Status == WorkOrderSmsStatus.Accepted, "Registration was not dispatched.");
var referral = await Stage(WorkOrderSmsEventType.Referral, "AssigneesAdded", 3, addAssignee: true);
var reply = await Stage(WorkOrderSmsEventType.Reply, "Reply", 1);
// A real read failure with the outbox still accessible, NOT an absent/blank key.
await admin.Database.ExecuteSqlRawAsync("ALTER TABLE AppSettings RENAME TO TemporarilyUnavailable");
Check(!await dispatcher.ProcessOneAsync() && (await Job(referral)) is { Status: WorkOrderSmsStatus.Pending, AttemptStartedAtUtc: null }
      && recording.Sends.Count == 1, "Unreadable settings must not consume a pending event.");
var unreadable = await Stage(WorkOrderSmsEventType.Reply, "Reply", 1);
Check((await Job(unreadable)) is { Status: WorkOrderSmsStatus.SettingsUnavailable, Recipient: null }, "Unreadable at staging is not Disabled.");
await admin.Database.ExecuteSqlRawAsync("ALTER TABLE TemporarilyUnavailable RENAME TO AppSettings");
Check(await dispatcher.ProcessOneAsync() && (await Job(referral)).Status == WorkOrderSmsStatus.Accepted, "Referral did not recover.");
Check(await dispatcher.ProcessOneAsync() && (await Job(reply)).Status == WorkOrderSmsStatus.Accepted, "Reply was not dispatched.");
Check(!await dispatcher.ProcessOneAsync() && recording.Sends.SequenceEqual(new[] {
    ("WorkOrderRegistration", "09120000002"), ("WorkOrderReferral", "09120000003"), ("ApprovedReply", "09120000001")
}), "Expected exactly one lookup per eligible event, using its own template and recipient.");

var queued = await Stage(WorkOrderSmsEventType.Reply, "Reply", 1);
saved.KavenegarSdkSendEnabled = false; // blank key on ordinary save retains it
saved = await editor.SaveSettingsAsync(saved);
Check(saved.KavenegarApiKeyHint?.EndsWith("only") == true && await dispatcher.ProcessOneAsync() &&
    (await Job(queued)).Status == WorkOrderSmsStatus.Disabled && recording.Sends.Count == 3, "Worker ignored a saved switch-off.");
var legacyFile = Options.Create(new KavenegarSmsOptions { ApiKey = "test-file", Sender = "1234", SdkSendEnabled = true });
var legacy = new KavenegarSmsGatewayFactory(workerDb, legacyFile, NullLogger<KavenegarSmsGatewayFactory>.Instance);
Check(await legacy.CreateAsync() is { IsEnabled: false, IsLookupConfigured: true, IsConfigured: false }, "Stored account, including empty sender and off switch, wins together.");
await admin.Database.ExecuteSqlRawAsync("ALTER TABLE AppSettings RENAME TO TemporarilyUnavailable");
Check(await legacy.CreateAsync() is null, "Read failure must not enable the legacy file account.");
await admin.Database.ExecuteSqlRawAsync("ALTER TABLE TemporarilyUnavailable RENAME TO AppSettings");
saved.ClearKavenegarApiKey = true;
await editor.SaveSettingsAsync(saved);
Check(await legacy.CreateAsync() is { IsEnabled: true, IsConfigured: true }, "Explicit clear should restore legacy file behavior.");
// Only the genuinely pre-key schema may fall back, not a partial schema.
await admin.Database.ExecuteSqlRawAsync("ALTER TABLE AppSettings RENAME COLUMN KavenegarSender TO TestMissingSender");
Check(await legacy.CreateAsync() is null, "A missing sender column is not evidence of an absent stored account.");
await admin.Database.ExecuteSqlRawAsync("ALTER TABLE AppSettings RENAME COLUMN TestMissingSender TO KavenegarSender");
await admin.Database.ExecuteSqlRawAsync("ALTER TABLE AppSettings RENAME COLUMN KavenegarApiKey TO TestMissingKey");
Check(await legacy.CreateAsync() is { IsEnabled: true, IsConfigured: true }, "Pre-key installs may still use their file account.");
Console.WriteLine("Work-order SMS database settings and dispatch checks passed (simulated sends).");

sealed class TemporaryDatabase : IDisposable
{
    public string Path { get; } = System.IO.Path.GetTempFileName();
    public void Dispose() => File.Delete(Path);
}

// Only replace the external send; enabled/configured are evaluated by the actual SDK gateway.
sealed class RecordingFactory(IKavenegarSmsGatewayFactory accounts) : IKavenegarSmsGatewayFactory
{
    public List<(string Template, string Recipient)> Sends { get; } = new();
    public async Task<IKavenegarSmsGateway?> CreateAsync(CancellationToken ct = default) =>
        await accounts.CreateAsync(ct) is { } account ? new Recorder(account, Sends) : null;

    private sealed class Recorder(IKavenegarSmsGateway account, List<(string, string)> sends) : IKavenegarSmsGateway
    {
        public bool IsEnabled => account.IsEnabled;
        public bool IsConfigured => account.IsConfigured;
        public bool IsLookupConfigured => account.IsLookupConfigured;
        public Task<KavenegarSmsSendResult> SendWorkOrderLookupAsync(string recipient, string year, string serial,
            string companyName, string title, string? deadline, string template)
        {
            sends.Add((template, recipient));
            return Task.FromResult(new KavenegarSmsSendResult(KavenegarSmsSendState.Accepted, sends.Count, 1, 200));
        }
        public Task<KavenegarSmsSendResult> SendAsync(string recipient, string message) => throw new NotSupportedException();
        public Task<KavenegarSmsSendResult> SendLookupAsync(string recipient, string token, string companyName, string template) => throw new NotSupportedException();
        public Task<KavenegarSmsSendResult> SendLookupWithToken20Async(string recipient, string token, string companyName, string token20, string template) => throw new NotSupportedException();
    }
}
