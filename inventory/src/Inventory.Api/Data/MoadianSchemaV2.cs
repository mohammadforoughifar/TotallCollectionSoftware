using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// Schema patch for the two sales sources, per-draft invoice type, and buyer tax/postal identity columns.
/// Existing installations use EnsureCreated/legacy migrations, so these columns
/// must be added idempotently at startup for both SQLite and SQL Server.
/// </summary>
public static class MoadianSchemaV2
{
    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        var needsLegacyStateReset = !await ColumnExistsAsync(db, "MoadianInvoices", "OperationsTransactionId");
        await AddSqliteColumnAsync(db, "MoadianSettings", "FiscalMemoryId", "TEXT NULL");
        await AddSqliteColumnAsync(db, "MoadianSettings", "SigningCertificatePem", "TEXT NULL");
        await AddSqliteColumnAsync(db, "MoadianSettings", "AutoDraftFromOperations", "INTEGER NOT NULL DEFAULT 0");
        await AddSqliteColumnAsync(db, "MoadianSettings", "AutoDraftFromFacInvoices", "INTEGER NOT NULL DEFAULT 0");
        await AddSqliteColumnAsync(db, "MoadianInvoices", "OperationsTransactionId", "INTEGER NULL");
        await AddSqliteColumnAsync(db, "MoadianInvoices", "InvoiceType", "INTEGER NOT NULL DEFAULT 0");
        await AddSqliteColumnAsync(db, "MoadianInvoiceLines", "UnitCode", "TEXT NULL");
        await AddSqliteColumnAsync(db, "Parties", "TaxId", "TEXT NULL");
        await AddSqliteColumnAsync(db, "Parties", "PostalCode", "TEXT NULL");
        await db.Database.ExecuteSqlRawAsync(@"
CREATE UNIQUE INDEX IF NOT EXISTS IX_MoadianInvoices_OperationsTransactionId
ON MoadianInvoices(OperationsTransactionId)
WHERE OperationsTransactionId IS NOT NULL;");
        if (needsLegacyStateReset) await PauseLegacySendStatesAsync(db, sqlite: true);
    }

    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        var needsLegacyStateReset = await SqlServerOperationsColumnMissingAsync(db);
        await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.MoadianSettings', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.MoadianSettings', N'FiscalMemoryId') IS NULL
    ALTER TABLE dbo.MoadianSettings ADD FiscalMemoryId nvarchar(20) NULL;
IF OBJECT_ID(N'dbo.MoadianSettings', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.MoadianSettings', N'SigningCertificatePem') IS NULL
    ALTER TABLE dbo.MoadianSettings ADD SigningCertificatePem nvarchar(max) NULL;
IF OBJECT_ID(N'dbo.MoadianSettings', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.MoadianSettings', N'AutoDraftFromOperations') IS NULL
    ALTER TABLE dbo.MoadianSettings ADD AutoDraftFromOperations bit NOT NULL CONSTRAINT DF_MoadianSettings_AutoDraftFromOperations DEFAULT(0);
IF OBJECT_ID(N'dbo.MoadianSettings', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.MoadianSettings', N'AutoDraftFromFacInvoices') IS NULL
    ALTER TABLE dbo.MoadianSettings ADD AutoDraftFromFacInvoices bit NOT NULL CONSTRAINT DF_MoadianSettings_AutoDraftFromFacInvoices DEFAULT(0);
IF OBJECT_ID(N'dbo.MoadianInvoices', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.MoadianInvoices', N'OperationsTransactionId') IS NULL
    ALTER TABLE dbo.MoadianInvoices ADD OperationsTransactionId int NULL;
IF OBJECT_ID(N'dbo.MoadianInvoices', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.MoadianInvoices', N'InvoiceType') IS NULL
    ALTER TABLE dbo.MoadianInvoices ADD InvoiceType int NOT NULL CONSTRAINT DF_MoadianInvoices_InvoiceType DEFAULT(0);
IF OBJECT_ID(N'dbo.MoadianInvoiceLines', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.MoadianInvoiceLines', N'UnitCode') IS NULL
    ALTER TABLE dbo.MoadianInvoiceLines ADD UnitCode nvarchar(20) NULL;
IF OBJECT_ID(N'dbo.Parties', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Parties', N'TaxId') IS NULL
    ALTER TABLE dbo.Parties ADD TaxId nvarchar(20) NULL;
IF OBJECT_ID(N'dbo.Parties', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Parties', N'PostalCode') IS NULL
    ALTER TABLE dbo.Parties ADD PostalCode nvarchar(20) NULL;
");
        // Compile the filtered index only after ALTER TABLE has committed the new column.
        // Dynamic SQL avoids SQL Server binding OperationsTransactionId against the old schema.
        await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.MoadianInvoices', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.MoadianInvoices', N'OperationsTransactionId') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MoadianInvoices_OperationsTransactionId' AND object_id = OBJECT_ID(N'dbo.MoadianInvoices'))
    EXEC(N'CREATE UNIQUE INDEX IX_MoadianInvoices_OperationsTransactionId ON dbo.MoadianInvoices(OperationsTransactionId) WHERE OperationsTransactionId IS NOT NULL');");
        if (needsLegacyStateReset) await PauseLegacySendStatesAsync(db, sqlite: false);
    }

    private static async Task<bool> SqlServerOperationsColumnMissingAsync(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere) await connection.OpenAsync();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT CASE WHEN OBJECT_ID(N'dbo.MoadianInvoices', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.MoadianInvoices', N'OperationsTransactionId') IS NULL THEN 1 ELSE 0 END";
            return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    private static Task PauseLegacySendStatesAsync(AppDbContext db, bool sqlite)
    {
        const string sqliteSql = @"
UPDATE MoadianInvoices
SET Status = 0, ReferenceId = NULL, TrackingId = NULL, SendAt = NULL,
    ErrorCode = NULL, ErrorMessage = NULL
WHERE Status = 3 AND (ReferenceId LIKE 'SIM-%' OR TrackingId LIKE 'SIM-%');
UPDATE MoadianInvoices
SET Status = 0, QueuedAt = NULL, ErrorCode = 'SEND_DISABLED',
    ErrorMessage = 'صف قدیمی برای بازبینی دستی متوقف شد؛ ارسال رسمی در این نسخه غیرفعال است.'
WHERE Status IN (1, 2);";
        const string sqlServerSql = @"
UPDATE dbo.MoadianInvoices
SET Status = 0, ReferenceId = NULL, TrackingId = NULL, SendAt = NULL,
    ErrorCode = NULL, ErrorMessage = NULL
WHERE Status = 3 AND (ReferenceId LIKE N'SIM-%' OR TrackingId LIKE N'SIM-%');
UPDATE dbo.MoadianInvoices
SET Status = 0, QueuedAt = NULL, ErrorCode = N'SEND_DISABLED',
    ErrorMessage = N'صف قدیمی برای بازبینی دستی متوقف شد؛ ارسال رسمی در این نسخه غیرفعال است.'
WHERE Status IN (1, 2);";
        return db.Database.ExecuteSqlRawAsync(sqlite ? sqliteSql : sqlServerSql);
    }

    private static async Task AddSqliteColumnAsync(AppDbContext db, string table, string column, string declaration)
    {
        if (!await ColumnExistsAsync(db, table, column))
            await db.Database.ExecuteSqlRawAsync($"ALTER TABLE [{table}] ADD COLUMN [{column}] {declaration};");
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
                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}'";
                return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
            }
            finally
            {
                if (openedHere) await connection.CloseAsync();
            }
        }
        catch
        {
            // A fresh EnsureCreated database already has the columns from the model.
            return false;
        }
    }
}
