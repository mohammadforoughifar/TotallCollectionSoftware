-- =====================================================================
--  ایندکس‌های داشبورد دستور کار
--
--  چرا این فایل لازم است؟
--    جدول WorkOrderAssignees در مایگریشن اولیه فقط کلید اصلی دارد؛ نه روی
--    OrderId ایندکسی ساخته شده و نه روی UserId. اما تقریباً هر ویجت دستور کار
--    یک نیمه‌جوین روی همین جدول می‌زند:
--        WHERE EXISTS (SELECT 1 FROM WorkOrderAssignees a
--                      WHERE a.OrderId = w.Id AND a.UserId = @me)
--    بدون ایندکس، این کوئری با رشد داده به اسکن کامل تبدیل می‌شود.
--
--  ⚠ این فایل دیگر لازم نیست دستی اجرا شود.
--    ایندکس‌ها به داخل سورس منتقل شدند:
--        Inventory.Api/Data/WorkOrderDashboardIndexSchemaV1.cs
--    و در DbInitializer به‌صورت خودکار و idempotent اجرا می‌شوند
--    (هم SQL Server و هم SQLite). این فایل فقط برای مرجع/اجرای دستی
--    توسط راهبر دیتابیس نگه داشته شده است.
--  همهٔ دستورها idempotent هستند؛ اجرای دوباره خطا نمی‌دهد.
--  ایندکس‌های موجود (مثل IX_WorkOrders_Status_DueAt که سرویس یادآور از آن
--  استفاده می‌کند) دست‌نخورده می‌مانند.
-- =====================================================================

SET NOCOUNT ON;

-- ۱) حیاتی‌ترین: مسیر «دستورهای من» ------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_WorkOrderAssignees_UserId_OrderId'
                 AND object_id = OBJECT_ID(N'dbo.WorkOrderAssignees'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_WorkOrderAssignees_UserId_OrderId
        ON dbo.WorkOrderAssignees (UserId, OrderId)
        INCLUDE (Done, RepliedAt, SeenAt, OwnerDecision);
    PRINT 'IX_WorkOrderAssignees_UserId_OrderId ساخته شد.';
END

-- ۲) مسیر «گیرندگانِ این دستور» ---------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_WorkOrderAssignees_OrderId'
                 AND object_id = OBJECT_ID(N'dbo.WorkOrderAssignees'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_WorkOrderAssignees_OrderId
        ON dbo.WorkOrderAssignees (OrderId);
    PRINT 'IX_WorkOrderAssignees_OrderId ساخته شد.';
END

-- ۳) کوئری غالب داشبورد: شرکت + وضعیت + مهلت ---------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_WorkOrders_Company_Status_DueAt'
                 AND object_id = OBJECT_ID(N'dbo.WorkOrders'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_WorkOrders_Company_Status_DueAt
        ON dbo.WorkOrders (CompanyId, Status, DueAt)
        INCLUDE (OwnerUserId, Priority, ProjectId, ExtensionCount)
        WHERE DeletedAt IS NULL;
    PRINT 'IX_WorkOrders_Company_Status_DueAt ساخته شد.';
END

-- ۴) ویجت‌های بازه‌دار روی تاریخ ثبت -----------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_WorkOrders_Company_CreatedAt'
                 AND object_id = OBJECT_ID(N'dbo.WorkOrders'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_WorkOrders_Company_CreatedAt
        ON dbo.WorkOrders (CompanyId, CreatedAt)
        INCLUDE (OwnerUserId, Status, ClosedAt)
        WHERE DeletedAt IS NULL;
    PRINT 'IX_WorkOrders_Company_CreatedAt ساخته شد.';
END

-- ۵) دستورهای صادرشده توسط من -----------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_WorkOrders_OwnerUserId'
                 AND object_id = OBJECT_ID(N'dbo.WorkOrders'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_WorkOrders_OwnerUserId
        ON dbo.WorkOrders (OwnerUserId)
        WHERE DeletedAt IS NULL;
    PRINT 'IX_WorkOrders_OwnerUserId ساخته شد.';
END

-- ۶) چک‌لیست: پیشرفت پروژه و درصد تکمیل --------------------------------
--    (اگر IX_WorkOrderChecklistItems_OrderId از قبل هست، این نسخهٔ پوششی است)
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'IX_WorkOrderChecklistItems_OrderId_IsDone'
                 AND object_id = OBJECT_ID(N'dbo.WorkOrderChecklistItems'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_WorkOrderChecklistItems_OrderId_IsDone
        ON dbo.WorkOrderChecklistItems (OrderId, IsDone);
    PRINT 'IX_WorkOrderChecklistItems_OrderId_IsDone ساخته شد.';
END

PRINT 'ایندکس‌های داشبورد دستور کار بررسی/ساخته شدند.';

-- =====================================================================
--  SQLite (توسعه محلی) — نسخهٔ سادهٔ همین ایندکس‌ها
--  ایندکس فیلتردار (WHERE) در SQLite هم پشتیبانی می‌شود.
-- =====================================================================
-- CREATE INDEX IF NOT EXISTS IX_WorkOrderAssignees_UserId_OrderId ON WorkOrderAssignees (UserId, OrderId);
-- CREATE INDEX IF NOT EXISTS IX_WorkOrderAssignees_OrderId        ON WorkOrderAssignees (OrderId);
-- CREATE INDEX IF NOT EXISTS IX_WorkOrders_Company_Status_DueAt   ON WorkOrders (CompanyId, Status, DueAt) WHERE DeletedAt IS NULL;
-- CREATE INDEX IF NOT EXISTS IX_WorkOrders_Company_CreatedAt      ON WorkOrders (CompanyId, CreatedAt)   WHERE DeletedAt IS NULL;
-- CREATE INDEX IF NOT EXISTS IX_WorkOrders_OwnerUserId            ON WorkOrders (OwnerUserId)            WHERE DeletedAt IS NULL;
-- CREATE INDEX IF NOT EXISTS IX_WorkOrderChecklistItems_OrderId_IsDone ON WorkOrderChecklistItems (OrderId, IsDone);
