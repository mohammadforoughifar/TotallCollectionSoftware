using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>افزودن سند معکوس به وصول، بدون حذف تاریخچه سند اصلی.</summary>
public static class SaleChequeReceiptReversalSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.SaleChequeReceipts', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.SaleChequeReceipts', N'ReversalOperationId') IS NULL ALTER TABLE [dbo].[SaleChequeReceipts] ADD [ReversalOperationId] uniqueidentifier NULL;
    IF COL_LENGTH(N'dbo.SaleChequeReceipts', N'ReversalVoucherId') IS NULL ALTER TABLE [dbo].[SaleChequeReceipts] ADD [ReversalVoucherId] int NULL;
    IF COL_LENGTH(N'dbo.SaleChequeReceipts', N'ReversalDate') IS NULL ALTER TABLE [dbo].[SaleChequeReceipts] ADD [ReversalDate] datetime2 NULL;
    IF COL_LENGTH(N'dbo.SaleChequeReceipts', N'PreviousAccountKnown') IS NULL ALTER TABLE [dbo].[SaleChequeReceipts] ADD [PreviousAccountKnown] bit NOT NULL CONSTRAINT [DF_SaleChequeReceipts_PreviousAccountKnown] DEFAULT(0);
    IF COL_LENGTH(N'dbo.SaleChequeReceipts', N'PreviousTrsAccountId') IS NULL ALTER TABLE [dbo].[SaleChequeReceipts] ADD [PreviousTrsAccountId] int NULL;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.SaleChequeReceipts') AND name = N'IX_SaleChequeReceipts_ChequeId' AND has_filter = 0)
        DROP INDEX [IX_SaleChequeReceipts_ChequeId] ON [dbo].[SaleChequeReceipts];
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.SaleChequeReceipts') AND name = N'IX_SaleChequeReceipts_ChequeId')
        EXEC(N'CREATE UNIQUE INDEX [IX_SaleChequeReceipts_ChequeId] ON [dbo].[SaleChequeReceipts] ([ChequeId]) WHERE [ReversalDate] IS NULL;');
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.SaleChequeReceipts') AND name = N'IX_SaleChequeReceipts_ReversalOperationId')
        EXEC(N'CREATE UNIQUE INDEX [IX_SaleChequeReceipts_ReversalOperationId] ON [dbo].[SaleChequeReceipts] ([ReversalOperationId]) WHERE [ReversalOperationId] IS NOT NULL;');
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.SaleChequeReceipts') AND name = N'IX_SaleChequeReceipts_ReversalVoucherId')
        EXEC(N'CREATE UNIQUE INDEX [IX_SaleChequeReceipts_ReversalVoucherId] ON [dbo].[SaleChequeReceipts] ([ReversalVoucherId]) WHERE [ReversalVoucherId] IS NOT NULL;');
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.SaleChequeReceipts') AND name = N'FK_SaleChequeReceipts_ReversalVoucher')
        EXEC(N'ALTER TABLE [dbo].[SaleChequeReceipts] ADD CONSTRAINT [FK_SaleChequeReceipts_ReversalVoucher] FOREIGN KEY ([ReversalVoucherId]) REFERENCES [dbo].[AccVouchers] ([Id]) ON DELETE NO ACTION;');
END";

    public static async Task EnsureAsync(AppDbContext db)
    {
        if (!db.Database.IsSqlite()) { await db.Database.ExecuteSqlRawAsync(SqlServer); return; }
        var connection = db.Database.GetDbConnection();
        var close = connection.State != ConnectionState.Open;
        if (close) await connection.OpenAsync();
        try
        {
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info('SaleChequeReceipts')";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) present.Add(reader.GetString(1));
            }
            if (present.Count == 0) return;
            foreach (var (name, type) in new[] { ("ReversalOperationId", "TEXT NULL"), ("ReversalVoucherId", "INTEGER NULL REFERENCES AccVouchers(Id) ON DELETE RESTRICT"),
                ("ReversalDate", "TEXT NULL"), ("PreviousAccountKnown", "INTEGER NOT NULL DEFAULT 0"), ("PreviousTrsAccountId", "INTEGER NULL") })
            {
                if (present.Contains(name)) continue;
                await using var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE SaleChequeReceipts ADD COLUMN {name} {type}";
                await alter.ExecuteNonQueryAsync();
            }
            // Keep the previous uniqueness constraint if replacement fails (e.g. corrupt legacy rows).
            await using var indexTransaction = await connection.BeginTransactionAsync();
            await using var indexes = connection.CreateCommand();
            indexes.Transaction = indexTransaction;
            indexes.CommandText = @"DROP INDEX IF EXISTS ""IX_SaleChequeReceipts_ChequeId"";
CREATE UNIQUE INDEX ""IX_SaleChequeReceipts_ChequeId"" ON ""SaleChequeReceipts"" (""ChequeId"") WHERE ""ReversalDate"" IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeReceipts_ReversalOperationId"" ON ""SaleChequeReceipts"" (""ReversalOperationId"") WHERE ""ReversalOperationId"" IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeReceipts_ReversalVoucherId"" ON ""SaleChequeReceipts"" (""ReversalVoucherId"") WHERE ""ReversalVoucherId"" IS NOT NULL;";
            await indexes.ExecuteNonQueryAsync();
            await indexTransaction.CommitAsync();
        }
        finally { if (close) await connection.CloseAsync(); }
    }
}
