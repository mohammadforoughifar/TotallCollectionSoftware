using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>جدول دریافت و لغو دریافت قسط فاکتورهای دارای تاخیر تادیه چک.</summary>
public static class SaleChequeInstallmentReceiptSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.Installments', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Transactions', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.TrsAccounts', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.AccVouchers', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.SaleChequeInstallmentReceipts', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SaleChequeInstallmentReceipts] (
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SaleChequeInstallmentReceipts] PRIMARY KEY,
        [CompanyId] int NULL, [OperationId] uniqueidentifier NOT NULL,
        [InstallmentId] int NOT NULL, [TransactionId] int NOT NULL, [TrsAccountId] int NOT NULL,
        [BankAccountId] int NOT NULL, [PartyAccountId] int NOT NULL, [AccVoucherId] int NOT NULL,
        [Date] datetime2 NOT NULL, [Amount] decimal(18,2) NOT NULL,
        [ReversalOperationId] uniqueidentifier NULL, [ReversalVoucherId] int NULL, [ReversalDate] datetime2 NULL,
        CONSTRAINT [FK_SaleChequeInstReceipts_Installment] FOREIGN KEY ([InstallmentId]) REFERENCES [dbo].[Installments] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeInstReceipts_Transaction] FOREIGN KEY ([TransactionId]) REFERENCES [dbo].[Transactions] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeInstReceipts_TrsAccount] FOREIGN KEY ([TrsAccountId]) REFERENCES [dbo].[TrsAccounts] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeInstReceipts_Voucher] FOREIGN KEY ([AccVoucherId]) REFERENCES [dbo].[AccVouchers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeInstReceipts_RevVoucher] FOREIGN KEY ([ReversalVoucherId]) REFERENCES [dbo].[AccVouchers] ([Id]) ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX [IX_SaleChequeInstReceipts_OperationId] ON [dbo].[SaleChequeInstallmentReceipts] ([OperationId]);
    CREATE UNIQUE INDEX [IX_SaleChequeInstReceipts_ActiveInstallment] ON [dbo].[SaleChequeInstallmentReceipts] ([InstallmentId]) WHERE [ReversalVoucherId] IS NULL;
    CREATE UNIQUE INDEX [IX_SaleChequeInstReceipts_AccVoucherId] ON [dbo].[SaleChequeInstallmentReceipts] ([AccVoucherId]);
    CREATE UNIQUE INDEX [IX_SaleChequeInstReceipts_ReversalOperationId] ON [dbo].[SaleChequeInstallmentReceipts] ([ReversalOperationId]) WHERE [ReversalOperationId] IS NOT NULL;
    CREATE UNIQUE INDEX [IX_SaleChequeInstReceipts_ReversalVoucherId] ON [dbo].[SaleChequeInstallmentReceipts] ([ReversalVoucherId]) WHERE [ReversalVoucherId] IS NOT NULL;
    CREATE INDEX [IX_SaleChequeInstReceipts_TransactionId] ON [dbo].[SaleChequeInstallmentReceipts] ([TransactionId]);
END";

    public const string Sqlite = @"
CREATE TABLE IF NOT EXISTS ""SaleChequeInstallmentReceipts"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_SaleChequeInstallmentReceipts"" PRIMARY KEY AUTOINCREMENT,
    ""CompanyId"" INTEGER NULL, ""OperationId"" TEXT NOT NULL,
    ""InstallmentId"" INTEGER NOT NULL, ""TransactionId"" INTEGER NOT NULL, ""TrsAccountId"" INTEGER NOT NULL,
    ""BankAccountId"" INTEGER NOT NULL, ""PartyAccountId"" INTEGER NOT NULL, ""AccVoucherId"" INTEGER NOT NULL,
    ""Date"" TEXT NOT NULL, ""Amount"" TEXT NOT NULL,
    ""ReversalOperationId"" TEXT NULL, ""ReversalVoucherId"" INTEGER NULL, ""ReversalDate"" TEXT NULL,
    CONSTRAINT ""FK_SaleChequeInstReceipts_Installment"" FOREIGN KEY (""InstallmentId"") REFERENCES ""Installments"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeInstReceipts_Transaction"" FOREIGN KEY (""TransactionId"") REFERENCES ""Transactions"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeInstReceipts_TrsAccount"" FOREIGN KEY (""TrsAccountId"") REFERENCES ""TrsAccounts"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeInstReceipts_Voucher"" FOREIGN KEY (""AccVoucherId"") REFERENCES ""AccVouchers"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeInstReceipts_RevVoucher"" FOREIGN KEY (""ReversalVoucherId"") REFERENCES ""AccVouchers"" (""Id"") ON DELETE RESTRICT
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeInstReceipts_OperationId"" ON ""SaleChequeInstallmentReceipts"" (""OperationId"");
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeInstReceipts_ActiveInstallment"" ON ""SaleChequeInstallmentReceipts"" (""InstallmentId"") WHERE ""ReversalVoucherId"" IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeInstReceipts_AccVoucherId"" ON ""SaleChequeInstallmentReceipts"" (""AccVoucherId"");
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeInstReceipts_ReversalOperationId"" ON ""SaleChequeInstallmentReceipts"" (""ReversalOperationId"") WHERE ""ReversalOperationId"" IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeInstReceipts_ReversalVoucherId"" ON ""SaleChequeInstallmentReceipts"" (""ReversalVoucherId"") WHERE ""ReversalVoucherId"" IS NOT NULL;
CREATE INDEX IF NOT EXISTS ""IX_SaleChequeInstReceipts_TransactionId"" ON ""SaleChequeInstallmentReceipts"" (""TransactionId"");";

    public static Task EnsureAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync(db.Database.IsSqlite() ? Sqlite : SqlServer);
}
