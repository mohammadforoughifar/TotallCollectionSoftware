using Microsoft.EntityFrameworkCore;
namespace Inventory.Api.Data;
public static class ShopExpenseChequeSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.ShopExpenseCheques',N'U') IS NULL
BEGIN
 CREATE TABLE [dbo].[ShopExpenseCheques] (
  [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ShopExpenseCheques] PRIMARY KEY,
  [CompanyId] int NULL, [OperationId] uniqueidentifier NOT NULL, [ExpenseId] int NOT NULL, [Kind] int NOT NULL, [State] int NOT NULL,
  [SourceChequeId] int NULL, [SourceTransactionId] int NULL, [IssuingAccountId] int NULL, [SettlementVoucherId] int NULL,
  [Number] nvarchar(50) NOT NULL, [BankName] nvarchar(200) NULL, [Recipient] nvarchar(150) NOT NULL,
  [Amount] decimal(18,2) NOT NULL, [DueDate] datetime2 NOT NULL, [HandedAt] datetime2 NOT NULL,
  [StatusOperationId] uniqueidentifier NULL, [SettledAt] datetime2 NULL, [Reason] nvarchar(200) NULL,
  [CreatedBy] nvarchar(120) NULL, [CreatedAt] datetime2 NOT NULL, [StatusBy] nvarchar(120) NULL, [StatusAt] datetime2 NULL,
  CONSTRAINT [FK_ShopExpenseCheques_Expenses_ExpenseId] FOREIGN KEY ([ExpenseId]) REFERENCES [dbo].[Expenses] ([Id]),
  CONSTRAINT [FK_ShopExpenseCheques_Cheques_SourceChequeId] FOREIGN KEY ([SourceChequeId]) REFERENCES [dbo].[Cheques] ([Id]),
  CONSTRAINT [FK_ShopExpenseCheques_Transactions_SourceTransactionId] FOREIGN KEY ([SourceTransactionId]) REFERENCES [dbo].[Transactions] ([Id]),
  CONSTRAINT [FK_ShopExpenseCheques_TrsAccounts_IssuingAccountId] FOREIGN KEY ([IssuingAccountId]) REFERENCES [dbo].[TrsAccounts] ([Id]),
  CONSTRAINT [FK_ShopExpenseCheques_TrsVouchers_SettlementVoucherId] FOREIGN KEY ([SettlementVoucherId]) REFERENCES [dbo].[TrsVouchers] ([Id])
 );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopExpenseCheques') AND [name]=N'IX_ShopExpenseCheques_OperationId') EXEC(N'CREATE UNIQUE INDEX [IX_ShopExpenseCheques_OperationId] ON [dbo].[ShopExpenseCheques] ([OperationId])');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopExpenseCheques') AND [name]=N'IX_ShopExpenseCheques_StatusOperationId') EXEC(N'CREATE UNIQUE INDEX [IX_ShopExpenseCheques_StatusOperationId] ON [dbo].[ShopExpenseCheques] ([StatusOperationId]) WHERE [StatusOperationId] IS NOT NULL');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopExpenseCheques') AND [name]=N'IX_ShopExpenseCheques_ExpenseId') EXEC(N'CREATE UNIQUE INDEX [IX_ShopExpenseCheques_ExpenseId] ON [dbo].[ShopExpenseCheques] ([ExpenseId]) WHERE [State] <> 3');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopExpenseCheques') AND [name]=N'IX_ShopExpenseCheques_SourceChequeId') EXEC(N'CREATE UNIQUE INDEX [IX_ShopExpenseCheques_SourceChequeId] ON [dbo].[ShopExpenseCheques] ([SourceChequeId]) WHERE [SourceChequeId] IS NOT NULL AND [State] <> 3');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopExpenseCheques') AND [name]=N'IX_ShopExpenseCheques_SourceTransactionId') EXEC(N'CREATE INDEX [IX_ShopExpenseCheques_SourceTransactionId] ON [dbo].[ShopExpenseCheques] ([SourceTransactionId])');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopExpenseCheques') AND [name]=N'IX_ShopExpenseCheques_IssuingAccountId') EXEC(N'CREATE INDEX [IX_ShopExpenseCheques_IssuingAccountId] ON [dbo].[ShopExpenseCheques] ([IssuingAccountId])');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopExpenseCheques') AND [name]=N'IX_ShopExpenseCheques_IssuingAccountId_Number') EXEC(N'CREATE UNIQUE INDEX [IX_ShopExpenseCheques_IssuingAccountId_Number] ON [dbo].[ShopExpenseCheques] ([IssuingAccountId],[Number]) WHERE [IssuingAccountId] IS NOT NULL');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id]=OBJECT_ID(N'dbo.ShopExpenseCheques') AND [name]=N'IX_ShopExpenseCheques_SettlementVoucherId') EXEC(N'CREATE UNIQUE INDEX [IX_ShopExpenseCheques_SettlementVoucherId] ON [dbo].[ShopExpenseCheques] ([SettlementVoucherId]) WHERE [SettlementVoucherId] IS NOT NULL');";
    public const string Sqlite = @"
CREATE TABLE IF NOT EXISTS [ShopExpenseCheques] (
 [Id] INTEGER NOT NULL CONSTRAINT [PK_ShopExpenseCheques] PRIMARY KEY AUTOINCREMENT,
 [CompanyId] INTEGER NULL, [OperationId] TEXT NOT NULL, [ExpenseId] INTEGER NOT NULL, [Kind] INTEGER NOT NULL, [State] INTEGER NOT NULL,
 [SourceChequeId] INTEGER NULL, [SourceTransactionId] INTEGER NULL, [IssuingAccountId] INTEGER NULL, [SettlementVoucherId] INTEGER NULL,
 [Number] TEXT NOT NULL, [BankName] TEXT NULL, [Recipient] TEXT NOT NULL, [Amount] TEXT NOT NULL, [DueDate] TEXT NOT NULL, [HandedAt] TEXT NOT NULL,
 [StatusOperationId] TEXT NULL, [SettledAt] TEXT NULL, [Reason] TEXT NULL, [CreatedBy] TEXT NULL, [CreatedAt] TEXT NOT NULL, [StatusBy] TEXT NULL, [StatusAt] TEXT NULL,
 CONSTRAINT [FK_ShopExpenseCheques_Expenses_ExpenseId] FOREIGN KEY ([ExpenseId]) REFERENCES [Expenses] ([Id]) ON DELETE RESTRICT,
 CONSTRAINT [FK_ShopExpenseCheques_Cheques_SourceChequeId] FOREIGN KEY ([SourceChequeId]) REFERENCES [Cheques] ([Id]) ON DELETE RESTRICT,
 CONSTRAINT [FK_ShopExpenseCheques_Transactions_SourceTransactionId] FOREIGN KEY ([SourceTransactionId]) REFERENCES [Transactions] ([Id]) ON DELETE RESTRICT,
 CONSTRAINT [FK_ShopExpenseCheques_TrsAccounts_IssuingAccountId] FOREIGN KEY ([IssuingAccountId]) REFERENCES [TrsAccounts] ([Id]) ON DELETE RESTRICT,
 CONSTRAINT [FK_ShopExpenseCheques_TrsVouchers_SettlementVoucherId] FOREIGN KEY ([SettlementVoucherId]) REFERENCES [TrsVouchers] ([Id]) ON DELETE RESTRICT
);
CREATE UNIQUE INDEX IF NOT EXISTS [IX_ShopExpenseCheques_OperationId] ON [ShopExpenseCheques] ([OperationId]);
CREATE UNIQUE INDEX IF NOT EXISTS [IX_ShopExpenseCheques_StatusOperationId] ON [ShopExpenseCheques] ([StatusOperationId]) WHERE [StatusOperationId] IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS [IX_ShopExpenseCheques_ExpenseId] ON [ShopExpenseCheques] ([ExpenseId]) WHERE [State] <> 3;
CREATE UNIQUE INDEX IF NOT EXISTS [IX_ShopExpenseCheques_SourceChequeId] ON [ShopExpenseCheques] ([SourceChequeId]) WHERE [SourceChequeId] IS NOT NULL AND [State] <> 3;
CREATE INDEX IF NOT EXISTS [IX_ShopExpenseCheques_SourceTransactionId] ON [ShopExpenseCheques] ([SourceTransactionId]);
CREATE INDEX IF NOT EXISTS [IX_ShopExpenseCheques_IssuingAccountId] ON [ShopExpenseCheques] ([IssuingAccountId]);
CREATE UNIQUE INDEX IF NOT EXISTS [IX_ShopExpenseCheques_IssuingAccountId_Number] ON [ShopExpenseCheques] ([IssuingAccountId],[Number]) WHERE [IssuingAccountId] IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS [IX_ShopExpenseCheques_SettlementVoucherId] ON [ShopExpenseCheques] ([SettlementVoucherId]) WHERE [SettlementVoucherId] IS NOT NULL;";
    public static Task EnsureAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync(db.Database.IsSqlite() ? Sqlite : SqlServer);
}
