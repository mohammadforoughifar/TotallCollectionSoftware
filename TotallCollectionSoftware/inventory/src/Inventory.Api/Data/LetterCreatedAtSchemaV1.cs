using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>Restore the nullable system timestamps still mapped on office letters.</summary>
public static class LetterCreatedAtSchemaV1
{
    // Trusted schema identifiers only. Invoices have a different, non-nullable contract.
    private static readonly (string Table, string SourceDate)[] Letters =
    {
        ("InnerLetters", "DateSabt"), ("OutgoingLetters", "DateSabt"), ("IncomingLetters", "Date")
    };

    // IncomingLetters is created outside migrations on some installations. Its CREATE TABLE
    // includes CreatedAt too. Never invent a table or a historical date on a partial schema.
    internal static IEnumerable<string> SqlServerBatches()
    {
        foreach (var (table, sourceDate) in Letters)
            yield return $"""
                IF OBJECT_ID(N'dbo.{table}', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.{table}', N'CreatedAt') IS NULL
                        ALTER TABLE [dbo].[{table}] ADD [CreatedAt] datetime2 NULL;
                    IF COL_LENGTH(N'dbo.{table}', N'{sourceDate}') IS NOT NULL
                        EXEC sys.sp_executesql N'IF EXISTS (SELECT 1 FROM [dbo].[{table}]
                                WHERE [CreatedAt] IS NULL AND [{sourceDate}] IS NOT NULL)
                            UPDATE TOP (50000) [dbo].[{table}] SET [CreatedAt] = [{sourceDate}]
                              WHERE [CreatedAt] IS NULL AND [{sourceDate}] IS NOT NULL;';
                END;
                """;
    }

    public static async Task EnsureAsync(AppDbContext db, CancellationToken ct = default)
    {
        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        if (db.Database.IsSqlServer())
        {
            // Compile UPDATE only after ALTER, even inside an idempotent migration script.
            foreach (var sql in SqlServerBatches()) await db.Database.ExecuteSqlRawAsync(sql, ct);
        }
        else if (db.Database.IsSqlite())
        {
            foreach (var (table, sourceDate) in Letters)
            {
                var names = await db.Database.SqlQuery<string>(
                    $"SELECT name AS Value FROM pragma_table_info({table})").ToListAsync(ct);
                if (names.Count == 0) continue;
                var columns = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!columns.Contains("CreatedAt"))
                {
                    var add = $"ALTER TABLE [{table}] ADD COLUMN [CreatedAt] TEXT NULL;";
                    await db.Database.ExecuteSqlRawAsync(add, ct);
                }
                if (!columns.Contains(sourceDate)) continue;
                // Limit write transactions on large archives. An unknown source date stays NULL;
                // rows beyond this batch remain readable and are eligible on the next startup.
                var fill = $"""
                    UPDATE [{table}] SET [CreatedAt] = [{sourceDate}]
                    WHERE [CreatedAt] IS NULL AND [{sourceDate}] IS NOT NULL AND [Id] IN
                        (SELECT [Id] FROM [{table}] WHERE [CreatedAt] IS NULL AND [{sourceDate}] IS NOT NULL
                         ORDER BY [Id] LIMIT 50000);
                    """;
                await db.Database.ExecuteSqlRawAsync(fill, ct);
            }
        }
        else throw new NotSupportedException("Letter timestamp upgrade supports SQL Server and SQLite only.");
        if (tx is not null) await tx.CommitAsync(ct);
    }
}
