using System.Collections;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Inventory.Api.Data;
using Inventory.Api.Migrations;
using Inventory.Client.Pages;
using Inventory.Client.Services;
using Inventory.Client.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + message);
    checks++;
    Console.WriteLine("PASS: " + message);
}
void Photo(string html, int id, string description, int atLeast = 1) => Check(
    Regex.Matches(html, $@"/api/auth/users/photo/{id}\?t=0").Count >= atLeast, description);

// Real Razor rendering, with network/JS/initial page loading replaced by fixtures.
var services = new ServiceCollection();
services.AddLogging();
services.AddSingleton<IAuthState, TestAuth>();
services.AddSingleton<IToastService, ToastService>();
services.AddSingleton<LayoutState>();
services.AddSingleton<RealtimeService>();
services.AddSingleton(new ApiOptions { BaseUrl = "https://api.test.invalid/" });
services.AddSingleton<IJSRuntime, SilentJs>();
services.AddSingleton<NavigationManager, TestNavigation>();
services.AddSingleton<IDocArchiveService>(DispatchProxy.Create<IDocArchiveService, NoCallsProxy>());
services.AddSingleton(new HttpClient(new FixtureHttp()) { BaseAddress = new Uri("https://api.test.invalid/") });
await using var provider = services.BuildServiceProvider();
async Task<string> Render<T>(Dictionary<string, object?> parameters) where T : IComponent
{
    await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
    return await renderer.Dispatcher.InvokeAsync(async () =>
    {
        var root = await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters));
        return WebUtility.HtmlDecode(root.ToHtmlString());
    });
}
async Task<string> Page(Action<WorkOrders> setup) => await Render<TestWorkOrders>(new()
{
    [nameof(TestWorkOrders.Setup)] = setup
});
void Field(WorkOrders page, string name, object? value) => typeof(WorkOrders)
    .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);
var now = new DateTime(2026, 11, 21, 10, 0, 0);
var order = new WorkOrders.WoItem
{
    Id = 100, Number = "WO/1405/100", Title = "Avatar regression", OwnerUserId = 11,
    OwnerName = "Owner", CreatedAt = now, DueAt = now.AddDays(1),
    Assignees = Enumerable.Range(21, 5).Select(id => new WorkOrders.AsgItem
        { Id = id, UserId = id, Name = "Assignee " + id }).ToList(),
    Children = new() { new() { Id = 101, Number = "WO/1405/101", Title = "Child", OwnerUserId = 31, OwnerName = "Child owner", Status = "Open" } }
};
void Base(WorkOrders page)
{
    Field(page, "loading", false);
    Field(page, "access", new WorkOrders.AccessInfo { UserId = 11, CanView = true, CanCreate = true });
    Field(page, "mineItems", new List<WorkOrders.WoItem> { order });
    Field(page, "assignedItems", new List<WorkOrders.WoItem> { order });
}
var html = await Page(p => { Base(p); Field(p, "whoOpenId", 100); Field(p, "expanded", new HashSet<int> { 100 }); });
Photo(html, 11, "owner photo in both desktop and mobile rows", 2);
foreach (var id in Enumerable.Range(21, 5)) Photo(html, id, $"assignee {id}: stack, popup and expanded row", 3);
Check(html.Contains("wo-av-photo-wrap") && !html.Contains("class=\"av "), "photo stack replaces initials without losing wrappers");
html = await Page(p => { Base(p); Field(p, "tab", "kanban"); });
foreach (var id in Enumerable.Range(21, 5)) Photo(html, id, $"kanban includes photo for named assignee {id}, including beyond the first three");
html = await Page(p => { Base(p); Field(p, "tab", "kanban"); Field(p, "kanbanScope", "assigned"); });
Photo(html, 11, "assigned kanban shows owner photo");

html = await Page(p =>
{
    Base(p); Field(p, "showNew", true); Field(p, "tDropOpen", true);
    Field(p, "targets", new List<WorkOrders.TargetOpt> { new() { Id = 41, Username = "Target" } });
    Field(p, "unavailableTargets", new List<WorkOrders.TargetOpt> { new() { Id = 42, Username = "Unavailable" } });
    Field(p, "selTargets", new HashSet<int> { 41, 42 });
});
Photo(html, 41, "new/edit form selected chip and dropdown", 2);
Photo(html, 42, "unavailable recipient retains their own photo");

var parent = new WorkOrders.CmItem { Id = 1, AuthorUserId = 51, AuthorName = "Parent author", Text = "Original", CreatedAt = now };
void Detail(WorkOrders page)
{
    Base(page); Field(page, "detail", order);
    Field(page, "detailChecklist", new List<WorkOrders.ClItem> { new() { Id = 1, Text = "Complete", IsDone = true, DoneByUserId = 61, DoneByName = "Checklist user", DoneAt = now } });
    Field(page, "comments", new List<WorkOrders.CmItem> { parent, new() { Id = 2, AuthorUserId = 52, AuthorName = "Reply author", Text = "Reply", ReplyToId = 1, CreatedAt = now } });
    Field(page, "replyTo", parent);
    Field(page, "smsEvents", new List<WorkOrderSmsEventDto> { new() { Id = 1, RecipientUserId = 71, RecipientName = "SMS recipient", CreatedAtUtc = now, Status = WorkOrderSmsStatus.Delivered } });
}
html = await Page(Detail);
Photo(html, 31, "sub-order owner");
Photo(html, 51, "comment author, quoted author and reply composer", 3);
Photo(html, 52, "reply author");
Photo(html, 61, "checklist completion user");
Photo(html, 71, "SMS recipient");
Photo(html, 91, "attachment uploader through opt-in shared component");

for (var view = 0; view < 3; view++)
{
    var selectedView = view;
    html = await Page(p =>
    {
        Detail(p); Field(p, "gardeshOpen", true); Field(p, "gardeshView", selectedView);
        Field(p, "selectedGardeshAssignee", order.Assignees[0]);
        Field(p, "gdAddOpen", true); Field(p, "gdAddSel", new HashSet<int> { 81 });
        Field(p, "targets", new List<WorkOrders.TargetOpt> { new() { Id = 81, Username = "New workflow user" } });
        Field(p, "logs", new List<WorkOrders.LogItem> { new() { Id = 1, ActorUserId = 82, ActorName = "History actor", Action = "Seen", CreatedAt = now } });
    });
    Photo(html, 11, $"workflow {view}: owner", 4); // desktop, mobile, detail, workflow
    Photo(html, 21, $"workflow {view}: assignee and selected-person description", 4);
    Photo(html, 81, $"workflow {view}: added-person chip and options", 2);
    Photo(html, 82, $"workflow {view}: history actor");
    if (view == 1) Check(Regex.IsMatch(html, "class=\"tnode\"[^>]*>\\s*<span class=\"mini\"><img"), "tree uses a photo in its person node");
    if (view == 2) Check(Regex.IsMatch(html, "class=\"fnode root\"[^>]*>\\s*<span class=\"mini\"><img"), "flowchart uses a photo in its root node");
}
html = await Page(p =>
{
    Base(p); Field(p, "tab", "perf");
    var field = typeof(WorkOrders).GetField("perfRows", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var rows = (IList)Activator.CreateInstance(field.FieldType)!;
    var itemType = field.FieldType.GetGenericArguments()[0];
    for (var rank = 1; rank <= 2; rank++)
    {
        var row = Activator.CreateInstance(itemType, true)!;
        itemType.GetProperty("Rank")!.SetValue(row, rank);
        itemType.GetProperty("UserId")!.SetValue(row, 100 + rank);
        itemType.GetProperty("Name")!.SetValue(row, "Rank " + rank);
        rows.Add(row);
    }
    field.SetValue(p, rows);
});
Photo(html, 101, "performance podium and table", 2);
Photo(html, 102, "second podium participant and table", 2);
html = await Page(p =>
{
    Base(p); Field(p, "tab", "stats");
    Field(p, "stats", new WorkOrders.StatsDto { Cards = new() { Total = 1 }, PerAssignee = new() { new() { UserId = 111, Name = "Dashboard user", Total = 1 } } });
});
Photo(html, 111, "dashboard's existing avatar is preserved");
html = await Render<AttachmentBox>(new() { ["Module"] = "WorkOrders", ["RefId"] = 100, ["CanUpload"] = false });
Check(html.Contains("Uploader") && !html.Contains("/api/auth/users/photo/"), "shared attachment component does not change other forms by default");
html = await Render<UserAvatar>(new() { ["LoginUserId"] = 21, ["CacheBust"] = 123L });
Check(html.Contains("https://api.test.invalid/api/auth/users/photo/21?t=123") && html.Contains("onerror=") && html.Contains("/uploads/users/default.jpg"), "photo URL respects API base/cache key and retains missing-photo fallback");
html = await Render<UserAvatar>(new() { ["LoginUserId"] = 0 });
Check(html.Contains("user-avatar-default") && html.Contains("/uploads/users/default.jpg") && !html.Contains("/photo/0"), "unknown legacy/system identity uses a default, never another person's photo");

// Real SQLite upgrade, identity persistence and fresh-database checks.
await using (var connection = new SqliteConnection("Data Source=:memory:"))
{
    await connection.OpenAsync();
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE Users (Id INTEGER PRIMARY KEY, Username TEXT NOT NULL);
CREATE TABLE WorkOrders (Id INTEGER PRIMARY KEY, OwnerUserId INTEGER NOT NULL);
CREATE TABLE WorkOrderLogs (Id INTEGER PRIMARY KEY, OrderId INTEGER NOT NULL, ActorName TEXT NOT NULL,
    Action TEXT NOT NULL, Text TEXT NULL, CreatedAt TEXT NOT NULL DEFAULT '2026-11-21 10:00:00',
    RequestKey TEXT NULL, PayloadFingerprint TEXT NULL);
INSERT INTO Users VALUES (11, 'owner-login'), (21, 'created-display'), (22, 'actor-login'),
    (31, 'duplicate'), (32, 'duplicate'), (40, 'سیستم');
INSERT INTO WorkOrders VALUES (100, 11);
INSERT INTO WorkOrderLogs(Id, OrderId, ActorName, Action) VALUES
    (1, 100, 'created-display', 'Created'), (2, 100, 'actor-login', 'Seen'),
    (3, 100, 'unknown', 'Reply'), (4, 100, 'duplicate', 'Reply'),
    (5, 100, 'سیستم', 'Scheduled'), (6, 100, 'سیستم', 'Created');");
    await WorkOrderActorSchemaV1.EnsureAsync(db);
    var actors = await db.WorkOrderLogs.AsNoTracking().OrderBy(x => x.Id).Select(x => x.ActorUserId).ToArrayAsync();
    Check(actors.SequenceEqual(new int?[] { 11, 22, null, null, null, null }), "legacy backfill uses owner/unique username, not display-name guessing or system identity");
    await db.Database.ExecuteSqlRawAsync("UPDATE Users SET Username='renamed' WHERE Id=22; INSERT INTO Users VALUES (23, 'actor-login'), (24, 'unknown');");
    await WorkOrderActorSchemaV1.EnsureAsync(db);
    Check((await db.WorkOrderLogs.AsNoTracking().OrderBy(x => x.Id).Select(x => x.ActorUserId).ToArrayAsync()).SequenceEqual(actors), "repeated startup never reassigns history after username reuse or creates late guesses");
    var log = new WorkOrderLog { OrderId = 100, ActorUserId = 22, ActorName = "Historical display", Action = "Reply" };
    db.WorkOrderLogs.Add(log); await db.SaveChangesAsync();
    await db.Database.ExecuteSqlRawAsync("DELETE FROM Users WHERE Id=22;");
    Check(await db.WorkOrderLogs.AsNoTracking().AnyAsync(x => x.Id == log.Id && x.ActorUserId == 22), "new logs retain stable actor identity even when a user is deleted");
}
await using (var connection = new SqliteConnection("Data Source=:memory:"))
{
    await connection.OpenAsync();
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    await db.Database.EnsureCreatedAsync();
    await WorkOrderActorSchemaV1.EnsureAsync(db);
    db.WorkOrderLogs.Add(new() { OrderId = 1, ActorUserId = 11, ActorName = "New", Action = "Created" });
    await db.SaveChangesAsync();
    Check(await db.WorkOrderLogs.AnyAsync(x => x.ActorUserId == 11), "fresh schema already includes nullable ActorUserId and requires no legacy tables");
}
var migration = new MigrationProbe();
var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
migration.Apply(builder);
Check(builder.Operations.OfType<SqlOperation>().Single().Sql == WorkOrderActorSchemaV1.SqlServer, "SQL Server migration uses the same idempotent schema SQL as startup");

// API shape and writer coverage complement the rendered-page tests, without claiming
// an integration test of a running/authenticated API or live SQL Server database.
var root = new DirectoryInfo(AppContext.BaseDirectory);
while (root != null && !File.Exists(Path.Combine(root.FullName, "Inventory.sln"))) root = root.Parent;
if (root == null) throw new InvalidOperationException("Run from the source checkout.");
string Source(string path) => File.ReadAllText(Path.Combine(root.FullName, "src", path));
var controller = Source("Inventory.Api/Controllers/ItAssets/WorkOrdersController.cs");
Check(controller.Contains("c.Id, c.Number, c.Title, c.Status, c.OwnerUserId, c.OwnerName") && controller.Contains("w.Status, w.OwnerUserId, w.OwnerName"), "children carry the owner's ID through both projections");
Check(controller.Contains("c.IsDone, c.DoneByUserId, c.DoneByName") && controller.Contains("l.Id, l.ActorUserId, l.ActorName") && controller.Contains("RecipientUserId = n.RecipientUserId"), "checklist, history and SMS expose IDs alongside names");
Check(controller.Contains("ActorUserId = MyUserId") && Source("Inventory.Api/Controllers/ItAssets/ServersController.cs").Contains("ActorUserId = UserId") && Source("Inventory.Api/Services/DevTeam/DevTeamService.cs").Contains("ActorUserId = userId"), "all interactive work-order log writers persist login-user IDs");
Check(Source("Inventory.Api/Data/DbInitializer.cs").Contains("await WorkOrderActorSchemaV1.EnsureAsync(db);"), "legacy SQLite and SQL Server startup both apply actor schema repair");
Console.WriteLine($"ALL {checks} WORK-ORDER AVATAR CHECKS PASSED.");

public sealed class TestWorkOrders : WorkOrders
{
    [Parameter] public Action<WorkOrders> Setup { get; set; } = _ => { };
    protected override Task OnInitializedAsync() { Setup(this); return Task.CompletedTask; }
    // Parent fixtures are static: ignore count-change notifications from AttachmentBox.
    // Its child still completes its real asynchronous loading/rendering normally.
    protected override bool ShouldRender() => false;
}
public sealed class MigrationProbe : AddWorkOrderLogActorId
{
    public void Apply(MigrationBuilder builder) => Up(builder);
}
public sealed class TestNavigation : NavigationManager
{
    public TestNavigation() => Initialize("https://app.test.invalid/", "https://app.test.invalid/work-orders");
    protected override void NavigateToCore(string uri, bool forceLoad) { }
}
public sealed class SilentJs : IJSRuntime
{
    public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
    public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(T)!);
}
public class NoCallsProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new InvalidOperationException("Unexpected service call: " + method?.Name);
}
public sealed class FixtureHttp : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var json = request.RequestUri!.AbsolutePath.StartsWith("/api/attachments/")
            ? "[{\"id\":1,\"fileName\":\"example.txt\",\"uploaderUserId\":91,\"uploaderName\":\"Uploader\",\"uploadedAt\":\"2026-11-21T10:00:00\",\"size\":12,\"canDownload\":true}]"
            : "[]";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}
public sealed class TestAuth : IAuthState
{
    public bool IsLoggedIn => true;
    public bool IsAdmin => false;
    public bool IsOperator => true;
    public bool IsReferrer => false;
    public bool CanOperate => true;
    public string? Token => null;
    public int UserId => 11;
    public string DisplayName => "Owner";
    public string Role => "Operator";
    public int? ReferrerId => null;
    public bool Has(string permission) => false;
    public bool HasDirect(string permission) => false;
    public bool HasModule(string module) => false;
    public bool HasModuleDirect(string module) => false;
    public bool CanSee(string module) => false;
    public event Action? Changed { add { } remove { } }
    public Task InitializeAsync() => Task.CompletedTask;
    public Task SignInAsync(LoginResponse login) => Task.CompletedTask;
    public Task SignOutAsync() => Task.CompletedTask;
}
