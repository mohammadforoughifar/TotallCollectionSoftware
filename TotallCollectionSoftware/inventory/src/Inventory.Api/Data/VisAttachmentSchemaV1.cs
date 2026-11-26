using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// خودتعمیر اسکیمای مشترک پیوست‌ها برای «عکس چک سفارش ویزیتور» — نسخه ۱.
/// <para>
/// ستون‌های تازه روی جدول عمومی <c>AppAttachments</c> اضافه می‌شوند: دسته (چک/عمومی)،
/// شرکت، تصمیم مالی و اندازه/زمان تغییر. همهٔ ستون‌ها nullable یا با پیش‌فرض‌اند، پس
/// روی دیتابیس موجود فقط «افزودنی» است و به رکوردهای قبلی کاری ندارد.
/// </para>
/// </summary>
public static class VisAttachmentSchemaV1
{
    private static readonly (string Column, string SqlServerType, string SqliteType)[] Columns =
    {
        ("Category", "nvarchar(40) NULL", "TEXT NULL"),
        ("CompanyId", "int NULL", "INTEGER NULL"),
        ("FinancialStatus", "int NOT NULL CONSTRAINT [DF_AppAttachments_FinancialStatus] DEFAULT(0)", "INTEGER NOT NULL DEFAULT 0"),
        ("FinancialDecidedAt", "datetime2 NULL", "TEXT NULL"),
        ("FinancialDecidedBy", "nvarchar(150) NULL", "TEXT NULL"),
        ("FinancialNote", "nvarchar(500) NULL", "TEXT NULL"),
        ("UpdatedAt", "datetime2 NULL", "TEXT NULL"),
        ("Size", "bigint NULL", "INTEGER NULL")
    };

    public static readonly string SqlServer = string.Join("\n", Columns.Select(c => $@"
IF OBJECT_ID(N'dbo.AppAttachments', N'U') IS NOT NULL AND COL_LENGTH('dbo.AppAttachments','{c.Column}') IS NULL
    ALTER TABLE [dbo].[AppAttachments] ADD [{c.Column}] {c.SqlServerType};"));

    public static async Task EnsureAsync(AppDbContext db)
    {
        if (!await TableExistsAsync(db, "AppAttachments")) return;

        if (db.Database.IsSqlite())
        {
            foreach (var (column, _, sqliteType) in Columns)
            {
                if (await SqliteHasColumnAsync(db, "AppAttachments", column)) continue;
                await SafeAsync(db, $"ALTER TABLE AppAttachments ADD COLUMN {column} {sqliteType};");
            }
            return;
        }

        await SafeAsync(db, SqlServer);
    }

    private static async Task<bool> TableExistsAsync(AppDbContext db, string table)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = db.Database.IsSqlite()
                ? $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';"
                : $"SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name='dbo' AND t.name='{table}';";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> SqliteHasColumnAsync(AppDbContext db, string table, string column)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}';";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch
        {
            return true;
        }
    }

    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var m = ex.Message ?? "";
            if (!m.Contains("duplicate", StringComparison.OrdinalIgnoreCase) &&
                !m.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"[DB] VisAttachmentSchemaV1: {m}");
        }
    }
}
