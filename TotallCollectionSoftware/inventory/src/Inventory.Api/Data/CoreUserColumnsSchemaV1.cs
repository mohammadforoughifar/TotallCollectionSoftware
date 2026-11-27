using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>Required columns on login accounts (Users), not the separate SystemUsers directory.</summary>
public static class CoreUserColumnsSchemaV1
{
    // Shared with the SQL Server migration. Do not infer an organization from a matching
    // SystemUsers.Id: the two tables have independent identities. Existing flags are preserved.
    internal const string SqlServer = @"
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
    THROW 51002, N'dbo.Users is missing or not visible. Verify the Inventory connection and schema permissions.', 1;
IF COL_LENGTH(N'dbo.Users', N'OrganizationId') IS NULL
    ALTER TABLE [dbo].[Users] ADD [OrganizationId] int NULL;
IF COL_LENGTH(N'dbo.Users', N'LicenseSeatExempt') IS NULL
    ALTER TABLE [dbo].[Users] ADD [LicenseSeatExempt] bit NOT NULL DEFAULT (0) WITH VALUES;";

    public static async Task EnsureAsync(AppDbContext db)
    {
        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync() : null;
        if (db.Database.IsSqlServer())
        {
            await db.Database.ExecuteSqlRawAsync(SqlServer);
        }
        else if (db.Database.IsSqlite())
        {
            // Query on EF's connection/transaction (works on :memory: too).
            var names = await db.Database.SqlQueryRaw<string>(
                "SELECT name AS Value FROM pragma_table_info('Users')").ToListAsync();
            if (names.Count == 0)
                throw new InvalidOperationException("Users is missing. Verify the Inventory database before upgrading.");
            var columns = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!columns.Contains("OrganizationId"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Users] ADD COLUMN [OrganizationId] INTEGER NULL;");
            if (!columns.Contains("LicenseSeatExempt"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Users] ADD COLUMN [LicenseSeatExempt] INTEGER NOT NULL DEFAULT 0;");
        }
        else throw new NotSupportedException("Core user schema upgrade supports SQL Server and SQLite only.");

        if (tx is not null) await tx.CommitAsync();
    }
}
