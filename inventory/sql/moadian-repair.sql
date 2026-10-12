-- ============================================================
-- تعمیر فوری جدول‌های سامانه مودیان (نسخه 0.0.27) — SQL Server
-- چرا؟ مایگریشن 20261011080837_AddMoadianV1 روی دیتابیس‌هایی که
-- ستونی مثل Users.Email را از قبل داشتند با خطای duplicate شکست
-- خورد و به‌صورت ناخواسته «اعمال‌شده» علامت خورد؛ در نتیجه جدول‌های
-- مودیان ساخته نشدند و API خطا می‌گیرد:
--     خطا در سرور: Invalid object name 'CPInfos'
-- اجرا: در SQL Server Management Studio روی دیتابیس InventoryDb
-- (یا هر دیتابیسِ تنظیم‌شده در ConnectionStrings:Default) اجرا کنید.
-- اسکریپت idempotent است؛ اجرای تکراری ضرری ندارد.
-- نکته: نسخهٔ جدید API هنگام راه‌اندازی، همین کار را خودکار انجام می‌دهد
-- (DbInitializer → MoadianSchemaV1.EnsureAsync). این فایل فقط برای رفع فوری
-- بدون نیاز به استقرار مجدد است.
-- ============================================================

IF OBJECT_ID(N'dbo.CPInfos', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CPInfos](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [PersianName] nvarchar(max) NULL,
        [Nationalid] nvarchar(max) NULL,
        [EconomicNumber] nvarchar(max) NULL
    );
END
GO

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
END
GO

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
END
GO

IF OBJECT_ID(N'dbo.UnitOfMeasurements', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[UnitOfMeasurements](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [Name] nvarchar(max) NULL,
        [Code] nvarchar(max) NULL,
        [IsDelete] bit NOT NULL DEFAULT(0)
    );
END
GO

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
END
GO

IF OBJECT_ID(N'dbo.FiscalYears', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[FiscalYears](
        [Id] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
        [CpInfo] int NULL,
        [StartDate] datetime2 NOT NULL,
        [EndDate] datetime2 NOT NULL
    );
END
GO

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
END
GO

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
END
GO

-- تأیید نهایی: باید ۸ سطر برگردانده شود
SELECT name FROM sys.tables WHERE name IN
    (N'CPInfos', N'ConnectInfos', N'Customers', N'UnitOfMeasurements',
     N'GoodsOrServices', N'FiscalYears', N'Invoices', N'InvoiceItems')
ORDER BY name;
GO

-- نکتهٔ تشخیص (اختیاری): اگر کنجکاوید مایگریشن چرا رد شده بود:
-- SELECT * FROM [dbo].[__EFMigrationsHistory]
-- WHERE [MigrationId] = N'20261011080837_AddMoadianV1';
-- برگشت آن یعنی مایگریشن «stamp» شده — با این خودتعمیر مشکل جامعی ندارد.
