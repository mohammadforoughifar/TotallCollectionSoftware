-- ============================================================================
--  پاک‌سازی جدول‌های لایسنس در دیتابیس پنل پشتیبانی
--
--  موتور لایسنس از پنل حذف شده و کد دیگر به این جدول‌ها کاری ندارد؛ این اسکریپت
--  فقط برای پاک‌سازی کامل است. ⚠️ غیرقابل بازگشت — پیش از اجرا بکاپ بگیرید.
--
--  اجرا:
--    SQL Server : sqlcmd -S <server> -d SupportDb -i drop-license-tables.sql
--    SQLite     : sqlite3 data/support.db < drop-license-tables.sql
--
--  ترتیب حذف از فرزند به والد است تا کلیدهای خارجی نشکنند.
-- ============================================================================

-- SQLite: اگر کلیدهای خارجی فعال باشند، ابتدا خاموششان کنید.
-- PRAGMA foreign_keys = OFF;

DROP TABLE IF EXISTS Deliveries;
DROP TABLE IF EXISTS IssuedLicenses;
DROP TABLE IF EXISTS LicenseDrafts;
DROP TABLE IF EXISTS IdentityChecks;
DROP TABLE IF EXISTS RequestAttachments;
DROP TABLE IF EXISTS RequestEvents;
DROP TABLE IF EXISTS LicenseRequests;
DROP TABLE IF EXISTS Installations;
DROP TABLE IF EXISTS Presets;

-- SQLite: پس از پایان
-- PRAGMA foreign_keys = ON;

-- جدول‌های باقی‌مانده (دست نزنید): Customers، Contacts، Users، AuditLogs،
-- MailTemplates، Settings، __EFMigrationsHistory
