using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>Schema only: old orders deliberately remain disabled until explicitly reconciled.</summary>
public static class ShopOrderCashSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.Transactions', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Transactions', N'ShopCashLedgerEnabled') IS NULL
    ALTER TABLE [dbo].[Transactions] ADD [ShopCashLedgerEnabled] bit NOT NULL CONSTRAINT [DF_Transactions_ShopCashLedgerEnabled] DEFAULT(0);
IF OBJECT_ID(N'dbo.ShopOrderCashEntries', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ShopOrderCashEntries] (
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ShopOrderCashEntries] PRIMARY KEY,
        [CompanyId] int NULL, [TransactionId] int NOT NULL, [OrderNumber] nvarchar(30) NOT NULL,
        [OrderType] int NOT NULL, [PartyId] int NULL, [PartyName] nvarchar(200) NULL, [IsOpening] bit NOT NULL DEFAULT 0,
        [TrsAccountId] int NOT NULL, [MatchedTrsVoucherId] int NULL, [CashType] int NOT NULL, [Reference] nvarchar(100) NULL,
        [Amount] decimal(18,2) NOT NULL, [Date] datetime2 NOT NULL, [OperationId] uniqueidentifier NOT NULL,
        [CreatedAt] datetime2 NOT NULL, [CreatedBy] nvarchar(120) NULL,
        [ReversalDate] datetime2 NULL, [ReversalOperationId] uniqueidentifier NULL,
        [ReversedAt] datetime2 NULL, [ReversedBy] nvarchar(120) NULL, [ReversalReason] nvarchar(200) NULL,
        CONSTRAINT [FK_ShopOrderCashEntries_TrsAccounts_TrsAccountId] FOREIGN KEY ([TrsAccountId]) REFERENCES [dbo].[TrsAccounts] ([Id]),
        CONSTRAINT [FK_ShopOrderCashEntries_TrsVouchers_MatchedTrsVoucherId] FOREIGN KEY ([MatchedTrsVoucherId]) REFERENCES [dbo].[TrsVouchers] ([Id])
    );
END;
IF COL_LENGTH(N'dbo.ShopOrderCashEntries', N'IsOpening') IS NULL
    ALTER TABLE [dbo].[ShopOrderCashEntries] ADD [IsOpening] bit NOT NULL CONSTRAINT [DF_ShopOrderCashEntries_IsOpening] DEFAULT 0;
IF COL_LENGTH(N'dbo.ShopOrderCashEntries', N'MatchedTrsVoucherId') IS NULL
    ALTER TABLE [dbo].[ShopOrderCashEntries] ADD [MatchedTrsVoucherId] int NULL;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.ShopOrderCashEntries') AND [name]=N'FK_ShopOrderCashEntries_TrsVouchers_MatchedTrsVoucherId')
    EXEC(N'ALTER TABLE [dbo].[ShopOrderCashEntries] WITH CHECK ADD CONSTRAINT [FK_ShopOrderCashEntries_TrsVouchers_MatchedTrsVoucherId] FOREIGN KEY ([MatchedTrsVoucherId]) REFERENCES [dbo].[TrsVouchers] ([Id]);');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopOrderCashEntries') AND [name]=N'IX_ShopOrderCashEntries_MatchedTrsVoucherId')
    EXEC(N'CREATE UNIQUE INDEX [IX_ShopOrderCashEntries_MatchedTrsVoucherId] ON [dbo].[ShopOrderCashEntries] ([MatchedTrsVoucherId]) WHERE [MatchedTrsVoucherId] IS NOT NULL');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopOrderCashEntries') AND [name]=N'IX_ShopOrderCashEntries_TransactionId')
    EXEC(N'CREATE UNIQUE INDEX [IX_ShopOrderCashEntries_TransactionId] ON [dbo].[ShopOrderCashEntries] ([TransactionId]) WHERE [ReversalDate] IS NULL');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopOrderCashEntries') AND [name]=N'IX_ShopOrderCashEntries_OperationId')
    EXEC(N'CREATE UNIQUE INDEX [IX_ShopOrderCashEntries_OperationId] ON [dbo].[ShopOrderCashEntries] ([OperationId])');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopOrderCashEntries') AND [name]=N'IX_ShopOrderCashEntries_ReversalOperationId')
    EXEC(N'CREATE UNIQUE INDEX [IX_ShopOrderCashEntries_ReversalOperationId] ON [dbo].[ShopOrderCashEntries] ([ReversalOperationId]) WHERE [ReversalOperationId] IS NOT NULL');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopOrderCashEntries') AND [name]=N'IX_ShopOrderCashEntries_CompanyId_TrsAccountId')
    EXEC(N'CREATE INDEX [IX_ShopOrderCashEntries_CompanyId_TrsAccountId] ON [dbo].[ShopOrderCashEntries] ([CompanyId], [TrsAccountId])');";

    public const string SqliteTable = @"
CREATE TABLE IF NOT EXISTS [ShopOrderCashEntries] (
 [Id] INTEGER NOT NULL CONSTRAINT [PK_ShopOrderCashEntries] PRIMARY KEY AUTOINCREMENT,
 [CompanyId] INTEGER NULL, [TransactionId] INTEGER NOT NULL, [OrderNumber] TEXT NOT NULL,
 [OrderType] INTEGER NOT NULL, [PartyId] INTEGER NULL, [PartyName] TEXT NULL, [IsOpening] INTEGER NOT NULL DEFAULT 0,
 [TrsAccountId] INTEGER NOT NULL, [MatchedTrsVoucherId] INTEGER NULL, [CashType] INTEGER NOT NULL, [Reference] TEXT NULL,
 [Amount] TEXT NOT NULL, [Date] TEXT NOT NULL, [OperationId] TEXT NOT NULL,
 [CreatedAt] TEXT NOT NULL, [CreatedBy] TEXT NULL, [ReversalDate] TEXT NULL,
 [ReversalOperationId] TEXT NULL, [ReversedAt] TEXT NULL, [ReversedBy] TEXT NULL, [ReversalReason] TEXT NULL,
 CONSTRAINT [FK_ShopOrderCashEntries_TrsAccounts_TrsAccountId] FOREIGN KEY ([TrsAccountId]) REFERENCES [TrsAccounts] ([Id]) ON DELETE RESTRICT,
 CONSTRAINT [FK_ShopOrderCashEntries_TrsVouchers_MatchedTrsVoucherId] FOREIGN KEY ([MatchedTrsVoucherId]) REFERENCES [TrsVouchers] ([Id]) ON DELETE RESTRICT
);
CREATE UNIQUE INDEX IF NOT EXISTS [IX_ShopOrderCashEntries_MatchedTrsVoucherId] ON [ShopOrderCashEntries] ([MatchedTrsVoucherId]) WHERE [MatchedTrsVoucherId] IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS [IX_ShopOrderCashEntries_TransactionId] ON [ShopOrderCashEntries] ([TransactionId]) WHERE [ReversalDate] IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS [IX_ShopOrderCashEntries_OperationId] ON [ShopOrderCashEntries] ([OperationId]);
CREATE UNIQUE INDEX IF NOT EXISTS [IX_ShopOrderCashEntries_ReversalOperationId] ON [ShopOrderCashEntries] ([ReversalOperationId]) WHERE [ReversalOperationId] IS NOT NULL;
CREATE INDEX IF NOT EXISTS [IX_ShopOrderCashEntries_CompanyId_TrsAccountId] ON [ShopOrderCashEntries] ([CompanyId], [TrsAccountId]);";

    public static async Task EnsureAsync(AppDbContext db)
    {
        if (!db.Database.IsSqlite()) { await db.Database.ExecuteSqlRawAsync(SqlServer); return; }
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info([Transactions]);";
        var names = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) names.Add(reader.GetString(1));
        if (names.Count == 0) throw new InvalidOperationException("جدول فاکتورهای فروشگاه یافت نشد.");
        if (!names.Contains("ShopCashLedgerEnabled", StringComparer.OrdinalIgnoreCase))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [Transactions] ADD COLUMN [ShopCashLedgerEnabled] INTEGER NOT NULL DEFAULT 0;");
        await using var cashColumns = connection.CreateCommand();
        cashColumns.CommandText = "PRAGMA table_info([ShopOrderCashEntries]);";
        var cashNames = new List<string>();
        await using (var reader = await cashColumns.ExecuteReaderAsync())
            while (await reader.ReadAsync()) cashNames.Add(reader.GetString(1));
        if (cashNames.Count > 0 && !cashNames.Contains("IsOpening", StringComparer.OrdinalIgnoreCase))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [ShopOrderCashEntries] ADD COLUMN [IsOpening] INTEGER NOT NULL DEFAULT 0;");
        if (cashNames.Count > 0 && !cashNames.Contains("MatchedTrsVoucherId", StringComparer.OrdinalIgnoreCase))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE [ShopOrderCashEntries] ADD COLUMN [MatchedTrsVoucherId] INTEGER NULL REFERENCES [TrsVouchers] ([Id]);");
        await db.Database.ExecuteSqlRawAsync(SqliteTable);
    }
}
