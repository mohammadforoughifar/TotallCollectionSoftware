using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>جدول پیگیری تغییر سررسید، شامل تغییراتی که به علت گردکردن سند ندارند.</summary>
public static class SaleChequeRescheduleSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.Cheques', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.SaleChequeCharges', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.SaleChequeChargeEvents', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.SaleChequeReschedules', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SaleChequeReschedules] (
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SaleChequeReschedules] PRIMARY KEY,
        [CompanyId] int NULL,
        [OperationId] uniqueidentifier NOT NULL,
        [ChequeId] int NOT NULL,
        [SaleChequeChargeId] int NOT NULL,
        [SaleChequeChargeEventId] int NULL,
        [PreviousDueDate] datetime2 NOT NULL,
        [NewDueDate] datetime2 NOT NULL,
        [Date] datetime2 NOT NULL,
        [BaseAmount] decimal(18,2) NOT NULL,
        [RatePercent] decimal(18,6) NOT NULL,
        [PeriodDays] int NOT NULL,
        [IncomeDelta] decimal(18,2) NOT NULL,
        [CreatedBy] nvarchar(120) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [FK_SaleChequeReschedules_Cheques] FOREIGN KEY ([ChequeId]) REFERENCES [dbo].[Cheques] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeReschedules_Charge] FOREIGN KEY ([SaleChequeChargeId]) REFERENCES [dbo].[SaleChequeCharges] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_SaleChequeReschedules_Event] FOREIGN KEY ([SaleChequeChargeEventId]) REFERENCES [dbo].[SaleChequeChargeEvents] ([Id]) ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX [IX_SaleChequeReschedules_OperationId] ON [dbo].[SaleChequeReschedules] ([OperationId]);
    CREATE UNIQUE INDEX [IX_SaleChequeReschedules_EventId] ON [dbo].[SaleChequeReschedules] ([SaleChequeChargeEventId]) WHERE [SaleChequeChargeEventId] IS NOT NULL;
    CREATE INDEX [IX_SaleChequeReschedules_ChequeId] ON [dbo].[SaleChequeReschedules] ([ChequeId]);
    CREATE INDEX [IX_SaleChequeReschedules_ChargeId] ON [dbo].[SaleChequeReschedules] ([SaleChequeChargeId]);
END";

    public const string Sqlite = @"
CREATE TABLE IF NOT EXISTS ""SaleChequeReschedules"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_SaleChequeReschedules"" PRIMARY KEY AUTOINCREMENT,
    ""CompanyId"" INTEGER NULL,
    ""OperationId"" TEXT NOT NULL,
    ""ChequeId"" INTEGER NOT NULL,
    ""SaleChequeChargeId"" INTEGER NOT NULL,
    ""SaleChequeChargeEventId"" INTEGER NULL,
    ""PreviousDueDate"" TEXT NOT NULL,
    ""NewDueDate"" TEXT NOT NULL,
    ""Date"" TEXT NOT NULL,
    ""BaseAmount"" TEXT NOT NULL,
    ""RatePercent"" TEXT NOT NULL,
    ""PeriodDays"" INTEGER NOT NULL,
    ""IncomeDelta"" TEXT NOT NULL,
    ""CreatedBy"" TEXT NULL,
    ""CreatedAt"" TEXT NOT NULL,
    CONSTRAINT ""FK_SaleChequeReschedules_Cheques"" FOREIGN KEY (""ChequeId"") REFERENCES ""Cheques"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeReschedules_Charge"" FOREIGN KEY (""SaleChequeChargeId"") REFERENCES ""SaleChequeCharges"" (""Id"") ON DELETE RESTRICT,
    CONSTRAINT ""FK_SaleChequeReschedules_Event"" FOREIGN KEY (""SaleChequeChargeEventId"") REFERENCES ""SaleChequeChargeEvents"" (""Id"") ON DELETE RESTRICT
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeReschedules_OperationId"" ON ""SaleChequeReschedules"" (""OperationId"");
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeReschedules_EventId"" ON ""SaleChequeReschedules"" (""SaleChequeChargeEventId"") WHERE ""SaleChequeChargeEventId"" IS NOT NULL;
CREATE INDEX IF NOT EXISTS ""IX_SaleChequeReschedules_ChequeId"" ON ""SaleChequeReschedules"" (""ChequeId"");
CREATE INDEX IF NOT EXISTS ""IX_SaleChequeReschedules_ChargeId"" ON ""SaleChequeReschedules"" (""SaleChequeChargeId"");";

    public static Task EnsureAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync(db.Database.IsSqlite() ? Sqlite : SqlServer);
}
