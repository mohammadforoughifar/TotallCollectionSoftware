using System.Data;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// «تعریف سال مالی» مودیان و شمارهٔ سند سالانه:
///   • جدول MoadianFiscalYears (سال شمسی + بازه + وضعیت باز/بسته)
///   • ستون‌های FiscalYearId / YearSerial / DocumentNumber / PayType روی MoadianInvoices
///   • ایندکس‌های یکتا برای شمارهٔ سند و (سال مالی، سریال)
///   • پرکردن یک‌بارهٔ شمارهٔ سند برای سوابق قبلی (بر اساس سال دورهٔ هر فاکتور)
/// افزودن ستون‌ها idempotent است و جدول‌های صف/ارسال و ERP دست‌نخورده می‌مانند.
/// </summary>
public static class MoadianSchemaV6
{
    private const string DocumentNumberIndex = "IX_MoadianInvoices_DocumentNumber";
    private const string YearSerialIndex = "IX_MoadianInvoices_FiscalYearId_YearSerial";

    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite()) await EnsureSqliteAsync(db);
        else await EnsureSqlServerAsync(db);

        await BackfillDocumentNumbersAsync(db);
    }

    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS [MoadianFiscalYears] (
    [Id] INTEGER NOT NULL CONSTRAINT [PK_MoadianFiscalYears] PRIMARY KEY AUTOINCREMENT,
    [Year] INTEGER NOT NULL,
    [StartDate] TEXT NOT NULL,
    [EndDate] TEXT NOT NULL,
    [IsClosed] INTEGER NOT NULL DEFAULT 0,
    [Notes] TEXT NULL,
    [CreatedAt] TEXT NOT NULL
);");

        // ایندکس یکتای قدیمی فقط روی (Year) تا زمانی اعتبار دارد که ایندکس به‌ازای هر خدمات‌دهندهٔ V13 نباشد.
        if (!await SqliteIndexExistsAsync(db, "IX_MoadianFiscalYears_ProviderYear"))
            await db.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS [IX_MoadianFiscalYears_Year] ON [MoadianFiscalYears] ([Year]);");

        await AddSqliteColumnAsync(db, "MoadianInvoices", "FiscalYearId", "INTEGER NULL");
        await AddSqliteColumnAsync(db, "MoadianInvoices", "YearSerial", "INTEGER NOT NULL DEFAULT 0");
        await AddSqliteColumnAsync(db, "MoadianInvoices", "DocumentNumber", "TEXT NULL");
        await AddSqliteColumnAsync(db, "MoadianInvoices", "PayType", "INTEGER NULL");

        // ایندکس یکتای سراسری DocumentNumber تا زمانی اعتبار دارد که ایندکس به‌ازای هر خدمات‌دهندهٔ V12 نباشد.
        if (!await SqliteIndexExistsAsync(db, "IX_MoadianInvoices_ProviderDocumentNumber"))
        {
            await db.Database.ExecuteSqlRawAsync($@"
CREATE UNIQUE INDEX IF NOT EXISTS [{DocumentNumberIndex}]
    ON [MoadianInvoices] ([DocumentNumber]) WHERE [DocumentNumber] IS NOT NULL;");
        }
        await db.Database.ExecuteSqlRawAsync($@"
CREATE UNIQUE INDEX IF NOT EXISTS [{YearSerialIndex}]
    ON [MoadianInvoices] ([FiscalYearId], [YearSerial])
    WHERE [FiscalYearId] IS NOT NULL AND [YearSerial] > 0;");
    }

    private static Task EnsureSqlServerAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync($@"
IF OBJECT_ID(N'dbo.MoadianFiscalYears', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MoadianFiscalYears (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_MoadianFiscalYears PRIMARY KEY,
        [Year] int NOT NULL,
        StartDate datetime2 NOT NULL,
        EndDate datetime2 NOT NULL,
        IsClosed bit NOT NULL CONSTRAINT DF_MoadianFiscalYears_IsClosed DEFAULT(0),
        Notes nvarchar(500) NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_MoadianFiscalYears_CreatedAt DEFAULT(SYSDATETIME())
    );
END;
IF OBJECT_ID(N'dbo.MoadianFiscalYears', N'U') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MoadianFiscalYears_Year' AND object_id = OBJECT_ID(N'dbo.MoadianFiscalYears'))
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MoadianFiscalYears_ProviderYear' AND object_id = OBJECT_ID(N'dbo.MoadianFiscalYears'))
    CREATE UNIQUE INDEX IX_MoadianFiscalYears_Year ON dbo.MoadianFiscalYears([Year]);
IF OBJECT_ID(N'dbo.MoadianInvoices', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.MoadianInvoices', N'FiscalYearId') IS NULL
    ALTER TABLE dbo.MoadianInvoices ADD FiscalYearId int NULL;
IF OBJECT_ID(N'dbo.MoadianInvoices', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.MoadianInvoices', N'YearSerial') IS NULL
    ALTER TABLE dbo.MoadianInvoices ADD YearSerial int NOT NULL CONSTRAINT DF_MoadianInvoices_YearSerial DEFAULT(0);
IF OBJECT_ID(N'dbo.MoadianInvoices', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.MoadianInvoices', N'DocumentNumber') IS NULL
    ALTER TABLE dbo.MoadianInvoices ADD DocumentNumber nvarchar(30) NULL;
IF OBJECT_ID(N'dbo.MoadianInvoices', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.MoadianInvoices', N'PayType') IS NULL
    ALTER TABLE dbo.MoadianInvoices ADD PayType int NULL;
IF OBJECT_ID(N'dbo.MoadianInvoices', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.MoadianInvoices', N'DocumentNumber') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{DocumentNumberIndex}' AND object_id = OBJECT_ID(N'dbo.MoadianInvoices'))
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MoadianInvoices_ProviderDocumentNumber' AND object_id = OBJECT_ID(N'dbo.MoadianInvoices'))
    EXEC(N'CREATE UNIQUE INDEX {DocumentNumberIndex} ON dbo.MoadianInvoices(DocumentNumber) WHERE DocumentNumber IS NOT NULL');
IF OBJECT_ID(N'dbo.MoadianInvoices', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.MoadianInvoices', N'FiscalYearId') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{YearSerialIndex}' AND object_id = OBJECT_ID(N'dbo.MoadianInvoices'))
    EXEC(N'CREATE UNIQUE INDEX {YearSerialIndex} ON dbo.MoadianInvoices(FiscalYearId, YearSerial) WHERE FiscalYearId IS NOT NULL AND YearSerial > 0');");

    /// <summary>
    /// سوابق قبلی که شمارهٔ سند سالانه ندارند بر اساس سال دورهٔ مالیاتی‌شان شماره‌گذاری می‌شوند
    /// تا فهرست فاکتورها خالی نماند. یک‌بار اجرا می‌شود و شماره‌های موجود را تغییر نمی‌دهد.
    /// </summary>
    private static async Task BackfillDocumentNumbersAsync(AppDbContext db)
    {
        try
        {
            var pending = await db.MoadianInvoices.AsNoTracking()
                .Where(i => i.DocumentNumber == null || i.DocumentNumber == "")
                .Select(i => new
                {
                    i.Id,
                    i.Date,
                    FiscalYearId = i.FiscalYearId,
                    PeriodYear = i.FiscalPeriod != null ? (int?)i.FiscalPeriod.Year : null
                })
                .ToListAsync();
            if (pending.Count == 0) return;

            var yearLookup = await db.MoadianFiscalYears.AsNoTracking()
                .ToDictionaryAsync(y => y.Id, y => y.Year);
            var nextSerialByYear = new Dictionary<int, int>();
            foreach (var row in await db.MoadianInvoices.AsNoTracking()
                         .Where(i => i.YearSerial > 0 && i.FiscalYearId != null)
                         .Select(i => new { i.FiscalYearId, i.YearSerial })
                         .ToListAsync())
            {
                if (row.FiscalYearId is null || !yearLookup.TryGetValue(row.FiscalYearId.Value, out var year)) continue;
                nextSerialByYear[year] = Math.Max(nextSerialByYear.GetValueOrDefault(year), row.YearSerial);
            }

            foreach (var invoice in pending.OrderBy(x => x.Id))
            {
                var year = invoice.PeriodYear
                           ?? (invoice.FiscalYearId is not null && yearLookup.TryGetValue(invoice.FiscalYearId.Value, out var mapped)
                               ? mapped
                               : PersianDate.FromGregorian(invoice.Date).Year);
                var serial = nextSerialByYear.GetValueOrDefault(year) + 1;
                nextSerialByYear[year] = serial;

                await db.Database.ExecuteSqlRawAsync(
                    "UPDATE MoadianInvoices SET YearSerial = {0}, DocumentNumber = {1} WHERE Id = {2} AND (DocumentNumber IS NULL OR DocumentNumber = '')",
                    serial, $"{year}/{serial:000000}", invoice.Id);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // شماره‌گذاری سوابق نباید مانع بالا آمدن برنامه شود؛ سال مالی و شماره‌های بعدی درست کار می‌کنند.
            Console.WriteLine($"[Moadian] Document-number backfill skipped: {ex.GetType().Name}");
        }
    }

    private static async Task AddSqliteColumnAsync(AppDbContext db, string table, string column, string definition)
    {
        if (!await SqliteColumnExistsAsync(db, table, column))
            await db.Database.ExecuteSqlRawAsync($"ALTER TABLE [{table}] ADD COLUMN [{column}] {definition};");
    }

    private static async Task<bool> SqliteColumnExistsAsync(AppDbContext db, string table, string column)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info('{table}');";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    private static async Task<bool> SqliteIndexExistsAsync(AppDbContext db, string indexName)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'index' AND name = '" + indexName + "';";
            return await command.ExecuteScalarAsync() is not null;
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }
}
