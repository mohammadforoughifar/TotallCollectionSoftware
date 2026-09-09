using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// خودتعمیرِ اسکیمای «سازمان‌ها و سمت‌ها» — مبنای جزء «واحد» در شماره اندیکاتور نامه‌ها.
/// (Organizations + Semats) — همه دستورات idempotent هستند و برای SQL Server و SQLite جدا اجرا می‌شوند؛
/// مثل DocArchiveSecuritySchemaV1 این اسکیما به‌صورت Ensure ساخته می‌شود نه EF Migration،
/// تا دیتابیس‌های موجود (که تاریخچه‌ی مایگریشن کامل ندارند) هم بدون مشکل ارتقا یابند.
/// </summary>
public static class OrganizationSchemaV1
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        try
        {
            if (db.Database.GetDbConnection() is SqliteConnection sqlConn)
                await EnsureSqliteAsync(sqlConn.ConnectionString);
            else if (db.Database.GetDbConnection() is SqlConnection sqlServerConn)
                await EnsureSqlServerAsync(sqlServerConn.ConnectionString);
        }
        catch (Exception ex)
        {
            // نباید استارتاپ را متوقف کند؛ در صورت خطا فقط هشدار می‌دهیم.
            Console.WriteLine($"[DB] OrganizationSchemaV1 خطا: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ================== SQLite ==================

    private static async Task EnsureSqliteAsync(string connectionString)
    {
        using var raw = new SqliteConnection(connectionString);
        raw.Open();

        void Exec(string sql)
        {
            using var c = raw.CreateCommand();
            c.CommandText = sql;
            c.ExecuteNonQuery();
        }

        Exec(@"
            CREATE TABLE IF NOT EXISTS Organizations (
                OrganizationId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                NameUnit TEXT NOT NULL,
                NameUniq TEXT NOT NULL,
                IsDefault INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1,
                IsDelete INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Organizations_NameUniq ON Organizations (NameUniq);");

        Exec(@"
            CREATE TABLE IF NOT EXISTS Semats (
                SematId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Parent INTEGER NULL,
                UserId INTEGER NULL,
                Title TEXT NOT NULL,
                OnvanMokatebati TEXT NULL,
                OrganizationId INTEGER NOT NULL,
                DefaultSemat INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1,
                IsDelete INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS IX_Semats_UserId ON Semats (UserId);
            CREATE INDEX IF NOT EXISTS IX_Semats_OrganizationId ON Semats (OrganizationId);");

        await Task.CompletedTask;
    }

    // ================== SQL Server ==================

    private static async Task EnsureSqlServerAsync(string connectionString)
    {
        using var raw = new SqlConnection(connectionString);
        await raw.OpenAsync();

        async Task ExecAsync(string sql)
        {
            using var c = raw.CreateCommand();
            c.CommandText = sql;
            await c.ExecuteNonQueryAsync();
        }

        // ---------- Organizations ----------
        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.Organizations', N'U') IS NULL
            BEGIN
                CREATE TABLE [Organizations] (
                    [OrganizationId] int NOT NULL IDENTITY PRIMARY KEY,
                    [NameUnit] nvarchar(200) NOT NULL,
                    [NameUniq] nvarchar(100) NOT NULL,
                    [IsDefault] bit NOT NULL CONSTRAINT [DF_Organizations_IsDefault] DEFAULT(0),
                    [IsActive] bit NOT NULL CONSTRAINT [DF_Organizations_IsActive] DEFAULT(1),
                    [IsDelete] bit NOT NULL CONSTRAINT [DF_Organizations_IsDelete] DEFAULT(0),
                    [CreatedAt] datetime2 NOT NULL
                );
            END
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Organizations_NameUniq' AND object_id = OBJECT_ID(N'dbo.Organizations'))
                CREATE UNIQUE INDEX [IX_Organizations_NameUniq] ON [Organizations] ([NameUniq]);");

        // ---------- Semats ----------
        await ExecAsync(@"
            IF OBJECT_ID(N'dbo.Semats', N'U') IS NULL
            BEGIN
                CREATE TABLE [Semats] (
                    [SematId] int NOT NULL IDENTITY PRIMARY KEY,
                    [Parent] int NULL,
                    [UserId] int NULL,
                    [Title] nvarchar(200) NOT NULL,
                    [OnvanMokatebati] nvarchar(300) NULL,
                    [OrganizationId] int NOT NULL,
                    [DefaultSemat] bit NOT NULL CONSTRAINT [DF_Semats_DefaultSemat] DEFAULT(0),
                    [IsActive] bit NOT NULL CONSTRAINT [DF_Semats_IsActive] DEFAULT(1),
                    [IsDelete] bit NOT NULL CONSTRAINT [DF_Semats_IsDelete] DEFAULT(0),
                    CONSTRAINT [FK_Semats_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId])
                        REFERENCES [Organizations] ([OrganizationId]) ON DELETE NO ACTION,
                    CONSTRAINT [FK_Semats_Users_UserId] FOREIGN KEY ([UserId])
                        REFERENCES [Users] ([Id]) ON DELETE SET NULL
                );
            END
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Semats_UserId' AND object_id = OBJECT_ID(N'dbo.Semats'))
                CREATE INDEX [IX_Semats_UserId] ON [Semats] ([UserId]);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Semats_OrganizationId' AND object_id = OBJECT_ID(N'dbo.Semats'))
                CREATE INDEX [IX_Semats_OrganizationId] ON [Semats] ([OrganizationId]);");
    }
}
