using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// Additive policy version and links. Version zero is intentionally retained for every
/// old record: no historical warranty is re-priced or turned into a receivable.
/// </summary>
public static class ShopWarrantyChargingSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.ShopWarranties',N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ShopWarranties',N'ChargePolicyVersion') IS NULL
 ALTER TABLE [dbo].[ShopWarranties] ADD [ChargePolicyVersion] int NOT NULL CONSTRAINT [DF_ShopWarranty_ChargePolicyVersion] DEFAULT (0);
IF OBJECT_ID(N'dbo.ShopWarranties',N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ShopWarranties',N'ChargeTransactionId') IS NULL
 ALTER TABLE [dbo].[ShopWarranties] ADD [ChargeTransactionId] int NULL;
IF OBJECT_ID(N'dbo.ShopWarrantyItems',N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ShopWarrantyItems',N'CustomerUnitPrice') IS NULL
 ALTER TABLE [dbo].[ShopWarrantyItems] ADD [CustomerUnitPrice] decimal(18,2) NULL;
IF OBJECT_ID(N'dbo.ShopWarrantyItems',N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ShopWarrantyItems',N'Performed') IS NULL
 ALTER TABLE [dbo].[ShopWarrantyItems] ADD [Performed] bit NOT NULL CONSTRAINT [DF_ShopWarrantyItem_Performed] DEFAULT (1);
IF OBJECT_ID(N'dbo.RepairOrders',N'U') IS NOT NULL AND COL_LENGTH(N'dbo.RepairOrders',N'WarrantyProcessed') IS NULL
 ALTER TABLE [dbo].[RepairOrders] ADD [WarrantyProcessed] bit NOT NULL CONSTRAINT [DF_RepairOrder_WarrantyProcessed] DEFAULT (0);
IF OBJECT_ID(N'dbo.RepairOrders',N'U') IS NOT NULL AND COL_LENGTH(N'dbo.RepairOrders',N'WarrantyStockTransactionId') IS NULL
 ALTER TABLE [dbo].[RepairOrders] ADD [WarrantyStockTransactionId] int NULL;
IF OBJECT_ID(N'dbo.RepairItems',N'U') IS NOT NULL AND COL_LENGTH(N'dbo.RepairItems',N'WarrantyDecision') IS NULL
 ALTER TABLE [dbo].[RepairItems] ADD [WarrantyDecision] int NOT NULL CONSTRAINT [DF_RepairItem_WarrantyDecision] DEFAULT (0);
IF OBJECT_ID(N'dbo.RepairItems',N'U') IS NOT NULL AND COL_LENGTH(N'dbo.RepairItems',N'WarrantySupplied') IS NULL
 ALTER TABLE [dbo].[RepairItems] ADD [WarrantySupplied] bit NOT NULL CONSTRAINT [DF_RepairItem_WarrantySupplied] DEFAULT (1);
IF OBJECT_ID(N'dbo.RepairItems',N'U') IS NOT NULL AND COL_LENGTH(N'dbo.RepairItems',N'WarrantyPerformed') IS NULL
 ALTER TABLE [dbo].[RepairItems] ADD [WarrantyPerformed] bit NOT NULL CONSTRAINT [DF_RepairItem_WarrantyPerformed] DEFAULT (1);
IF OBJECT_ID(N'dbo.ShopWarranties',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Transactions',N'U') IS NOT NULL
 AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [parent_object_id]=OBJECT_ID(N'dbo.ShopWarranties') AND [name]=N'FK_ShopWarranties_Transactions_ChargeTransactionId')
 EXEC(N'ALTER TABLE [dbo].[ShopWarranties] ADD CONSTRAINT [FK_ShopWarranties_Transactions_ChargeTransactionId] FOREIGN KEY ([ChargeTransactionId]) REFERENCES [dbo].[Transactions] ([Id])');
IF OBJECT_ID(N'dbo.RepairOrders',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Transactions',N'U') IS NOT NULL
 AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE [parent_object_id]=OBJECT_ID(N'dbo.RepairOrders') AND [name]=N'FK_RepairOrders_Transactions_WarrantyStockTransactionId')
 EXEC(N'ALTER TABLE [dbo].[RepairOrders] ADD CONSTRAINT [FK_RepairOrders_Transactions_WarrantyStockTransactionId] FOREIGN KEY ([WarrantyStockTransactionId]) REFERENCES [dbo].[Transactions] ([Id])');
IF OBJECT_ID(N'dbo.ShopWarranties',N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopWarranties') AND [name]=N'IX_ShopWarranties_ChargeTransactionId')
 EXEC(N'CREATE UNIQUE INDEX [IX_ShopWarranties_ChargeTransactionId] ON [dbo].[ShopWarranties] ([ChargeTransactionId]) WHERE [ChargeTransactionId] IS NOT NULL');
IF OBJECT_ID(N'dbo.RepairOrders',N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.RepairOrders') AND [name]=N'IX_RepairOrders_WarrantyStockTransactionId')
 EXEC(N'CREATE UNIQUE INDEX [IX_RepairOrders_WarrantyStockTransactionId] ON [dbo].[RepairOrders] ([WarrantyStockTransactionId]) WHERE [WarrantyStockTransactionId] IS NOT NULL');";

    // SQLite permits adding nullable REFERENCES columns without rebuilding existing tables.
    // Each statement is executed only if its column is absent; existing data is never rewritten.
    public static readonly (string Table, string Column, string Definition)[] SqliteColumns =
    {
        ("ShopWarranties", "ChargePolicyVersion", "INTEGER NOT NULL DEFAULT 0"),
        ("ShopWarranties", "ChargeTransactionId", "INTEGER NULL REFERENCES [Transactions]([Id]) ON DELETE RESTRICT"),
        ("ShopWarrantyItems", "CustomerUnitPrice", "TEXT NULL"),
        ("ShopWarrantyItems", "Performed", "INTEGER NOT NULL DEFAULT 1"),
        ("RepairOrders", "WarrantyProcessed", "INTEGER NOT NULL DEFAULT 0"),
        ("RepairOrders", "WarrantyStockTransactionId", "INTEGER NULL REFERENCES [Transactions]([Id]) ON DELETE RESTRICT"),
        ("RepairItems", "WarrantyDecision", "INTEGER NOT NULL DEFAULT 0"),
        ("RepairItems", "WarrantySupplied", "INTEGER NOT NULL DEFAULT 1"),
        ("RepairItems", "WarrantyPerformed", "INTEGER NOT NULL DEFAULT 1")
    };

    public const string SqliteIndexes = @"
CREATE UNIQUE INDEX IF NOT EXISTS [IX_ShopWarranties_ChargeTransactionId] ON [ShopWarranties] ([ChargeTransactionId]) WHERE [ChargeTransactionId] IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS [IX_RepairOrders_WarrantyStockTransactionId] ON [RepairOrders] ([WarrantyStockTransactionId]) WHERE [WarrantyStockTransactionId] IS NOT NULL;";

    public static async Task EnsureAsync(AppDbContext db)
    {
        if (!db.Database.IsSqlite())
        {
            await db.Database.ExecuteSqlRawAsync(SqlServer);
            return;
        }
        var connection = db.Database.GetDbConnection();
        var close = connection.State != ConnectionState.Open;
        if (close) await connection.OpenAsync();
        try
        {
            foreach (var (table, column, definition) in SqliteColumns)
            {
                var found = false;
                await using (var check = connection.CreateCommand())
                {
                    check.CommandText = $"PRAGMA table_info([{table}]);";
                    await using var reader = await check.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                        if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) found = true;
                }
                if (!found)
                {
                    await using var alter = connection.CreateCommand();
                    alter.CommandText = $"ALTER TABLE [{table}] ADD COLUMN [{column}] {definition};";
                    await alter.ExecuteNonQueryAsync();
                }
            }
            await using var indexes = connection.CreateCommand();
            indexes.CommandText = SqliteIndexes;
            await indexes.ExecuteNonQueryAsync();
        }
        finally { if (close) await connection.CloseAsync(); }
    }
}
