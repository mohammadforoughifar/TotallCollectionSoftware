-- اسکریپت کامل آپدیت دیتابیس InventoryDb (Idempotent)
IF DB_ID(N'InventoryDb') IS NULL CREATE DATABASE [InventoryDb];
GO
USE [InventoryDb];
GO

﻿IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
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

