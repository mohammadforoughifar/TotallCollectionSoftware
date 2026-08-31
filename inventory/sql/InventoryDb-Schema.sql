IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814161634_InitialCreate'
)
BEGIN
    CREATE TABLE [Parties] (
        [Id] int NOT NULL IDENTITY,
        [Type] int NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Phone] nvarchar(50) NULL,
        [Mobile] nvarchar(50) NULL,
        [Address] nvarchar(250) NULL,
        [Note] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Parties] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814161634_InitialCreate'
)
BEGIN
    CREATE TABLE [Products] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Unit] nvarchar(50) NOT NULL,
        [Category] nvarchar(100) NULL,
        [Barcode] nvarchar(100) NULL,
        [SalePrice] decimal(18,2) NOT NULL,
        [PurchasePrice] decimal(18,2) NOT NULL,
        [ReorderPoint] decimal(18,3) NOT NULL,
        [MaxStock] decimal(18,3) NOT NULL,
        [Description] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Products] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814161634_InitialCreate'
)
BEGIN
    CREATE TABLE [Stocks] (
        [Id] int NOT NULL IDENTITY,
        [WarehouseId] int NOT NULL,
        [ProductId] int NOT NULL,
        [Quantity] decimal(18,3) NOT NULL,
        [AvgCost] decimal(18,4) NOT NULL,
        CONSTRAINT [PK_Stocks] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814161634_InitialCreate'
)
BEGIN
    CREATE TABLE [Transactions] (
        [Id] int NOT NULL IDENTITY,
        [Number] nvarchar(30) NOT NULL,
        [Type] int NOT NULL,
        [Date] datetime2 NOT NULL,
        [Description] nvarchar(500) NULL,
        [WarehouseId] int NOT NULL,
        [PartyId] int NULL,
        [Amount] decimal(18,2) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Transactions] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814161634_InitialCreate'
)
BEGIN
    CREATE TABLE [Warehouses] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(150) NOT NULL,
        [Address] nvarchar(250) NULL,
        [Phone] nvarchar(50) NULL,
        [Note] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_Warehouses] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814161634_InitialCreate'
)
BEGIN
    CREATE TABLE [TransactionLines] (
        [Id] int NOT NULL IDENTITY,
        [TransactionId] int NOT NULL,
        [ProductId] int NOT NULL,
        [Quantity] decimal(18,3) NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        [Description] nvarchar(300) NULL,
        CONSTRAINT [PK_TransactionLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TransactionLines_Transactions_TransactionId] FOREIGN KEY ([TransactionId]) REFERENCES [Transactions] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814161634_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Products_Code] ON [Products] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814161634_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Stocks_WarehouseId_ProductId] ON [Stocks] ([WarehouseId], [ProductId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814161634_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TransactionLines_ProductId] ON [TransactionLines] ([ProductId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814161634_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_TransactionLines_TransactionId] ON [TransactionLines] ([TransactionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814161634_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Transactions_Date] ON [Transactions] ([Date]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814161634_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Transactions_Type] ON [Transactions] ([Type]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260814161634_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260814161634_InitialCreate', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815051102_AddProductCategories'
)
BEGIN
    CREATE TABLE [ProductCategories] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [Description] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ProductCategories] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815051102_AddProductCategories'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductCategories_Name] ON [ProductCategories] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815051102_AddProductCategories'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260815051102_AddProductCategories', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815052346_AddUnitsAndCategoryTree'
)
BEGIN
    ALTER TABLE [ProductCategories] ADD [ParentId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815052346_AddUnitsAndCategoryTree'
)
BEGIN
    CREATE TABLE [MeasureUnits] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(50) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_MeasureUnits] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815052346_AddUnitsAndCategoryTree'
)
BEGIN
    CREATE UNIQUE INDEX [IX_MeasureUnits_Name] ON [MeasureUnits] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815052346_AddUnitsAndCategoryTree'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260815052346_AddUnitsAndCategoryTree', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815062907_AddSettingsReferrersServices'
)
BEGIN
    ALTER TABLE [Transactions] ADD [ReferrerId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815062907_AddSettingsReferrersServices'
)
BEGIN
    ALTER TABLE [Products] ADD [IsService] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815062907_AddSettingsReferrersServices'
)
BEGIN
    CREATE TABLE [AppSettings] (
        [Id] int NOT NULL IDENTITY,
        [CostingMethod] nvarchar(20) NOT NULL,
        [AllowNegativeStock] bit NOT NULL,
        CONSTRAINT [PK_AppSettings] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815062907_AddSettingsReferrersServices'
)
BEGIN
    CREATE TABLE [Referrers] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(150) NOT NULL,
        [Phone] nvarchar(50) NULL,
        [GoodsCommissionPercent] decimal(5,2) NOT NULL,
        [ServiceCommissionPercent] decimal(5,2) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Referrers] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815062907_AddSettingsReferrersServices'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260815062907_AddSettingsReferrersServices', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815065428_AddReferrerCompanyName'
)
BEGIN
    ALTER TABLE [Referrers] ADD [CompanyName] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815065428_AddReferrerCompanyName'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260815065428_AddReferrerCompanyName', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815072211_AddWalletWarehouseCustomerReferrer'
)
BEGIN
    ALTER TABLE [Products] ADD [WarehouseId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815072211_AddWalletWarehouseCustomerReferrer'
)
BEGIN
    ALTER TABLE [Parties] ADD [ReferrerId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815072211_AddWalletWarehouseCustomerReferrer'
)
BEGIN
    CREATE TABLE [ReferrerPayments] (
        [Id] int NOT NULL IDENTITY,
        [ReferrerId] int NOT NULL,
        [Number] nvarchar(30) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Date] datetime2 NOT NULL,
        [Description] nvarchar(300) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ReferrerPayments] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815072211_AddWalletWarehouseCustomerReferrer'
)
BEGIN
    CREATE INDEX [IX_ReferrerPayments_ReferrerId] ON [ReferrerPayments] ([ReferrerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815072211_AddWalletWarehouseCustomerReferrer'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260815072211_AddWalletWarehouseCustomerReferrer', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815083133_AddUsers'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] int NOT NULL IDENTITY,
        [Username] nvarchar(100) NOT NULL,
        [PasswordHash] nvarchar(200) NOT NULL,
        [Role] nvarchar(20) NOT NULL,
        [ReferrerId] int NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815083133_AddUsers'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Username] ON [Users] ([Username]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815083133_AddUsers'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260815083133_AddUsers', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815091428_AddReferrerBankInfo'
)
BEGIN
    ALTER TABLE [Referrers] ADD [CardNumber] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815091428_AddReferrerBankInfo'
)
BEGIN
    ALTER TABLE [Referrers] ADD [Iban] nvarchar(30) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260815091428_AddReferrerBankInfo'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260815091428_AddReferrerBankInfo', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816033434_AddRepairs'
)
BEGIN
    CREATE TABLE [RepairOrders] (
        [Id] int NOT NULL IDENTITY,
        [Number] nvarchar(30) NOT NULL,
        [PartyId] int NOT NULL,
        [TechnicianId] int NULL,
        [DeviceType] nvarchar(100) NOT NULL,
        [DeviceModel] nvarchar(200) NULL,
        [SerialNumber] nvarchar(100) NULL,
        [ProblemDescription] nvarchar(1000) NULL,
        [Accessories] nvarchar(500) NULL,
        [Status] int NOT NULL,
        [ReceivedAt] datetime2 NOT NULL,
        [DeliveredAt] datetime2 NULL,
        [QuotedPrice] decimal(18,2) NOT NULL,
        [InvoiceTransactionId] int NULL,
        [Note] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_RepairOrders] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816033434_AddRepairs'
)
BEGIN
    CREATE TABLE [Technicians] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(150) NOT NULL,
        [Phone] nvarchar(50) NULL,
        [Specialty] nvarchar(150) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Technicians] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816033434_AddRepairs'
)
BEGIN
    CREATE TABLE [RepairItems] (
        [Id] int NOT NULL IDENTITY,
        [RepairOrderId] int NOT NULL,
        [Description] nvarchar(300) NOT NULL,
        [ProductId] int NULL,
        [Quantity] decimal(18,3) NOT NULL,
        [Cost] decimal(18,2) NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_RepairItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RepairItems_RepairOrders_RepairOrderId] FOREIGN KEY ([RepairOrderId]) REFERENCES [RepairOrders] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816033434_AddRepairs'
)
BEGIN
    CREATE INDEX [IX_RepairItems_RepairOrderId] ON [RepairItems] ([RepairOrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816033434_AddRepairs'
)
BEGIN
    CREATE INDEX [IX_RepairOrders_PartyId] ON [RepairOrders] ([PartyId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816033434_AddRepairs'
)
BEGIN
    CREATE INDEX [IX_RepairOrders_Status] ON [RepairOrders] ([Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816033434_AddRepairs'
)
BEGIN
    CREATE INDEX [IX_RepairOrders_TechnicianId] ON [RepairOrders] ([TechnicianId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816033434_AddRepairs'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260816033434_AddRepairs', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816042258_AddPaymentsAndRoles'
)
BEGIN
    ALTER TABLE [Transactions] ADD [CashType] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816042258_AddPaymentsAndRoles'
)
BEGIN
    ALTER TABLE [Transactions] ADD [DueDate] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816042258_AddPaymentsAndRoles'
)
BEGIN
    ALTER TABLE [Transactions] ADD [PaymentMethod] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816042258_AddPaymentsAndRoles'
)
BEGIN
    ALTER TABLE [Transactions] ADD [SettledAmount] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816042258_AddPaymentsAndRoles'
)
BEGIN
    CREATE TABLE [Cheques] (
        [Id] int NOT NULL IDENTITY,
        [TransactionId] int NOT NULL,
        [Number] nvarchar(50) NOT NULL,
        [BankName] nvarchar(100) NULL,
        [AccountInfo] nvarchar(100) NULL,
        [OwnerName] nvarchar(150) NULL,
        [Amount] decimal(18,2) NOT NULL,
        [DueDate] datetime2 NOT NULL,
        [IsCleared] bit NOT NULL,
        [ClearedAt] datetime2 NULL,
        [Note] nvarchar(300) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Cheques] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Cheques_Transactions_TransactionId] FOREIGN KEY ([TransactionId]) REFERENCES [Transactions] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816042258_AddPaymentsAndRoles'
)
BEGIN
    CREATE TABLE [Installments] (
        [Id] int NOT NULL IDENTITY,
        [TransactionId] int NOT NULL,
        [No] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [DueDate] datetime2 NOT NULL,
        [IsPaid] bit NOT NULL,
        [PaidAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Installments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Installments_Transactions_TransactionId] FOREIGN KEY ([TransactionId]) REFERENCES [Transactions] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816042258_AddPaymentsAndRoles'
)
BEGIN
    CREATE INDEX [IX_Cheques_DueDate] ON [Cheques] ([DueDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816042258_AddPaymentsAndRoles'
)
BEGIN
    CREATE INDEX [IX_Cheques_IsCleared] ON [Cheques] ([IsCleared]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816042258_AddPaymentsAndRoles'
)
BEGIN
    CREATE INDEX [IX_Cheques_TransactionId] ON [Cheques] ([TransactionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816042258_AddPaymentsAndRoles'
)
BEGIN
    CREATE INDEX [IX_Installments_DueDate] ON [Installments] ([DueDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816042258_AddPaymentsAndRoles'
)
BEGIN
    CREATE INDEX [IX_Installments_TransactionId] ON [Installments] ([TransactionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816042258_AddPaymentsAndRoles'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260816042258_AddPaymentsAndRoles', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816043917_AddCashAmountMixedPayment'
)
BEGIN
    ALTER TABLE [Transactions] ADD [CashAmount] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816043917_AddCashAmountMixedPayment'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260816043917_AddCashAmountMixedPayment', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816051853_AddReferrerCanViewProducts'
)
BEGIN
    ALTER TABLE [Referrers] ADD [CanViewProducts] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816051853_AddReferrerCanViewProducts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260816051853_AddReferrerCanViewProducts', N'8.0.0');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816053917_AddExpenses'
)
BEGIN
    CREATE TABLE [ExpenseCategories] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(150) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ExpenseCategories] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816053917_AddExpenses'
)
BEGIN
    CREATE TABLE [Expenses] (
        [Id] int NOT NULL IDENTITY,
        [Number] nvarchar(30) NOT NULL,
        [CategoryId] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Date] datetime2 NOT NULL,
        [PayType] int NOT NULL,
        [Payee] nvarchar(150) NULL,
        [Description] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Expenses] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816053917_AddExpenses'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ExpenseCategories_Name] ON [ExpenseCategories] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816053917_AddExpenses'
)
BEGIN
    CREATE INDEX [IX_Expenses_CategoryId] ON [Expenses] ([CategoryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816053917_AddExpenses'
)
BEGIN
    CREATE INDEX [IX_Expenses_Date] ON [Expenses] ([Date]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260816053917_AddExpenses'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260816053917_AddExpenses', N'8.0.0');
END;
GO

COMMIT;
GO


-- ============ جدول‌های سیستم (System*) — اضافه‌شده برای تطابق با Entity ها ============
IF OBJECT_ID('dbo.SystemCompanies', 'U') IS NULL
CREATE TABLE dbo.SystemCompanies (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(200) NOT NULL DEFAULT N'',
    Code NVARCHAR(100) NULL, Phone NVARCHAR(50) NULL, Address NVARCHAR(250) NULL,
    IsActive BIT NOT NULL DEFAULT 1, CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
IF OBJECT_ID('dbo.SystemDepartments', 'U') IS NULL
CREATE TABLE dbo.SystemDepartments (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(150) NOT NULL DEFAULT N'',
    CompanyId INT NULL REFERENCES dbo.SystemCompanies(Id),
    IsActive BIT NOT NULL DEFAULT 1, CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
IF OBJECT_ID('dbo.SystemUsers', 'U') IS NULL
CREATE TABLE dbo.SystemUsers (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    FirstName NVARCHAR(100) NOT NULL DEFAULT N'',
    LastName NVARCHAR(100) NOT NULL DEFAULT N'',
    StaffNumber NVARCHAR(50) NOT NULL DEFAULT N'',
    DepartmentId INT NULL REFERENCES dbo.SystemDepartments(Id),
    CompanyId INT NULL REFERENCES dbo.SystemCompanies(Id),
    Role NVARCHAR(20) NULL, IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
IF OBJECT_ID('dbo.SystemInfos', 'U') IS NULL
CREATE TABLE dbo.SystemInfos (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    AgentId NVARCHAR(MAX) NOT NULL DEFAULT N'',
    Motherboard NVARCHAR(MAX) NULL, Cpu NVARCHAR(MAX) NULL, Ram NVARCHAR(MAX) NULL,
    HardDisk NVARCHAR(MAX) NULL, Graphics NVARCHAR(MAX) NULL, Monitor NVARCHAR(MAX) NULL,
    IsApproved BIT NOT NULL DEFAULT 0, OsName NVARCHAR(MAX) NULL,
    TotalRamGb INT NOT NULL DEFAULT 0,
    CompanyId INT NULL REFERENCES dbo.SystemCompanies(Id),
    DepartmentId INT NULL REFERENCES dbo.SystemDepartments(Id),
    UserId INT NULL REFERENCES dbo.SystemUsers(Id),
    ReceivedAt DATETIME2 NOT NULL DEFAULT GETDATE()
);
