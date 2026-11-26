using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// خودتعمیر اسکیمای «پرداخت ترکیبی ویزیتور» — نسخه ۱
/// <para>
/// ستون‌های اجزای پرداخت (نقد/چک/اقساط/نسیه) و تصمیم مالی روی پیوست‌ها به جدول
/// <c>VisOrders</c> اضافه می‌شوند. همهٔ ستون‌ها nullable یا با پیش‌فرض صفر هستند،
/// پس روی دیتابیس موجود فقط «افزودنی» است و هیچ سند یا مبلغی بازنویسی نمی‌شود.
/// </para>
/// <para>idempotent: اجرای چندباره خطا نمی‌دهد و ستون موجود را دست نمی‌زند.</para>
/// </summary>
public static class VisPaymentPlanSchemaV1
{
    private static readonly (string Column, string SqlServerType, string SqliteType)[] Columns =
    {
        ("CashAmount", "decimal(18,2) NOT NULL DEFAULT(0)", "TEXT NOT NULL DEFAULT '0'"),
        ("CashType", "int NULL", "INTEGER NULL"),
        ("TrsAccountId", "int NULL", "INTEGER NULL"),
        ("DueDate", "datetime2 NULL", "TEXT NULL"),
        ("ChequesJson", "nvarchar(max) NULL", "TEXT NULL"),
        ("InstallmentsJson", "nvarchar(max) NULL", "TEXT NULL"),
        ("PaymentCashAmount", "decimal(18,2) NOT NULL DEFAULT(0)", "TEXT NOT NULL DEFAULT '0'"),
        ("PaymentChequeSum", "decimal(18,2) NOT NULL DEFAULT(0)", "TEXT NOT NULL DEFAULT '0'"),
        ("PaymentInstallmentSum", "decimal(18,2) NOT NULL DEFAULT(0)", "TEXT NOT NULL DEFAULT '0'"),
        ("PaymentCreditRemainder", "decimal(18,2) NOT NULL DEFAULT(0)", "TEXT NOT NULL DEFAULT '0'"),
        ("FinanceAttachmentsApproved", "bit NULL", "INTEGER NULL"),
        ("FinanceAttachmentDecidedAt", "datetime2 NULL", "TEXT NULL"),
        ("FinanceAttachmentDecidedBy", "nvarchar(150) NULL", "TEXT NULL"),
        ("FinanceAttachmentNote", "nvarchar(500) NULL", "TEXT NULL")
    };

    /// <summary>
    /// SQL Server — همان دستورهای idempotent که مایگریشن هم اجرا می‌کند.
    /// عمداً «بعد از» تعریف Columns آمده است: مقداردهی فیلدهای استاتیک به ترتیب متن
    /// اجرا می‌شود و اگر بالا باشد، Columns هنوز null است.
    /// </summary>
    public static readonly string SqlServer = string.Join("\n", Columns.Select(c => $@"
IF OBJECT_ID(N'dbo.{SalesVisitSchemaV1.TableOrders}', N'U') IS NOT NULL AND COL_LENGTH('dbo.{SalesVisitSchemaV1.TableOrders}','{c.Column}') IS NULL
    ALTER TABLE [dbo].[{SalesVisitSchemaV1.TableOrders}] ADD [{c.Column}] {c.SqlServerType};"));

    public static async Task EnsureAsync(AppDbContext db)
    {
        if (!await TableExistsAsync(db, SalesVisitSchemaV1.TableOrders)) return;

        if (db.Database.IsSqlite())
        {
            foreach (var (column, _, sqliteType) in Columns)
            {
                if (await SqliteHasColumnAsync(db, SalesVisitSchemaV1.TableOrders, column)) continue;
                await SafeAsync(db, $"ALTER TABLE {SalesVisitSchemaV1.TableOrders} ADD COLUMN {column} {sqliteType};");
            }
            return;
        }

        foreach (var (column, sqlServerType, _) in Columns)
        {
            await SafeAsync(db, $@"
IF COL_LENGTH('dbo.{SalesVisitSchemaV1.TableOrders}','{column}') IS NULL
    ALTER TABLE [dbo].[{SalesVisitSchemaV1.TableOrders}] ADD [{column}] {sqlServerType};");
        }
    }

    private static async Task<bool> TableExistsAsync(AppDbContext db, string table)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = db.Database.IsSqlite()
                ? "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='VisOrders';"
                : "SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name='dbo' AND t.name='VisOrders';";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch
        {
            // اگر نتوانستیم بسنجیم، کاری نمی‌کنیم — DbInitializer خودش جدول را می‌سازد
            return false;
        }
    }

    private static async Task<bool> SqliteHasColumnAsync(AppDbContext db, string table, string column)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}';";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
        }
        catch
        {
            return true;   // اگر نتوانستیم بسنجیم، دست نمی‌زنیم
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
                Console.WriteLine($"[DB] VisPaymentPlanSchemaV1: {m}");
        }
    }
}
