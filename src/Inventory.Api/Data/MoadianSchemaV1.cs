using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

// ============================================================
//  خودتعمیرِ اسکیمای «سامانه مودیان (نسخه 0.0.27)» — نسخه ۲
//  چرا لازم است؟
//  مایگریشن AddMoadianV1 (20261011080837) روی دیتابیس‌هایی که ستونی مثل
//  Users.Email را از قبل داشتند با خطای «duplicate» شکست می‌خورد؛
//  MigrateSqlServer آن مایگریشن را «stamp و رد» می‌کند و در نتیجه جدول‌های
//  مودیان (CPInfos و بقیه) هرگز ساخته نمی‌شوند. سرور در SaveCpInfo با
//  «Invalid object name 'CPInfos'» می‌افتد.
//  نسخه ۲: ستون‌های جاافتاده نسبت به جدول‌های پروژهٔ مبدأ هم اضافه شد
//  (EnglishName/PostalCode/IsDelete/CompanyId، CertificatePath، Title/IsActive/
//  CreatedDate/IsDelete سال مالی، IsDelete کالا و مشتری، TaxMemoryID صورتحساب،
//  PriceNaghd/PriceNesye آیتم و جدول InvoicePayments).
//  این خودتعمیر — مانند بقیهٔ *SchemaV1* ها — همه را idempotent می‌سازد؛
//  روی دیتابیسِ جدید که مایگریشن کامل اجرا شده هیچ کاری انجام نمی‌دهد.
// ============================================================
public static class MoadianSchemaV1
{
    public static Task EnsureAsync(AppDbContext db) =>
        db.Database.IsSqlite() ? EnsureSqliteAsync(db) : EnsureSqlServerAsync(db);

    // ==================== SQL Server ====================
    private static async Task EnsureSqlServerAsync(AppDbContext db)
    {
        // ترتیب ساخت مهم است: جداول مرجع اول، سپس Invoices و ردیف‌ها/پرداخت‌ها
        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.CPInfos', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CPInfos](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [CompanyId] int NULL,
        [Type] nvarchar(20) NULL,
        [EconomicNumber] nvarchar(max) NULL,
        [PersianName] nvarchar(max) NULL,
        [EnglishName] nvarchar(max) NULL,
        [Nationalid] nvarchar(max) NULL,
        [PostalCode] nvarchar(max) NULL,
        [IsDelete] bit NOT NULL DEFAULT(0)
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
        [CertificatePath] nvarchar(max) NULL,
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
        [Type] nvarchar(max) NULL,
        [Name] nvarchar(max) NULL,
        [NationalID] nvarchar(max) NULL,
        [EconomicNumber] nvarchar(max) NULL,
        [Phone] nvarchar(max) NULL,
        [PostalCode] nvarchar(max) NULL,
        [Address] nvarchar(max) NULL,
        [IsDelete] bit NOT NULL DEFAULT(0)
    );
END");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.UnitOfMeasurements', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[UnitOfMeasurements](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [CpInfo] int NULL,
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
        [Price] decimal(18,2) NOT NULL DEFAULT(0),
        [IsDelete] bit NOT NULL DEFAULT(0)
    );
END");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.FiscalYears', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[FiscalYears](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [CpInfo] int NULL,
        [Title] nvarchar(max) NULL,
        [StartDate] datetime2 NOT NULL,
        [EndDate] datetime2 NOT NULL,
        [CreatedDate] datetime2 NOT NULL DEFAULT(SYSDATETIME()),
        [IsActive] bit NOT NULL DEFAULT(1),
        [IsDelete] bit NOT NULL DEFAULT(0)
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
        [TaxMemoryID] nvarchar(max) NULL,
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
        [PriceNaghd] decimal(18,2) NULL,
        [PriceNesye] decimal(18,2) NULL,
        [ValueAddedPercentage] int NOT NULL DEFAULT(0),
        [Discount] decimal(18,2) NOT NULL DEFAULT(0),
        [GoodsOrServicesDescripton] nvarchar(max) NULL,
        [IsDelete] bit NOT NULL DEFAULT(0)
    );
    CREATE INDEX [IX_InvoiceItems_InvoiceId] ON [dbo].[InvoiceItems] ([InvoiceId]);
    ALTER TABLE [dbo].[InvoiceItems] ADD CONSTRAINT [FK_InvoiceItems_Invoices_InvoiceId]
        FOREIGN KEY ([InvoiceId]) REFERENCES [dbo].[Invoices] ([Id]) ON DELETE CASCADE;
END");

        await SafeAsync(db, @"
IF OBJECT_ID(N'dbo.InvoicePayments', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[InvoicePayments](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [InvoiceId] int NOT NULL,
        [TaxNumber] nvarchar(max) NULL,
        [Amount] int NOT NULL DEFAULT(0),
        [PaymentDate] nvarchar(max) NULL,
        [PaymentMethod] nvarchar(max) NULL,
        [TerminalNumber] nvarchar(max) NULL,
        [ReferenceNumber] nvarchar(max) NULL,
        [IsDelete] bit NOT NULL DEFAULT(0)
    );
    CREATE INDEX [IX_InvoicePayments_InvoiceId] ON [dbo].[InvoicePayments] ([InvoiceId]);
    ALTER TABLE [dbo].[InvoicePayments] ADD CONSTRAINT [FK_InvoicePayments_Invoices_InvoiceId]
        FOREIGN KEY ([InvoiceId]) REFERENCES [dbo].[Invoices] ([Id]) ON DELETE CASCADE;
END");

        // ---------- ستون‌های جدید روی جدول‌های موجود (اختلاف با پروژهٔ مبدأ) ----------
        // CPInfos: Type/EnglishName/PostalCode/IsDelete/CompanyId
        await AddSqlServerColumnIfMissing(db, "CPInfos", "Type", "nvarchar(20)", "NULL");
        await AddSqlServerColumnIfMissing(db, "CPInfos", "EnglishName", "nvarchar(max)", "NULL");
        await AddSqlServerColumnIfMissing(db, "CPInfos", "PostalCode", "nvarchar(max)", "NULL");
        await AddSqlServerColumnIfMissing(db, "CPInfos", "CompanyId", "int", "NULL");
        await AddSqlServerBitIfMissing(db, "CPInfos", "IsDelete", "0");
        // ConnectInfos: CertificatePath
        await AddSqlServerColumnIfMissing(db, "ConnectInfos", "CertificatePath", "nvarchar(max)", "NULL");
        // Customers: IsDelete
        await AddSqlServerBitIfMissing(db, "Customers", "IsDelete", "0");
        // GoodsOrServices: IsDelete
        await AddSqlServerBitIfMissing(db, "GoodsOrServices", "IsDelete", "0");
        // UnitOfMeasurements: CpInfo (تفکیک واحدها به‌ازای هر خدمات‌دهنده)
        await AddSqlServerColumnIfMissing(db, "UnitOfMeasurements", "CpInfo", "int", "NULL");
        // FiscalYears: Title/CreatedDate/IsActive/IsDelete
        await AddSqlServerColumnIfMissing(db, "FiscalYears", "Title", "nvarchar(max)", "NULL");
        await AddSqlServerColumnIfMissing(db, "FiscalYears", "CreatedDate", "datetime2", "NOT NULL DEFAULT(SYSDATETIME())");
        await AddSqlServerBitIfMissing(db, "FiscalYears", "IsActive", "1");
        await AddSqlServerBitIfMissing(db, "FiscalYears", "IsDelete", "0");
        // Invoices: TaxMemoryID
        await AddSqlServerColumnIfMissing(db, "Invoices", "TaxMemoryID", "nvarchar(max)", "NULL");
        // InvoiceItems: PriceNaghd/PriceNesye
        await AddSqlServerColumnIfMissing(db, "InvoiceItems", "PriceNaghd", "decimal(18,2)", "NULL");
        await AddSqlServerColumnIfMissing(db, "InvoiceItems", "PriceNesye", "decimal(18,2)", "NULL");
    }

    private static Task AddSqlServerColumnIfMissing(AppDbContext db, string table, string column, string type, string nullClause) =>
        SafeAsync(db, $@"
IF OBJECT_ID(N'dbo.{table}', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.{table}', N'{column}') IS NULL
    ALTER TABLE [dbo].[{table}] ADD [{column}] {type} {nullClause};");

    private static Task AddSqlServerBitIfMissing(AppDbContext db, string table, string column, string def) =>
        SafeAsync(db, $@"
IF OBJECT_ID(N'dbo.{table}', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.{table}', N'{column}') IS NULL
    ALTER TABLE [dbo].[{table}] ADD [{column}] bit NOT NULL DEFAULT({def});");

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

        // ---------- ستون‌های جدید روی جدول‌های موجود — پیش از return ----------
        // (خطای «ستون تکراری» یا «جدول نیست» در همهٔ این‌ها نادیده گرفته می‌شود)
        string[] alters =
        {
            "ALTER TABLE CPInfos ADD COLUMN Type TEXT NULL;",
            "ALTER TABLE CPInfos ADD COLUMN EnglishName TEXT NULL;",
            "ALTER TABLE CPInfos ADD COLUMN PostalCode TEXT NULL;",
            "ALTER TABLE CPInfos ADD COLUMN CompanyId INTEGER NULL;",
            "ALTER TABLE CPInfos ADD COLUMN IsDelete INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE ConnectInfos ADD COLUMN CertificatePath TEXT NULL;",
            "ALTER TABLE Customers ADD COLUMN IsDelete INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE GoodsOrServices ADD COLUMN IsDelete INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE UnitOfMeasurements ADD COLUMN CpInfo INTEGER NULL;",
            "ALTER TABLE FiscalYears ADD COLUMN Title TEXT NULL;",
            "ALTER TABLE FiscalYears ADD COLUMN CreatedDate TEXT NULL;",
            "ALTER TABLE FiscalYears ADD COLUMN IsActive INTEGER NOT NULL DEFAULT 1;",
            "ALTER TABLE FiscalYears ADD COLUMN IsDelete INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE Invoices ADD COLUMN TaxMemoryID TEXT NULL;",
            "ALTER TABLE InvoiceItems ADD COLUMN PriceNaghd TEXT NULL;",
            "ALTER TABLE InvoiceItems ADD COLUMN PriceNesye TEXT NULL;",
        };
        foreach (var a in alters)
        {
            try { await db.Database.ExecuteSqlRawAsync(a); } catch { }
        }

        if (hasTable) return;

        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS CPInfos (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CompanyId INTEGER NULL,
    Type TEXT NULL,
    EconomicNumber TEXT NULL,
    PersianName TEXT NULL,
    EnglishName TEXT NULL,
    Nationalid TEXT NULL,
    PostalCode TEXT NULL,
    IsDelete INTEGER NOT NULL DEFAULT(0)
);");
        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS ConnectInfos (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CpInfo INTEGER NOT NULL DEFAULT(0),
    TaxMemoryID TEXT NULL,
    PrivateKeyPath TEXT NULL,
    CertificatePath TEXT NULL,
    WebServiceAddress TEXT NULL,
    IsDelete INTEGER NOT NULL DEFAULT(0)
);");
        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS Customers (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CpInfoId INTEGER NULL,
    Type TEXT NULL,
    Name TEXT NULL,
    PostalCode TEXT NULL,
    Phone TEXT NULL,
    NationalID TEXT NULL,
    EconomicNumber TEXT NULL,
    Address TEXT NULL,
    IsDelete INTEGER NOT NULL DEFAULT(0)
);");
        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS UnitOfMeasurements (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CpInfo INTEGER NULL,
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
    Price TEXT NOT NULL DEFAULT('0'),
    IsDelete INTEGER NOT NULL DEFAULT(0)
);");
        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS FiscalYears (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    CpInfo INTEGER NULL,
    Title TEXT NULL,
    StartDate TEXT NOT NULL,
    EndDate TEXT NOT NULL,
    CreatedDate TEXT NULL,
    IsActive INTEGER NOT NULL DEFAULT(1),
    IsDelete INTEGER NOT NULL DEFAULT(0)
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
    TaxMemoryID TEXT NULL,
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
    PriceNaghd TEXT NULL,
    PriceNesye TEXT NULL,
    ValueAddedPercentage INTEGER NOT NULL DEFAULT(0),
    Discount TEXT NOT NULL DEFAULT('0'),
    GoodsOrServicesDescripton TEXT NULL,
    IsDelete INTEGER NOT NULL DEFAULT(0)
);");
        await SafeAsync(db, @"
CREATE TABLE IF NOT EXISTS InvoicePayments (
    Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    InvoiceId INTEGER NOT NULL REFERENCES Invoices (Id) ON DELETE CASCADE,
    TaxNumber TEXT NULL,
    Amount INTEGER NOT NULL DEFAULT(0),
    PaymentDate TEXT NULL,
    PaymentMethod TEXT NULL,
    TerminalNumber TEXT NULL,
    ReferenceNumber TEXT NULL,
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
