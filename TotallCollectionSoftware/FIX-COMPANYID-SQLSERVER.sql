/* ============================================================
   اصلاح دستی دیتابیس SQL Server — چندشرکتی شدن «سامانه فروشگاهی»
   خطای «Invalid column name 'CompanyId'»

   ⚠️ اجرای این اسکریپت اختیاری است: نسخهٔ جدید برنامه در اولین اجرا
      خودش همین کارها را انجام می‌دهد. این فایل برای وقتی است که
      بخواهید دیتابیس را دستی/قبل از استقرار آماده کنید.
   • قابل اجرای مکرر (idempotent) است.
   • توصیه: قبل از اجرا یک BACKUP از InventoryDb بگیرید.
============================================================ */
USE [InventoryDb];
GO

-- ==================== ۱) افزودن ستون CompanyId ====================
IF OBJECT_ID(N'dbo.Products', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Products', N'CompanyId') IS NULL
    ALTER TABLE [dbo].[Products] ADD [CompanyId] int NULL;
GO
IF OBJECT_ID(N'dbo.ProductCategories', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ProductCategories', N'CompanyId') IS NULL
    ALTER TABLE [dbo].[ProductCategories] ADD [CompanyId] int NULL;
GO
IF OBJECT_ID(N'dbo.MeasureUnits', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.MeasureUnits', N'CompanyId') IS NULL
    ALTER TABLE [dbo].[MeasureUnits] ADD [CompanyId] int NULL;
GO
IF OBJECT_ID(N'dbo.Warehouses', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Warehouses', N'CompanyId') IS NULL
    ALTER TABLE [dbo].[Warehouses] ADD [CompanyId] int NULL;
GO
IF OBJECT_ID(N'dbo.Parties', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Parties', N'CompanyId') IS NULL
    ALTER TABLE [dbo].[Parties] ADD [CompanyId] int NULL;
GO
IF OBJECT_ID(N'dbo.Referrers', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Referrers', N'CompanyId') IS NULL
    ALTER TABLE [dbo].[Referrers] ADD [CompanyId] int NULL;
GO
IF OBJECT_ID(N'dbo.ReferrerPayments', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ReferrerPayments', N'CompanyId') IS NULL
    ALTER TABLE [dbo].[ReferrerPayments] ADD [CompanyId] int NULL;
GO
IF OBJECT_ID(N'dbo.Stocks', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Stocks', N'CompanyId') IS NULL
    ALTER TABLE [dbo].[Stocks] ADD [CompanyId] int NULL;
GO
IF OBJECT_ID(N'dbo.Transactions', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Transactions', N'CompanyId') IS NULL
    ALTER TABLE [dbo].[Transactions] ADD [CompanyId] int NULL;
GO
IF OBJECT_ID(N'dbo.TransactionLines', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.TransactionLines', N'CompanyId') IS NULL
    ALTER TABLE [dbo].[TransactionLines] ADD [CompanyId] int NULL;
GO
IF OBJECT_ID(N'dbo.GoodsIssues', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.GoodsIssues', N'CompanyId') IS NULL
    ALTER TABLE [dbo].[GoodsIssues] ADD [CompanyId] int NULL;
GO
IF OBJECT_ID(N'dbo.GoodsIssueLines', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.GoodsIssueLines', N'CompanyId') IS NULL
    ALTER TABLE [dbo].[GoodsIssueLines] ADD [CompanyId] int NULL;
GO
IF OBJECT_ID(N'dbo.Expenses', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Expenses', N'CompanyId') IS NULL
    ALTER TABLE [dbo].[Expenses] ADD [CompanyId] int NULL;
GO
IF OBJECT_ID(N'dbo.ExpenseCategories', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ExpenseCategories', N'CompanyId') IS NULL
    ALTER TABLE [dbo].[ExpenseCategories] ADD [CompanyId] int NULL;
GO
IF OBJECT_ID(N'dbo.Cheques', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Cheques', N'CompanyId') IS NULL
    ALTER TABLE [dbo].[Cheques] ADD [CompanyId] int NULL;
GO
IF OBJECT_ID(N'dbo.Installments', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Installments', N'CompanyId') IS NULL
    ALTER TABLE [dbo].[Installments] ADD [CompanyId] int NULL;
GO
IF OBJECT_ID(N'dbo.Technicians', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Technicians', N'CompanyId') IS NULL
    ALTER TABLE [dbo].[Technicians] ADD [CompanyId] int NULL;
GO
IF OBJECT_ID(N'dbo.RepairOrders', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.RepairOrders', N'CompanyId') IS NULL
    ALTER TABLE [dbo].[RepairOrders] ADD [CompanyId] int NULL;
GO
IF OBJECT_ID(N'dbo.RepairDevices', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.RepairDevices', N'CompanyId') IS NULL
    ALTER TABLE [dbo].[RepairDevices] ADD [CompanyId] int NULL;
GO
IF OBJECT_ID(N'dbo.RepairItems', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.RepairItems', N'CompanyId') IS NULL
    ALTER TABLE [dbo].[RepairItems] ADD [CompanyId] int NULL;
GO

-- ==================== ۲) انتساب دادهٔ قدیمی به اولین شرکت فعال ====================
-- پیش‌فرض: اولین شرکت فعال (کوچک‌ترین Id).
-- اگر می‌خواهید دادهٔ فعلی به شرکت دیگری برود، خط WHERE را با شناسهٔ دلخواه عوض کنید:
--    SET [CompanyId] = 2

IF OBJECT_ID(N'dbo.Products', N'U') IS NOT NULL
    UPDATE [dbo].[Products]
       SET [CompanyId] = (SELECT TOP 1 [Id] FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1 ORDER BY [Id])
     WHERE [CompanyId] IS NULL
       AND EXISTS (SELECT 1 FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1);
GO
IF OBJECT_ID(N'dbo.ProductCategories', N'U') IS NOT NULL
    UPDATE [dbo].[ProductCategories]
       SET [CompanyId] = (SELECT TOP 1 [Id] FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1 ORDER BY [Id])
     WHERE [CompanyId] IS NULL
       AND EXISTS (SELECT 1 FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1);
GO
IF OBJECT_ID(N'dbo.MeasureUnits', N'U') IS NOT NULL
    UPDATE [dbo].[MeasureUnits]
       SET [CompanyId] = (SELECT TOP 1 [Id] FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1 ORDER BY [Id])
     WHERE [CompanyId] IS NULL
       AND EXISTS (SELECT 1 FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1);
GO
IF OBJECT_ID(N'dbo.Warehouses', N'U') IS NOT NULL
    UPDATE [dbo].[Warehouses]
       SET [CompanyId] = (SELECT TOP 1 [Id] FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1 ORDER BY [Id])
     WHERE [CompanyId] IS NULL
       AND EXISTS (SELECT 1 FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1);
GO
IF OBJECT_ID(N'dbo.Parties', N'U') IS NOT NULL
    UPDATE [dbo].[Parties]
       SET [CompanyId] = (SELECT TOP 1 [Id] FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1 ORDER BY [Id])
     WHERE [CompanyId] IS NULL
       AND EXISTS (SELECT 1 FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1);
GO
IF OBJECT_ID(N'dbo.Referrers', N'U') IS NOT NULL
    UPDATE [dbo].[Referrers]
       SET [CompanyId] = (SELECT TOP 1 [Id] FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1 ORDER BY [Id])
     WHERE [CompanyId] IS NULL
       AND EXISTS (SELECT 1 FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1);
GO
IF OBJECT_ID(N'dbo.ReferrerPayments', N'U') IS NOT NULL
    UPDATE [dbo].[ReferrerPayments]
       SET [CompanyId] = (SELECT TOP 1 [Id] FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1 ORDER BY [Id])
     WHERE [CompanyId] IS NULL
       AND EXISTS (SELECT 1 FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1);
GO
IF OBJECT_ID(N'dbo.Stocks', N'U') IS NOT NULL
    UPDATE [dbo].[Stocks]
       SET [CompanyId] = (SELECT TOP 1 [Id] FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1 ORDER BY [Id])
     WHERE [CompanyId] IS NULL
       AND EXISTS (SELECT 1 FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1);
GO
IF OBJECT_ID(N'dbo.Transactions', N'U') IS NOT NULL
    UPDATE [dbo].[Transactions]
       SET [CompanyId] = (SELECT TOP 1 [Id] FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1 ORDER BY [Id])
     WHERE [CompanyId] IS NULL
       AND EXISTS (SELECT 1 FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1);
GO
IF OBJECT_ID(N'dbo.TransactionLines', N'U') IS NOT NULL
    UPDATE [dbo].[TransactionLines]
       SET [CompanyId] = (SELECT TOP 1 [Id] FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1 ORDER BY [Id])
     WHERE [CompanyId] IS NULL
       AND EXISTS (SELECT 1 FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1);
GO
IF OBJECT_ID(N'dbo.GoodsIssues', N'U') IS NOT NULL
    UPDATE [dbo].[GoodsIssues]
       SET [CompanyId] = (SELECT TOP 1 [Id] FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1 ORDER BY [Id])
     WHERE [CompanyId] IS NULL
       AND EXISTS (SELECT 1 FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1);
GO
IF OBJECT_ID(N'dbo.GoodsIssueLines', N'U') IS NOT NULL
    UPDATE [dbo].[GoodsIssueLines]
       SET [CompanyId] = (SELECT TOP 1 [Id] FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1 ORDER BY [Id])
     WHERE [CompanyId] IS NULL
       AND EXISTS (SELECT 1 FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1);
GO
IF OBJECT_ID(N'dbo.Expenses', N'U') IS NOT NULL
    UPDATE [dbo].[Expenses]
       SET [CompanyId] = (SELECT TOP 1 [Id] FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1 ORDER BY [Id])
     WHERE [CompanyId] IS NULL
       AND EXISTS (SELECT 1 FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1);
GO
IF OBJECT_ID(N'dbo.ExpenseCategories', N'U') IS NOT NULL
    UPDATE [dbo].[ExpenseCategories]
       SET [CompanyId] = (SELECT TOP 1 [Id] FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1 ORDER BY [Id])
     WHERE [CompanyId] IS NULL
       AND EXISTS (SELECT 1 FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1);
GO
IF OBJECT_ID(N'dbo.Cheques', N'U') IS NOT NULL
    UPDATE [dbo].[Cheques]
       SET [CompanyId] = (SELECT TOP 1 [Id] FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1 ORDER BY [Id])
     WHERE [CompanyId] IS NULL
       AND EXISTS (SELECT 1 FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1);
GO
IF OBJECT_ID(N'dbo.Installments', N'U') IS NOT NULL
    UPDATE [dbo].[Installments]
       SET [CompanyId] = (SELECT TOP 1 [Id] FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1 ORDER BY [Id])
     WHERE [CompanyId] IS NULL
       AND EXISTS (SELECT 1 FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1);
GO
IF OBJECT_ID(N'dbo.Technicians', N'U') IS NOT NULL
    UPDATE [dbo].[Technicians]
       SET [CompanyId] = (SELECT TOP 1 [Id] FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1 ORDER BY [Id])
     WHERE [CompanyId] IS NULL
       AND EXISTS (SELECT 1 FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1);
GO
IF OBJECT_ID(N'dbo.RepairOrders', N'U') IS NOT NULL
    UPDATE [dbo].[RepairOrders]
       SET [CompanyId] = (SELECT TOP 1 [Id] FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1 ORDER BY [Id])
     WHERE [CompanyId] IS NULL
       AND EXISTS (SELECT 1 FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1);
GO
IF OBJECT_ID(N'dbo.RepairDevices', N'U') IS NOT NULL
    UPDATE [dbo].[RepairDevices]
       SET [CompanyId] = (SELECT TOP 1 [Id] FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1 ORDER BY [Id])
     WHERE [CompanyId] IS NULL
       AND EXISTS (SELECT 1 FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1);
GO
IF OBJECT_ID(N'dbo.RepairItems', N'U') IS NOT NULL
    UPDATE [dbo].[RepairItems]
       SET [CompanyId] = (SELECT TOP 1 [Id] FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1 ORDER BY [Id])
     WHERE [CompanyId] IS NULL
       AND EXISTS (SELECT 1 FROM [dbo].[SystemCompanies] WHERE [IsActive] = 1);
GO

-- ==================== ۳) یکتایی کد/نام به تفکیک شرکت ====================
-- ایندکس یکتای تک‌ستونی قبلی حذف و ایندکس یکتای مرکب (CompanyId, ستون) ساخته می‌شود.
IF OBJECT_ID(N'dbo.Products', N'U') IS NOT NULL
BEGIN
    DECLARE @drop nvarchar(max) = N'';
    SELECT @drop = @drop + N'DROP INDEX ' + QUOTENAME(i.[name]) + N' ON [dbo].[Products];'
    FROM sys.indexes i
    WHERE i.[object_id] = OBJECT_ID(N'dbo.Products')
      AND i.[is_unique] = 1 AND i.[is_primary_key] = 0 AND i.[is_unique_constraint] = 0
      AND (SELECT COUNT(*) FROM sys.index_columns ic
            WHERE ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id] AND ic.[is_included_column] = 0) = 1
      AND EXISTS (SELECT 1 FROM sys.index_columns ic
                   JOIN sys.columns c ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id]
                  WHERE ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id] AND c.[name] = N'Code');
    IF @drop <> N'' EXEC(@drop);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Products_CompanyId_Code' AND [object_id] = OBJECT_ID(N'dbo.Products'))
        EXEC(N'CREATE UNIQUE INDEX [IX_Products_CompanyId_Code] ON [dbo].[Products] ([CompanyId], [Code]) WHERE [CompanyId] IS NOT NULL');
END
GO
IF OBJECT_ID(N'dbo.ProductCategories', N'U') IS NOT NULL
BEGIN
    DECLARE @drop nvarchar(max) = N'';
    SELECT @drop = @drop + N'DROP INDEX ' + QUOTENAME(i.[name]) + N' ON [dbo].[ProductCategories];'
    FROM sys.indexes i
    WHERE i.[object_id] = OBJECT_ID(N'dbo.ProductCategories')
      AND i.[is_unique] = 1 AND i.[is_primary_key] = 0 AND i.[is_unique_constraint] = 0
      AND (SELECT COUNT(*) FROM sys.index_columns ic
            WHERE ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id] AND ic.[is_included_column] = 0) = 1
      AND EXISTS (SELECT 1 FROM sys.index_columns ic
                   JOIN sys.columns c ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id]
                  WHERE ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id] AND c.[name] = N'Name');
    IF @drop <> N'' EXEC(@drop);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_ProductCategories_CompanyId_Name' AND [object_id] = OBJECT_ID(N'dbo.ProductCategories'))
        EXEC(N'CREATE UNIQUE INDEX [IX_ProductCategories_CompanyId_Name] ON [dbo].[ProductCategories] ([CompanyId], [Name]) WHERE [CompanyId] IS NOT NULL');
END
GO
IF OBJECT_ID(N'dbo.MeasureUnits', N'U') IS NOT NULL
BEGIN
    DECLARE @drop nvarchar(max) = N'';
    SELECT @drop = @drop + N'DROP INDEX ' + QUOTENAME(i.[name]) + N' ON [dbo].[MeasureUnits];'
    FROM sys.indexes i
    WHERE i.[object_id] = OBJECT_ID(N'dbo.MeasureUnits')
      AND i.[is_unique] = 1 AND i.[is_primary_key] = 0 AND i.[is_unique_constraint] = 0
      AND (SELECT COUNT(*) FROM sys.index_columns ic
            WHERE ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id] AND ic.[is_included_column] = 0) = 1
      AND EXISTS (SELECT 1 FROM sys.index_columns ic
                   JOIN sys.columns c ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id]
                  WHERE ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id] AND c.[name] = N'Name');
    IF @drop <> N'' EXEC(@drop);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_MeasureUnits_CompanyId_Name' AND [object_id] = OBJECT_ID(N'dbo.MeasureUnits'))
        EXEC(N'CREATE UNIQUE INDEX [IX_MeasureUnits_CompanyId_Name] ON [dbo].[MeasureUnits] ([CompanyId], [Name]) WHERE [CompanyId] IS NOT NULL');
END
GO
IF OBJECT_ID(N'dbo.GoodsIssues', N'U') IS NOT NULL
BEGIN
    DECLARE @drop nvarchar(max) = N'';
    SELECT @drop = @drop + N'DROP INDEX ' + QUOTENAME(i.[name]) + N' ON [dbo].[GoodsIssues];'
    FROM sys.indexes i
    WHERE i.[object_id] = OBJECT_ID(N'dbo.GoodsIssues')
      AND i.[is_unique] = 1 AND i.[is_primary_key] = 0 AND i.[is_unique_constraint] = 0
      AND (SELECT COUNT(*) FROM sys.index_columns ic
            WHERE ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id] AND ic.[is_included_column] = 0) = 1
      AND EXISTS (SELECT 1 FROM sys.index_columns ic
                   JOIN sys.columns c ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id]
                  WHERE ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id] AND c.[name] = N'Number');
    IF @drop <> N'' EXEC(@drop);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_GoodsIssues_CompanyId_Number' AND [object_id] = OBJECT_ID(N'dbo.GoodsIssues'))
        EXEC(N'CREATE UNIQUE INDEX [IX_GoodsIssues_CompanyId_Number] ON [dbo].[GoodsIssues] ([CompanyId], [Number]) WHERE [CompanyId] IS NOT NULL');
END
GO
IF OBJECT_ID(N'dbo.ExpenseCategories', N'U') IS NOT NULL
BEGIN
    DECLARE @drop nvarchar(max) = N'';
    SELECT @drop = @drop + N'DROP INDEX ' + QUOTENAME(i.[name]) + N' ON [dbo].[ExpenseCategories];'
    FROM sys.indexes i
    WHERE i.[object_id] = OBJECT_ID(N'dbo.ExpenseCategories')
      AND i.[is_unique] = 1 AND i.[is_primary_key] = 0 AND i.[is_unique_constraint] = 0
      AND (SELECT COUNT(*) FROM sys.index_columns ic
            WHERE ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id] AND ic.[is_included_column] = 0) = 1
      AND EXISTS (SELECT 1 FROM sys.index_columns ic
                   JOIN sys.columns c ON c.[object_id] = ic.[object_id] AND c.[column_id] = ic.[column_id]
                  WHERE ic.[object_id] = i.[object_id] AND ic.[index_id] = i.[index_id] AND c.[name] = N'Name');
    IF @drop <> N'' EXEC(@drop);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_ExpenseCategories_CompanyId_Name' AND [object_id] = OBJECT_ID(N'dbo.ExpenseCategories'))
        EXEC(N'CREATE UNIQUE INDEX [IX_ExpenseCategories_CompanyId_Name] ON [dbo].[ExpenseCategories] ([CompanyId], [Name]) WHERE [CompanyId] IS NOT NULL');
END
GO

-- ==================== ۴) ایندکس فیلتر شرکت ====================
IF OBJECT_ID(N'dbo.Products', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Products_CompanyId' AND [object_id] = OBJECT_ID(N'dbo.Products'))
    CREATE INDEX [IX_Products_CompanyId] ON [dbo].[Products] ([CompanyId]);
GO
IF OBJECT_ID(N'dbo.Parties', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Parties_CompanyId' AND [object_id] = OBJECT_ID(N'dbo.Parties'))
    CREATE INDEX [IX_Parties_CompanyId] ON [dbo].[Parties] ([CompanyId]);
GO
IF OBJECT_ID(N'dbo.Stocks', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Stocks_CompanyId' AND [object_id] = OBJECT_ID(N'dbo.Stocks'))
    CREATE INDEX [IX_Stocks_CompanyId] ON [dbo].[Stocks] ([CompanyId]);
GO
IF OBJECT_ID(N'dbo.Transactions', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Transactions_CompanyId' AND [object_id] = OBJECT_ID(N'dbo.Transactions'))
    CREATE INDEX [IX_Transactions_CompanyId] ON [dbo].[Transactions] ([CompanyId]);
GO
IF OBJECT_ID(N'dbo.TransactionLines', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_TransactionLines_CompanyId' AND [object_id] = OBJECT_ID(N'dbo.TransactionLines'))
    CREATE INDEX [IX_TransactionLines_CompanyId] ON [dbo].[TransactionLines] ([CompanyId]);
GO
IF OBJECT_ID(N'dbo.GoodsIssues', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_GoodsIssues_CompanyId' AND [object_id] = OBJECT_ID(N'dbo.GoodsIssues'))
    CREATE INDEX [IX_GoodsIssues_CompanyId] ON [dbo].[GoodsIssues] ([CompanyId]);
GO
IF OBJECT_ID(N'dbo.Expenses', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Expenses_CompanyId' AND [object_id] = OBJECT_ID(N'dbo.Expenses'))
    CREATE INDEX [IX_Expenses_CompanyId] ON [dbo].[Expenses] ([CompanyId]);
GO
IF OBJECT_ID(N'dbo.RepairOrders', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_RepairOrders_CompanyId' AND [object_id] = OBJECT_ID(N'dbo.RepairOrders'))
    CREATE INDEX [IX_RepairOrders_CompanyId] ON [dbo].[RepairOrders] ([CompanyId]);
GO
IF OBJECT_ID(N'dbo.Cheques', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Cheques_CompanyId' AND [object_id] = OBJECT_ID(N'dbo.Cheques'))
    CREATE INDEX [IX_Cheques_CompanyId] ON [dbo].[Cheques] ([CompanyId]);
GO
IF OBJECT_ID(N'dbo.Installments', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Installments_CompanyId' AND [object_id] = OBJECT_ID(N'dbo.Installments'))
    CREATE INDEX [IX_Installments_CompanyId] ON [dbo].[Installments] ([CompanyId]);
GO

PRINT N'✔ ستون‌ها، انتساب دادهٔ قدیمی و ایندکس‌های چندشرکتی آماده شد.';
GO
