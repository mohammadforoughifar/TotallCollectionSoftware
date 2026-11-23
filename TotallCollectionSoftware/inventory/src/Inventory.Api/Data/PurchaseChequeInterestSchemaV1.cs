using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// ساخت افزایشی جدول «سود چک خرید». روی دیتابیس موجود فقط جدول تازه ساخته
/// می‌شود و هیچ سند یا مبلغی تغییر نمی‌کند؛ فاکتورهای قدیمی یعنی سود صفر.
/// </summary>
public static class PurchaseChequeInterestSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.Transactions', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.PurchaseChequeInterests', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PurchaseChequeInterests] (
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_PurchaseChequeInterests] PRIMARY KEY,
        [CompanyId] int NULL,
        [TransactionId] int NOT NULL,
        [BaseAmount] decimal(18,2) NOT NULL,
        [RatePercent] decimal(18,6) NOT NULL,
        [Months] int NOT NULL,
        [CalculatorBaseAmount] decimal(18,2) NOT NULL,
        [CalculatorRatePercent] decimal(18,6) NOT NULL,
        [CalculatorMonths] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(120) NULL,
        CONSTRAINT [FK_PurchaseChequeInterests_Transactions_TransactionId] FOREIGN KEY ([TransactionId]) REFERENCES [dbo].[Transactions] ([Id]) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX [IX_PurchaseChequeInterests_TransactionId] ON [dbo].[PurchaseChequeInterests] ([TransactionId]);
END";

    public const string Sqlite = @"
CREATE TABLE IF NOT EXISTS ""PurchaseChequeInterests"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_PurchaseChequeInterests"" PRIMARY KEY AUTOINCREMENT,
    ""CompanyId"" INTEGER NULL,
    ""TransactionId"" INTEGER NOT NULL,
    ""BaseAmount"" TEXT NOT NULL,
    ""RatePercent"" TEXT NOT NULL,
    ""Months"" INTEGER NOT NULL,
    ""CalculatorBaseAmount"" TEXT NOT NULL,
    ""CalculatorRatePercent"" TEXT NOT NULL,
    ""CalculatorMonths"" INTEGER NOT NULL,
    ""Amount"" TEXT NOT NULL,
    ""CreatedAt"" TEXT NOT NULL,
    ""CreatedBy"" TEXT NULL,
    CONSTRAINT ""FK_PurchaseChequeInterests_Transactions_TransactionId"" FOREIGN KEY (""TransactionId"") REFERENCES ""Transactions"" (""Id"") ON DELETE CASCADE
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PurchaseChequeInterests_TransactionId"" ON ""PurchaseChequeInterests"" (""TransactionId"");";

    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.ExecuteSqlRawAsync(db.Database.IsSqlite() ? Sqlite : SqlServer);
}
