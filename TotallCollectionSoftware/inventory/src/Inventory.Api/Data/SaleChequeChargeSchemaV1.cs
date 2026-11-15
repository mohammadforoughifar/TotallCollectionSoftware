using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>ساخت افزایشی جدول ردیف مالی، بدون تغییر اسناد موجود.</summary>
public static class SaleChequeChargeSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.Transactions', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.SaleChequeCharges', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SaleChequeCharges] (
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SaleChequeCharges] PRIMARY KEY,
        [CompanyId] int NULL,
        [TransactionId] int NOT NULL,
        [BaseAmount] decimal(18,2) NOT NULL,
        [RatePercent] decimal(18,6) NOT NULL,
        [PeriodDays] int NOT NULL,
        [InitialAmount] decimal(18,2) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [CustomerCredit] decimal(18,2) NOT NULL,
        CONSTRAINT [FK_SaleChequeCharges_Transactions_TransactionId] FOREIGN KEY ([TransactionId]) REFERENCES [dbo].[Transactions] ([Id]) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX [IX_SaleChequeCharges_TransactionId] ON [dbo].[SaleChequeCharges] ([TransactionId]);
END";

    public const string Sqlite = @"
CREATE TABLE IF NOT EXISTS ""SaleChequeCharges"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_SaleChequeCharges"" PRIMARY KEY AUTOINCREMENT,
    ""CompanyId"" INTEGER NULL,
    ""TransactionId"" INTEGER NOT NULL,
    ""BaseAmount"" TEXT NOT NULL,
    ""RatePercent"" TEXT NOT NULL,
    ""PeriodDays"" INTEGER NOT NULL,
    ""InitialAmount"" TEXT NOT NULL,
    ""Amount"" TEXT NOT NULL,
    ""CustomerCredit"" TEXT NOT NULL,
    CONSTRAINT ""FK_SaleChequeCharges_Transactions_TransactionId"" FOREIGN KEY (""TransactionId"") REFERENCES ""Transactions"" (""Id"") ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeCharges_TransactionId"" ON ""SaleChequeCharges"" (""TransactionId"");";

    public static Task EnsureAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync(db.Database.IsSqlite() ? Sqlite : SqlServer);
}
