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
    VALUES (N'20260814161634_InitialCreate', N'8.0.1');
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
    VALUES (N'20260815051102_AddProductCategories', N'8.0.1');
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
    VALUES (N'20260815052346_AddUnitsAndCategoryTree', N'8.0.1');
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
    VALUES (N'20260815062907_AddSettingsReferrersServices', N'8.0.1');
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
    VALUES (N'20260815065428_AddReferrerCompanyName', N'8.0.1');
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
    VALUES (N'20260815072211_AddWalletWarehouseCustomerReferrer', N'8.0.1');
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
    VALUES (N'20260815083133_AddUsers', N'8.0.1');
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
    VALUES (N'20260815091428_AddReferrerBankInfo', N'8.0.1');
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
    VALUES (N'20260816033434_AddRepairs', N'8.0.1');
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
    VALUES (N'20260816042258_AddPaymentsAndRoles', N'8.0.1');
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
    VALUES (N'20260816043917_AddCashAmountMixedPayment', N'8.0.1');
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
    VALUES (N'20260816051853_AddReferrerCanViewProducts', N'8.0.1');
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
    VALUES (N'20260816053917_AddExpenses', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819040730_AddSystemTables'
)
BEGIN
    IF OBJECT_ID('dbo.SystemInfos', 'U')    IS NOT NULL DROP TABLE dbo.SystemInfos;
    IF OBJECT_ID('dbo.SystemUsers', 'U')    IS NOT NULL DROP TABLE dbo.SystemUsers;
    IF OBJECT_ID('dbo.SystemDepartments', 'U') IS NOT NULL DROP TABLE dbo.SystemDepartments;
    IF OBJECT_ID('dbo.SystemCompanies', 'U') IS NOT NULL DROP TABLE dbo.SystemCompanies;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819040730_AddSystemTables'
)
BEGIN
    CREATE TABLE [SystemCompanies] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(200) NOT NULL,
        [Code] nvarchar(100) NULL,
        [Phone] nvarchar(50) NULL,
        [Address] nvarchar(250) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_SystemCompanies] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819040730_AddSystemTables'
)
BEGIN
    CREATE TABLE [SystemDepartments] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(150) NOT NULL,
        [CompanyId] int NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_SystemDepartments] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819040730_AddSystemTables'
)
BEGIN
    CREATE TABLE [SystemInfos] (
        [Id] int NOT NULL IDENTITY,
        [AgentId] nvarchar(max) NOT NULL,
        [Motherboard] nvarchar(max) NULL,
        [Cpu] nvarchar(max) NULL,
        [Ram] nvarchar(max) NULL,
        [HardDisk] nvarchar(max) NULL,
        [Graphics] nvarchar(max) NULL,
        [Monitor] nvarchar(max) NULL,
        [IsApproved] bit NOT NULL,
        [OsName] nvarchar(max) NULL,
        [TotalRamGb] int NOT NULL,
        [CompanyId] int NULL,
        [DepartmentId] int NULL,
        [UserId] int NULL,
        [ReceivedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_SystemInfos] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819040730_AddSystemTables'
)
BEGIN
    CREATE TABLE [SystemUsers] (
        [Id] int NOT NULL IDENTITY,
        [FirstName] nvarchar(100) NOT NULL,
        [LastName] nvarchar(100) NOT NULL,
        [StaffNumber] nvarchar(50) NOT NULL,
        [DepartmentId] int NULL,
        [CompanyId] int NULL,
        [Role] nvarchar(20) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_SystemUsers] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819040730_AddSystemTables'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260819040730_AddSystemTables', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819043459_AddSystemInfoDetails'
)
BEGIN
    ALTER TABLE [SystemInfos] ADD [DetailsJson] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819043459_AddSystemInfoDetails'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260819043459_AddSystemInfoDetails', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819060845_AddSystemInfoChangeTracking'
)
BEGIN
    ALTER TABLE [SystemInfos] ADD [PendingPayloadJson] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819060845_AddSystemInfoChangeTracking'
)
BEGIN
    ALTER TABLE [SystemInfos] ADD [PendingReceivedAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819060845_AddSystemInfoChangeTracking'
)
BEGIN
    CREATE TABLE [SystemInfoChangeLogs] (
        [Id] int NOT NULL IDENTITY,
        [SystemInfoId] int NOT NULL,
        [AgentId] nvarchar(max) NULL,
        [ChangedAt] datetime2 NOT NULL,
        [ChangeCount] int NOT NULL,
        [ChangesJson] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_SystemInfoChangeLogs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819060845_AddSystemInfoChangeTracking'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260819060845_AddSystemInfoChangeTracking', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819061901_AddCctvCameras'
)
BEGIN
    CREATE TABLE [CctvCameras] (
        [Id] int NOT NULL IDENTITY,
        [Model] nvarchar(150) NOT NULL,
        [SerialNumber] nvarchar(150) NOT NULL,
        [Ip] nvarchar(50) NULL,
        [Mac] nvarchar(50) NULL,
        [Location] nvarchar(250) NULL,
        [Notes] nvarchar(500) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_CctvCameras] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819061901_AddCctvCameras'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260819061901_AddCctvCameras', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819063240_AddCctvNvrs'
)
BEGIN
    ALTER TABLE [CctvCameras] ADD [NvrId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819063240_AddCctvNvrs'
)
BEGIN
    CREATE TABLE [CctvNvrs] (
        [Id] int NOT NULL IDENTITY,
        [Model] nvarchar(150) NOT NULL,
        [SerialNumber] nvarchar(150) NOT NULL,
        [Ip] nvarchar(50) NULL,
        [Mac] nvarchar(50) NULL,
        [Location] nvarchar(250) NULL,
        [Notes] nvarchar(500) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_CctvNvrs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819063240_AddCctvNvrs'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260819063240_AddCctvNvrs', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819065947_AddOfficeMachines'
)
BEGIN
    CREATE TABLE [OfficeMachineCosts] (
        [Id] int NOT NULL IDENTITY,
        [MachineId] int NOT NULL,
        [CostDate] datetime2 NOT NULL,
        [Title] nvarchar(300) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_OfficeMachineCosts] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819065947_AddOfficeMachines'
)
BEGIN
    CREATE TABLE [OfficeMachineRepairs] (
        [Id] int NOT NULL IDENTITY,
        [MachineId] int NOT NULL,
        [RepairDate] datetime2 NOT NULL,
        [Problem] nvarchar(1000) NOT NULL,
        [Fixed] bit NOT NULL,
        CONSTRAINT [PK_OfficeMachineRepairs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819065947_AddOfficeMachines'
)
BEGIN
    CREATE TABLE [OfficeMachines] (
        [Id] int NOT NULL IDENTITY,
        [Model] nvarchar(150) NOT NULL,
        [SerialNumber] nvarchar(150) NULL,
        [Location] nvarchar(250) NULL,
        [InstallDate] datetime2 NULL,
        [IsActive] bit NOT NULL,
        [GoneDate] datetime2 NULL,
        [ReturnDate] datetime2 NULL,
        [Notes] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_OfficeMachines] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819065947_AddOfficeMachines'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260819065947_AddOfficeMachines', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819070927_AddRepairDetails'
)
BEGIN
    ALTER TABLE [OfficeMachineRepairs] ADD [Cost] decimal(18,2) NOT NULL DEFAULT 0.0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819070927_AddRepairDetails'
)
BEGIN
    ALTER TABLE [OfficeMachineRepairs] ADD [GoneDate] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819070927_AddRepairDetails'
)
BEGIN
    ALTER TABLE [OfficeMachineRepairs] ADD [PerformedWork] nvarchar(2000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819070927_AddRepairDetails'
)
BEGIN
    ALTER TABLE [OfficeMachineRepairs] ADD [ReturnDate] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819070927_AddRepairDetails'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260819070927_AddRepairDetails', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819084326_AddSystemComponentTables'
)
BEGIN
    CREATE TABLE [SystemCpus] (
        [Id] int NOT NULL IDENTITY,
        [SystemInfoId] int NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Cores] int NOT NULL,
        [Threads] int NOT NULL,
        [ClockGhz] float NOT NULL,
        CONSTRAINT [PK_SystemCpus] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819084326_AddSystemComponentTables'
)
BEGIN
    CREATE TABLE [SystemDisks] (
        [Id] int NOT NULL IDENTITY,
        [SystemInfoId] int NOT NULL,
        [Model] nvarchar(200) NOT NULL,
        [SizeGb] int NOT NULL,
        [Interface] nvarchar(50) NOT NULL,
        [SerialNumber] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_SystemDisks] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819084326_AddSystemComponentTables'
)
BEGIN
    CREATE TABLE [SystemGpus] (
        [Id] int NOT NULL IDENTITY,
        [SystemInfoId] int NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Resolution] nvarchar(30) NOT NULL,
        CONSTRAINT [PK_SystemGpus] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819084326_AddSystemComponentTables'
)
BEGIN
    CREATE TABLE [SystemMonitors] (
        [Id] int NOT NULL IDENTITY,
        [SystemInfoId] int NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Resolution] nvarchar(30) NOT NULL,
        [SerialNumber] nvarchar(100) NOT NULL,
        [IsPrimary] bit NOT NULL,
        CONSTRAINT [PK_SystemMonitors] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819084326_AddSystemComponentTables'
)
BEGIN
    CREATE TABLE [SystemNetAdapters] (
        [Id] int NOT NULL IDENTITY,
        [SystemInfoId] int NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Description] nvarchar(100) NOT NULL,
        [Type] nvarchar(50) NOT NULL,
        [MacAddress] nvarchar(50) NOT NULL,
        [Ipv4] nvarchar(200) NOT NULL,
        [Gateway] nvarchar(50) NOT NULL,
        CONSTRAINT [PK_SystemNetAdapters] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819084326_AddSystemComponentTables'
)
BEGIN
    CREATE TABLE [SystemRams] (
        [Id] int NOT NULL IDENTITY,
        [SystemInfoId] int NOT NULL,
        [Slot] nvarchar(100) NOT NULL,
        [CapacityGb] int NOT NULL,
        [Type] nvarchar(20) NOT NULL,
        [SpeedMhz] int NOT NULL,
        [Manufacturer] nvarchar(100) NOT NULL,
        [PartNumber] nvarchar(100) NOT NULL,
        [SerialNumber] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_SystemRams] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819084326_AddSystemComponentTables'
)
BEGIN
    CREATE TABLE [SystemVolumes] (
        [Id] int NOT NULL IDENTITY,
        [SystemInfoId] int NOT NULL,
        [Letter] nvarchar(10) NOT NULL,
        [Label] nvarchar(100) NOT NULL,
        [TotalGb] int NOT NULL,
        [UsedGb] int NOT NULL,
        CONSTRAINT [PK_SystemVolumes] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819084326_AddSystemComponentTables'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260819084326_AddSystemComponentTables', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819090102_AddSystemBoard'
)
BEGIN
    CREATE TABLE [SystemBoards] (
        [Id] int NOT NULL IDENTITY,
        [SystemInfoId] int NOT NULL,
        [Board] nvarchar(250) NOT NULL,
        [BoardSerial] nvarchar(150) NOT NULL,
        [ComputerModel] nvarchar(250) NOT NULL,
        CONSTRAINT [PK_SystemBoards] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260819090102_AddSystemBoard'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260819090102_AddSystemBoard', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822033216_AddRbacTables'
)
BEGIN
    ALTER TABLE [SystemUsers] ADD [Username] nvarchar(50) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822033216_AddRbacTables'
)
BEGIN
    CREATE TABLE [Permissions] (
        [Id] int NOT NULL IDENTITY,
        [Module] nvarchar(450) NOT NULL,
        [Action] nvarchar(450) NOT NULL,
        [Description] nvarchar(max) NULL,
        CONSTRAINT [PK_Permissions] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822033216_AddRbacTables'
)
BEGIN
    CREATE TABLE [Roles] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(450) NOT NULL,
        [Description] nvarchar(max) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Roles] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822033216_AddRbacTables'
)
BEGIN
    CREATE TABLE [RolePermissions] (
        [RoleId] int NOT NULL,
        [PermissionId] int NOT NULL,
        CONSTRAINT [PK_RolePermissions] PRIMARY KEY ([RoleId], [PermissionId]),
        CONSTRAINT [FK_RolePermissions_Permissions_PermissionId] FOREIGN KEY ([PermissionId]) REFERENCES [Permissions] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_RolePermissions_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822033216_AddRbacTables'
)
BEGIN
    CREATE TABLE [UserRoles] (
        [UserId] int NOT NULL,
        [RoleId] int NOT NULL,
        [AssignedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_UserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_UserRoles_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822033216_AddRbacTables'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Permissions_Module_Action] ON [Permissions] ([Module], [Action]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822033216_AddRbacTables'
)
BEGIN
    CREATE INDEX [IX_RolePermissions_PermissionId] ON [RolePermissions] ([PermissionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822033216_AddRbacTables'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Roles_Name] ON [Roles] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822033216_AddRbacTables'
)
BEGIN
    CREATE INDEX [IX_UserRoles_RoleId] ON [UserRoles] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822033216_AddRbacTables'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260822033216_AddRbacTables', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822103039_AddItRequests'
)
BEGIN
    CREATE TABLE [ItRequestAssignments] (
        [Id] int NOT NULL IDENTITY,
        [RequestId] int NOT NULL,
        [ExpertUserId] int NOT NULL,
        [ExpertName] nvarchar(150) NOT NULL,
        [ManagerInstruction] nvarchar(2000) NULL,
        [ExpertReport] nvarchar(4000) NULL,
        [ReportSubmitted] bit NOT NULL,
        [IncludeInFinal] bit NOT NULL,
        [RepliedAt] datetime2 NULL,
        CONSTRAINT [PK_ItRequestAssignments] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822103039_AddItRequests'
)
BEGIN
    CREATE TABLE [ItRequestAttachments] (
        [Id] int NOT NULL IDENTITY,
        [RequestId] int NOT NULL,
        [FileName] nvarchar(255) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [Data] varbinary(max) NOT NULL,
        [UploaderRole] nvarchar(20) NOT NULL,
        [UploaderName] nvarchar(150) NOT NULL,
        [UploaderUserId] int NOT NULL,
        [UploadedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ItRequestAttachments] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822103039_AddItRequests'
)
BEGIN
    CREATE TABLE [ItRequests] (
        [Id] int NOT NULL IDENTITY,
        [RequesterName] nvarchar(150) NOT NULL,
        [RequesterUserId] int NOT NULL,
        [SystemInfoId] int NULL,
        [SystemLabel] nvarchar(250) NULL,
        [Title] nvarchar(200) NOT NULL,
        [Description] nvarchar(2000) NOT NULL,
        [Status] nvarchar(30) NOT NULL,
        [ManagerNote] nvarchar(2000) NULL,
        [FinalResponse] nvarchar(4000) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [AssignedAt] datetime2 NULL,
        [ApprovedAt] datetime2 NULL,
        [CompletedAt] datetime2 NULL,
        CONSTRAINT [PK_ItRequests] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260822103039_AddItRequests'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260822103039_AddItRequests', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823043823_AddItWorkflowAndNotifications'
)
BEGIN
    ALTER TABLE [OfficeMachines] ADD [ConnectionType] nvarchar(20) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823043823_AddItWorkflowAndNotifications'
)
BEGIN
    ALTER TABLE [OfficeMachines] ADD [IpAddress] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823043823_AddItWorkflowAndNotifications'
)
BEGIN
    ALTER TABLE [OfficeMachines] ADD [LinkedSystemInfoId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823043823_AddItWorkflowAndNotifications'
)
BEGIN
    ALTER TABLE [OfficeMachines] ADD [LinkedSystemLabel] nvarchar(250) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823043823_AddItWorkflowAndNotifications'
)
BEGIN
    ALTER TABLE [ItRequests] ADD [RequestType] nvarchar(30) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823043823_AddItWorkflowAndNotifications'
)
BEGIN
    ALTER TABLE [ItRequestAssignments] ADD [Done] bit NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823043823_AddItWorkflowAndNotifications'
)
BEGIN
    ALTER TABLE [ItRequestAssignments] ADD [ManagerDecision] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823043823_AddItWorkflowAndNotifications'
)
BEGIN
    ALTER TABLE [ItRequestAssignments] ADD [ManagerDecisionNote] nvarchar(1000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823043823_AddItWorkflowAndNotifications'
)
BEGIN
    CREATE TABLE [AppNotifications] (
        [Id] int NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Body] nvarchar(500) NULL,
        [FromName] nvarchar(150) NOT NULL,
        [FormName] nvarchar(100) NOT NULL,
        [Link] nvarchar(200) NULL,
        [IsRead] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_AppNotifications] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823043823_AddItWorkflowAndNotifications'
)
BEGIN
    CREATE TABLE [ItRequestLogs] (
        [Id] int NOT NULL IDENTITY,
        [RequestId] int NOT NULL,
        [ActorName] nvarchar(150) NOT NULL,
        [ActorRole] nvarchar(20) NOT NULL,
        [Action] nvarchar(30) NOT NULL,
        [Text] nvarchar(4000) NULL,
        [InternalOnly] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ItRequestLogs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823043823_AddItWorkflowAndNotifications'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823043823_AddItWorkflowAndNotifications', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823052806_AddItNumberSeenReject'
)
BEGIN
    ALTER TABLE [ItRequests] ADD [Number] nvarchar(30) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823052806_AddItNumberSeenReject'
)
BEGIN
    CREATE TABLE [ItRequestSeens] (
        [Id] int NOT NULL IDENTITY,
        [RequestId] int NOT NULL,
        [UserId] int NOT NULL,
        [SeenAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ItRequestSeens] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823052806_AddItNumberSeenReject'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823052806_AddItNumberSeenReject', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823063932_AddItServerConfig'
)
BEGIN
    ALTER TABLE [AppSettings] ADD [ItCompanyName] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823063932_AddItServerConfig'
)
BEGIN
    ALTER TABLE [AppSettings] ADD [ItServerUrl] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823063932_AddItServerConfig'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823063932_AddItServerConfig', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823071118_AddMessengers'
)
BEGIN
    ALTER TABLE [Users] ADD [BaleChatId] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823071118_AddMessengers'
)
BEGIN
    ALTER TABLE [Users] ADD [EitaaChatId] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823071118_AddMessengers'
)
BEGIN
    ALTER TABLE [Users] ADD [Mobile] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823071118_AddMessengers'
)
BEGIN
    ALTER TABLE [AppSettings] ADD [BaleBotToken] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823071118_AddMessengers'
)
BEGIN
    ALTER TABLE [AppSettings] ADD [EitaaToken] nvarchar(max) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823071118_AddMessengers'
)
BEGIN
    ALTER TABLE [AppSettings] ADD [MessengerSenderNumber] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823071118_AddMessengers'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823071118_AddMessengers', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823085629_AddWorkOrders'
)
BEGIN
    CREATE TABLE [WorkOrderAllowedAssignees] (
        [Id] int NOT NULL IDENTITY,
        [OwnerUserId] int NOT NULL,
        [TargetUserId] int NOT NULL,
        CONSTRAINT [PK_WorkOrderAllowedAssignees] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823085629_AddWorkOrders'
)
BEGIN
    CREATE TABLE [WorkOrderAssignees] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [UserId] int NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [SeenAt] datetime2 NULL,
        [RepliedAt] datetime2 NULL,
        [Done] bit NULL,
        [ReplyText] nvarchar(2000) NULL,
        [OwnerDecision] nvarchar(20) NULL,
        [OwnerDecisionNote] nvarchar(1000) NULL,
        CONSTRAINT [PK_WorkOrderAssignees] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823085629_AddWorkOrders'
)
BEGIN
    CREATE TABLE [WorkOrderAttachments] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [FileName] nvarchar(255) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [Data] varbinary(max) NOT NULL,
        [UploaderName] nvarchar(150) NOT NULL,
        [UploaderUserId] int NOT NULL,
        [UploadedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_WorkOrderAttachments] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823085629_AddWorkOrders'
)
BEGIN
    CREATE TABLE [WorkOrderLogs] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [ActorName] nvarchar(150) NOT NULL,
        [Action] nvarchar(30) NOT NULL,
        [Text] nvarchar(4000) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_WorkOrderLogs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823085629_AddWorkOrders'
)
BEGIN
    CREATE TABLE [WorkOrders] (
        [Id] int NOT NULL IDENTITY,
        [Number] nvarchar(30) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Description] nvarchar(max) NOT NULL,
        [OwnerUserId] int NOT NULL,
        [OwnerName] nvarchar(150) NOT NULL,
        [DueAt] datetime2 NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [CloseNote] nvarchar(1000) NULL,
        [ClosedAt] datetime2 NULL,
        [ExtensionCount] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_WorkOrders] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823085629_AddWorkOrders'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823085629_AddWorkOrders', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823094428_AddArchiveAndAttachments'
)
BEGIN
    ALTER TABLE [Users] ADD [FirstName] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823094428_AddArchiveAndAttachments'
)
BEGIN
    ALTER TABLE [Users] ADD [LastName] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823094428_AddArchiveAndAttachments'
)
BEGIN
    CREATE TABLE [AppAttachments] (
        [Id] int NOT NULL IDENTITY,
        [Module] nvarchar(50) NOT NULL,
        [RefId] int NOT NULL,
        [FileName] nvarchar(255) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [Data] varbinary(max) NOT NULL,
        [UploaderName] nvarchar(150) NOT NULL,
        [UploaderUserId] int NOT NULL,
        [UploadedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_AppAttachments] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823094428_AddArchiveAndAttachments'
)
BEGIN
    CREATE TABLE [ArchiveFolders] (
        [Id] int NOT NULL IDENTITY,
        [OwnerUserId] int NOT NULL,
        [ParentId] int NULL,
        [Name] nvarchar(150) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ArchiveFolders] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823094428_AddArchiveAndAttachments'
)
BEGIN
    CREATE TABLE [ArchiveItems] (
        [Id] int NOT NULL IDENTITY,
        [OwnerUserId] int NOT NULL,
        [FolderId] int NOT NULL,
        [Module] nvarchar(50) NOT NULL,
        [RefId] int NOT NULL,
        [Title] nvarchar(250) NOT NULL,
        [Link] nvarchar(250) NULL,
        [Note] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ArchiveItems] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823094428_AddArchiveAndAttachments'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823094428_AddArchiveAndAttachments', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823120204_AddProjectManagement'
)
BEGIN
    CREATE TABLE [KarFarmas] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(200) NOT NULL,
        [Address] nvarchar(500) NULL,
        [ModirAmelPhone] nvarchar(20) NULL,
        [Telephone] nvarchar(20) NULL,
        [Fax] nvarchar(20) NULL,
        [ShomareSabt] nvarchar(50) NULL,
        [IsDelete] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_KarFarmas] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823120204_AddProjectManagement'
)
BEGIN
    CREATE TABLE [TypeFactors] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(150) NOT NULL,
        [IsDelete] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_TypeFactors] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823120204_AddProjectManagement'
)
BEGIN
    CREATE TABLE [ProjectEntryExits] (
        [Id] int NOT NULL IDENTITY,
        [ReturnProjectId] int NOT NULL,
        [SerialNumber] nvarchar(50) NOT NULL,
        [ProjectName] nvarchar(250) NOT NULL,
        [GhabzExit] nvarchar(50) NULL,
        [FactorNumber] nvarchar(50) NULL,
        [KarshenasiAvalie] nvarchar(50) NULL,
        [ProjectReceiver] nvarchar(200) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [KarFarmaId] int NOT NULL,
        [FactorTypeId] int NOT NULL,
        [UserId] int NOT NULL,
        [ExitDate] datetime2 NULL,
        [EntryDate] datetime2 NULL,
        [FileDate] datetime2 NULL,
        [DeliveryDate] datetime2 NULL,
        [TemporaryExitDate] datetime2 NULL,
        [ProjectRegistrationDate] datetime2 NULL,
        [CustomerRequiredDate] datetime2 NULL,
        [IsFolder] bit NULL,
        [IsDelete] bit NOT NULL,
        [TotalSpentTime] time NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ProjectEntryExits] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProjectEntryExits_KarFarmas_KarFarmaId] FOREIGN KEY ([KarFarmaId]) REFERENCES [KarFarmas] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProjectEntryExits_TypeFactors_FactorTypeId] FOREIGN KEY ([FactorTypeId]) REFERENCES [TypeFactors] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProjectEntryExits_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823120204_AddProjectManagement'
)
BEGIN
    CREATE TABLE [ProjectAttaches] (
        [Id] int NOT NULL IDENTITY,
        [OriginalFileNameEncrypted] nvarchar(max) NOT NULL,
        [StoredFileName] nvarchar(100) NOT NULL,
        [Extension] nvarchar(20) NOT NULL,
        [FileSize] bigint NOT NULL,
        [DateSabt] datetime2 NOT NULL,
        [Type] int NOT NULL,
        [IsDelete] bit NOT NULL,
        [UserId] int NOT NULL,
        [ProjectId] int NOT NULL,
        CONSTRAINT [PK_ProjectAttaches] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProjectAttaches_ProjectEntryExits_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [ProjectEntryExits] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ProjectAttaches_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823120204_AddProjectManagement'
)
BEGIN
    CREATE TABLE [ReportWorks] (
        [Id] int NOT NULL IDENTITY,
        [ReportDate] datetime2 NOT NULL,
        [UserId] int NOT NULL,
        [WorkDescription] nvarchar(1000) NOT NULL,
        [ProjectId] int NOT NULL,
        [StartTime] time NOT NULL,
        [EndTime] time NOT NULL,
        [BreakfastTime] time NOT NULL,
        [LunchTime] time NOT NULL,
        [SpentTime] time NOT NULL,
        [IsDelete] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ReportWorks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ReportWorks_ProjectEntryExits_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [ProjectEntryExits] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ReportWorks_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823120204_AddProjectManagement'
)
BEGIN
    CREATE INDEX [IX_KarFarmas_Name] ON [KarFarmas] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823120204_AddProjectManagement'
)
BEGIN
    CREATE INDEX [IX_ProjectAttaches_ProjectId] ON [ProjectAttaches] ([ProjectId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823120204_AddProjectManagement'
)
BEGIN
    CREATE INDEX [IX_ProjectAttaches_UserId] ON [ProjectAttaches] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823120204_AddProjectManagement'
)
BEGIN
    CREATE INDEX [IX_ProjectEntryExits_FactorTypeId] ON [ProjectEntryExits] ([FactorTypeId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823120204_AddProjectManagement'
)
BEGIN
    CREATE INDEX [IX_ProjectEntryExits_KarFarmaId] ON [ProjectEntryExits] ([KarFarmaId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823120204_AddProjectManagement'
)
BEGIN
    CREATE INDEX [IX_ProjectEntryExits_SerialNumber] ON [ProjectEntryExits] ([SerialNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823120204_AddProjectManagement'
)
BEGIN
    CREATE INDEX [IX_ProjectEntryExits_UserId] ON [ProjectEntryExits] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823120204_AddProjectManagement'
)
BEGIN
    CREATE INDEX [IX_ReportWorks_ProjectId] ON [ReportWorks] ([ProjectId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823120204_AddProjectManagement'
)
BEGIN
    CREATE INDEX [IX_ReportWorks_UserId] ON [ReportWorks] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823120204_AddProjectManagement'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823120204_AddProjectManagement', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824050941_AddSystemIdModuleFeatures'
)
BEGIN
    ALTER TABLE [SystemDisks] ADD [SmartStatus] nvarchar(20) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824050941_AddSystemIdModuleFeatures'
)
BEGIN
    ALTER TABLE [SystemDisks] ADD [SmartUpdatedAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824050941_AddSystemIdModuleFeatures'
)
BEGIN
    CREATE TABLE [SystemHandovers] (
        [Id] int NOT NULL IDENTITY,
        [SystemInfoId] int NOT NULL,
        [FromUserName] nvarchar(200) NOT NULL,
        [ToUserId] int NULL,
        [ToUserName] nvarchar(200) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [IsCompleted] bit NOT NULL,
        [CompletedAt] datetime2 NULL,
        [ChecklistJson] nvarchar(max) NOT NULL,
        [SignatureDataUrl] nvarchar(max) NULL,
        [Note] nvarchar(500) NULL,
        CONSTRAINT [PK_SystemHandovers] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824050941_AddSystemIdModuleFeatures'
)
BEGIN
    CREATE TABLE [SystemInfoUserHistories] (
        [Id] int NOT NULL IDENTITY,
        [SystemInfoId] int NOT NULL,
        [UserId] int NULL,
        [UserName] nvarchar(200) NOT NULL,
        [StaffNumber] nvarchar(50) NULL,
        [CompanyName] nvarchar(200) NULL,
        [FromAt] datetime2 NOT NULL,
        [ToAt] datetime2 NULL,
        CONSTRAINT [PK_SystemInfoUserHistories] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824050941_AddSystemIdModuleFeatures'
)
BEGIN
    CREATE TABLE [SystemRemoteCommands] (
        [Id] int NOT NULL IDENTITY,
        [SystemInfoId] int NOT NULL,
        [Action] nvarchar(30) NOT NULL,
        [Status] nvarchar(20) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ByUserName] nvarchar(200) NULL,
        [CompletedAt] datetime2 NULL,
        [Result] nvarchar(500) NULL,
        CONSTRAINT [PK_SystemRemoteCommands] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824050941_AddSystemIdModuleFeatures'
)
BEGIN
    CREATE INDEX [IX_SystemHandovers_SystemInfoId] ON [SystemHandovers] ([SystemInfoId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824050941_AddSystemIdModuleFeatures'
)
BEGIN
    CREATE INDEX [IX_SystemInfoUserHistories_SystemInfoId] ON [SystemInfoUserHistories] ([SystemInfoId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824050941_AddSystemIdModuleFeatures'
)
BEGIN
    CREATE INDEX [IX_SystemRemoteCommands_Status] ON [SystemRemoteCommands] ([Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824050941_AddSystemIdModuleFeatures'
)
BEGIN
    CREATE INDEX [IX_SystemRemoteCommands_SystemInfoId] ON [SystemRemoteCommands] ([SystemInfoId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824050941_AddSystemIdModuleFeatures'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824050941_AddSystemIdModuleFeatures', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824051902_ProjectFactorOptional'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[ProjectEntryExits]') AND [c].[name] = N'FactorTypeId');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [ProjectEntryExits] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [ProjectEntryExits] ALTER COLUMN [FactorTypeId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824051902_ProjectFactorOptional'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824051902_ProjectFactorOptional', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824075826_AddUserPhotosAndFileStore'
)
BEGIN
    ALTER TABLE [WorkOrderAttachments] ADD [FilePath] nvarchar(255) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824075826_AddUserPhotosAndFileStore'
)
BEGIN
    ALTER TABLE [Users] ADD [PhotoPath] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824075826_AddUserPhotosAndFileStore'
)
BEGIN
    ALTER TABLE [SystemUsers] ADD [PhotoPath] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824075826_AddUserPhotosAndFileStore'
)
BEGIN
    ALTER TABLE [ItRequestAttachments] ADD [FilePath] nvarchar(255) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824075826_AddUserPhotosAndFileStore'
)
BEGIN
    ALTER TABLE [AppAttachments] ADD [FilePath] nvarchar(255) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824075826_AddUserPhotosAndFileStore'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824075826_AddUserPhotosAndFileStore', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824081257_AddCodeProject'
)
BEGIN
    ALTER TABLE [ReportWorks] ADD [CodeProject] nvarchar(60) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824081257_AddCodeProject'
)
BEGIN
    ALTER TABLE [ProjectEntryExits] ADD [CodeProject] nvarchar(60) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824081257_AddCodeProject'
)
BEGIN
    CREATE INDEX [IX_ProjectEntryExits_CodeProject] ON [ProjectEntryExits] ([CodeProject]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260824081257_AddCodeProject'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260824081257_AddCodeProject', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825045236_AddLeaveRequests'
)
BEGIN
    CREATE TABLE [LeaveRequests] (
        [Id] int NOT NULL IDENTITY,
        [Number] nvarchar(30) NOT NULL,
        [Type] nvarchar(20) NOT NULL,
        [RequesterUserId] int NOT NULL,
        [RequesterName] nvarchar(150) NOT NULL,
        [StartDate] datetime2 NOT NULL,
        [EndDate] datetime2 NOT NULL,
        [Days] float NOT NULL,
        [Hours] int NOT NULL,
        [Destination] nvarchar(200) NULL,
        [Reason] nvarchar(1000) NULL,
        [Status] nvarchar(20) NOT NULL,
        [ApprovedByUserId] int NULL,
        [ApprovedByName] nvarchar(max) NULL,
        [ApprovedAt] datetime2 NULL,
        [ApproveNote] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_LeaveRequests] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825045236_AddLeaveRequests'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260825045236_AddLeaveRequests', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825121203_AddProjectFlowCartableAndAttachFolders'
)
BEGIN
    ALTER TABLE [ProjectEntryExits] ADD [ExpertActionAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825121203_AddProjectFlowCartableAndAttachFolders'
)
BEGIN
    ALTER TABLE [ProjectEntryExits] ADD [ExpertActionById] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825121203_AddProjectFlowCartableAndAttachFolders'
)
BEGIN
    ALTER TABLE [ProjectEntryExits] ADD [ExpertNote] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825121203_AddProjectFlowCartableAndAttachFolders'
)
BEGIN
    ALTER TABLE [ProjectEntryExits] ADD [FlowStatus] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825121203_AddProjectFlowCartableAndAttachFolders'
)
BEGIN
    ALTER TABLE [ProjectEntryExits] ADD [ManagerActionAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825121203_AddProjectFlowCartableAndAttachFolders'
)
BEGIN
    ALTER TABLE [ProjectEntryExits] ADD [ManagerActionById] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825121203_AddProjectFlowCartableAndAttachFolders'
)
BEGIN
    ALTER TABLE [ProjectEntryExits] ADD [ManagerNote] nvarchar(500) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825121203_AddProjectFlowCartableAndAttachFolders'
)
BEGIN
    UPDATE [ProjectEntryExits] SET [FlowStatus] = 3 WHERE [FlowStatus] = 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825121203_AddProjectFlowCartableAndAttachFolders'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260825121203_AddProjectFlowCartableAndAttachFolders', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826035600_AddShiftGroupsAndAttendance'
)
BEGIN
    ALTER TABLE [Users] ADD [ShiftGroupId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826035600_AddShiftGroupsAndAttendance'
)
BEGIN
    CREATE TABLE [ShiftGroups] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [Description] nvarchar(200) NULL,
        [StartTime] time NOT NULL,
        [EndTime] time NOT NULL,
        [GraceMinutes] int NOT NULL,
        [IncludeFriday] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ShiftGroups] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826035600_AddShiftGroupsAndAttendance'
)
BEGIN
    CREATE TABLE [AttendanceRecords] (
        [Id] int NOT NULL IDENTITY,
        [WorkDate] datetime2 NOT NULL,
        [UserId] int NOT NULL,
        [UserName] nvarchar(150) NOT NULL,
        [ShiftGroupId] int NULL,
        [EnterAt] datetime2 NULL,
        [ExitAt] datetime2 NULL,
        [Note] nvarchar(500) NULL,
        [EnterIp] nvarchar(50) NULL,
        [ExitIp] nvarchar(50) NULL,
        [EnterStatus] nvarchar(20) NULL,
        [LateMinutes] int NOT NULL,
        [EarlyLeaveMinutes] int NOT NULL,
        [WorkMinutes] int NOT NULL,
        [DeficitMinutes] int NOT NULL,
        [HasApprovedLeave] bit NOT NULL,
        [FinalStatus] nvarchar(20) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_AttendanceRecords] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AttendanceRecords_ShiftGroups_ShiftGroupId] FOREIGN KEY ([ShiftGroupId]) REFERENCES [ShiftGroups] ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826035600_AddShiftGroupsAndAttendance'
)
BEGIN
    CREATE INDEX [IX_Users_ShiftGroupId] ON [Users] ([ShiftGroupId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826035600_AddShiftGroupsAndAttendance'
)
BEGIN
    CREATE INDEX [IX_AttendanceRecords_ShiftGroupId] ON [AttendanceRecords] ([ShiftGroupId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826035600_AddShiftGroupsAndAttendance'
)
BEGIN
    ALTER TABLE [Users] ADD CONSTRAINT [FK_Users_ShiftGroups_ShiftGroupId] FOREIGN KEY ([ShiftGroupId]) REFERENCES [ShiftGroups] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826035600_AddShiftGroupsAndAttendance'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260826035600_AddShiftGroupsAndAttendance', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826044011_AddHourlyTimesAndAttendanceSegments'
)
BEGIN
    -- Hours: int -> float (فقط اگر هنوز int باشد)
    IF EXISTS (
        SELECT 1 FROM sys.columns c
        JOIN sys.types t ON c.user_type_id = t.user_type_id
        WHERE c.object_id = OBJECT_ID(N'[dbo].[LeaveRequests]')
          AND c.name = N'Hours' AND t.name IN ('int', 'smallint', 'tinyint')
    )
        ALTER TABLE [dbo].[LeaveRequests] ALTER COLUMN [Hours] float NOT NULL;
    -- ستون‌های ساعتی (فقط اگر نباشند)
    IF COL_LENGTH(N'[dbo].[LeaveRequests]', N'EndTime') IS NULL
        ALTER TABLE [dbo].[LeaveRequests] ADD [EndTime] time NULL;
    IF COL_LENGTH(N'[dbo].[LeaveRequests]', N'StartTime') IS NULL
        ALTER TABLE [dbo].[LeaveRequests] ADD [StartTime] time NULL;
    IF COL_LENGTH(N'[dbo].[AttendanceRecords]', N'CoveredGapMinutes') IS NULL
        ALTER TABLE [dbo].[AttendanceRecords] ADD [CoveredGapMinutes] int NOT NULL CONSTRAINT [DF_AttendanceRecords_CoveredGapMinutes] DEFAULT 0;
    -- جدول بازه‌های ورود/خروج (فقط اگر نباشد)
    IF OBJECT_ID(N'[dbo].[AttendanceSegments]', N'U') IS NULL
    BEGIN
        CREATE TABLE [dbo].[AttendanceSegments] (
            [Id] int NOT NULL IDENTITY,
            [UserId] int NOT NULL,
            [UserName] nvarchar(150) NOT NULL,
            [WorkDate] datetime2 NOT NULL,
            [Seq] int NOT NULL,
            [EnterAt] datetime2 NULL,
            [EnterIp] nvarchar(max) NULL,
            [ExitAt] datetime2 NULL,
            [ExitIp] nvarchar(max) NULL,
            [EnterStatus] nvarchar(20) NULL,
            [LateMinutes] int NOT NULL,
            [ExitCovered] bit NOT NULL,
            [LinkedLeaveRequestId] int NULL,
            [LinkedLeaveNumber] nvarchar(30) NULL,
            [Note] nvarchar(500) NULL,
            [CreatedAt] datetime2 NOT NULL,
            CONSTRAINT [PK_AttendanceSegments] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_AttendanceSegments_LeaveRequests_LinkedLeaveRequestId]
                FOREIGN KEY ([LinkedLeaveRequestId]) REFERENCES [dbo].[LeaveRequests] ([Id]) ON DELETE SET NULL
        );
    END;
    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'IX_AttendanceSegments_LinkedLeaveRequestId'
          AND object_id = OBJECT_ID(N'[dbo].[AttendanceSegments]')
    )
        CREATE INDEX [IX_AttendanceSegments_LinkedLeaveRequestId]
            ON [dbo].[AttendanceSegments] ([LinkedLeaveRequestId]);
    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'IX_AttendanceSegments_UserId_WorkDate_Seq'
          AND object_id = OBJECT_ID(N'[dbo].[AttendanceSegments]')
    )
        CREATE UNIQUE INDEX [IX_AttendanceSegments_UserId_WorkDate_Seq]
            ON [dbo].[AttendanceSegments] ([UserId], [WorkDate], [Seq]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826044011_AddHourlyTimesAndAttendanceSegments'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260826044011_AddHourlyTimesAndAttendanceSegments', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826101303_AddCompanyHolidaysAndAdminLeave'
)
BEGIN
    ALTER TABLE [LeaveRequests] ADD [AdminCreated] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826101303_AddCompanyHolidaysAndAdminLeave'
)
BEGIN
    CREATE TABLE [CompanyHolidays] (
        [Id] int NOT NULL IDENTITY,
        [HolidayDate] datetime2 NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [CreatedByName] nvarchar(150) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_CompanyHolidays] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826101303_AddCompanyHolidaysAndAdminLeave'
)
BEGIN
    CREATE INDEX [IX_CompanyHolidays_HolidayDate] ON [CompanyHolidays] ([HolidayDate]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260826101303_AddCompanyHolidaysAndAdminLeave'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260826101303_AddCompanyHolidaysAndAdminLeave', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831041448_AddWorkCalendarAndOvertime'
)
BEGIN
    ALTER TABLE [AttendanceSegments] ADD [IsUnauthorized] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831041448_AddWorkCalendarAndOvertime'
)
BEGIN
    ALTER TABLE [AttendanceSegments] ADD [OvertimeMinutes] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831041448_AddWorkCalendarAndOvertime'
)
BEGIN
    ALTER TABLE [AttendanceRecords] ADD [OvertimeMinutes] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831041448_AddWorkCalendarAndOvertime'
)
BEGIN
    ALTER TABLE [AttendanceRecords] ADD [UnauthorizedMinutes] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831041448_AddWorkCalendarAndOvertime'
)
BEGIN
    CREATE TABLE [WorkCalendarDays] (
        [Id] int NOT NULL IDENTITY,
        [Date] datetime2 NOT NULL,
        [IsWorkday] bit NOT NULL,
        [StartTime] time NULL,
        [EndTime] time NULL,
        [GraceMinutes] int NOT NULL,
        [OvertimeHours] float NOT NULL,
        [Note] nvarchar(100) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_WorkCalendarDays] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831041448_AddWorkCalendarAndOvertime'
)
BEGIN
    CREATE UNIQUE INDEX [IX_WorkCalendarDays_Date] ON [WorkCalendarDays] ([Date]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831041448_AddWorkCalendarAndOvertime'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260831041448_AddWorkCalendarAndOvertime', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831062257_AddOvertimeModesToWorkCalendar'
)
BEGIN
    ALTER TABLE [WorkCalendarDays] ADD [OvertimeEnd] time NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831062257_AddOvertimeModesToWorkCalendar'
)
BEGIN
    ALTER TABLE [WorkCalendarDays] ADD [OvertimeMode] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831062257_AddOvertimeModesToWorkCalendar'
)
BEGIN
    ALTER TABLE [WorkCalendarDays] ADD [OvertimeStart] time NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831062257_AddOvertimeModesToWorkCalendar'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260831062257_AddOvertimeModesToWorkCalendar', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831102303_AddCalendarSettingsAndOfficialHolidays'
)
BEGIN
    ALTER TABLE [CompanyHolidays] ADD [IsOfficial] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831102303_AddCalendarSettingsAndOfficialHolidays'
)
BEGIN
    CREATE TABLE [WorkCalendarSettings] (
        [Id] int NOT NULL IDENTITY,
        [DefaultStart] time NOT NULL,
        [DefaultEnd] time NOT NULL,
        [GraceMinutes] int NOT NULL,
        [RestDayFlags] int NOT NULL,
        [ApplyOfficialHolidays] bit NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_WorkCalendarSettings] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831102303_AddCalendarSettingsAndOfficialHolidays'
)
BEGIN
    CREATE UNIQUE INDEX [IX_WorkCalendarSettings_Id] ON [WorkCalendarSettings] ([Id]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831102303_AddCalendarSettingsAndOfficialHolidays'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260831102303_AddCalendarSettingsAndOfficialHolidays', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831103000_AddOfficeAutomation'
)
BEGIN
    CREATE TABLE [Amalgars] (
        [AmalgarId] int NOT NULL IDENTITY,
        [Title] nvarchar(100) NOT NULL,
        [TaeedEmza] nvarchar(30) NOT NULL,
        [IsDelete] bit NOT NULL,
        CONSTRAINT [PK_Amalgars] PRIMARY KEY ([AmalgarId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831103000_AddOfficeAutomation'
)
BEGIN
    CREATE TABLE [LetterBayeganis] (
        [BayeganiId] int NOT NULL IDENTITY,
        [Title] nvarchar(200) NOT NULL,
        [ErjaId] int NULL,
        [ParentId] int NOT NULL,
        [UserId] int NOT NULL,
        [SematId] int NULL,
        [TypeBayegani] int NOT NULL,
        [IsFolder] bit NOT NULL,
        [IsDelete] bit NOT NULL,
        CONSTRAINT [PK_LetterBayeganis] PRIMARY KEY ([BayeganiId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831103000_AddOfficeAutomation'
)
BEGIN
    CREATE TABLE [LetterSources] (
        [Id] int NOT NULL IDENTITY,
        [SourceType] int NOT NULL,
        [IsDelete] bit NOT NULL,
        CONSTRAINT [PK_LetterSources] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831103000_AddOfficeAutomation'
)
BEGIN
    CREATE TABLE [PishnevisLetters] (
        [PishnevisId] int NOT NULL IDENTITY,
        [Title] nvarchar(300) NOT NULL,
        [Text] nvarchar(max) NOT NULL,
        [UserId] int NOT NULL,
        [SematId] int NULL,
        [IsNeshan] bit NOT NULL,
        [IsDelete] bit NOT NULL,
        CONSTRAINT [PK_PishnevisLetters] PRIMARY KEY ([PishnevisId]),
        CONSTRAINT [FK_PishnevisLetters_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831103000_AddOfficeAutomation'
)
BEGIN
    CREATE TABLE [Erjas] (
        [ErjaId] int NOT NULL IDENTITY,
        [SourceId] int NOT NULL,
        [SenderUserId] int NOT NULL,
        [ReciverUserId] int NOT NULL,
        [SenderSematId] int NULL,
        [ReciverSematId] int NULL,
        [Date] datetime2 NOT NULL,
        [Type] nvarchar(20) NOT NULL,
        [TypeTaeed] int NOT NULL,
        [Answer] nvarchar(max) NOT NULL,
        [IsRead] bit NOT NULL,
        [IsBayegani] bit NULL,
        [MohlatPasokh] datetime2 NULL,
        [MatnErja] nvarchar(max) NOT NULL,
        [AmalgarId] int NOT NULL,
        [IsNeshan] bit NOT NULL,
        [ShowForAll] bit NOT NULL,
        [ShowMassage] bit NOT NULL,
        [DateRead] datetime2 NULL,
        [DateEmza] datetime2 NULL,
        [DateAnswer] datetime2 NULL,
        [IsReadAnswer] bit NOT NULL,
        [ShowMassageAnswer] bit NOT NULL,
        [IsDelete] bit NOT NULL,
        [ParentErjaId] int NULL,
        CONSTRAINT [PK_Erjas] PRIMARY KEY ([ErjaId]),
        CONSTRAINT [FK_Erjas_Amalgars_AmalgarId] FOREIGN KEY ([AmalgarId]) REFERENCES [Amalgars] ([AmalgarId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Erjas_LetterSources_SourceId] FOREIGN KEY ([SourceId]) REFERENCES [LetterSources] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Erjas_Users_ReciverUserId] FOREIGN KEY ([ReciverUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Erjas_Users_SenderUserId] FOREIGN KEY ([SenderUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831103000_AddOfficeAutomation'
)
BEGIN
    CREATE TABLE [InnerLetters] (
        [Id] int NOT NULL,
        [LetterNumber] nvarchar(60) NULL,
        [Number] int NOT NULL,
        [CreatorUserId] int NOT NULL,
        [CreatorSematId] int NULL,
        [Title] nvarchar(300) NOT NULL,
        [Text] nvarchar(max) NULL,
        [DateSabt] datetime2 NOT NULL,
        [Mahramanegi] nvarchar(20) NOT NULL,
        [Foriat] nvarchar(20) NOT NULL,
        [IsDelete] bit NOT NULL,
        CONSTRAINT [PK_InnerLetters] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InnerLetters_LetterSources_Id] FOREIGN KEY ([Id]) REFERENCES [LetterSources] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_InnerLetters_Users_CreatorUserId] FOREIGN KEY ([CreatorUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831103000_AddOfficeAutomation'
)
BEGIN
    CREATE TABLE [RelatedLetters] (
        [Id] int NOT NULL IDENTITY,
        [Related] int NOT NULL,
        [LetterId] int NOT NULL,
        [RelateLetterId] int NOT NULL,
        [UserId] int NOT NULL,
        [SematId] int NULL,
        [IsDelete] bit NOT NULL,
        CONSTRAINT [PK_RelatedLetters] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RelatedLetters_LetterSources_LetterId] FOREIGN KEY ([LetterId]) REFERENCES [LetterSources] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_RelatedLetters_LetterSources_RelateLetterId] FOREIGN KEY ([RelateLetterId]) REFERENCES [LetterSources] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831103000_AddOfficeAutomation'
)
BEGIN
    CREATE INDEX [IX_Erjas_AmalgarId] ON [Erjas] ([AmalgarId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831103000_AddOfficeAutomation'
)
BEGIN
    CREATE INDEX [IX_Erjas_ReciverUserId] ON [Erjas] ([ReciverUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831103000_AddOfficeAutomation'
)
BEGIN
    CREATE INDEX [IX_Erjas_ReciverUserId_IsRead] ON [Erjas] ([ReciverUserId], [IsRead]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831103000_AddOfficeAutomation'
)
BEGIN
    CREATE INDEX [IX_Erjas_SenderUserId] ON [Erjas] ([SenderUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831103000_AddOfficeAutomation'
)
BEGIN
    CREATE INDEX [IX_Erjas_SourceId] ON [Erjas] ([SourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831103000_AddOfficeAutomation'
)
BEGIN
    CREATE INDEX [IX_InnerLetters_CreatorUserId] ON [InnerLetters] ([CreatorUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831103000_AddOfficeAutomation'
)
BEGIN
    CREATE INDEX [IX_InnerLetters_DateSabt] ON [InnerLetters] ([DateSabt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831103000_AddOfficeAutomation'
)
BEGIN
    CREATE INDEX [IX_InnerLetters_Number] ON [InnerLetters] ([Number]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831103000_AddOfficeAutomation'
)
BEGIN
    CREATE INDEX [IX_LetterBayeganis_UserId] ON [LetterBayeganis] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831103000_AddOfficeAutomation'
)
BEGIN
    CREATE INDEX [IX_PishnevisLetters_UserId] ON [PishnevisLetters] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831103000_AddOfficeAutomation'
)
BEGIN
    CREATE INDEX [IX_RelatedLetters_LetterId] ON [RelatedLetters] ([LetterId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831103000_AddOfficeAutomation'
)
BEGIN
    CREATE INDEX [IX_RelatedLetters_RelateLetterId] ON [RelatedLetters] ([RelateLetterId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831103000_AddOfficeAutomation'
)
BEGIN
    CREATE TABLE [LetterGroups] (
        [GroupId] int NOT NULL IDENTITY,
        [NameGroup] nvarchar(150) NOT NULL,
        [Condition] bit NOT NULL,
        [IsDelete] bit NOT NULL,
        [CreatorUserId] int NOT NULL,
        [CreatorSematId] int NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_LetterGroups] PRIMARY KEY ([GroupId]),
        CONSTRAINT [FK_LetterGroups_Users_CreatorUserId] FOREIGN KEY ([CreatorUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831103000_AddOfficeAutomation'
)
BEGIN
    CREATE TABLE [LetterGroupMembers] (
        [Id] int NOT NULL IDENTITY,
        [GroupId] int NOT NULL,
        [UserId] int NOT NULL,
        [SematId] int NULL,
        CONSTRAINT [PK_LetterGroupMembers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_LetterGroupMembers_LetterGroups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [LetterGroups] ([GroupId]) ON DELETE CASCADE,
        CONSTRAINT [FK_LetterGroupMembers_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831103000_AddOfficeAutomation'
)
BEGIN
    CREATE UNIQUE INDEX [IX_LetterGroupMembers_GroupId_UserId] ON [LetterGroupMembers] ([GroupId], [UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831103000_AddOfficeAutomation'
)
BEGIN
    CREATE INDEX [IX_LetterGroupMembers_UserId] ON [LetterGroupMembers] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831103000_AddOfficeAutomation'
)
BEGIN
    CREATE INDEX [IX_LetterGroups_CreatorUserId] ON [LetterGroups] ([CreatorUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831103000_AddOfficeAutomation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260831103000_AddOfficeAutomation', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901000000_AddOutgoingLetters'
)
BEGIN
    CREATE TABLE [OutgoingPishnevisLetters] (
        [PishnevisId] int NOT NULL IDENTITY,
        [Title] nvarchar(300) NOT NULL,
        [Text] nvarchar(max) NOT NULL,
        [ReceiverOrganization] nvarchar(250) NULL,
        [ReceiverName] nvarchar(250) NULL,
        [ReceiverTitle] nvarchar(250) NULL,
        [UserId] int NOT NULL,
        [SematId] int NULL,
        [IsNeshan] bit NOT NULL,
        [IsDelete] bit NOT NULL,
        CONSTRAINT [PK_OutgoingPishnevisLetters] PRIMARY KEY ([PishnevisId]),
        CONSTRAINT [FK_OutgoingPishnevisLetters_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901000000_AddOutgoingLetters'
)
BEGIN
    CREATE TABLE [OutgoingLetters] (
        [Id] int NOT NULL,
        [LetterNumber] nvarchar(60) NULL,
        [Number] int NOT NULL,
        [CreatorUserId] int NOT NULL,
        [CreatorSematId] int NULL,
        [Title] nvarchar(300) NOT NULL,
        [Text] nvarchar(max) NULL,
        [DateSabt] datetime2 NOT NULL,
        [Mahramanegi] nvarchar(20) NOT NULL,
        [Foriat] nvarchar(20) NOT NULL,
        [ReceiverOrganization] nvarchar(250) NOT NULL,
        [ReceiverName] nvarchar(250) NULL,
        [ReceiverTitle] nvarchar(250) NULL,
        [ReceiverAddress] nvarchar(500) NULL,
        [CopyTo] nvarchar(1000) NULL,
        [ExternalRefNumber] nvarchar(100) NULL,
        [Status] int NOT NULL,
        [IsDelete] bit NOT NULL,
        CONSTRAINT [PK_OutgoingLetters] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OutgoingLetters_LetterSources_Id] FOREIGN KEY ([Id]) REFERENCES [LetterSources] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_OutgoingLetters_Users_CreatorUserId] FOREIGN KEY ([CreatorUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901000000_AddOutgoingLetters'
)
BEGIN
    CREATE INDEX [IX_OutgoingLetters_CreatorUserId] ON [OutgoingLetters] ([CreatorUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901000000_AddOutgoingLetters'
)
BEGIN
    CREATE INDEX [IX_OutgoingLetters_DateSabt] ON [OutgoingLetters] ([DateSabt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901000000_AddOutgoingLetters'
)
BEGIN
    CREATE INDEX [IX_OutgoingLetters_Number] ON [OutgoingLetters] ([Number]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901000000_AddOutgoingLetters'
)
BEGIN
    CREATE INDEX [IX_OutgoingLetters_ReceiverOrganization] ON [OutgoingLetters] ([ReceiverOrganization]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901000000_AddOutgoingLetters'
)
BEGIN
    CREATE INDEX [IX_OutgoingPishnevisLetters_UserId] ON [OutgoingPishnevisLetters] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901000000_AddOutgoingLetters'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260901000000_AddOutgoingLetters', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901042209_AddAuditLogAndDeviceTracking'
)
BEGIN
    ALTER TABLE [AttendanceSegments] ADD [EnterDevice] nvarchar(250) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901042209_AddAuditLogAndDeviceTracking'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [At] datetime2 NOT NULL,
        [UserId] int NULL,
        [Username] nvarchar(100) NULL,
        [Module] nvarchar(80) NOT NULL,
        [Action] nvarchar(80) NOT NULL,
        [HttpMethod] nvarchar(10) NOT NULL,
        [Path] nvarchar(300) NULL,
        [Summary] nvarchar(200) NULL,
        [Payload] nvarchar(4000) NULL,
        [Ip] nvarchar(64) NULL,
        [Device] nvarchar(250) NULL,
        [StatusCode] int NOT NULL,
        [DurationMs] int NOT NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901042209_AddAuditLogAndDeviceTracking'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_At] ON [AuditLogs] ([At]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901042209_AddAuditLogAndDeviceTracking'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_UserId] ON [AuditLogs] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901042209_AddAuditLogAndDeviceTracking'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260901042209_AddAuditLogAndDeviceTracking', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901090000_AddSplitShiftWindows'
)
BEGIN
    ALTER TABLE [ShiftGroups] ADD [StartTime2] time NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901090000_AddSplitShiftWindows'
)
BEGIN
    ALTER TABLE [ShiftGroups] ADD [EndTime2] time NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260901090000_AddSplitShiftWindows'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260901090000_AddSplitShiftWindows', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902000000_AddOutgoingSignersAndSadereNumber'
)
BEGIN
    ALTER TABLE [OutgoingLetters] ADD [SadereNumber] nvarchar(60) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902000000_AddOutgoingSignersAndSadereNumber'
)
BEGIN
    ALTER TABLE [OutgoingLetters] ADD [DateSadere] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902000000_AddOutgoingSignersAndSadereNumber'
)
BEGIN
    CREATE INDEX [IX_OutgoingLetters_SadereNumber] ON [OutgoingLetters] ([SadereNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902000000_AddOutgoingSignersAndSadereNumber'
)
BEGIN
    CREATE TABLE [OutgoingLetterSigners] (
        [Id] int NOT NULL IDENTITY,
        [SourceId] int NOT NULL,
        [UserId] int NOT NULL,
        [SematId] int NULL,
        [Order] int NOT NULL,
        [IsSigned] bit NOT NULL,
        [DateSigned] datetime2 NULL,
        [SignNote] nvarchar(1000) NULL,
        [IsDelete] bit NOT NULL,
        CONSTRAINT [PK_OutgoingLetterSigners] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OutgoingLetterSigners_LetterSources_SourceId] FOREIGN KEY ([SourceId]) REFERENCES [LetterSources] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_OutgoingLetterSigners_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902000000_AddOutgoingSignersAndSadereNumber'
)
BEGIN
    CREATE INDEX [IX_OutgoingLetterSigners_SourceId] ON [OutgoingLetterSigners] ([SourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902000000_AddOutgoingSignersAndSadereNumber'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OutgoingLetterSigners_SourceId_UserId] ON [OutgoingLetterSigners] ([SourceId], [UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902000000_AddOutgoingSignersAndSadereNumber'
)
BEGIN
    CREATE INDEX [IX_OutgoingLetterSigners_UserId] ON [OutgoingLetterSigners] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902000000_AddOutgoingSignersAndSadereNumber'
)
BEGIN
    CREATE INDEX [IX_OutgoingLetterSigners_UserId_IsSigned] ON [OutgoingLetterSigners] ([UserId], [IsSigned]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902000000_AddOutgoingSignersAndSadereNumber'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260902000000_AddOutgoingSignersAndSadereNumber', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902100000_AddOutgoingDabirkhaneAndLetterhead'
)
BEGIN
    ALTER TABLE [OutgoingLetters] ADD [CompanyId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902100000_AddOutgoingDabirkhaneAndLetterhead'
)
BEGIN
    ALTER TABLE [OutgoingLetters] ADD [DabirkhaneSabt] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902100000_AddOutgoingDabirkhaneAndLetterhead'
)
BEGIN
    ALTER TABLE [OutgoingLetters] ADD [DabirkhaneUserId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902100000_AddOutgoingDabirkhaneAndLetterhead'
)
BEGIN
    ALTER TABLE [OutgoingLetters] ADD [DateDabirkhane] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902100000_AddOutgoingDabirkhaneAndLetterhead'
)
BEGIN
    ALTER TABLE [OutgoingLetters] ADD [DestRegNumber] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902100000_AddOutgoingDabirkhaneAndLetterhead'
)
BEGIN
    ALTER TABLE [OutgoingLetters] ADD [SendMethod] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902100000_AddOutgoingDabirkhaneAndLetterhead'
)
BEGIN
    ALTER TABLE [OutgoingLetters] ADD [DabirkhaneNote] nvarchar(1000) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902100000_AddOutgoingDabirkhaneAndLetterhead'
)
BEGIN
    ALTER TABLE [SystemCompanies] ADD [LetterheadFileName] nvarchar(200) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902100000_AddOutgoingDabirkhaneAndLetterhead'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260902100000_AddOutgoingDabirkhaneAndLetterhead', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902110000_AddLetterStrature'
)
BEGIN
    CREATE TABLE [LetterStratures] (
        [StratureId] int NOT NULL IDENTITY,
        [TypeForm] int NOT NULL,
        [TypeStrature] nvarchar(50) NOT NULL,
        CONSTRAINT [PK_LetterStratures] PRIMARY KEY ([StratureId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902110000_AddLetterStrature'
)
BEGIN
    CREATE INDEX [IX_LetterStratures_TypeForm] ON [LetterStratures] ([TypeForm]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902110000_AddLetterStrature'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260902110000_AddLetterStrature', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902120000_AddLetterNeshan'
)
BEGIN
    ALTER TABLE [InnerLetters] ADD [IsNeshan] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902120000_AddLetterNeshan'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260902120000_AddLetterNeshan', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902130000_AddBayeganiLetterId'
)
BEGIN
    ALTER TABLE [LetterBayeganis] ADD [LetterId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260902130000_AddBayeganiLetterId'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260902130000_AddBayeganiLetterId', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903070000_LegacyAriaImportPrep'
)
BEGIN
    ALTER TABLE [ReportWorks] ADD [OperatorId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903070000_LegacyAriaImportPrep'
)
BEGIN
    DECLARE @var1 sysname;
    SELECT @var1 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[ProjectEntryExits]') AND [c].[name] = N'KarshenasiAvalie');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [ProjectEntryExits] DROP CONSTRAINT [' + @var1 + '];');
    ALTER TABLE [ProjectEntryExits] ALTER COLUMN [KarshenasiAvalie] nvarchar(100) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903070000_LegacyAriaImportPrep'
)
BEGIN
    ALTER TABLE [ProjectEntryExits] ADD [TotalSpentTime_Ticks] bigint NOT NULL CONSTRAINT [DF_ProjectEntryExits_TotalSpentTime_Ticks] DEFAULT(0);
    EXEC('UPDATE [ProjectEntryExits] SET [TotalSpentTime_Ticks] = CAST(DATEDIFF(millisecond, CAST(''00:00:00'' AS time), [TotalSpentTime]) AS bigint) * 10000;');
    ALTER TABLE [ProjectEntryExits] DROP COLUMN [TotalSpentTime];
    EXEC sp_rename 'ProjectEntryExits.TotalSpentTime_Ticks', 'TotalSpentTime', 'COLUMN';
    ALTER TABLE [ProjectEntryExits] DROP CONSTRAINT [DF_ProjectEntryExits_TotalSpentTime_Ticks];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903070000_LegacyAriaImportPrep'
)
BEGIN
    CREATE INDEX [IX_ReportWorks_OperatorId] ON [ReportWorks] ([OperatorId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903070000_LegacyAriaImportPrep'
)
BEGIN
    ALTER TABLE [ReportWorks] ADD CONSTRAINT [FK_ReportWorks_Users_OperatorId] FOREIGN KEY ([OperatorId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903070000_LegacyAriaImportPrep'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260903070000_LegacyAriaImportPrep', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903101000_AddWorkOrderSourceLink'
)
BEGIN
    ALTER TABLE [WorkOrders] ADD [SourceModule] nvarchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903101000_AddWorkOrderSourceLink'
)
BEGIN
    ALTER TABLE [WorkOrders] ADD [SourceId] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903101000_AddWorkOrderSourceLink'
)
BEGIN
    CREATE INDEX [IX_WorkOrders_SourceModule_SourceId] ON [WorkOrders] ([SourceModule], [SourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260903101000_AddWorkOrderSourceLink'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260903101000_AddWorkOrderSourceLink', N'8.0.1');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_PushSubscriptions_UserId' AND [object_id] = OBJECT_ID(N'[PushSubscriptions]'))
    BEGIN
        DROP INDEX [IX_PushSubscriptions_UserId] ON [PushSubscriptions];
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_PushSubscriptions_UserId_Endpoint' AND [object_id] = OBJECT_ID(N'[PushSubscriptions]'))
    BEGIN
        DROP INDEX [IX_PushSubscriptions_UserId_Endpoint] ON [PushSubscriptions];
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Warehouses]', N'AllowNegative') IS NULL
    BEGIN
        ALTER TABLE [Warehouses] ADD [AllowNegative] bit NOT NULL DEFAULT CAST(0 AS bit);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Warehouses]', N'Code') IS NULL
    BEGIN
        ALTER TABLE [Warehouses] ADD [Code] nvarchar(30) NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Warehouses]', N'IsDefault') IS NULL
    BEGIN
        ALTER TABLE [Warehouses] ADD [IsDefault] bit NOT NULL DEFAULT CAST(0 AS bit);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Warehouses]', N'KeeperName') IS NULL
    BEGIN
        ALTER TABLE [Warehouses] ADD [KeeperName] nvarchar(120) NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Warehouses]', N'Kind') IS NULL
    BEGIN
        ALTER TABLE [Warehouses] ADD [Kind] int NOT NULL DEFAULT 0;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[ShiftGroups]', N'EndTime2') IS NULL
    BEGIN
        ALTER TABLE [ShiftGroups] ADD [EndTime2] time NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[ShiftGroups]', N'StartTime2') IS NULL
    BEGIN
        ALTER TABLE [ShiftGroups] ADD [StartTime2] time NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'Brand') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [Brand] nvarchar(100) NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'CategoryId') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [CategoryId] int NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'CountryOfOrigin') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [CountryOfOrigin] nvarchar(80) NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'CustomsCode') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [CustomsCode] nvarchar(30) NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'DutyRate') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [DutyRate] decimal(6,2) NOT NULL DEFAULT 0.0;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'EnName') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [EnName] nvarchar(200) NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'Height') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [Height] decimal(18,3) NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'ImageUrl') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [ImageUrl] nvarchar(300) NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'IsVatIncluded') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [IsVatIncluded] bit NOT NULL DEFAULT CAST(0 AS bit);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'Length') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [Length] decimal(18,3) NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'MinOrderQty') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [MinOrderQty] decimal(18,3) NOT NULL DEFAULT 0.0;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'Model') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [Model] nvarchar(100) NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'Note') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [Note] nvarchar(max) NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'OtherTaxRate') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [OtherTaxRate] decimal(6,2) NOT NULL DEFAULT 0.0;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'PartNumber') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [PartNumber] nvarchar(100) NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'SalePrice2') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [SalePrice2] decimal(18,2) NOT NULL DEFAULT 0.0;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'SecondUnit') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [SecondUnit] nvarchar(50) NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'ShelfCode') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [ShelfCode] nvarchar(50) NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'ShelfLifeDays') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [ShelfLifeDays] int NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'TaxCode') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [TaxCode] nvarchar(20) NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'TaxUnitCode') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [TaxUnitCode] nvarchar(20) NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'TrackBatch') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [TrackBatch] bit NOT NULL DEFAULT CAST(0 AS bit);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'TrackExpiry') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [TrackExpiry] bit NOT NULL DEFAULT CAST(0 AS bit);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'TrackSerial') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [TrackSerial] bit NOT NULL DEFAULT CAST(0 AS bit);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'UnitFactor') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [UnitFactor] decimal(18,4) NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'Valuation') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [Valuation] int NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'VatRate') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [VatRate] decimal(6,2) NOT NULL DEFAULT 0.0;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'Weight') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [Weight] decimal(18,3) NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[Products]', N'Width') IS NULL
    BEGIN
        ALTER TABLE [Products] ADD [Width] decimal(18,3) NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[ProductCategories]', N'Code') IS NULL
    BEGIN
        ALTER TABLE [ProductCategories] ADD [Code] nvarchar(30) NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[ProductCategories]', N'SortOrder') IS NULL
    BEGIN
        ALTER TABLE [ProductCategories] ADD [SortOrder] int NOT NULL DEFAULT 0;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[ProductCategories]', N'Valuation') IS NULL
    BEGIN
        ALTER TABLE [ProductCategories] ADD [Valuation] int NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[OutgoingLetterSigners]', N'OutgoingLetterId') IS NULL
    BEGIN
        ALTER TABLE [OutgoingLetterSigners] ADD [OutgoingLetterId] int NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[CompanyHolidays]', N'IsOfficial') IS NULL
    BEGIN
        ALTER TABLE [CompanyHolidays] ADD [IsOfficial] bit NOT NULL DEFAULT CAST(0 AS bit);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[AttendanceSegments]', N'EnterDevice') IS NULL
    BEGIN
        ALTER TABLE [AttendanceSegments] ADD [EnterDevice] nvarchar(250) NULL;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[AttendanceSegments]', N'IsUnauthorized') IS NULL
    BEGIN
        ALTER TABLE [AttendanceSegments] ADD [IsUnauthorized] bit NOT NULL DEFAULT CAST(0 AS bit);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[AttendanceSegments]', N'OvertimeMinutes') IS NULL
    BEGIN
        ALTER TABLE [AttendanceSegments] ADD [OvertimeMinutes] int NOT NULL DEFAULT 0;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[AttendanceRecords]', N'OvertimeMinutes') IS NULL
    BEGIN
        ALTER TABLE [AttendanceRecords] ADD [OvertimeMinutes] int NOT NULL DEFAULT 0;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF COL_LENGTH(N'[AttendanceRecords]', N'UnauthorizedMinutes') IS NULL
    BEGIN
        ALTER TABLE [AttendanceRecords] ADD [UnauthorizedMinutes] int NOT NULL DEFAULT 0;
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[AuditLogs]', N'U') IS NULL
    BEGIN
        CREATE TABLE [AuditLogs] (
            [Id] bigint NOT NULL IDENTITY,
            [At] datetime2 NOT NULL,
            [UserId] int NULL,
            [Username] nvarchar(100) NULL,
            [Module] nvarchar(80) NOT NULL,
            [Action] nvarchar(80) NOT NULL,
            [HttpMethod] nvarchar(10) NOT NULL,
            [Path] nvarchar(300) NULL,
            [Summary] nvarchar(200) NULL,
            [Payload] nvarchar(4000) NULL,
            [Ip] nvarchar(64) NULL,
            [Device] nvarchar(250) NULL,
            [StatusCode] int NOT NULL,
            [DurationMs] int NOT NULL,
            CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
        );
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[InvDocs]', N'U') IS NULL
    BEGIN
        CREATE TABLE [InvDocs] (
            [Id] int NOT NULL IDENTITY,
            [Number] nvarchar(30) NOT NULL,
            [DocTypeId] int NOT NULL,
            [WarehouseId] int NOT NULL,
            [CounterWarehouseId] int NULL,
            [PartyId] int NULL,
            [Date] datetime2 NOT NULL,
            [RefNumber] nvarchar(50) NULL,
            [Description] nvarchar(max) NULL,
            [Status] int NOT NULL,
            [TotalQuantity] decimal(18,3) NOT NULL,
            [TotalValue] decimal(18,2) NOT NULL,
            [CreatedBy] nvarchar(80) NULL,
            [CreatedAt] datetime2 NOT NULL,
            [ConfirmedBy] nvarchar(80) NULL,
            [ConfirmedAt] datetime2 NULL,
            CONSTRAINT [PK_InvDocs] PRIMARY KEY ([Id])
        );
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[InvDocTypes]', N'U') IS NULL
    BEGIN
        CREATE TABLE [InvDocTypes] (
            [Id] int NOT NULL IDENTITY,
            [Code] nvarchar(30) NOT NULL,
            [Name] nvarchar(120) NOT NULL,
            [Nature] int NOT NULL,
            [IsTransfer] bit NOT NULL,
            [RequiresParty] bit NOT NULL,
            [RequiresPrice] bit NOT NULL,
            [NumberPrefix] nvarchar(10) NULL,
            [Color] nvarchar(30) NULL,
            [Icon] nvarchar(50) NULL,
            [IsSystem] bit NOT NULL,
            [IsActive] bit NOT NULL,
            [SortOrder] int NOT NULL,
            [Description] nvarchar(max) NULL,
            [CreatedAt] datetime2 NOT NULL,
            CONSTRAINT [PK_InvDocTypes] PRIMARY KEY ([Id])
        );
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[InvLedger]', N'U') IS NULL
    BEGIN
        CREATE TABLE [InvLedger] (
            [Id] bigint NOT NULL IDENTITY,
            [ProductId] int NOT NULL,
            [WarehouseId] int NOT NULL,
            [Date] datetime2 NOT NULL,
            [DocId] int NOT NULL,
            [DocLineId] int NOT NULL,
            [DocTypeId] int NOT NULL,
            [Nature] int NOT NULL,
            [Number] nvarchar(30) NOT NULL,
            [Description] nvarchar(300) NULL,
            [QtyIn] decimal(18,3) NOT NULL,
            [QtyOut] decimal(18,3) NOT NULL,
            [UnitCost] decimal(18,4) NOT NULL,
            [ValueIn] decimal(18,2) NOT NULL,
            [ValueOut] decimal(18,2) NOT NULL,
            [BalanceQty] decimal(18,3) NOT NULL,
            [BalanceValue] decimal(18,2) NOT NULL,
            [RemainingQty] decimal(18,3) NOT NULL,
            [Seq] int NOT NULL,
            CONSTRAINT [PK_InvLedger] PRIMARY KEY ([Id])
        );
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[InvStocks]', N'U') IS NULL
    BEGIN
        CREATE TABLE [InvStocks] (
            [Id] int NOT NULL IDENTITY,
            [ProductId] int NOT NULL,
            [WarehouseId] int NOT NULL,
            [Quantity] decimal(18,3) NOT NULL,
            [Value] decimal(18,2) NOT NULL,
            [AvgCost] decimal(18,4) NOT NULL,
            [UpdatedAt] datetime2 NOT NULL,
            CONSTRAINT [PK_InvStocks] PRIMARY KEY ([Id])
        );
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[ProductAttributeDefs]', N'U') IS NULL
    BEGIN
        CREATE TABLE [ProductAttributeDefs] (
            [Id] int NOT NULL IDENTITY,
            [Name] nvarchar(120) NOT NULL,
            [Code] nvarchar(50) NULL,
            [ValueType] int NOT NULL,
            [Unit] nvarchar(50) NULL,
            [CategoryId] int NULL,
            [IsRequired] bit NOT NULL,
            [ShowInList] bit NOT NULL,
            [SortOrder] int NOT NULL,
            [IsActive] bit NOT NULL,
            [Description] nvarchar(max) NULL,
            [CreatedAt] datetime2 NOT NULL,
            CONSTRAINT [PK_ProductAttributeDefs] PRIMARY KEY ([Id])
        );
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[ProductAttributeOptions]', N'U') IS NULL
    BEGIN
        CREATE TABLE [ProductAttributeOptions] (
            [Id] int NOT NULL IDENTITY,
            [AttributeId] int NOT NULL,
            [Title] nvarchar(150) NOT NULL,
            [SortOrder] int NOT NULL,
            CONSTRAINT [PK_ProductAttributeOptions] PRIMARY KEY ([Id])
        );
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[ProductAttributeValues]', N'U') IS NULL
    BEGIN
        CREATE TABLE [ProductAttributeValues] (
            [Id] int NOT NULL IDENTITY,
            [ProductId] int NOT NULL,
            [AttributeId] int NOT NULL,
            [OptionId] int NULL,
            [TextValue] nvarchar(500) NULL,
            [NumberValue] decimal(18,4) NULL,
            [BoolValue] bit NULL,
            [DateValue] datetime2 NULL,
            CONSTRAINT [PK_ProductAttributeValues] PRIMARY KEY ([Id])
        );
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[WorkCalendarDays]', N'U') IS NULL
    BEGIN
        CREATE TABLE [WorkCalendarDays] (
            [Id] int NOT NULL IDENTITY,
            [Date] datetime2 NOT NULL,
            [IsWorkday] bit NOT NULL,
            [StartTime] time NULL,
            [EndTime] time NULL,
            [GraceMinutes] int NOT NULL,
            [OvertimeHours] float NOT NULL,
            [OvertimeMode] int NOT NULL,
            [OvertimeStart] time NULL,
            [OvertimeEnd] time NULL,
            [Note] nvarchar(100) NULL,
            [CreatedAt] datetime2 NOT NULL,
            [UpdatedAt] datetime2 NULL,
            CONSTRAINT [PK_WorkCalendarDays] PRIMARY KEY ([Id])
        );
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[WorkCalendarSettings]', N'U') IS NULL
    BEGIN
        CREATE TABLE [WorkCalendarSettings] (
            [Id] int NOT NULL IDENTITY,
            [DefaultStart] time NOT NULL,
            [DefaultEnd] time NOT NULL,
            [GraceMinutes] int NOT NULL,
            [RestDayFlags] int NOT NULL,
            [ApplyOfficialHolidays] bit NOT NULL,
            [UpdatedAt] datetime2 NOT NULL,
            CONSTRAINT [PK_WorkCalendarSettings] PRIMARY KEY ([Id])
        );
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[InvDocLines]', N'U') IS NULL
    BEGIN
        CREATE TABLE [InvDocLines] (
            [Id] int NOT NULL IDENTITY,
            [DocId] int NOT NULL,
            [RowNo] int NOT NULL,
            [ProductId] int NOT NULL,
            [Quantity] decimal(18,3) NOT NULL,
            [UnitPrice] decimal(18,2) NOT NULL,
            [Discount] decimal(18,2) NOT NULL,
            [BatchNo] nvarchar(50) NULL,
            [SerialNo] nvarchar(80) NULL,
            [ExpiryDate] datetime2 NULL,
            [Description] nvarchar(300) NULL,
            [OutCost] decimal(18,4) NULL,
            CONSTRAINT [PK_InvDocLines] PRIMARY KEY ([Id]),
            CONSTRAINT [FK_InvDocLines_InvDocs_DocId] FOREIGN KEY ([DocId]) REFERENCES [InvDocs] ([Id]) ON DELETE CASCADE
        );
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[Warehouses]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Warehouses_Code' AND [object_id] = OBJECT_ID(N'[Warehouses]'))
    BEGIN
        CREATE INDEX [IX_Warehouses_Code] ON [Warehouses] ([Code]);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[Products]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Products_CategoryId' AND [object_id] = OBJECT_ID(N'[Products]'))
    BEGIN
        CREATE INDEX [IX_Products_CategoryId] ON [Products] ([CategoryId]);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[Products]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Products_TaxCode' AND [object_id] = OBJECT_ID(N'[Products]'))
    BEGIN
        CREATE INDEX [IX_Products_TaxCode] ON [Products] ([TaxCode]);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[ProductCategories]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_ProductCategories_ParentId' AND [object_id] = OBJECT_ID(N'[ProductCategories]'))
    BEGIN
        CREATE INDEX [IX_ProductCategories_ParentId] ON [ProductCategories] ([ParentId]);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[OutgoingLetterSigners]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_OutgoingLetterSigners_OutgoingLetterId' AND [object_id] = OBJECT_ID(N'[OutgoingLetterSigners]'))
    BEGIN
        CREATE INDEX [IX_OutgoingLetterSigners_OutgoingLetterId] ON [OutgoingLetterSigners] ([OutgoingLetterId]);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[AuditLogs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AuditLogs_At' AND [object_id] = OBJECT_ID(N'[AuditLogs]'))
    BEGIN
        CREATE INDEX [IX_AuditLogs_At] ON [AuditLogs] ([At]);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[AuditLogs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_AuditLogs_UserId' AND [object_id] = OBJECT_ID(N'[AuditLogs]'))
    BEGIN
        CREATE INDEX [IX_AuditLogs_UserId] ON [AuditLogs] ([UserId]);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[InvDocLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocLines_DocId' AND [object_id] = OBJECT_ID(N'[InvDocLines]'))
    BEGIN
        CREATE INDEX [IX_InvDocLines_DocId] ON [InvDocLines] ([DocId]);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[InvDocLines]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocLines_ProductId' AND [object_id] = OBJECT_ID(N'[InvDocLines]'))
    BEGIN
        CREATE INDEX [IX_InvDocLines_ProductId] ON [InvDocLines] ([ProductId]);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[InvDocs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocs_Date' AND [object_id] = OBJECT_ID(N'[InvDocs]'))
    BEGIN
        CREATE INDEX [IX_InvDocs_Date] ON [InvDocs] ([Date]);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[InvDocs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocs_DocTypeId' AND [object_id] = OBJECT_ID(N'[InvDocs]'))
    BEGIN
        CREATE INDEX [IX_InvDocs_DocTypeId] ON [InvDocs] ([DocTypeId]);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[InvDocs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocs_Number' AND [object_id] = OBJECT_ID(N'[InvDocs]'))
    BEGIN
        CREATE UNIQUE INDEX [IX_InvDocs_Number] ON [InvDocs] ([Number]);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[InvDocs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocs_Status' AND [object_id] = OBJECT_ID(N'[InvDocs]'))
    BEGIN
        CREATE INDEX [IX_InvDocs_Status] ON [InvDocs] ([Status]);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[InvDocs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocs_WarehouseId' AND [object_id] = OBJECT_ID(N'[InvDocs]'))
    BEGIN
        CREATE INDEX [IX_InvDocs_WarehouseId] ON [InvDocs] ([WarehouseId]);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[InvDocTypes]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocTypes_Code' AND [object_id] = OBJECT_ID(N'[InvDocTypes]'))
    BEGIN
        CREATE UNIQUE INDEX [IX_InvDocTypes_Code] ON [InvDocTypes] ([Code]);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[InvDocTypes]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvDocTypes_Nature' AND [object_id] = OBJECT_ID(N'[InvDocTypes]'))
    BEGIN
        CREATE INDEX [IX_InvDocTypes_Nature] ON [InvDocTypes] ([Nature]);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[InvLedger]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvLedger_DocId' AND [object_id] = OBJECT_ID(N'[InvLedger]'))
    BEGIN
        CREATE INDEX [IX_InvLedger_DocId] ON [InvLedger] ([DocId]);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[InvLedger]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvLedger_ProductId_WarehouseId_Date' AND [object_id] = OBJECT_ID(N'[InvLedger]'))
    BEGIN
        CREATE INDEX [IX_InvLedger_ProductId_WarehouseId_Date] ON [InvLedger] ([ProductId], [WarehouseId], [Date]);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[InvStocks]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_InvStocks_ProductId_WarehouseId' AND [object_id] = OBJECT_ID(N'[InvStocks]'))
    BEGIN
        CREATE UNIQUE INDEX [IX_InvStocks_ProductId_WarehouseId] ON [InvStocks] ([ProductId], [WarehouseId]);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[ProductAttributeDefs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_ProductAttributeDefs_CategoryId' AND [object_id] = OBJECT_ID(N'[ProductAttributeDefs]'))
    BEGIN
        CREATE INDEX [IX_ProductAttributeDefs_CategoryId] ON [ProductAttributeDefs] ([CategoryId]);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[ProductAttributeDefs]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_ProductAttributeDefs_Name' AND [object_id] = OBJECT_ID(N'[ProductAttributeDefs]'))
    BEGIN
        CREATE INDEX [IX_ProductAttributeDefs_Name] ON [ProductAttributeDefs] ([Name]);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[ProductAttributeOptions]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_ProductAttributeOptions_AttributeId' AND [object_id] = OBJECT_ID(N'[ProductAttributeOptions]'))
    BEGIN
        CREATE INDEX [IX_ProductAttributeOptions_AttributeId] ON [ProductAttributeOptions] ([AttributeId]);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[ProductAttributeValues]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_ProductAttributeValues_ProductId_AttributeId' AND [object_id] = OBJECT_ID(N'[ProductAttributeValues]'))
    BEGIN
        CREATE UNIQUE INDEX [IX_ProductAttributeValues_ProductId_AttributeId] ON [ProductAttributeValues] ([ProductId], [AttributeId]);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[WorkCalendarDays]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_WorkCalendarDays_Date' AND [object_id] = OBJECT_ID(N'[WorkCalendarDays]'))
    BEGIN
        CREATE UNIQUE INDEX [IX_WorkCalendarDays_Date] ON [WorkCalendarDays] ([Date]);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[WorkCalendarSettings]', N'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_WorkCalendarSettings_Id' AND [object_id] = OBJECT_ID(N'[WorkCalendarSettings]'))
    BEGIN
        CREATE UNIQUE INDEX [IX_WorkCalendarSettings_Id] ON [WorkCalendarSettings] ([Id]);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    IF OBJECT_ID(N'[FK_OutgoingLetterSigners_OutgoingLetters_OutgoingLetterId]', N'F') IS NULL
    BEGIN
        ALTER TABLE [OutgoingLetterSigners] ADD CONSTRAINT [FK_OutgoingLetterSigners_OutgoingLetters_OutgoingLetterId] FOREIGN KEY ([OutgoingLetterId]) REFERENCES [OutgoingLetters] ([Id]);
    END;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260905154353_AddWarehousingModule'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260905154353_AddWarehousingModule', N'8.0.1');
END;
GO

COMMIT;
GO

