using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// ستون‌های «اتصال انبوهٔ بله» — گسترشِ اسکیمای کاربران و تنظیمات برنامه.
/// <para>
/// Migrate (SQL Server) و EnsureCreated (SQLite) ستون‌های تازه را به دیتابیس‌های
/// قدیمی اضافه نمی‌کنند؛ بنابراین اینجا ستون‌ها را idempotent و امن اضافه می‌کنیم.
/// </para>
/// <list type="bullet">
/// <item>Users.BalePhone — شمارهٔ نرمال‌شدهٔ بله (۹۸…) پس از تأیید اکانت بله</item>
/// <item>Users.BalePhoneLinked — آیا اتصال از مسیر شماره برقرار شده است</item>
/// <item>AppSettings.BaleSafirApiAccessKey — کلید دسترسی سرویس سفیر</item>
/// <item>AppSettings.BaleSafirBotId — شناسهٔ عددی بازو برای سرویس سفیر</item>
/// </list>
/// </summary>
public static class BaleLinkSchemaV1
{
    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    // ==================== SQL Server ====================
    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.Users', N'BalePhone') IS NULL
        ALTER TABLE dbo.Users ADD BalePhone nvarchar(20) NULL;
    IF COL_LENGTH(N'dbo.Users', N'BalePhoneLinked') IS NULL
        ALTER TABLE dbo.Users ADD BalePhoneLinked bit NOT NULL CONSTRAINT DF_Users_BalePhoneLinked DEFAULT(0);
END;");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.AppSettings', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.AppSettings', N'BaleSafirApiAccessKey') IS NULL
        ALTER TABLE dbo.AppSettings ADD BaleSafirApiAccessKey nvarchar(200) NULL;
    IF COL_LENGTH(N'dbo.AppSettings', N'BaleSafirBotId') IS NULL
        ALTER TABLE dbo.AppSettings ADD BaleSafirBotId int NULL;
END;");
    }

    // ==================== SQLite ====================
    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        if (await TableExistsAsync(db, "Users"))
        {
            if (!await ColumnExistsAsync(db, "Users", "BalePhone"))
                await SafeAsync(db, "ALTER TABLE Users ADD COLUMN BalePhone TEXT NULL;");
            if (!await ColumnExistsAsync(db, "Users", "BalePhoneLinked"))
                await SafeAsync(db, "ALTER TABLE Users ADD COLUMN BalePhoneLinked INTEGER NOT NULL DEFAULT 0;");
        }

        if (await TableExistsAsync(db, "AppSettings"))
        {
            if (!await ColumnExistsAsync(db, "AppSettings", "BaleSafirApiAccessKey"))
                await SafeAsync(db, "ALTER TABLE AppSettings ADD COLUMN BaleSafirApiAccessKey TEXT NULL;");
            if (!await ColumnExistsAsync(db, "AppSettings", "BaleSafirBotId"))
                await SafeAsync(db, "ALTER TABLE AppSettings ADD COLUMN BaleSafirBotId INTEGER NULL;");
        }
    }

    private static async Task<bool> TableExistsAsync(AppDbContext db, string table)
    {
        try
        {
            var connection = db.Database.GetDbConnection();
            var openedHere = connection.State != System.Data.ConnectionState.Open;
            if (openedHere) await connection.OpenAsync();
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@name";
                parameter.Value = table;
                command.Parameters.Add(parameter);
                return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
            }
            finally
            {
                if (openedHere) await connection.CloseAsync();
            }
        }
        catch { return false; }
    }

    private static async Task<bool> ColumnExistsAsync(AppDbContext db, string table, string column)
    {
        try
        {
            var connection = db.Database.GetDbConnection();
            var openedHere = connection.State != System.Data.ConnectionState.Open;
            if (openedHere) await connection.OpenAsync();
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"PRAGMA table_info(\"{table}\")";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
            finally
            {
                if (openedHere) await connection.CloseAsync();
            }
        }
        catch { return false; }
    }

    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var message = ex.Message ?? "";
            if (!message.Contains("already exists", StringComparison.OrdinalIgnoreCase) &&
                !message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"[DB] BaleLinkSchemaV1: {message}");
        }
    }
}
