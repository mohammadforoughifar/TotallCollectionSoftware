using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace RadisHr.Api.Data;

public static class RadisHrDatabaseInitializer
{
    public static async Task InitializeAsync(AppDbContext db)
    {
        if (!db.Database.IsSqlite())
        {
            // Keep the original SQL Server migration IDs, table names and separate history.
            await db.Database.MigrateAsync();
            return;
        }

        // EnsureCreated is not enough: Inventory has already created tables in this database.
        // Nor can the SQL Server migrations (nvarchar(max), identity, etc.) run on SQLite.
        // Build only this context's tables using the SQLite provider's own model/SQL generator.
        await db.Database.OpenConnectionAsync();
        try
        {
            var expected = db.Model.GetEntityTypes().Select(e => e.GetTableName())
                .OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = db.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) existing.Add(reader.GetString(0));
            }

            if (expected.All(existing.Contains)) return;
            if (expected.Any(existing.Contains))
                throw new InvalidOperationException(
                    "ساختار SQLite منابع انسانی ناقص است. قبل از ارتقا از دیتابیس بکاپ بگیرید؛ " +
                    "جدول‌های مفقود: " + string.Join(", ", expected.Where(t => !existing.Contains(t))));

            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
            await transaction.CommitAsync();
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
