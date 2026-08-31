/* ============================================================
   پاکسازی کامل داده‌های نمونه/تستی از دیتابیس InventoryDb
   ------------------------------------------------------------
   این اسکریپت را در SSMS روی دیتابیس InventoryDb اجرا کنید.

   بخش ۱ (همیشه اجرا می‌شود):
     - تمام اسناد خرید / فروش / اصلاح موجودی
     - چک‌ها و اقساط
     - هزینه‌ها
     - تعمیرات (سفارش‌ها و آیتم‌ها)
     - موجودی انبارها
     - پرداخت‌های معرف

   بخش ۲ (اختیاری — به‌صورت پیش‌فرض فعال است):
     - کالاها، گروه‌های کالا، طرف حساب‌ها (مشتری/تأمین‌کننده)،
       معرف‌ها و انبارهای اضافی نمونه
     - اگر می‌خواهید کالاها و طرف حساب‌ها بمانند، مقدار
       @CleanMasterData را 0 کنید.

   نکته: کاربران، تنظیمات، واحدهای شمارش و دسته‌های هزینه
   دست‌نخورده باقی می‌مانند.
   ============================================================ */

USE [InventoryDb];
GO

SET NOCOUNT ON;

DECLARE @CleanMasterData BIT = 1;  -- 1 = کالاها/طرف حساب‌ها/معرف‌ها هم پاک شوند

BEGIN TRAN;

BEGIN TRY

    /* ---------- بخش ۱: اسناد و گردش‌ها ---------- */

    -- تعمیرات
    DELETE FROM [RepairItems];
    DELETE FROM [RepairOrders];

    -- چک و اقساط (وابسته به اسناد)
    DELETE FROM [Cheques];
    DELETE FROM [Installments];

    -- سطرهای اسناد و خود اسناد (خرید/فروش/اصلاح موجودی)
    DELETE FROM [TransactionLines];
    DELETE FROM [Transactions];

    -- هزینه‌ها
    DELETE FROM [Expenses];

    -- موجودی انبار
    DELETE FROM [Stocks];

    -- پرداخت‌های معرف
    DELETE FROM [ReferrerPayments];

    /* ---------- بخش ۲: داده‌های پایه نمونه (اختیاری) ---------- */
    IF @CleanMasterData = 1
    BEGIN
        -- کاربران متصل به معرف‌ها باید اول جدا شوند
        UPDATE [Users] SET [ReferrerId] = NULL WHERE [ReferrerId] IS NOT NULL;
        DELETE FROM [Users] WHERE [Role] = N'Referrer';

        DELETE FROM [Products];
        DELETE FROM [ProductCategories];
        DELETE FROM [Parties];
        DELETE FROM [Referrers];
        DELETE FROM [Technicians];

        -- همه انبارها حذف و یک انبار مرکزی خالی ساخته می‌شود
        DELETE FROM [Warehouses];
        INSERT INTO [Warehouses] ([Name]) VALUES (N'انبار مرکزی');
    END

    /* ---------- ریست شماره‌گذاری Identity ---------- */
    DECLARE @t SYSNAME;
    DECLARE c CURSOR LOCAL FAST_FORWARD FOR
        SELECT t.name
        FROM sys.tables t
        WHERE EXISTS (SELECT 1 FROM sys.identity_columns ic WHERE ic.object_id = t.object_id)
          AND t.name IN (N'Transactions', N'TransactionLines', N'Cheques', N'Installments',
                         N'Expenses', N'Stocks', N'ReferrerPayments', N'RepairOrders', N'RepairItems')
           OR (@CleanMasterData = 1 AND t.name IN (N'Products', N'ProductCategories', N'Parties',
                         N'Referrers', N'Technicians', N'Warehouses')
               AND EXISTS (SELECT 1 FROM sys.identity_columns ic WHERE ic.object_id = t.object_id));
    OPEN c;
    FETCH NEXT FROM c INTO @t;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        DECLARE @sql NVARCHAR(300) = N'DBCC CHECKIDENT ([' + @t + N'], RESEED, 0) WITH NO_INFOMSGS;';
        EXEC sp_executesql @sql;
        FETCH NEXT FROM c INTO @t;
    END
    CLOSE c; DEALLOCATE c;

    COMMIT TRAN;
    PRINT N'✔ پاکسازی با موفقیت انجام شد. دیتابیس برای استفاده واقعی آماده است.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;
    PRINT N'✘ خطا در پاکسازی: ' + ERROR_MESSAGE();
END CATCH
GO
