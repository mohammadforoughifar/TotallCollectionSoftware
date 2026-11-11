using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>ثبت مستقل وصول واقعی چک فروش با پیوند حذف‌نشدنی به سند حسابداری.</summary>
public static class SaleChequeReceiptSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.Cheques', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Transactions', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.TrsAccounts', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.AccVouchers', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.SaleChequeReceipts', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SaleChequeReceipts] (
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SaleChequeReceipts] PRIMARY KEY,
        [CompanyId] int NULL, [OperationId] uniqueidentifier NOT NULL,
        [ChequeId] int NOT NULL, [TransactionId] int NOT NULL, [TrsAccountId] int NOT NULL,
        [BankAccountId] int NOT NULL, [PartyAccountId] int NOT NULL, [AccVoucherId] int NOT NULL,
        [Date] datetime2 NOT NULL, [Amount] decimal(18,2) NOT NULL,
        CONSTRAINT [FK_SaleChequeReceipts_Cheques] FOREIGN KEY ([ChequeId]) REFERENCES [dbo].[Cheques] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeReceipts_Transactions] FOREIGN KEY ([TransactionId]) REFERENCES [dbo].[Transactions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeReceipts_TrsAccounts] FOREIGN KEY ([TrsAccountId]) REFERENCES [dbo].[TrsAccounts] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeReceipts_AccVouchers] FOREIGN KEY ([AccVoucherId]) REFERENCES [dbo].[AccVouchers] ([Id]) ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX [IX_SaleChequeReceipts_OperationId] ON [dbo].[SaleChequeReceipts] ([OperationId]);
    CREATE UNIQUE INDEX [IX_SaleChequeReceipts_ChequeId] ON [dbo].[SaleChequeReceipts] ([ChequeId]);
    CREATE UNIQUE INDEX [IX_SaleChequeReceipts_AccVoucherId] ON [dbo].[SaleChequeReceipts] ([AccVoucherId]);
    CREATE INDEX [IX_SaleChequeReceipts_TransactionId] ON [dbo].[SaleChequeReceipts] ([TransactionId]);
    CREATE INDEX [IX_SaleChequeReceipts_TrsAccountId] ON [dbo].[SaleChequeReceipts] ([TrsAccountId]);
END";

    public const string Sqlite = @"
CREATE TABLE IF NOT EXISTS ""SaleChequeReceipts"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_SaleChequeReceipts"" PRIMARY KEY AUTOINCREMENT,
    ""CompanyId"" INTEGER NULL, ""OperationId"" TEXT NOT NULL,
    ""ChequeId"" INTEGER NOT NULL, ""TransactionId"" INTEGER NOT NULL, ""TrsAccountId"" INTEGER NOT NULL,
    ""BankAccountId"" INTEGER NOT NULL, ""PartyAccountId"" INTEGER NOT NULL, ""AccVoucherId"" INTEGER NOT NULL,
    ""Date"" TEXT NOT NULL, ""Amount"" TEXT NOT NULL,
    CONSTRAINT ""FK_SaleChequeReceipts_Cheques"" FOREIGN KEY (""ChequeId"") REFERENCES ""Cheques"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeReceipts_Transactions"" FOREIGN KEY (""TransactionId"") REFERENCES ""Transactions"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeReceipts_TrsAccounts"" FOREIGN KEY (""TrsAccountId"") REFERENCES ""TrsAccounts"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeReceipts_AccVouchers"" FOREIGN KEY (""AccVoucherId"") REFERENCES ""AccVouchers"" (""Id"") ON DELETE RESTRICT
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeReceipts_OperationId"" ON ""SaleChequeReceipts"" (""OperationId"");
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeReceipts_ChequeId"" ON ""SaleChequeReceipts"" (""ChequeId"");
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeReceipts_AccVoucherId"" ON ""SaleChequeReceipts"" (""AccVoucherId"");
CREATE INDEX IF NOT EXISTS ""IX_SaleChequeReceipts_TransactionId"" ON ""SaleChequeReceipts"" (""TransactionId"");
CREATE INDEX IF NOT EXISTS ""IX_SaleChequeReceipts_TrsAccountId"" ON ""SaleChequeReceipts"" (""TrsAccountId"");";

    public static Task EnsureAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync(db.Database.IsSqlite() ? Sqlite : SqlServer);
}
