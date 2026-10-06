using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// Idempotently creates the provider-scoped Moadian master-data tables for both
/// fresh databases and existing installations whose schema is maintained at startup.
/// Invoice, queue, and sender tables are deliberately not changed here.
/// </summary>
public static class MoadianSchemaV5
{
    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    /// <summary>
    /// بعد از حذف نسخه‌های تکراری اتصال (MoadianConnectionConsolidationV1)، ایندکس یکتای
    /// «هر خدمات‌دهنده حداکثر یک اتصال» (بدون شرط IsDeleted) جایگزین ایندکس قدیمیِ
    /// فقط-نسخه‌های-فعال می‌شود.
    /// </summary>
    public static Task EnsureSingleConnectionIndexAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSingleConnectionIndexSqliteAsync(db) : EnsureSingleConnectionIndexSqlServerAsync(db);

    private static Task EnsureSingleConnectionIndexSqliteAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync(@"\
DROP INDEX IF EXISTS [IX_MoadianProviderConnections_ServiceProviderId_Active];
CREATE UNIQUE INDEX IF NOT EXISTS [IX_MoadianProviderConnections_ServiceProviderId_Single]
    ON [MoadianProviderConnections] ([ServiceProviderId]);");

    private static Task EnsureSingleConnectionIndexSqlServerAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync(@"\
IF OBJECT_ID(N'dbo.MoadianProviderConnections', N'U') IS NOT NULL
AND EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MoadianProviderConnections_ServiceProviderId_Active' AND object_id = OBJECT_ID(N'dbo.MoadianProviderConnections'))
    DROP INDEX IX_MoadianProviderConnections_ServiceProviderId_Active ON dbo.MoadianProviderConnections;
IF OBJECT_ID(N'dbo.MoadianProviderConnections', N'U') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MoadianProviderConnections_ServiceProviderId_Single' AND object_id = OBJECT_ID(N'dbo.MoadianProviderConnections'))
    CREATE UNIQUE INDEX IX_MoadianProviderConnections_ServiceProviderId_Single
        ON dbo.MoadianProviderConnections(ServiceProviderId);");

    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS [MoadianServiceProviderProfiles] (
    [Id] INTEGER NOT NULL CONSTRAINT [PK_MoadianServiceProviderProfiles] PRIMARY KEY AUTOINCREMENT,
    [Type] TEXT NULL,
    [EconomicNumber] TEXT NULL,
    [PersianName] TEXT NULL,
    [EnglishName] TEXT NULL,
    [NationalID] TEXT NOT NULL,
    [PostalCode] TEXT NULL,
    [IsDeleted] INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS [MoadianProviderConnections] (
    [Id] INTEGER NOT NULL CONSTRAINT [PK_MoadianProviderConnections] PRIMARY KEY AUTOINCREMENT,
    [ServiceProviderId] INTEGER NOT NULL,
    [WebServiceAddress] TEXT NOT NULL,
    [TaxMemoryID] TEXT NOT NULL,
    [PrivateKeyPath] TEXT NOT NULL,
    [IsDeleted] INTEGER NOT NULL DEFAULT 0,
    [LastConnectionTestAt] TEXT NULL,
    [LastConnectionTestSucceeded] INTEGER NULL,
    [LastConnectionTestMessage] TEXT NULL,
    CONSTRAINT [FK_MoadianProviderConnections_MoadianServiceProviderProfiles_ServiceProviderId]
        FOREIGN KEY ([ServiceProviderId]) REFERENCES [MoadianServiceProviderProfiles] ([Id]) ON DELETE RESTRICT
);
CREATE TABLE IF NOT EXISTS [MoadianCustomerProfiles] (
    [Id] INTEGER NOT NULL CONSTRAINT [PK_MoadianCustomerProfiles] PRIMARY KEY AUTOINCREMENT,
    [ServiceProviderId] INTEGER NOT NULL,
    [Type] TEXT NULL,
    [Name] TEXT NULL,
    [NationalID] TEXT NULL,
    [EconomicNumber] TEXT NULL,
    [Phone] TEXT NULL,
    [PostalCode] TEXT NULL,
    [Address] TEXT NULL,
    [IsDeleted] INTEGER NOT NULL DEFAULT 0,
    CONSTRAINT [FK_MoadianCustomerProfiles_MoadianServiceProviderProfiles_ServiceProviderId]
        FOREIGN KEY ([ServiceProviderId]) REFERENCES [MoadianServiceProviderProfiles] ([Id]) ON DELETE RESTRICT
);
CREATE TABLE IF NOT EXISTS [MoadianGoodsOrServices] (
    [Id] INTEGER NOT NULL CONSTRAINT [PK_MoadianGoodsOrServices] PRIMARY KEY AUTOINCREMENT,
    [ServiceProviderId] INTEGER NOT NULL,
    [Name] TEXT NULL,
    [UniqueIdentifier] TEXT NULL,
    [UnitOfMeasurement] TEXT NOT NULL,
    [ValueAddedPercentage] INTEGER NOT NULL,
    [Price] TEXT NOT NULL,
    [IsDeleted] INTEGER NOT NULL DEFAULT 0,
    CONSTRAINT [FK_MoadianGoodsOrServices_MoadianServiceProviderProfiles_ServiceProviderId]
        FOREIGN KEY ([ServiceProviderId]) REFERENCES [MoadianServiceProviderProfiles] ([Id]) ON DELETE RESTRICT
);
CREATE TABLE IF NOT EXISTS [MoadianUnitsOfMeasurement] (
    [Id] INTEGER NOT NULL CONSTRAINT [PK_MoadianUnitsOfMeasurement] PRIMARY KEY AUTOINCREMENT,
    [Code] TEXT NOT NULL,
    [Name] TEXT NOT NULL,
    [IsDeleted] INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS [IX_MoadianServiceProviderProfiles_NationalID]
    ON [MoadianServiceProviderProfiles] ([NationalID]);
CREATE INDEX IF NOT EXISTS [IX_MoadianCustomerProfiles_ServiceProviderId_IsDeleted]
    ON [MoadianCustomerProfiles] ([ServiceProviderId], [IsDeleted]);
CREATE INDEX IF NOT EXISTS [IX_MoadianGoodsOrServices_ServiceProviderId_IsDeleted]
    ON [MoadianGoodsOrServices] ([ServiceProviderId], [IsDeleted]);
CREATE UNIQUE INDEX IF NOT EXISTS [IX_MoadianUnitsOfMeasurement_Code]
    ON [MoadianUnitsOfMeasurement] ([Code]);");

        var connection = db.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection) await connection.OpenAsync();
        try
        {
            // ایندکس قدیمی «فقط نسخه‌های فعال» فقط زمانی ساخته می‌شود که ایندکس جدیدِ
            // «یک اتصال به ازای هر خدمات‌دهنده» هنوز نساخته شده باشد؛ در غیر این صورت
            // هر راه‌اندازی دوباره آن را می‌ساخت و مرحلهٔ بعدی حذفش می‌کرد.
            bool singleIndexExists;
            await using (var indexCheck = connection.CreateCommand())
            {
                indexCheck.CommandText =
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_MoadianProviderConnections_ServiceProviderId_Single';";
                singleIndexExists = Convert.ToInt32(await indexCheck.ExecuteScalarAsync()) > 0;
            }
            if (!singleIndexExists)
            {
                await db.Database.ExecuteSqlRawAsync(@"\
DROP INDEX IF EXISTS [IX_MoadianProviderConnections_ServiceProviderId];
CREATE UNIQUE INDEX IF NOT EXISTS [IX_MoadianProviderConnections_ServiceProviderId_Active]
    ON [MoadianProviderConnections] ([ServiceProviderId]) WHERE [IsDeleted] = 0;");
            }

            var obsoleteColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ApiVersion", "ClientType", "ClientId", "KeyId"
            };
            var existingObsoleteColumns = new List<string>();
            var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info('MoadianProviderConnections');";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var columnName = reader.GetString(1);
                    existingColumns.Add(columnName);
                    if (obsoleteColumns.Remove(columnName)) existingObsoleteColumns.Add(columnName);
                }
            }

            foreach (var columnName in existingObsoleteColumns)
                await db.Database.ExecuteSqlRawAsync($"ALTER TABLE [MoadianProviderConnections] DROP COLUMN [{columnName}];");

            // ستون‌های نتیجهٔ تست اتصال برای نصب‌های قدیمی‌تر که جدول را از قبل دارند.
            if (!existingColumns.Contains("LastConnectionTestAt"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE [MoadianProviderConnections] ADD COLUMN [LastConnectionTestAt] TEXT NULL;");
            if (!existingColumns.Contains("LastConnectionTestSucceeded"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE [MoadianProviderConnections] ADD COLUMN [LastConnectionTestSucceeded] INTEGER NULL;");
            if (!existingColumns.Contains("LastConnectionTestMessage"))
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE [MoadianProviderConnections] ADD COLUMN [LastConnectionTestMessage] TEXT NULL;");
        }
        finally
        {
            if (closeConnection) await connection.CloseAsync();
        }
    }

    private static Task EnsureSqlServerAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.MoadianServiceProviderProfiles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MoadianServiceProviderProfiles (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_MoadianServiceProviderProfiles PRIMARY KEY,
        Type nvarchar(50) NULL,
        EconomicNumber nvarchar(50) NULL,
        PersianName nvarchar(50) NULL,
        EnglishName nvarchar(50) NULL,
        NationalID nvarchar(50) NOT NULL,
        PostalCode nvarchar(50) NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_MoadianServiceProviderProfiles_IsDeleted DEFAULT(0)
    );
END;
IF OBJECT_ID(N'dbo.MoadianProviderConnections', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MoadianProviderConnections (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_MoadianProviderConnections PRIMARY KEY,
        ServiceProviderId int NOT NULL,
        WebServiceAddress nvarchar(2048) NOT NULL,
        TaxMemoryID nvarchar(50) NOT NULL,
        PrivateKeyPath nvarchar(2048) NOT NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_MoadianProviderConnections_IsDeleted DEFAULT(0),
        LastConnectionTestAt datetime2 NULL,
        LastConnectionTestSucceeded bit NULL,
        LastConnectionTestMessage nvarchar(500) NULL,
        CONSTRAINT FK_MoadianProviderConnections_MoadianServiceProviderProfiles_ServiceProviderId
            FOREIGN KEY (ServiceProviderId) REFERENCES dbo.MoadianServiceProviderProfiles(Id)
    );
END;
IF OBJECT_ID(N'dbo.MoadianCustomerProfiles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MoadianCustomerProfiles (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_MoadianCustomerProfiles PRIMARY KEY,
        ServiceProviderId int NOT NULL,
        Type nvarchar(50) NULL,
        Name nvarchar(50) NULL,
        NationalID nvarchar(50) NULL,
        EconomicNumber nvarchar(50) NULL,
        Phone nvarchar(50) NULL,
        PostalCode nvarchar(50) NULL,
        Address nvarchar(max) NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_MoadianCustomerProfiles_IsDeleted DEFAULT(0),
        CONSTRAINT FK_MoadianCustomerProfiles_MoadianServiceProviderProfiles_ServiceProviderId
            FOREIGN KEY (ServiceProviderId) REFERENCES dbo.MoadianServiceProviderProfiles(Id)
    );
END;
IF OBJECT_ID(N'dbo.MoadianGoodsOrServices', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MoadianGoodsOrServices (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_MoadianGoodsOrServices PRIMARY KEY,
        ServiceProviderId int NOT NULL,
        Name nvarchar(50) NULL,
        UniqueIdentifier nvarchar(50) NULL,
        UnitOfMeasurement nvarchar(50) NOT NULL,
        ValueAddedPercentage tinyint NOT NULL,
        Price decimal(18,2) NOT NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_MoadianGoodsOrServices_IsDeleted DEFAULT(0),
        CONSTRAINT FK_MoadianGoodsOrServices_MoadianServiceProviderProfiles_ServiceProviderId
            FOREIGN KEY (ServiceProviderId) REFERENCES dbo.MoadianServiceProviderProfiles(Id)
    );
END;
IF OBJECT_ID(N'dbo.MoadianUnitsOfMeasurement', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MoadianUnitsOfMeasurement (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_MoadianUnitsOfMeasurement PRIMARY KEY,
        Code nvarchar(50) NOT NULL,
        Name nvarchar(50) NOT NULL,
        IsDeleted bit NOT NULL CONSTRAINT DF_MoadianUnitsOfMeasurement_IsDeleted DEFAULT(0)
    );
END;
IF OBJECT_ID(N'dbo.MoadianProviderConnections', N'U') IS NOT NULL
AND EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MoadianProviderConnections_ServiceProviderId' AND object_id = OBJECT_ID(N'dbo.MoadianProviderConnections'))
    DROP INDEX IX_MoadianProviderConnections_ServiceProviderId ON dbo.MoadianProviderConnections;
IF OBJECT_ID(N'dbo.MoadianProviderConnections', N'U') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MoadianProviderConnections_ServiceProviderId_Single' AND object_id = OBJECT_ID(N'dbo.MoadianProviderConnections'))
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MoadianProviderConnections_ServiceProviderId_Active' AND object_id = OBJECT_ID(N'dbo.MoadianProviderConnections'))
    CREATE UNIQUE INDEX IX_MoadianProviderConnections_ServiceProviderId_Active
        ON dbo.MoadianProviderConnections(ServiceProviderId) WHERE IsDeleted = 0;
IF OBJECT_ID(N'dbo.MoadianServiceProviderProfiles', N'U') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MoadianServiceProviderProfiles_NationalID' AND object_id = OBJECT_ID(N'dbo.MoadianServiceProviderProfiles'))
    CREATE INDEX IX_MoadianServiceProviderProfiles_NationalID ON dbo.MoadianServiceProviderProfiles(NationalID);
IF OBJECT_ID(N'dbo.MoadianCustomerProfiles', N'U') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MoadianCustomerProfiles_ServiceProviderId_IsDeleted' AND object_id = OBJECT_ID(N'dbo.MoadianCustomerProfiles'))
    CREATE INDEX IX_MoadianCustomerProfiles_ServiceProviderId_IsDeleted ON dbo.MoadianCustomerProfiles(ServiceProviderId, IsDeleted);
IF OBJECT_ID(N'dbo.MoadianGoodsOrServices', N'U') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MoadianGoodsOrServices_ServiceProviderId_IsDeleted' AND object_id = OBJECT_ID(N'dbo.MoadianGoodsOrServices'))
    CREATE INDEX IX_MoadianGoodsOrServices_ServiceProviderId_IsDeleted ON dbo.MoadianGoodsOrServices(ServiceProviderId, IsDeleted);
IF OBJECT_ID(N'dbo.MoadianUnitsOfMeasurement', N'U') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MoadianUnitsOfMeasurement_Code' AND object_id = OBJECT_ID(N'dbo.MoadianUnitsOfMeasurement'))
    CREATE UNIQUE INDEX IX_MoadianUnitsOfMeasurement_Code ON dbo.MoadianUnitsOfMeasurement(Code);
IF COL_LENGTH(N'dbo.MoadianProviderConnections', N'ApiVersion') IS NOT NULL
    ALTER TABLE dbo.MoadianProviderConnections DROP COLUMN ApiVersion;
IF COL_LENGTH(N'dbo.MoadianProviderConnections', N'ClientType') IS NOT NULL
    ALTER TABLE dbo.MoadianProviderConnections DROP COLUMN ClientType;
IF COL_LENGTH(N'dbo.MoadianProviderConnections', N'ClientId') IS NOT NULL
    ALTER TABLE dbo.MoadianProviderConnections DROP COLUMN ClientId;
IF COL_LENGTH(N'dbo.MoadianProviderConnections', N'KeyId') IS NOT NULL
    ALTER TABLE dbo.MoadianProviderConnections DROP COLUMN KeyId;
IF COL_LENGTH(N'dbo.MoadianProviderConnections', N'LastConnectionTestAt') IS NULL
    ALTER TABLE dbo.MoadianProviderConnections ADD LastConnectionTestAt datetime2 NULL;
IF COL_LENGTH(N'dbo.MoadianProviderConnections', N'LastConnectionTestSucceeded') IS NULL
    ALTER TABLE dbo.MoadianProviderConnections ADD LastConnectionTestSucceeded bit NULL;
IF COL_LENGTH(N'dbo.MoadianProviderConnections', N'LastConnectionTestMessage') IS NULL
    ALTER TABLE dbo.MoadianProviderConnections ADD LastConnectionTestMessage nvarchar(500) NULL;");
}
