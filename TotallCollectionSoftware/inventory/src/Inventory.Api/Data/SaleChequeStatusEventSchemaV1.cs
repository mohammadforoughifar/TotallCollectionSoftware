using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>تاریخچه وضعیت چک‌های فروش مشمول تاخیر تادیه چک.</summary>
public static class SaleChequeStatusEventSchemaV1
{
    public const string SqlServer = @"
IF OBJECT_ID(N'dbo.Cheques', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.SaleChequeStatusEvents', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SaleChequeStatusEvents] (
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SaleChequeStatusEvents] PRIMARY KEY,
        [CompanyId] int NULL, [OperationId] uniqueidentifier NOT NULL, [ChequeId] int NOT NULL,
        [PreviousStatus] int NOT NULL, [NewStatus] int NOT NULL, [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(120) NULL, [Reason] nvarchar(200) NULL,
        CONSTRAINT [FK_SaleChequeStatusEvents_Cheques] FOREIGN KEY ([ChequeId]) REFERENCES [dbo].[Cheques] ([Id]) ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX [IX_SaleChequeStatusEvents_OperationId] ON [dbo].[SaleChequeStatusEvents] ([OperationId]);
    CREATE INDEX [IX_SaleChequeStatusEvents_ChequeId] ON [dbo].[SaleChequeStatusEvents] ([ChequeId]);
END";

    public const string Sqlite = @"
CREATE TABLE IF NOT EXISTS ""SaleChequeStatusEvents"" (
    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_SaleChequeStatusEvents"" PRIMARY KEY AUTOINCREMENT,
    ""CompanyId"" INTEGER NULL, ""OperationId"" TEXT NOT NULL, ""ChequeId"" INTEGER NOT NULL,
    ""PreviousStatus"" INTEGER NOT NULL, ""NewStatus"" INTEGER NOT NULL, ""CreatedAt"" TEXT NOT NULL,
    ""CreatedBy"" TEXT NULL, ""Reason"" TEXT NULL,
    CONSTRAINT ""FK_SaleChequeStatusEvents_Cheques"" FOREIGN KEY (""ChequeId"") REFERENCES ""Cheques"" (""Id"") ON DELETE RESTRICT
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SaleChequeStatusEvents_OperationId"" ON ""SaleChequeStatusEvents"" (""OperationId"");
CREATE INDEX IF NOT EXISTS ""IX_SaleChequeStatusEvents_ChequeId"" ON ""SaleChequeStatusEvents"" (""ChequeId"");";

    public static Task EnsureAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync(db.Database.IsSqlite() ? Sqlite : SqlServer);
}
