using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Inventory.Api.Controllers;
using Inventory.Api.Controllers.Office;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Services;
using Inventory.Api.Services.Office.Outgoing;
using Inventory.Shared.Dtos;
using Inventory.Shared.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL: " + message);
    Console.WriteLine($"PASS {++checks}: {message}");
}
async Task Reject(Func<Task> operation, string message)
{
    try { await operation(); }
    catch (Exception ex) when (ex is InvalidOperationException || ex.Message.Contains("بایگانی") || ex.Message.Contains("پوشه"))
    { Check(true, message); return; }
    throw new Exception("FAIL (unexpectedly allowed): " + message);
}
static IEnumerable<BayeganiNodeDto> Flat(IEnumerable<BayeganiNodeDto> nodes) =>
    nodes.SelectMany(n => new[] { n }.Concat(Flat(n.Children)));

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
await db.Database.EnsureCreatedAsync();
var notify = new Notice();
var temporaryRoot = Path.Combine(Path.GetTempPath(), "shared-secretariat-test-" + Guid.NewGuid().ToString("N"));
var files = new FileStore(new FixtureEnvironment(temporaryRoot));
try
{
    var archive = new ArchiveService(db, notify);
    var incoming = new IncomingLetterService(db, notify, files, null!);
    var outgoing = new OutgoingLetterService(db, notify, null!, null!, null!);
    var flow = new ErjaService(db, notify, null!);
    for (var id = 1; id <= 7; id++)
        db.Users.Add(new User { Id = id, Username = "fixture-" + id, FirstName = "کاربر", LastName = id.ToString(), Role = id == 5 ? "Admin" : "Operator" });
    db.Roles.AddRange(new Role { Id = 1, Name = "Secretariat", IsActive = true },
        new Role { Id = 2, Name = "Personal only", IsActive = true },
        new Role { Id = 3, Name = "Incoming secretariat", IsActive = true },
        new Role { Id = 4, Name = "Inactive secretariat", IsActive = false });
    db.Permissions.AddRange(
        new Permission { Id = 1, Module = "OutgoingLetters", Action = "Dabirkhane" },
        new Permission { Id = 2, Module = "IncomingLetters", Action = "Read" },
        new Permission { Id = 3, Module = "OutgoingLetters", Action = "Read" },
        new Permission { Id = 4, Module = "IncomingLetters", Action = "Dabirkhane" });
    db.RolePermissions.AddRange(new RolePermission { RoleId = 1, PermissionId = 1 },
        new RolePermission { RoleId = 2, PermissionId = 2 }, new RolePermission { RoleId = 2, PermissionId = 3 },
        new RolePermission { RoleId = 3, PermissionId = 4 }, new RolePermission { RoleId = 4, PermissionId = 1 });
    foreach (var pair in new[] { (1, 1), (1, 2), (2, 1), (3, 2), (4, 3), (6, 4), (7, 2) })
        db.UserRoles.Add(new UserRole { UserId = pair.Item1, RoleId = pair.Item2 });
    for (var id = 10; id <= 16; id++)
        db.OutgoingLetters.Add(new OutgoingLetter { Id = id, Source = new LetterSource { Id = id, SourceType = 2, IsDelete = id == 15 },
            CreatorUserId = id == 11 ? 2 : 1, CreatorId = id == 11 ? 2 : 1, Title = "صادره " + id, SadereNumber = id == 14 ? null : "S-" + id,
            LetterNumber = "O-" + id, ReceiverOrganization = "مقصد نمونه", IsDelete = id == 16 });
    for (var id = 100; id <= 108; id++)
        db.IncomingLetters.Add(new IncomingLetter { Id = id, Source = new LetterSource { Id = id, SourceType = 3, IsDelete = id == 108 },
            CreateUserId = id == 104 ? 2 : 1, Title = "وارده " + id, NumberSabt = id, LetterNumber = "I-" + id,
            Ferestande = "مبدأ بیرونی", IsBayegani = id is 100 or 103, IsDelete = id == 107,
            DateErsal = new DateTime(2026, 11, 21).AddMinutes(id) });
    db.InnerLetters.Add(new InnerLetter { Id = 200, Source = new LetterSource { Id = 200, SourceType = 1 }, CreatorUserId = 1, Title = "نامه داخلی خصوصی" });
    db.Amalgars.Add(new Amalgar { AmalgarId = 1, Title = "جهت اطلاع" });
    db.Erjas.AddRange(
        new Erja { ErjaId = 501, SourceId = 101, SenderUserId = 4, ReciverUserId = 1, IsBayegani = true },
        new Erja { ErjaId = 502, SourceId = 102, SenderUserId = 4, ReciverUserId = 1 },
        new Erja { ErjaId = 503, SourceId = 102, SenderUserId = 4, ReciverUserId = 3 },
        new Erja { ErjaId = 504, SourceId = 103, SenderUserId = 4, ReciverUserId = 1, IsBayegani = true },
        new Erja { ErjaId = 505, SourceId = 100, SenderUserId = 4, ReciverUserId = 3, Answer = "PRIVATE-ANSWER", ShowForAll = false },
        new Erja { ErjaId = 506, SourceId = 200, SenderUserId = 1, ReciverUserId = 3 });
    db.AppAttachments.Add(new AppAttachment { Id = 1, Module = "IncomingLetters", RefId = 100,
        FileName = "synthetic.txt", ContentType = "text/plain", Data = System.Text.Encoding.UTF8.GetBytes("Synthetic fixture only"), UploaderUserId = 1 });
    try { await db.SaveChangesAsync(); }
    catch (DbUpdateException ex)
    {
        Console.WriteLine("Synthetic seed failed for: " + string.Join(", ", ex.Entries.Select(e => e.Metadata.ClrType.Name)));
        foreach (var entry in ex.Entries)
            Console.WriteLine(string.Join("; ", entry.Properties.Where(p => p.Metadata.IsPrimaryKey() || p.Metadata.IsForeignKey()).Select(p => p.Metadata.Name + "=" + p.CurrentValue)));
        throw;
    }

    var personalA = await archive.AddMainCategoryAsync(1, new() { Title = "پوشه شخصی الف" });
    var personalB = await archive.AddMainCategoryAsync(3, new() { Title = "پوشه شخصی ب" });
    var outgoingA = await archive.AddOutgoingMainCategoryAsync(1, new() { Title = "دبیرخانه صادره الف" });
    var outgoingB = await archive.AddOutgoingMainCategoryAsync(2, new() { Title = "دبیرخانه صادره ب" });
    var incomingA = await archive.AddIncomingSecretariatFolderAsync(1, new() { Title = "دبیرخانه وارده" }, true);
    Check(Flat(await archive.GetOutgoingTreeAsync(2)).Any(n => n.BayeganiId == outgoingA.BayeganiId), "second clerk sees first clerk's outgoing folder");
    Check(!Flat(await archive.GetOutgoingTreeAsync(2)).Any(n => n.BayeganiId == personalA.BayeganiId || n.BayeganiId == incomingA.BayeganiId), "outgoing tree excludes personal and incoming folders");
    Check(!Flat(await archive.GetTreeAsync(3)).Any(n => n.BayeganiId == personalA.BayeganiId || n.TypeBayegani > 1), "personal tree excludes all shared nodes and other owners");
    await Reject(() => archive.AddMainCategoryAsync(3, new() { Title = "forged", TypeBayegani = 2 }), "personal folder POST cannot forge a shared type");
    await Reject(() => archive.AddSubCategoryAsync(3, new() { Title = "forged", ParentId = outgoingA.BayeganiId, TypeBayegani = 3 }), "personal child creation cannot enter shared scope");
    await Reject(() => archive.EditFolderAsync(outgoingA.BayeganiId, 1, new() { Title = "forged" }), "even original creator cannot edit shared nodes through personal API");
    await Reject(() => archive.DeleteAsync(outgoingA.BayeganiId, 1), "personal deletion cannot delegate to shared deletion");
    var sub = await archive.AddOutgoingSubCategoryAsync(2, new() { Title = "زیرپوشه همکار", ParentId = outgoingA.BayeganiId });
    await archive.EditOutgoingFolderAsync(outgoingA.BayeganiId, 2, new() { Title = "عنوان مشترک" });
    Check((await db.LetterBayeganis.FindAsync(outgoingA.BayeganiId))!.UserId == 1, "shared rename preserves original actor ID");
    await Reject(() => archive.MoveOutgoingFolderAsync(outgoingA.BayeganiId, sub.BayeganiId, 2), "cross-user folder cycle blocked");
    await Reject(() => archive.MoveOutgoingFolderAsync(sub.BayeganiId, sub.BayeganiId, 2), "folder cannot move into itself");
    await Reject(() => archive.DeleteOutgoingAsync(outgoingA.BayeganiId, 2), "non-empty shared folder cannot be deleted");
    await archive.MoveOutgoingFolderAsync(sub.BayeganiId, outgoingB.BayeganiId, 1);
    Check((await db.LetterBayeganis.FindAsync(sub.BayeganiId))!.ParentId == outgoingB.BayeganiId, "clerk can move another clerk's folder");
    await archive.MoveOutgoingFolderAsync(sub.BayeganiId, 0, 1);
    Check((await db.LetterBayeganis.FindAsync(sub.BayeganiId))!.ParentId == 0, "folder can move back to shared root");
    foreach (var destination in new[] { personalA.BayeganiId, incomingA.BayeganiId, -1, 999999 })
        await Reject(() => archive.ArchiveOutgoingLettersAsync(2, new() { LetterIds = new() { 10 }, FolderId = destination }), "invalid or cross-scope outgoing destination blocked: " + destination);
    await Reject(() => archive.ArchiveOutgoingLettersAsync(2, new() { LetterIds = new() { 10, 14 }, FolderId = outgoingA.BayeganiId }), "mixed signed/unsigned batch rejected atomically");
    Check(!await db.LetterBayeganis.AnyAsync(b => b.TypeBayegani == 2 && b.LetterId == 10), "invalid outgoing batch leaves no partial archive links");
    foreach (var invalidId in new[] { 15, 16, 100, 99999, -1 })
        await Reject(() => archive.ArchiveOutgoingLettersAsync(2, new() { LetterIds = new() { invalidId }, FolderId = 0 }), "deleted/wrong-kind/invalid outgoing letter blocked: " + invalidId);
    await archive.ArchiveOutgoingLettersAsync(1, new() { LetterIds = new() { 10, 10 }, FolderId = outgoingA.BayeganiId });
    var outgoingNode = await db.LetterBayeganis.SingleAsync(b => b.TypeBayegani == 2 && b.LetterId == 10 && !b.IsDelete);
    Check(outgoingNode.UserId == 1 && (await db.OutgoingLetters.FindAsync(10))!.ArchivedByUserId == 1, "archive actor attribution retained");
    Check(Flat(await archive.GetOutgoingTreeAsync(2)).Single(n => n.LetterId == 10).CreatorUserId == 1, "shared tree retains authoritative creator/avatar identity");
    await Reject(() => archive.ArchiveOutgoingLettersAsync(2, new() { LetterIds = new() { 10, 11 }, FolderId = outgoingB.BayeganiId }), "second user cannot create duplicate shared archive");
    Check(!(await db.OutgoingLetters.FindAsync(11))!.IsArchived, "duplicate batch leaves other letters untouched");
    await archive.MoveArchivedLetterAsync(new() { BayeganiId = outgoingNode.BayeganiId, NewParentId = 0 }, 2);
    Check(outgoingNode.ParentId == 0, "second user can move first user's letter to root");
    await archive.AddLettersToArchiveAsync(1, new() { FolderId = personalA.BayeganiId, OutgoingLetterIds = new() { 10 } });
    await archive.UnarchiveByLetterAsync(10, 1);
    Check((await db.OutgoingLetters.FindAsync(10))!.IsArchived, "personal outgoing unarchive cannot clear shared state");
    db.LetterBayeganis.Add(new LetterBayegani { Title = "legacy duplicate", UserId = 2, LetterId = 10, TypeBayegani = 2 });
    await db.SaveChangesAsync();
    var outgoingList = await outgoing.GetDabirkhaneAsync(new() { ArchivedOnly = true });
    Check(outgoingList.Count == 1 && outgoingList[0].BayeganiId == outgoingNode.BayeganiId, "old per-user duplicates do not crash list hydration; stable node chosen");
    await archive.UnarchiveOutgoingLetterAsync(10, 2);
    await archive.UnarchiveOutgoingLetterAsync(10, 2);
    Check(!await db.LetterBayeganis.AnyAsync(b => b.TypeBayegani == 2 && b.LetterId == 10 && !b.IsDelete), "shared unarchive closes all legacy duplicates and is idempotent");
    Check(!(await db.OutgoingLetters.FindAsync(10))!.IsArchived && (await outgoing.GetDabirkhaneStatsAsync()).Archived == 0, "outgoing list and global archive stats agree after another user's unarchive");

    var privateFlags = await db.Erjas.OrderBy(e => e.ErjaId).Select(e => new { e.ErjaId, e.IsBayegani, e.IsRead }).ToListAsync();
    var sharedIncoming = await archive.GetIncomingSecretariatAsync(true, null);
    Check(sharedIncoming.Items.Select(i => i.LetterId).Order().SequenceEqual(new[] { 100, 103 }), "old letter-level registrar archives are shared, referral-only archives are not imported");
    Check(sharedIncoming.Items.All(i => i.ErjaId == null && i.SenderUserId == 1 && i.SecretariatBayeganiId > 0), "shared incoming rows use creator identity and shared node IDs, never another user's referral ID");
    Check(JsonSerializer.Serialize(privateFlags) == JsonSerializer.Serialize(await db.Erjas.OrderBy(e => e.ErjaId).Select(e => new { e.ErjaId, e.IsBayegani, e.IsRead }).ToListAsync()), "legacy import changes no personal referral archive or read flag");
    var importCount = await db.LetterBayeganis.CountAsync(b => b.TypeBayegani == 3);
    await archive.GetIncomingSecretariatAsync(true, null);
    await archive.GetIncomingSecretariatTreeAsync();
    Check(importCount == await db.LetterBayeganis.CountAsync(b => b.TypeBayegani == 3), "legacy import is idempotent across list/tree reloads");
    Check((await incoming.GetArchiveAsync(1, null)).Items.All(i => i.LetterId != 100), "personal archive does not acquire shared registrar-only archives");
    Check((await incoming.GetArchiveAsync(3, null)).TotalCount == 0, "other recipient's private archive stays empty");
    var privateRow = (await incoming.GetArchiveAsync(1, null)).Items.First(i => i.LetterId == 101);
    Check(privateRow.SenderUserId == 4, "private received archive retains direct-referrer photo identity");
    var incomingSub = await archive.AddIncomingSecretariatFolderAsync(2, new() { Title = "زیرپوشه وارده", ParentId = incomingA.BayeganiId }, false);
    await archive.EditIncomingSecretariatFolderAsync(incomingA.BayeganiId, new() { Title = "وارده مشترک" });
    await Reject(() => archive.MoveIncomingSecretariatFolderAsync(incomingA.BayeganiId, incomingSub.BayeganiId), "incoming cross-user folder cycle is rejected");
    await Reject(() => archive.ArchiveIncomingSecretariatLettersAsync(2, new() { LetterIds = new() { 102 }, FolderId = outgoingA.BayeganiId }), "incoming archive cannot use outgoing folder");
    await Reject(() => archive.ArchiveIncomingSecretariatLettersAsync(2, new() { LetterIds = new() { 102, 107 }, FolderId = incomingSub.BayeganiId }), "invalid incoming batch is all-or-nothing");
    Check(!(await db.IncomingLetters.FindAsync(102))!.IsBayegani, "invalid incoming batch leaves shared state unchanged");
    await archive.ArchiveIncomingSecretariatLettersAsync(2, new() { LetterIds = new() { 102 }, FolderId = incomingSub.BayeganiId });
    Check((await incoming.GetInboxAsync(1, null, null)).Items.Any(i => i.LetterId == 102)
        && (await incoming.GetInboxAsync(3, null, null)).Items.Any(i => i.LetterId == 102), "shared archive never removes letter from either personal current inbox");
    await archive.AddLettersToArchiveAsync(1, new() { FolderId = personalA.BayeganiId, ErjaIds = new() { 502 } });
    Check((await db.IncomingLetters.FindAsync(102))!.IsBayegani && (await db.Erjas.FindAsync(502))!.IsBayegani == true
        && (await db.Erjas.FindAsync(503))!.IsBayegani != true, "personal archive affects one recipient only and leaves shared status intact");
    await archive.UnarchiveIncomingSecretariatLetterAsync(102);
    Check((await db.Erjas.FindAsync(502))!.IsBayegani == true && (await incoming.GetArchiveAsync(1, null)).Items.Any(i => i.LetterId == 102), "other clerk's shared unarchive preserves recipient's personal archive");
    await archive.ArchiveIncomingSecretariatLettersAsync(1, new() { LetterIds = new() { 102 }, FolderId = incomingSub.BayeganiId });
    await archive.UnarchiveIncomingLetterAsync(102, 1);
    Check((await db.IncomingLetters.FindAsync(102))!.IsBayegani && (await db.Erjas.FindAsync(502))!.IsBayegani != true, "personal unarchive cannot clear the shared incoming flag");
    var incomingNode = await db.LetterBayeganis.SingleAsync(b => b.TypeBayegani == 3 && b.LetterId == 102 && !b.IsDelete);
    await archive.MoveIncomingSecretariatLetterAsync(new() { BayeganiId = incomingNode.BayeganiId, NewParentId = 0 });
    Check(incomingNode.ParentId == 0, "incoming shared letter can move to root");
    await Reject(() => archive.MoveLetterAsync(incomingNode.BayeganiId, personalA.BayeganiId, 1), "personal move route cannot steal a shared incoming node");
    await archive.DeleteIncomingSecretariatNodeAsync(incomingSub.BayeganiId);
    Check((await db.LetterBayeganis.FindAsync(incomingSub.BayeganiId))!.IsDelete, "other clerk can delete an empty incoming shared folder");
    await Reject(() => archive.ArchiveIncomingSecretariatLettersAsync(1, new() { LetterIds = new() { 104 }, FolderId = incomingSub.BayeganiId }), "deleted destination cannot receive new letters");
    var search = await archive.GetIncomingSecretariatAsync(true, "102", 1, 1);
    Check(search.TotalCount == 1 && search.Items.Single().LetterId == 102, "shared incoming search and server paging agree");
    var pagedTree = new Paging.Request(0, 1);
    Check((await archive.GetIncomingSecretariatTreeAsync(pagedTree)).Count == 1, "shared incoming tree retains root paging");
    Check(!Flat(await archive.GetIncomingSecretariatTreeAsync()).Any(n => n.TypeBayegani != 3), "shared incoming tree never reveals personal/outgoing folders");
    var registered = await archive.GetIncomingSecretariatAsync(false, null);
    Check(registered.Items.Any(i => i.LetterId == 104 && i.SenderUserId == 2)
        && registered.Items.Any(i => i.LetterId == 101)
        && registered.Items.All(i => i.LetterId is not (107 or 108)), "shared register includes different registrars, ignores personal archive flags, excludes deleted records");

    // Real MVC route authorization and request binding, without production startup/configuration.
    var host = new WebHostBuilder().ConfigureLogging(l => l.ClearProviders())
        .ConfigureServices(services =>
        {
            services.AddSingleton(db); services.AddSingleton<IArchiveService>(archive);
            services.AddSingleton<IIncomingSecretariatArchiveService>(archive);
            services.AddSingleton<IIncomingLetterService>(incoming); services.AddSingleton<IErjaService>(flow);
            services.AddSingleton<IOutgoingLetterService>(outgoing); services.AddSingleton<INotifyService>(notify);
            services.AddSingleton(files);
            services.AddTransient<IncomingLettersController>(_ => new(incoming, flow, null!, archive, files, notify, db));
            services.AddTransient<OutgoingLettersController>(_ => new(db, outgoing, null!, flow, null!, null!, archive, files, NullLogger<OutgoingLettersController>.Instance));
            services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("Fixture", _ => { });
            services.AddAuthorization();
            services.AddControllers(o => o.Filters.Add<ApiExceptionFilter>()).AddApplicationPart(typeof(IncomingLettersController).Assembly)
                .ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new OnlyArchiveControllers())).AddControllersAsServices();
        }).Configure(app => { app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.UseEndpoints(e => e.MapControllers()); });
    using var server = new TestServer(host);
    using var client = server.CreateClient();
    async Task<HttpResponseMessage> Request(int? user, HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (user.HasValue) request.Headers.Add("Fixture-User", user.Value.ToString());
        if (body != null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
    var routes = new (HttpMethod Method, string Suffix, object? Body)[]
    {
        (HttpMethod.Get, "", null), (HttpMethod.Get, "/bayegani/tree", null),
        (HttpMethod.Post, "/bayegani/main-category", new { title = "blocked" }),
        (HttpMethod.Post, "/bayegani/sub-category", new { title = "blocked", parentId = incomingA.BayeganiId }),
        (HttpMethod.Put, $"/bayegani/folder/{incomingA.BayeganiId}", new { title = "blocked" }),
        (HttpMethod.Post, $"/bayegani/folder/{incomingA.BayeganiId}/move?newParentId=0", null),
        (HttpMethod.Delete, $"/bayegani/{incomingA.BayeganiId}", null),
        (HttpMethod.Post, "/bayegani/letters", new { letterIds = new[] { 104 }, folderId = 0 }),
        (HttpMethod.Delete, "/bayegani/letter/100", null),
        (HttpMethod.Post, "/bayegani/move-letter", new { bayeganiId = incomingNode.BayeganiId, newParentId = 0 })
    };
    foreach (var route in routes)
    {
        var url = "/api/incoming-letters/dabirkhane" + route.Suffix;
        using var anonymous = await Request(null, route.Method, url, route.Body);
        Check(anonymous.StatusCode == HttpStatusCode.Unauthorized, "anonymous incoming shared route denied: " + route.Suffix);
        foreach (var deniedUser in new[] { 3, 6 })
        {
            using var denied = await Request(deniedUser, route.Method, url, route.Body);
            Check(denied.StatusCode == HttpStatusCode.Forbidden, "read-only/inactive-role user denied shared route: " + deniedUser + route.Suffix);
        }
    }
    foreach (var allowedUser in new[] { 1, 2, 4, 5 })
    {
        using var response = await Request(allowedUser, HttpMethod.Get, "/api/incoming-letters/dabirkhane?archived=true");
        Check(response.IsSuccessStatusCode, "incoming shared list allows unified/incoming/legacy-admin permission: " + allowedUser);
        var body = await response.Content.ReadFromJsonAsync<PagedResult<IncomingLetterListItemDto>>();
        Check(body!.Items.Any(i => i.LetterId == 100), "other registrar's old archived letter is visible to clerk: " + allowedUser);
    }
    using (var response = await Request(2, HttpMethod.Put, $"/api/incoming-letters/dabirkhane/bayegani/folder/{incomingA.BayeganiId}", new { title = "ویرایش همکار دوم", typeBayegani = 1 }))
        Check(response.IsSuccessStatusCode && (await db.LetterBayeganis.FindAsync(incomingA.BayeganiId))!.TypeBayegani == 3, "shared write uses permission not ownership and ignores spoofed archive type");
    using (var response = await Request(3, HttpMethod.Get, "/api/incoming-letters/100"))
        Check(response.IsSuccessStatusCode, "actual private recipient retains detail access");
    using (var response = await Request(7, HttpMethod.Get, "/api/incoming-letters/100"))
        Check(response.StatusCode == HttpStatusCode.Forbidden, "generic read non-participant still cannot read another letter");
    using (var response = await Request(2, HttpMethod.Get, "/api/incoming-letters/100"))
    {
        Check(response.IsSuccessStatusCode, "secretariat-only clerk can open another registrar's letter");
        var text = await response.Content.ReadAsStringAsync();
        Check(!text.Contains("PRIVATE-ANSWER"), "shared viewing does not grant private-referral-answer access");
    }
    using (var response = await Request(2, HttpMethod.Get, "/api/incoming-letters/100/gardesh"))
    {
        Check(response.IsSuccessStatusCode, "secretariat-only clerk can open circulation");
        Check(!(await response.Content.ReadAsStringAsync()).Contains("PRIVATE-ANSWER"), "shared circulation does not impersonate an administrator");
    }
    foreach (var url in new[] { "/api/incoming-letters/100/attachments", "/api/incoming-letters/100/attachments/download-zip", "/api/incoming-letters/attachments/1/download" })
    {
        using var allowed = await Request(2, HttpMethod.Get, url);
        Check(allowed.IsSuccessStatusCode, "shared attachment read permitted: " + url);
        using var denied = await Request(7, HttpMethod.Get, url);
        Check(denied.StatusCode == HttpStatusCode.Forbidden, "attachment read remains protected from generic readers: " + url);
    }
    using (var response = await Request(2, HttpMethod.Get, "/api/incoming-letters/200/gardesh"))
        Check(!response.IsSuccessStatusCode, "incoming-secretariat read capability cannot be used to read an internal letter's flow");
    using (var response = await Request(1, HttpMethod.Post, "/api/incoming-letters/100/bayegani"))
        Check(response.IsSuccessStatusCode && (await db.IncomingLetters.FindAsync(100))!.IsBayegani, "private registrar toggle no longer changes shared letter-level archive state");
    Check((await incoming.GetArchiveAsync(1, null)).Items.Any(i => i.LetterId == 100), "direct private registrar archive is backed by an owner-scoped node");
    Check((await incoming.GetDetailAsync(100, 1, false))!.IsBayegani, "private direct archive is reflected in the letter reader without using shared state");
    Check((await incoming.GetStatsAsync(1)).TotalArchive == (await incoming.GetArchiveAsync(1, null)).TotalCount,
        "private archive counter matches both direct and referral personal rows");
    using (var response = await Request(1, HttpMethod.Post, "/api/incoming-letters/100/unarchive"))
        Check(response.IsSuccessStatusCode && (await db.IncomingLetters.FindAsync(100))!.IsBayegani, "private complete-unarchive route cannot remove shared registrar archive");
    foreach (var suffix in new[] { "/dabirkhane/bayegani/tree", "/dabirkhane", "/dabirkhane/stats" })
    {
        using var allowed = await Request(2, HttpMethod.Get, "/api/outgoing-letters" + suffix);
        Check(allowed.IsSuccessStatusCode, "outgoing shared read works for second clerk: " + suffix);
        using var denied = await Request(3, HttpMethod.Get, "/api/outgoing-letters" + suffix);
        Check(suffix.EndsWith("stats") ? (await denied.Content.ReadAsStringAsync()).Contains("\"archived\":0") : denied.StatusCode == HttpStatusCode.Forbidden,
            "unauthorized outgoing read exposes no archive data: " + suffix);
    }
    foreach (var route in routes.Where(r => r.Method != HttpMethod.Get))
    {
        var url = "/api/outgoing-letters/dabirkhane" + route.Suffix;
        using var denied = await Request(3, route.Method, url, route.Body);
        Check(denied.StatusCode == HttpStatusCode.Forbidden, "all outgoing shared write routes require secretariat permission: " + route.Suffix);
        using var anonymous = await Request(null, route.Method, url, route.Body);
        Check(anonymous.StatusCode == HttpStatusCode.Unauthorized, "all outgoing shared write routes reject anonymous callers: " + route.Suffix);
    }
    db.UserRoles.RemoveRange(db.UserRoles.Where(r => r.UserId == 2));
    await db.SaveChangesAsync();
    using (var response = await Request(2, HttpMethod.Get, "/api/incoming-letters/dabirkhane?archived=true"))
        Check(response.StatusCode == HttpStatusCode.Forbidden, "revoked secretariat role is enforced on the next request");
    Check(notify.Channels.Contains("outgoing-letters") && notify.Channels.Contains("incoming-letters"), "shared mutations publish both modules' established live-refresh channels");
    Console.WriteLine($"ALL {checks} SHARED SECRETARIAT ARCHIVE CHECKS PASSED (real production API/model/services, SQLite, authenticated MVC TestServer).");
}
finally { if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, true); }

sealed class Notice : INotifyService
{
    public List<string> Channels { get; } = new();
    public Task BroadcastChangedAsync(string scope) { Channels.Add(scope); return Task.CompletedTask; }
    public Task SendAsync(int userId, string title, string? body, string fromName, string formName, string? link, int? companyId = null) => Task.CompletedTask;
    public Task SendManyAsync(IEnumerable<int> ids, string title, string? body, string fromName, string formName, string? link, int? companyId = null) => Task.CompletedTask;
    public Task SendToRoleAsync(string role, string title, string? body, string fromName, string formName, string? link, int? companyId = null) => Task.CompletedTask;
}
sealed class FixtureEnvironment(string root) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "SharedSecretariatArchive";
    public string EnvironmentName { get; set; } = "Testing";
    public string ContentRootPath { get; set; } = root;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
sealed class OnlyArchiveControllers : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        foreach (var controller in feature.Controllers.ToList())
            if (controller.AsType() != typeof(IncomingLettersController) && controller.AsType() != typeof(OutgoingLettersController))
                feature.Controllers.Remove(controller);
    }
}
sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory log, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, log, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!int.TryParse(Request.Headers["Fixture-User"].FirstOrDefault(), out var id) || id is < 1 or > 7)
            return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()),
            new Claim(ClaimTypes.Name, "fixture-" + id), new Claim(ClaimTypes.Role, id == 5 ? "Admin" : "Operator") }, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
