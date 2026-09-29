using System.Data.Common;
using System.Text.Json;
using Inventory.Api.Services;
using Inventory.Client.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

// Run with: dotnet run --project inventory/tests/DatabasePagination
// These are relational tests, not EF's InMemory provider: inspect executed SQL.
await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var recorder = new SqlRecorder();
await using var db = new TestDb(new DbContextOptionsBuilder<TestDb>()
    .UseSqlite(connection).AddInterceptors(recorder).Options);
await db.Database.EnsureCreatedAsync();
db.Rows.AddRange(Enumerable.Range(1, 12).Select(i => new Row
{
    Id = i, TenantId = i <= 10 ? 1 : 2, Name = "same", Active = i % 2 == 0,
    Quantity = i / 10m
}));
await db.SaveChangesAsync();
db.ChangeTracker.Clear();
var query = db.Rows.AsNoTracking().Where(r => r.TenantId == 1 && r.Active)
    .OrderBy(r => r.Name).ThenBy(r => r.Id);

recorder.Commands.Clear();
var request = new Paging.Request(1, 2);
var rows = await query.ToPageListAsync(request);
Check(request.Total == 5, "total must count all filtered rows, not page rows");
Check(rows.Select(r => r.Id).SequenceEqual(new[] { 4, 6 }), "exact non-page-aligned offset");
Check(recorder.Commands.Count == 2, "one COUNT and one bounded SELECT");
Check(recorder.Commands[0].Contains("COUNT(*)", StringComparison.OrdinalIgnoreCase), "COUNT executes in SQL");
Check(recorder.Commands[1].Contains("LIMIT", StringComparison.OrdinalIgnoreCase)
    && recorder.Commands[1].Contains("OFFSET", StringComparison.OrdinalIgnoreCase), "paging executes in SQL");
Check(db.ChangeTracker.Entries().Count() == 0, "no-tracking query stays no-tracking");
var response = JsonSerializer.SerializeToElement(request.Result(rows.Select(r => new { r.Id })));
Check(response.GetProperty("total").GetInt32() == 5, "mapping retains database count");
Check(response.GetProperty("items").GetArrayLength() == 2, "mapping must not slice a second time");

var mappedCount = 0;
var mapped = await Paging.ResultAsync(async page =>
{
    var selected = await query.ToPageListAsync(page);
    return selected.Select(r => { mappedCount++; return r.Id; }).ToList();
}, 3, 1);
Check(mappedCount == 1, "only selected page rows are mapped");
Check(JsonSerializer.SerializeToElement(mapped).GetProperty("total").GetInt32() == 5, "callback count");

var beyondCap = new Paging.Request(3, 2);
var afterCap = await query.ToPageListAsync(beyondCap, defaultCap: 2);
Check(beyondCap.Total == 5 && afterCap.Select(r => r.Id).SequenceEqual(new[] { 8, 10 }),
    "legacy cap must not truncate a requested page or its count");
Check((await query.ToPageListAsync(null, defaultCap: 2)).Count == 2, "legacy cap without pagination");
Check((await query.ToPageListAsync(null)).Count == 5, "legacy unpaged internal calls stay unpaged");
Check(JsonSerializer.SerializeToElement(await Paging.ResultAsync(query, 0, null)).ValueKind == JsonValueKind.Array,
    "legacy array response is preserved");
var far = new Paging.Request(100, 3);
Check((await query.ToPageListAsync(far)).Count == 0 && far.Total == 5, "empty page retains total");
var skipOnly = new Paging.Request(2, null);
Check((await query.ToPageListAsync(skipOnly)).Count == 3, "legacy skip-only contract");
var negative = new Paging.Request(-50, 2);
Check((await query.ToPageListAsync(negative)).First().Id == 2, "negative skip normalized to zero");
Check(!new Paging.Request(-1, 0).IsPaged, "legacy zero-take contract");
Check(Paging.Offset(int.MaxValue, int.MaxValue) == int.MaxValue, "offset cannot overflow");
var empty = await Paging.ResultAsync<int>(_ => Task.FromResult(new List<int>()), 0, 10);
Check(JsonSerializer.SerializeToElement(empty).GetProperty("total").GetInt32() == 0, "early empty result");
await Throws<InvalidOperationException>(() => Paging.ResultAsync<int>(_ => Task.FromResult(new List<int> { 1 }), 0, 10),
    "non-empty service results must have applied database pagination");
await Throws<InvalidOperationException>(async () => { await query.ToPageListAsync(request); }, "request cannot page two queries");
await Throws<InvalidOperationException>(async () =>
{
    await new[] { new Row() }.AsQueryable().ToPageListAsync(new Paging.Request(0, 1));
}, "LINQ-to-Objects is not database pagination");

var oldPage = await Paging.QueryAsync(query, 1, 2);
Check(oldPage.Total == 5 && oldPage.Rows.Select(r => r.Id).SequenceEqual(new[] { 4, 6 }), "existing QueryAsync contract");

// A combined cartable must page the union, not concatenate two separately paged lists.
recorder.Commands.Clear();
var combined = db.Rows.Where(r => r.TenantId == 1 && r.Id <= 3).Select(r => new { r.Id, r.Name })
    .Concat(db.Rows.Where(r => r.TenantId == 1 && r.Id >= 7).Select(r => new { r.Id, r.Name }))
    .OrderBy(r => r.Name).ThenBy(r => r.Id);
var unionPage = new Paging.Request(2, 3);
Check((await combined.ToPageListAsync(unionPage)).Select(r => r.Id).SequenceEqual(new[] { 3, 7, 8 }), "union page boundary");
Check(unionPage.Total == 7 && recorder.Commands.Last().Contains("UNION ALL"), "union count and paging in database");

// Translation smoke check for the production SQLite quantity predicate.
var quantities = db.Rows.Where(r => Math.Round(db.Rows.Where(s => s.Id == r.Id)
    .Sum(s => (double?)s.Quantity) ?? 0, 3) <= 0.5).OrderBy(r => r.Id);
var quantityPage = new Paging.Request(1, 2);
Check((await quantities.ToPageListAsync(quantityPage)).Select(r => r.Id).SequenceEqual(new[] { 2, 3 }), "quantity filtering before page");
Check(quantityPage.Total == 5, "quantity predicate count");

// Dictionary-shaped endpoints keep their object contract, including on empty pages.
var dictionaryRequest = new Paging.Request(1, 1);
var dictionaryRows = await query.ToPageListAsync(dictionaryRequest);
var dictionaryJson = JsonSerializer.SerializeToElement(dictionaryRequest.DictionaryResult(dictionaryRows.ToDictionary(r => r.Id, r => r.Name)));
Check(dictionaryJson.GetProperty("total").GetInt32() == 5, "dictionary global count");
Check(dictionaryJson.GetProperty("items").ValueKind == JsonValueKind.Object
    && dictionaryJson.GetProperty("items").GetProperty("4").GetString() == "same", "dictionary items remain an object");
var emptyDictionaryRequest = new Paging.Request(100, 2);
var emptyDictionaryRows = await query.ToPageListAsync(emptyDictionaryRequest);
Check(JsonSerializer.SerializeToElement(emptyDictionaryRequest.DictionaryResult(emptyDictionaryRows.ToDictionary(r => r.Id, r => r.Name)))
    .GetProperty("items").EnumerateObject().Count() == 0, "empty dictionary beyond final page");

// Exercise the production tree helper: count roots, retain complete subtrees,
// honor tenant scope and orphan policy, and never read unselected branches.
db.Nodes.AddRange(
    new TreeNode { Id = 10, TenantId = 1 },
    new TreeNode { Id = 11, TenantId = 1, ParentId = 10 },
    new TreeNode { Id = 12, TenantId = 1, ParentId = 10 },
    new TreeNode { Id = 20, TenantId = 1 },
    new TreeNode { Id = 21, TenantId = 1, ParentId = 20 },
    new TreeNode { Id = 22, TenantId = 1, ParentId = 21 },
    new TreeNode { Id = 30, TenantId = 1, ParentId = 90 },
    new TreeNode { Id = 40, TenantId = 1, ParentId = 41 },
    new TreeNode { Id = 41, TenantId = 1, ParentId = 40 },
    new TreeNode { Id = 90, TenantId = 2 });
await db.SaveChangesAsync();
db.ChangeTracker.Clear();
var treeScope = db.Nodes.AsNoTracking().Where(n => n.TenantId == 1);
var treeRoots = treeScope.Where(n => n.ParentId == null || !treeScope.Any(p => p.Id == n.ParentId)).OrderBy(n => n.Id);
IQueryable<TreeNode> Children(int[] ids) => treeScope.Where(n => n.ParentId != null && ids.Contains(n.ParentId.Value)).OrderBy(n => n.Id);
var treeRequest = new Paging.Request(1, 1);
recorder.Commands.Clear();
var subtree = await treeScope.OrderBy(n => n.Id).ReadTreePageAsync(treeRoots, Children, n => n.Id, treeRequest);
Check(treeRequest.Total == 3, "tree count counts visible roots, not all nodes");
Check(subtree.Select(n => n.Id).SequenceEqual(new[] { 20, 21, 22 }), "complete subtree only for selected root");
Check(recorder.Commands[0].Contains("COUNT(*)") && recorder.Commands[1].Contains("LIMIT"), "root pagination executes before descendant reads");
Check(subtree.All(n => n.TenantId == 1) && !subtree.Any(n => n.Id == 11), "tree excludes other tenants and unselected branches");
var orphanRequest = new Paging.Request(2, 1);
Check((await treeScope.ReadTreePageAsync(treeRoots, Children, n => n.Id, orphanRequest)).Single().Id == 30,
    "parent outside authorized scope does not hide an orphan root");
var missingRootRequest = new Paging.Request(10, 1);
recorder.Commands.Clear();
Check((await treeScope.ReadTreePageAsync(treeRoots, Children, n => n.Id, missingRootRequest)).Count == 0
    && missingRootRequest.Total == 3 && recorder.Commands.Count == 2, "no descendant query for empty root page");
Check((await treeScope.OrderBy(n => n.Id).ReadTreePageAsync(treeRoots, Children, n => n.Id, null)).Count == 9,
    "unpaged tree source retains legacy behavior, including disconnected cycles");

// Regression for the SQL grouping/rounding shape used by performance ranking.
// These are translation/algorithm tests; they do not instantiate its HTTP controller.
for (var userId = 1; userId <= 4; userId++)
    for (var i = 0; i < 8; i++) db.Replies.Add(new Reply { UserId = userId, OnTime = i < userId * 2 - 1 });
await db.SaveChangesAsync();
var groupedReplies = db.Replies.GroupBy(x => x.UserId)
    .Select(g => new { UserId = g.Key, Total = g.Count(), OnTime = g.Count(x => x.OnTime) });
var ranking = groupedReplies.Select(g => new
{
    g.UserId,
    Percent = (int)(g.OnTime * 100L / g.Total +
        ((g.OnTime * 100L % g.Total * 2 > g.Total ||
          (g.OnTime * 100L % g.Total * 2 == g.Total && g.OnTime * 100L / g.Total % 2 != 0)) ? 1 : 0))
}).OrderByDescending(x => x.Percent).ThenBy(x => x.UserId);
var rankingRequest = new Paging.Request(1, 2);
var rankedPage = await ranking.ToPageListAsync(rankingRequest);
Check(rankingRequest.Total == 4 && rankedPage.Select(x => x.Percent).SequenceEqual(new[] { 62, 38 }),
    "SQL grouped page preserves midpoint-to-even percentage ranking");

// SQL Server translation can be checked without a running SQL Server.
await using var sqlServer = new TestDb(new DbContextOptionsBuilder<TestDb>()
    .UseSqlServer("Server=unused;Database=paging;Integrated Security=true;TrustServerCertificate=true").Options);
var sql = sqlServer.Rows.Where(r => r.Active).OrderBy(r => r.Name).ThenBy(r => r.Id).Skip(3).Take(2).ToQueryString();
Check(sql.Contains("OFFSET") && sql.Contains("FETCH NEXT"), "SQL Server OFFSET/FETCH translation");

foreach (var countProperty in new[] { "total", "totalCount", "Total", "TotalCount" })
{
    using var doc = JsonDocument.Parse($$"""{"{{countProperty}}":23,"items":[4,6]}""");
    var page = ListOrPaged.NormalizePaged<int>(doc.RootElement);
    Check(page.TotalCount == 23 && page.Items.SequenceEqual(new[] { 4, 6 }), $"client recognizes {countProperty}");
}
using (var doc = JsonDocument.Parse("[1,2,3]"))
    Check(ListOrPaged.NormalizePaged<int>(doc.RootElement).TotalCount == 3, "client legacy arrays");
Console.WriteLine("Database pagination regression checks passed.");

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static async Task Throws<T>(Func<Task> run, string message) where T : Exception
{
    try { await run(); } catch (T) { return; }
    throw new Exception(message);
}

public sealed class Row
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public string Name { get; set; } = "";
    public bool Active { get; set; }
    public decimal Quantity { get; set; }
}
public sealed class TreeNode
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int? ParentId { get; set; }
}
public sealed class Reply
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public bool OnTime { get; set; }
}
public sealed class TestDb(DbContextOptions<TestDb> options) : DbContext(options)
{
    public DbSet<Row> Rows => Set<Row>();
    public DbSet<TreeNode> Nodes => Set<TreeNode>();
    public DbSet<Reply> Replies => Set<Reply>();
}
public sealed class SqlRecorder : DbCommandInterceptor
{
    public List<string> Commands { get; } = new();
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Commands.Add(command.CommandText);
        return ValueTask.FromResult(result);
    }
}
