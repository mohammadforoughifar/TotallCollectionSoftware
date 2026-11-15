-- =====================================================================
--  بررسی اسکیمای لایسنس روی SQL Server  (مرحله ۱ و ۲)
--
--  چرا این فایل؟ چون در محیط توسعهٔ ما SQL Server در دسترس نبود و
--  مهاجرت فقط روی SQLite اجرا شد. این اسکریپت همان چیزی را که باید
--  روی سرور واقعی دیده شود، در یک نگاه نشان می‌دهد.
--
--  روش استفاده:
--    1) یک بار API را با Provider=SqlServer بالا بیاورید (اسکیما در بوت
--       به‌صورت افزایشی و idempotent اجرا می‌شود).
--    2) این اسکریپت را در همان دیتابیس اجرا کنید.
--    3) API را دوباره بالا بیاورید و همین اسکریپت را دوباره اجرا کنید؛
--       هیچ چیزی نباید تغییر کند (اثبات idempotent بودن).
--
--  همهٔ نتایج فقط «SELECT» هستند؛ این فایل هیچ داده‌ای را تغییر نمی‌دهد.
-- =====================================================================

SET NOCOUNT ON;

-- ۱) جدول‌های لایسنس (انتظار: ۷ ردیف)
SELECT N'۱) جدول‌های لایسنس' AS بخش, name AS نام, create_date AS تاریخ_ساخت
FROM sys.tables
WHERE name IN (N'LicenseInstallations', N'LicenseGrants', N'LicenseItemRows',
               N'LicenseUsages', N'LicenseUsageLedger', N'LicenseSessions', N'LicenseEvents')
ORDER BY name;

-- ۲) ستون‌های تازه در جدول‌های موجود (افزایشی‌ها)
SELECT N'۲) ستون افزایشی' AS بخش, OBJECT_NAME(object_id) AS جدول, name AS ستون, TYPE_NAME(user_type_id) AS نوع, is_nullable AS نال_پذیر
FROM sys.columns
WHERE (OBJECT_NAME(object_id) = N'Users' AND name = N'LicenseSeatExempt')
   OR (OBJECT_NAME(object_id) = N'LicenseUsages' AND name IN (N'Hmac', N'Limit'))
ORDER BY جدول, ستون;

-- ۳) ایندکس‌های لایسنس (کلید یکتا روی سریال و کلید دفتر مصرف)
SELECT N'۳) ایندکس‌ها' AS بخش, OBJECT_NAME(i.object_id) AS جدول, i.name AS ایندکس,
       i.is_unique AS یکتا, i.type_desc AS نوع
FROM sys.indexes i
WHERE OBJECT_NAME(i.object_id) LIKE N'License%'
  AND i.name IS NOT NULL
ORDER BY جدول, ایندکس;

-- ۴) سلامت دادهٔ مصرف (ردیف‌های منفی یا بیشتر از سقف = نشانهٔ مشکل)
SELECT N'۴) سلامت شمارنده‌ها' AS بخش, Module, Metric, PeriodKey, [Limit], Used,
       CASE
           WHEN Used < 0 THEN N'منفی — نیاز به بررسی'
           WHEN Used > [Limit] THEN N'بیشتر از سقف — نیاز به بررسی'
           ELSE N'سالم'
       END AS وضعیت
FROM dbo.LicenseUsages
ORDER BY Module, Metric, PeriodKey;

-- ۵) آخرین رخدادهای لایسنس (شامل LICENSE_QUOTA_NO_REFUND و *_TAMPERED)
SELECT TOP (20) N'۵) آخرین رخدادها' AS بخش, At, Kind, Severity,
       LEFT(ISNULL(Message, N''), 120) AS پیام
FROM dbo.LicenseEvents
ORDER BY At DESC, Id DESC;

-- ۶) بررسی نهایی و صریح سه پیش‌شرط مرحله ۲ روی SQL Server
SELECT N'۶) پیش‌شرط‌های مرحله ۲' AS بخش,
       CASE WHEN OBJECT_ID(N'dbo.LicenseUsageLedger', N'U') IS NOT NULL THEN N'✅' ELSE N'❌' END AS دفتر_مصرف_هست,
       CASE WHEN COL_LENGTH(N'dbo.LicenseUsages', 'Hmac') IS NOT NULL THEN N'✅' ELSE N'❌' END AS ستون_Hmac_هست,
       CASE WHEN EXISTS (SELECT 1 FROM sys.indexes
                         WHERE name = N'IX_LicenseUsageLedger_Key'
                           AND object_id = OBJECT_ID(N'dbo.LicenseUsageLedger')) THEN N'✅' ELSE N'❌' END AS ایندکس_کلید_دفتر_هست;

-- =====================================================================
--  آزمون بار هم‌زمان (فقط روی محیط تست — نه تولید)
--  هدف: اثبات اینکه UPDATE شرطی سقف را نمی‌شکند.
--  این بخش را عمداً کامنت گذاشته‌ام تا اجرای ناخواسته داده نسازد.
-- =====================================================================
/*
-- ۲۰ تراکنش هم‌زمان، هرکدام یک واحد مصرف، سقف ۵:
BEGIN TRAN;
-- در دو پنجرهٔ جدا (SSMS) هم‌زمان اجرا کنید:
UPDATE dbo.LicenseUsages
   SET Used = Used + 1
 WHERE Module = N'invoicing' AND Metric = N'invoice.create' AND PeriodKey = N'1405-08'
   AND Used + 1 <= [Limit];
SELECT @@ROWCOUNT AS تغییر_کرد;   -- اگر صفر بود یعنی سقف پر است
COMMIT;
*/
