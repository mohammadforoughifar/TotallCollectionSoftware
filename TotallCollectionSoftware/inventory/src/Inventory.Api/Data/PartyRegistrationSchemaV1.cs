using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>Add missing optional Party fields on existing databases; never rewrite existing values.</summary>
public static class PartyRegistrationSchemaV1
{
    // PostalCode and TaxId match AddMoadianV1. Upgrades that skipped that migration
    // can be missing either column even when the migration history looks current.
    internal const string SqlServer = @"
IF OBJECT_ID(N'dbo.Parties', N'U') IS NULL
    THROW 51001, N'dbo.Parties was not found or is not visible. Check the target database and permissions.', 1;
IF COL_LENGTH(N'dbo.Parties', N'PostalCode') IS NULL
    ALTER TABLE [dbo].[Parties] ADD [PostalCode] nvarchar(20) NULL;
IF COL_LENGTH(N'dbo.Parties', N'TaxId') IS NULL
    ALTER TABLE [dbo].[Parties] ADD [TaxId] nvarchar(20) NULL;
IF COL_LENGTH(N'dbo.Parties', N'RegisteredByUserId') IS NULL
    ALTER TABLE [dbo].[Parties] ADD [RegisteredByUserId] int NULL;
IF COL_LENGTH(N'dbo.Parties', N'RegisteredByName') IS NULL
    ALTER TABLE [dbo].[Parties] ADD [RegisteredByName] nvarchar(150) NULL;
IF COL_LENGTH(N'dbo.Parties', N'RegisteredByReferrerId') IS NULL
    ALTER TABLE [dbo].[Parties] ADD [RegisteredByReferrerId] int NULL;
IF COL_LENGTH(N'dbo.Parties', N'RegisteredAt') IS NULL
    ALTER TABLE [dbo].[Parties] ADD [RegisteredAt] datetime2 NULL;";

    private static readonly (string Name, string Sql)[] SqliteColumns =
    {
        ("PostalCode", "ALTER TABLE Parties ADD COLUMN PostalCode TEXT NULL;"),
        ("TaxId", "ALTER TABLE Parties ADD COLUMN TaxId TEXT NULL;"),
        ("RegisteredByUserId", "ALTER TABLE Parties ADD COLUMN RegisteredByUserId INTEGER NULL;"),
        ("RegisteredByName", "ALTER TABLE Parties ADD COLUMN RegisteredByName TEXT NULL;"),
        ("RegisteredByReferrerId", "ALTER TABLE Parties ADD COLUMN RegisteredByReferrerId INTEGER NULL;"),
        ("RegisteredAt", "ALTER TABLE Parties ADD COLUMN RegisteredAt TEXT NULL;")
    };

    public static async Task EnsureAsync(AppDbContext db)
    {
        if (!db.Database.IsSqlite())
        {
            // Required schema: let permission or DDL failures surface at their source.
            await db.Database.ExecuteSqlRawAsync(SqlServer);
            return;
        }

        var names = await db.Database.SqlQueryRaw<string>(
            "SELECT name AS Value FROM pragma_table_info('Parties')").ToListAsync();
        if (names.Count == 0)
            throw new InvalidOperationException("The Parties table is missing. Check the target database.");

        var existing = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, sql) in SqliteColumns)
            if (!existing.Contains(name)) await db.Database.ExecuteSqlRawAsync(sql);
    }
}
