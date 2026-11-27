using Inventory.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

// Offline: never connect to an installed database or use real account data.
static void Check(bool pass, string message)
{
    if (!pass) throw new InvalidOperationException(message);
    Console.WriteLine("PASS " + message);
}

foreach (var (hasOrg, hasSeat) in new[] { (false, false), (true, false), (false, true), (true, true) })
{
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE Users (Id INTEGER PRIMARY KEY, Username TEXT NOT NULL, PasswordHash TEXT NOT NULL, Role TEXT NOT NULL,
            ReferrerId INTEGER NULL, FirstName TEXT NULL, LastName TEXT NULL, Mobile TEXT NULL, MobileVerified INTEGER NOT NULL DEFAULT 0,
            Email TEXT NULL, EmailVerified INTEGER NOT NULL DEFAULT 0, BaleChatId TEXT NULL, EitaaChatId TEXT NULL, BalePhone TEXT NULL,
            BalePhoneLinked INTEGER NOT NULL DEFAULT 0, IsActive INTEGER NOT NULL, CreatedAt TEXT NOT NULL, PhotoPath TEXT NULL,
            SignaturePath TEXT NULL, ShiftGroupId INTEGER NULL);
        CREATE TABLE SystemUsers (Id INTEGER PRIMARY KEY, OrganizationId INTEGER NULL);
        INSERT INTO SystemUsers VALUES (1, 999);
        INSERT INTO Users (Id, Username, PasswordHash, Role, IsActive, CreatedAt) VALUES (1,'fixture','fixture','Operator',1,'2026-01-01');
        """);
    if (hasOrg) await db.Database.ExecuteSqlRawAsync("ALTER TABLE Users ADD COLUMN OrganizationId INTEGER NULL; UPDATE Users SET OrganizationId=37 WHERE Id=1;");
    if (hasSeat) await db.Database.ExecuteSqlRawAsync("ALTER TABLE Users ADD COLUMN LicenseSeatExempt INTEGER NOT NULL DEFAULT 0; UPDATE Users SET LicenseSeatExempt=1 WHERE Id=1;");
    if (!hasOrg || !hasSeat)
    {
        bool missing = false;
        try { await db.Users.AsNoTracking().SingleAsync(); }
        catch (SqliteException e) when (e.SqliteErrorCode == 1 && e.Message.Contains("no such column", StringComparison.OrdinalIgnoreCase)) { missing = true; }
        Check(missing, "legacy full entity read reproduces missing-column failure");
    }
    if (!hasOrg && !hasSeat)
    {
        await using var outer = await db.Database.BeginTransactionAsync();
        await CoreUserColumnsSchemaV1.EnsureAsync(db);
        await outer.RollbackAsync();
        Check(await db.Database.SqlQueryRaw<int>("SELECT count(*) AS Value FROM pragma_table_info('Users') WHERE name IN ('OrganizationId','LicenseSeatExempt')").SingleAsync() == 0,
            "repair respects an existing transaction");
    }
    await CoreUserColumnsSchemaV1.EnsureAsync(db);
    await CoreUserColumnsSchemaV1.EnsureAsync(db);
    var existing = await db.Users.AsNoTracking().SingleAsync(u => u.Username == "fixture");
    Check(existing.Id == 1 && existing.Role == "Operator" && existing.OrganizationId == (hasOrg ? 37 : (int?)null) && existing.SessionSeatExempt == hasSeat,
        $"repeated repair retains existing values, adds only missing fields (OrganizationId={hasOrg}, LicenseSeatExempt={hasSeat})");
    Check(await db.Database.SqlQueryRaw<int>("SELECT OrganizationId AS Value FROM SystemUsers WHERE Id=1").SingleAsync() == 999,
        "the unrelated SystemUsers record is untouched");
    db.Users.Add(new User { Username = "new", PasswordHash = "fixture", Role = "Operator" });
    await db.SaveChangesAsync();
    var newUser = await db.Users.AsNoTracking().SingleAsync(u => u.Username == "new");
    Check(newUser.OrganizationId is null && !newUser.SessionSeatExempt, "new EF account uses nullable organization and false compatibility flag");
    await db.Database.ExecuteSqlRawAsync("INSERT INTO Users (Username,PasswordHash,Role,IsActive,CreatedAt) VALUES ('old-writer','fixture','Operator',1,'2026-01-01');");
    Check(!await db.Users.Where(u => u.Username == "old-writer").Select(u => u.SessionSeatExempt).SingleAsync(), "old writers can omit newly added flag");
}

// Generate genuine SQL Server migration and entity SQL without opening a connection.
await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer("Server=offline.invalid;Database=unused;Integrated Security=True").Options))
{
    const string migration = "20261127120000_EnsureCoreUserColumns";
    var query = db.Users.AsNoTracking().ToQueryString();
    Check(query.Contains("FROM [Users]") && query.Contains("[OrganizationId]") && query.Contains("[LicenseSeatExempt]") && !query.Contains("[SessionSeatExempt]"),
        "SQL Server reads both reported fields from the login Users table");
    Check(db.Database.GetMigrations().Contains(migration), "migration is discoverable");
    var sql = db.GetService<IMigrator>().GenerateScript("20261125120000_AddVisPaymentPlan", migration, MigrationsSqlGenerationOptions.Idempotent);
    Check(sql.Contains("COL_LENGTH(N'dbo.Users', N'OrganizationId') IS NULL") && sql.Contains("ADD [OrganizationId] int NULL") &&
          sql.Contains("COL_LENGTH(N'dbo.Users', N'LicenseSeatExempt') IS NULL") && sql.Contains("ADD [LicenseSeatExempt] bit NOT NULL DEFAULT (0) WITH VALUES") && !sql.Contains("[dbo].[SystemUsers]"),
        "migration adds only absent columns to dbo.Users, with type-correct defaults");
    var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model.FindEntityType(typeof(User).FullName!)!;
    Check(snapshot.FindProperty(nameof(User.OrganizationId))!.IsNullable &&
          snapshot.FindProperty(nameof(User.SessionSeatExempt))!.GetColumnName(StoreObjectIdentifier.Table("Users", null)) == "LicenseSeatExempt",
        "SQL Server snapshot records the physical column and nullable organization");
}
