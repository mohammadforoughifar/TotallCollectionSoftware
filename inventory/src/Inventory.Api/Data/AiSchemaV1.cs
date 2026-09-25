using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// =====================================================================
// اسکیمای جداول هوش مصنوعی فروغ آریا — SQL Server و SQLite
// =====================================================================
public static class AiSchemaV1
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<AiConversation>().HasIndex(c => new { c.UserId, c.LastMessageAtUtc });
        model.Entity<AiMessage>().HasIndex(m => m.ConversationId);
        model.Entity<AiKnowledgeDoc>().HasIndex(d => d.DocKey).IsUnique();
    }

    public static async Task EnsureAsync(AppDbContext db)
    {
        var sqlite = db.Database.IsSqlite();
        string T(string cols) => cols
            .Replace("IDKEY", sqlite ? "INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT" : "INT NOT NULL IDENTITY PRIMARY KEY")
            .Replace("DT", sqlite ? "TEXT" : "datetime2")
            .Replace("BIGTEXT", sqlite ? "TEXT" : "NVARCHAR(MAX)");

        var conv = T("Id IDKEY, UserId INT NOT NULL, Channel NVARCHAR(20) NOT NULL, Title NVARCHAR(200) NOT NULL, CreatedAtUtc DT NOT NULL, LastMessageAtUtc DT NOT NULL, IsArchived INT NOT NULL");
        var msg = T("Id IDKEY, ConversationId INT NOT NULL, Role NVARCHAR(20) NOT NULL, Content BIGTEXT NOT NULL, ToolsUsed NVARCHAR(500) NULL, UsedFallback INT NOT NULL, CreatedAtUtc DT NOT NULL");
        var doc = T("Id IDKEY, Category NVARCHAR(100) NOT NULL, Title NVARCHAR(200) NOT NULL, Content BIGTEXT NOT NULL, Link NVARCHAR(300) NULL, DocKey NVARCHAR(100) NOT NULL, EmbeddingJson BIGTEXT NULL, IsActive INT NOT NULL, UpdatedAtUtc DT NOT NULL");

        foreach (var (table, cols) in new[] { ("AiConversations", conv), ("AiMessages", msg), ("AiKnowledgeDocs", doc) })
        {
            var sql = sqlite
                ? $"CREATE TABLE IF NOT EXISTS {table} ({cols})"
                : $"IF OBJECT_ID(N'dbo.{table}',N'U') IS NULL CREATE TABLE {table} ({cols})";
            await db.Database.ExecuteSqlRawAsync(sql);
        }

        // ستون‌های جدید برای دیتابیس‌های قدیمی (امن برای اجرای چندباره)
        await AddColumnIfMissingAsync(db, "AiKnowledgeDocs", "EmbeddingJson", sqlite ? "TEXT NULL" : "NVARCHAR(MAX) NULL");
        await AddColumnIfMissingAsync(db, "AiMessages", "UsedFallback", "INT NOT NULL DEFAULT 0");
        await AddColumnIfMissingAsync(db, "AiMessages", "ToolsUsed", "NVARCHAR(500) NULL");

        foreach (var (table, cols, unique) in new[]
                 {
                     ("AiConversations", "UserId, LastMessageAtUtc", false),
                     ("AiMessages", "ConversationId", false),
                     ("AiKnowledgeDocs", "DocKey", true),
                 })
        {
            var name = "IX_" + table + "_" + cols.Replace(", ", "_");
            var sql = $"CREATE {(unique ? "UNIQUE " : "")}INDEX {(sqlite ? "IF NOT EXISTS " : "")}[{name}] ON [{table}] ({cols});";
            if (!sqlite) sql = $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'{name}' AND object_id=OBJECT_ID(N'dbo.{table}')) " + sql;
            await db.Database.ExecuteSqlRawAsync(sql);
        }
    }

    private static async Task AddColumnIfMissingAsync(AppDbContext db, string table, string column, string type)
    {
        var sqlite = db.Database.IsSqlite();
        if (sqlite)
        {
            var cols = await db.Database.SqlQueryRaw<string>($"SELECT name FROM pragma_table_info('{table}')").ToListAsync();
            if (cols.Any(c => string.Equals(c, column, StringComparison.OrdinalIgnoreCase))) return;
            // SQLite نوع INT NOT NULL بدون DEFAULT را روی جدول پر قبول نمی‌کند؛ پس DEFAULT اضافه می‌کنیم
            var t = type.Replace("INT NOT NULL DEFAULT 0", "INTEGER NOT NULL DEFAULT 0");
            await db.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} ADD COLUMN {column} {t}");
        }
        else
        {
            await db.Database.ExecuteSqlRawAsync(
                $"IF COL_LENGTH(N'dbo.{table}', N'{column}') IS NULL ALTER TABLE {table} ADD {column} {type}");
        }
    }
}
