using Inventory.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

// Offline. Never open, create, or change an installed database.
static void Check(bool ok, string what)
{
    if (!ok) throw new InvalidOperationException(what);
    Console.WriteLine("PASS " + what);
}
static AppDbContext OpenDb(SqliteConnection connection) => new(
    new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
static Task<int> TimestampColumns(AppDbContext db, string table) => db.Database.SqlQuery<int>(
    $"SELECT COUNT(*) AS Value FROM pragma_table_info({table}) WHERE name='CreatedAt' AND type='TEXT' AND [notnull]=0").SingleAsync();
static LetterSource Source(int id, int kind) => new() { Id = id, SourceType = kind };

var date = new DateTime(2018, 3, 21, 10, 11, 12).AddTicks(7654321);
var recorded = date.AddYears(1); // deliberately different from the editable document dates
var tables = new[] { (typeof(InnerLetter), "InnerLetters", "DateSabt"),
    (typeof(OutgoingLetter), "OutgoingLetters", "DateSabt"), (typeof(IncomingLetter), "IncomingLetters", "Date") };

await using (var connection = new SqliteConnection("Data Source=:memory:"))
{
    await connection.OpenAsync();
    await using var db = OpenDb(connection);
    await db.Database.EnsureCreatedAsync();
    await LetterCreatedAtSchemaV1.EnsureAsync(db);
    foreach (var (_, table, _) in tables)
        Check(await TimestampColumns(db, table) == 1, $"fresh EF schema: {table}.CreatedAt is nullable TEXT");

    db.Users.Add(new User { Id = 1, Username = "fixture", Role = "Operator", CreatedAt = recorded });
    db.InnerLetters.AddRange(
        new InnerLetter { Id = 10, Source = Source(10, 1), CreatorUserId = 1, DateSabt = date, Number = 7, LetterNumber = "old/7", Title = "داخلی" },
        new InnerLetter { Id = 11, Source = Source(11, 1), CreatorUserId = 1, DateSabt = date.AddDays(-1), Number = 6, IsDelete = true });
    db.OutgoingLetters.AddRange(
        new OutgoingLetter { Id = 20, Source = Source(20, 2), CreatorUserId = 1, CreatorId = 1, CreatedAt = recorded, DateSabt = date, Number = 8, LetterNumber = "old/8" },
        new OutgoingLetter { Id = 21, Source = Source(21, 2), CreatorUserId = 1, CreatorId = 1, CreatedAt = null, DateSabt = date.AddHours(1) });
    db.IncomingLetters.Add(new IncomingLetter { Id = 30, Source = Source(30, 3), CreateUserId = 1, Date = date.AddDays(4), DateErsal = date.AddYears(2), NumberSabt = 432 });
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
    // Simulate two missing columns and a third that already contains a mix of null and non-null times.
    await db.Database.ExecuteSqlRawAsync("ALTER TABLE InnerLetters DROP COLUMN CreatedAt; ALTER TABLE IncomingLetters DROP COLUMN CreatedAt;");
    var reproduced = false;
    try { db.InnerLetters.Where(l => !l.IsDelete).ToList(); }
    catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("no such column") && ex.Message.Contains("CreatedAt")) { reproduced = true; }
    Check(reproduced, "original startup full-entity query fails specifically on the absent timestamp");

    await using (var tx = await db.Database.BeginTransactionAsync())
    {
        await LetterCreatedAtSchemaV1.EnsureAsync(db);
        await tx.RollbackAsync();
    }
    Check(await TimestampColumns(db, "InnerLetters") == 0 && await TimestampColumns(db, "IncomingLetters") == 0 &&
          await db.OutgoingLetters.CountAsync(x => x.CreatedAt == null) == 1, "outer rollback restores both schema and data");
    await LetterCreatedAtSchemaV1.EnsureAsync(db);
    var inner = db.InnerLetters.Where(l => !l.IsDelete).ToList().Single();
    Check(inner.Id == 10 && inner.CreatedAt == date && inner.DateSabt == date && inner.Number == 7 && inner.LetterNumber == "old/7" && inner.Title == "داخلی",
        "the same startup query succeeds; historical time, text, and both numbers round-trip");
    Check((await db.InnerLetters.SingleAsync(x => x.Id == 11)).CreatedAt == date.AddDays(-1), "soft-deleted letter keeps its history too");
    var outgoing = await db.OutgoingLetters.OrderBy(l => l.Id).AsNoTracking().ToArrayAsync();
    Check(outgoing[0].CreatedAt == recorded && outgoing[0].DateSabt == date && outgoing[0].LetterNumber == "old/8" && outgoing[1].CreatedAt == date.AddHours(1),
        "existing outgoing timestamp retained; only the NULL row inherits its own DateSabt");
    var received = await db.IncomingLetters.AsNoTracking().SingleAsync();
    Check(received.CreatedAt == received.Date && received.CreatedAt == date.AddDays(4) && received.DateErsal == date.AddYears(2) && received.NumberSabt == 432,
        "incoming uses Date, not DateErsal, and keeps the registration number");
    // Changing the editable date must not alter an already recorded system time on the next startup.
    inner.DateSabt = date.AddMonths(2);
    var fresh = new InnerLetter { Id = 12, Source = Source(12, 1), CreatorUserId = 1, DateSabt = date, Number = 9 };
    db.InnerLetters.Add(fresh);
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
    await LetterCreatedAtSchemaV1.EnsureAsync(db);
    var reloaded = await db.InnerLetters.AsNoTracking().SingleAsync(x => x.Id == 10);
    Check(reloaded.DateSabt == date.AddMonths(2) && reloaded.CreatedAt == date, "rerun does not reset creation time after editing the document date");
    Check(fresh.CreatedAt.HasValue && fresh.CreatedAt != date && (await db.InnerLetters.AsNoTracking().SingleAsync(x => x.Id == 12)).CreatedAt == fresh.CreatedAt,
        "new EF letter retains the model-provided creation timestamp");
    Check((await db.Users.AsNoTracking().SingleAsync()).CreatedAt == recorded && await db.LetterSources.CountAsync() == 6, "unrelated user timestamp and letter source keys are preserved");
}

// Old partial schema, large archive, no source date, and IncomingLetters created late, as on SQL Server startup.
await using (var connection = new SqliteConnection("Data Source=:memory:"))
{
    await connection.OpenAsync();
    await using var db = OpenDb(connection);
    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE LetterSources(Id INTEGER PRIMARY KEY);
        CREATE TABLE InnerLetters(Id INTEGER PRIMARY KEY, DateSabt TEXT NULL);
        CREATE TABLE OutgoingLetters(Id INTEGER PRIMARY KEY);
        INSERT INTO OutgoingLetters VALUES (1);
        INSERT INTO InnerLetters VALUES (0, NULL);
        WITH RECURSIVE n(i) AS (VALUES (1) UNION ALL SELECT i+1 FROM n WHERE i < 50001)
        INSERT INTO InnerLetters SELECT i, '2010-01-01 00:00:00' FROM n;
        """);
    await LetterCreatedAtSchemaV1.EnsureAsync(db);
    var remaining = db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM InnerLetters WHERE CreatedAt IS NULL AND DateSabt IS NOT NULL");
    Check(await remaining.SingleAsync() == 1, "large archive: first pass backfills at most 50,000 rows");
    Check(await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM OutgoingLetters WHERE CreatedAt IS NULL").SingleAsync() == 1,
        "partial legacy schema with no source date does not get a fabricated timestamp");
    Check(await TimestampColumns(db, "IncomingLetters") == 0, "early migration does not fabricate a missing module table");
    await IncomingLetterSchemaV1.EnsureAsync(db);
    Check(await TimestampColumns(db, "IncomingLetters") == 1 && (await db.IncomingLetters.AsNoTracking().ToListAsync()).Count == 0,
        "late-created incoming table is readable immediately, on the same boot");
    await LetterCreatedAtSchemaV1.EnsureAsync(db);
    Check(await remaining.SingleAsync() == 0 && await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM InnerLetters WHERE CreatedAt IS NULL AND DateSabt IS NULL").SingleAsync() == 1,
        "next pass finishes the backlog and an unknown historical date remains NULL");
}

// SQL generation only. No connection to SQL Server (or any remote endpoint) is opened.
await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer("Server=offline.invalid;Database=unused;Integrated Security=True").Options))
{
    const string id = "20261127130000_EnsureLetterCreatedAt";
    Check(db.Database.GetMigrations().Contains(id), "new migration is registered on the actual application context");
    var script = db.GetService<IMigrator>().GenerateScript("20261127120000_EnsureCoreUserColumns", id, MigrationsSqlGenerationOptions.Idempotent);
    if (args is ["--print-sql"]) Console.WriteLine(script);
    var query = db.InnerLetters.Where(l => !l.IsDelete).ToQueryString();
    Check(query.Contains("FROM [InnerLetters]") && query.Contains("[CreatedAt]") && query.Contains("[IsDelete]"), "SQL Server startup query genuinely reads InnerLetters.CreatedAt");
    foreach (var (type, table, sourceDate) in tables)
    {
        Check(script.Contains($"IF OBJECT_ID(N'dbo.{table}', N'U') IS NOT NULL") &&
              script.Contains($"IF COL_LENGTH(N'dbo.{table}', N'CreatedAt') IS NULL") &&
              script.Contains($"ALTER TABLE [dbo].[{table}] ADD [CreatedAt] datetime2 NULL") &&
              script.Contains($"EXEC sys.sp_executesql N'IF EXISTS (SELECT 1 FROM [dbo].[{table}]") &&
              script.Contains($"UPDATE TOP (50000) [dbo].[{table}] SET [CreatedAt] = [{sourceDate}]") &&
              script.Contains($"WHERE [CreatedAt] IS NULL AND [{sourceDate}] IS NOT NULL"), $"guarded T-SQL DDL and late-bound fill from {table}.{sourceDate}");
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model.FindEntityType(type.FullName!)!.FindProperty("CreatedAt");
        var model = db.Model.FindEntityType(type)!.FindProperty("CreatedAt");
        Check(snapshot is { IsNullable: true } && model is { IsNullable: true } && snapshot.GetColumnType() == "datetime2" && model.GetColumnType() == "datetime2",
            $"SQL Server snapshot and runtime agree on {table}.CreatedAt");
    }
    Check(!script.Contains("[dbo].[FacInvoices]") && !script.Contains("[dbo].[Users]") && !script.Contains("DROP ", StringComparison.OrdinalIgnoreCase),
        "no drop, invoice rewrite, or account rewrite in the letter migration");
}
