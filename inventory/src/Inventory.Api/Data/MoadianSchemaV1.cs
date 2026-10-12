using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  خودتعمیرِ اسکیمای «سامانه مودیان (نسخه 0.0.27)» — نسخه ۱
//  چرا لازم است؟
//  مایگریشن AddMoadianV1 (20261011080837) روی دیتابیس‌هایی که ستونی مثل
//  Users.Email را از قبل داشتند با خطای «duplicate» شکست می‌خورد؛
//  MigrateSqlServer آن مایگریشن را «stamp و رد» می‌کند و در نتیجه جدول‌های
//  مودیان (CPInfos و بقیه) هرگز ساخته نمی‌شوند. سرور در SaveCpInfo با
//  «Invalid object name 'CPInfos'» می‌افتد. این خودتعمیر — مانند بقیهٔ
//  *SchemaV1* ها — جدول‌ها را idempotent می‌سازد؛ روی دیتابیسِ جدید که
//  مایگریشن کامل اجرا شده هیچ کاری انجام نمی‌دهد.
// ============================================================
public static class MoadianSchemaV1
{
    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    // ==================== SQL Server ====================
    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        // ترتیب ساخت مهم است: جداول مرجع اول، سپس Invoices و InvoiceItems
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.CPInfos', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CPInfos](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [PersianName] nvarchar(max) NULL,
        [Nationalid] nvarchar(max) NULL,
        [EconomicNumber] nvarchar(max) NULL
    );
END");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.ConnectInfos', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ConnectInfos](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [CpInfo] int NOT NULL,
        [TaxMemoryID] nvarchar(max) NULL,
        [PrivateKeyPath] nvarchar(max) NULL,
        [WebServiceAddress] nvarchar(max) NULL,
        [IsDelete] bit NOT NULL DEFAULT(0)
    );
END");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Customers](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [CpInfoId] int NULL,
        [Name] nvarchar(max) NULL,
        [PostalCode] nvarchar(max) NULL,
        [Phone] nvarchar(max) NULL,
        [NationalID] nvarchar(max) NULL,
        [Type] nvarchar(max) NULL,
        [EconomicNumber] nvarchar(max) NULL,
        [Address] nvarchar(max) NULL
    );
END");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.UnitOfMeasurements', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[UnitOfMeasurements](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [Name] nvarchar(max) NULL,
        [Code] nvarchar(max) NULL,
        [IsDelete] bit NOT NULL DEFAULT(0)
    );
END");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.GoodsOrServices', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[GoodsOrServices](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [CpInfo] int NOT NULL,
        [Name] nvarchar(max) NULL,
        [UniqueIdentifier] nvarchar(max) NULL,
        [UnitOfMeasurement] nvarchar(max) NULL,
        [ValueAddedPercentage] tinyint NOT NULL DEFAULT(0),
        [Price] decimal(18,2) NOT NULL DEFAULT(0)
    );
END");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.FiscalYears', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[FiscalYears](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [CpInfo] int NULL,
        [StartDate] datetime2 NOT NULL,
        [EndDate] datetime2 NOT NULL
    );
END");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.Invoices', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Invoices](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [CustomerId] int NULL,
        [CpInfo] int NULL,
        [InvoiceNumber] int NULL,
        [FiscalYearId] int NULL,
        [DateOrder] date NULL,
        [TypePattern] int NOT NULL DEFAULT(0),
        [PaymentType] nvarchar(max) NULL,
        [PriceNaghd] decimal(18,2) NULL,
        [PriceNesye] decimal(18,2) NULL,
        [Description] nvarchar(max) NULL,
        [StatusSentToSystem] nvarchar(max) NULL,
        [ShowButtons] bit NOT NULL DEFAULT(0),
        [TaxNumber] nvarchar(max) NULL,
        [NewTaxNumber] nvarchar(max) NULL,
        [ReferenceId] nvarchar(max) NULL,
        [Subject] nvarchar(max) NULL,
        [Name] nvarchar(max) NULL,
        [IsDelete] bit NOT NULL DEFAULT(0)
    );
    CREATE INDEX [IX_Invoices_FiscalYearId] ON [dbo].[Invoices] ([FiscalYearId]);
    ALTER TABLE [dbo].[Invoices] ADD CONSTRAINT [FK_Invoices_FiscalYears_FiscalYearId]
        FOREIGN KEY ([FiscalYearId]) REFERENCES [dbo].[FiscalYears] ([Id]);
END");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.InvoiceItems', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[InvoiceItems](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [InvoiceId] int NOT NULL,
        [GoodsOrServicesId] int NOT NULL,
        [UniqueIdentifier] nvarchar(max) NULL,
        [Count] decimal(18,2) NOT NULL DEFAULT(0),
        [PriceUnit] decimal(18,2) NOT NULL DEFAULT(0),
        [ValueAddedPercentage] int NOT NULL DEFAULT(0),
        [Discount] decimal(18,2) NOT NULL DEFAULT(0),
        [GoodsOrServicesDescripton] nvarchar(max) NULL,
        [IsDelete] bit NOT NULL DEFAULT(0)
    );
    CREATE INDEX [IX_InvoiceItems_InvoiceId] ON [dbo].[InvoiceItems] ([InvoiceId]);
    ALTER TABLE [dbo].[InvoiceItems] ADD CONSTRAINT [FK_InvoiceItems_Invoices_InvoiceId]
        FOREIGN KEY ([InvoiceId]) REFERENCES [dbo].[Invoices] ([Id]) ON DELETE CASCADE;
END");
    }

    // ==================== SQLite ====================
    private static async Task EnsureSqliteAsync(AppDbContext db)
    {
        // EnsureCreated روی دیتابیس تازه همهٔ جدول‌ها را از مدل می‌سازد؛ این
        // خودتعمیر فقط برای دیتابیس‌های SQLite قدیمی است که جدول را ندارند.
        var hasTable = false;
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CPInfos';";
            hasTable = Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0L) > 0;
        }
        catch { /* EnsureCreated خودش جدول را ساخته است */ }

        if (hasTable) return;

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS CPInfos (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    PersianName TEXT NULL,
    Nationalid TEXT NULL,
    EconomicNumber TEXT NULL
);");
        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS ConnectInfos (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CpInfo INTEGER NOT NULL DEFAULT(0),
    TaxMemoryID TEXT NULL,
    PrivateKeyPath TEXT NULL,
    WebServiceAddress TEXT NULL,
    IsDelete INTEGER NOT NULL DEFAULT(0)
);");
        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS Customers (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CpInfoId INTEGER NULL,
    Name TEXT NULL,
    PostalCode TEXT NULL,
    Phone TEXT NULL,
    NationalID TEXT NULL,
    Type TEXT NULL,
    EconomicNumber TEXT NULL,
    Address TEXT NULL
);");
        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS UnitOfMeasurements (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Name TEXT NULL,
    Code TEXT NULL,
    IsDelete INTEGER NOT NULL DEFAULT(0)
);");
        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS GoodsOrServices (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CpInfo INTEGER NOT NULL DEFAULT(0),
    Name TEXT NULL,
    UniqueIdentifier TEXT NULL,
    UnitOfMeasurement TEXT NULL,
    ValueAddedPercentage INTEGER NOT NULL DEFAULT(0),
    Price TEXT NOT NULL DEFAULT('0')
);");
        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS FiscalYears (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CpInfo INTEGER NULL,
    StartDate TEXT NOT NULL,
    EndDate TEXT NOT NULL
);");
        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS Invoices (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CustomerId INTEGER NULL,
    CpInfo INTEGER NULL,
    InvoiceNumber INTEGER NULL,
    FiscalYearId INTEGER NULL REFERENCES FiscalYears (Id),
    DateOrder TEXT NULL,
    TypePattern INTEGER NOT NULL DEFAULT(0),
    PaymentType TEXT NULL,
    PriceNaghd TEXT NULL,
    PriceNesye TEXT NULL,
    Description TEXT NULL,
    StatusSentToSystem TEXT NULL,
    ShowButtons INTEGER NOT NULL DEFAULT(0),
    TaxNumber TEXT NULL,
    NewTaxNumber TEXT NULL,
    ReferenceId TEXT NULL,
    Subject TEXT NULL,
    Name TEXT NULL,
    IsDelete INTEGER NOT NULL DEFAULT(0)
);");
        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS InvoiceItems (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    InvoiceId INTEGER NOT NULL REFERENCES Invoices (Id) ON DELETE CASCADE,
    GoodsOrServicesId INTEGER NOT NULL,
    UniqueIdentifier TEXT NULL,
    Count TEXT NOT NULL DEFAULT('0'),
    PriceUnit TEXT NOT NULL DEFAULT('0'),
    ValueAddedPercentage INTEGER NOT NULL DEFAULT(0),
    Discount TEXT NOT NULL DEFAULT('0'),
    GoodsOrServicesDescripton TEXT NULL,
    IsDelete INTEGER NOT NULL DEFAULT(0)
);");
    }

    private static async Task SafeAsync(AppDbContext db, string sql)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql); }
        catch (Exception ex)
        {
            var m = ex.Message ?? "";
            if (!m.Contains("duplicate", StringComparison.OrdinalIgnoreCase) &&
                !m.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"[DB] MoadianSchemaV1: {m}");
        }
    }
}
