/* پاک‌سازی کامل ماژول پروژه‌ها برای اجرای مجدد Import-LegacyAria.sql
   ⚠️ همهٔ پروژه‌ها، گزارش‌های کار، پیوست‌ها، کارفرماها و انواع فاکتور حذف می‌شوند. */
USE [InventoryDb];
GO
BEGIN TRANSACTION;
DELETE FROM dbo.ProjectAttaches;
DELETE FROM dbo.ReportWorks;
DELETE FROM dbo.ProjectEntryExits;
DELETE FROM dbo.KarFarmas;
DELETE FROM dbo.TypeFactors;
DELETE FROM dbo.Users WHERE Username LIKE N'legacy[_]%' AND IsActive = 0;
DBCC CHECKIDENT ('dbo.ProjectAttaches', RESEED, 0);
DBCC CHECKIDENT ('dbo.ReportWorks', RESEED, 0);
DBCC CHECKIDENT ('dbo.ProjectEntryExits', RESEED, 0);
DBCC CHECKIDENT ('dbo.KarFarmas', RESEED, 0);
DBCC CHECKIDENT ('dbo.TypeFactors', RESEED, 0);
COMMIT TRANSACTION;
PRINT N'ماژول پروژه‌ها پاک شد.';
GO
