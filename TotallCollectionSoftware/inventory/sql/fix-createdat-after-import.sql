-- =====================================================================
--  اصلاح CreatedAt پس از ایمپورت انبوه  (۲۰ آبان ۱۴۰۵ / 2026-11-11)
--
--  مسئله: اگر ایمپورت، CreatedAt را با «الان» نوشته باشد، همهٔ اسناد قدیمی در ماه
--  جاری شمرده می‌شوند و سقف ماهانهٔ مشتری را پر می‌کنند. این اسکریپت CreatedAt را
--  از تاریخ اصلی سند برمی‌گرداند — و هیچ ستون دیگری را لمس نمی‌کند.
--
--  گام‌ها:
--    ۱) @From / @To را روی «بازهٔ زمانی اجرای ایمپورت» بگذارید (نه تاریخ اسناد).
--    ۲) بخش «شمارش» را اجرا کنید و اعداد را ببینید.
--    ۳) اگر درست بود، بخش «اصلاح» را اجرا کنید (در تراکنش).
--    ۴) پیش از هر چیز: بکاپ دیتابیس.
-- =====================================================================

SET NOCOUNT ON;

DECLARE @From datetime2 = '2026-01-01 00:00:00';   -- ← شروع بازهٔ ایمپورت
DECLARE @To   datetime2 = '2026-12-31 23:59:59';   -- ← پایان بازهٔ ایمپورت

-- ---------- ۱) شمارش: چند ردیف در این بازه، تاریخ سیستمی‌شان با تاریخ سند فرق دارد؟ ----------
SELECT 'FacInvoices' AS TableName,
       COUNT_BIG(*) AS RowsToFix
  FROM [dbo].[FacInvoices]
 WHERE [CreatedAt] >= @From AND [CreatedAt] < @To
   AND [Date] IS NOT NULL AND [CreatedAt] <> [Date]
UNION ALL
SELECT 'IncomingLetters', COUNT_BIG(*)
  FROM [dbo].[IncomingLetters]
 WHERE [CreatedAt] >= @From AND [CreatedAt] < @To
   AND [Date] IS NOT NULL AND [CreatedAt] <> [Date]
UNION ALL
SELECT 'OutgoingLetters', COUNT_BIG(*)
  FROM [dbo].[OutgoingLetters]
 WHERE [CreatedAt] >= @From AND [CreatedAt] < @To
   AND [DateSabt] IS NOT NULL AND [CreatedAt] <> [DateSabt]
UNION ALL
SELECT 'InnerLetters', COUNT_BIG(*)
  FROM [dbo].[InnerLetters]
 WHERE [CreatedAt] >= @From AND [CreatedAt] < @To
   AND [DateSabt] IS NOT NULL AND [CreatedAt] <> [DateSabt];

-- ---------- ۲) اصلاح (فقط بعد از تأیید اعداد بالا) ----------
--  ⚠️ اگر عمداً این بخش را اجرا نمی‌کنید، خطوط زیر را کامنت بگذارید.
BEGIN TRANSACTION;

UPDATE [dbo].[FacInvoices]
   SET [CreatedAt] = [Date]
 WHERE [CreatedAt] >= @From AND [CreatedAt] < @To
   AND [Date] IS NOT NULL AND [CreatedAt] <> [Date];

UPDATE [dbo].[IncomingLetters]
   SET [CreatedAt] = [Date]
 WHERE [CreatedAt] >= @From AND [CreatedAt] < @To
   AND [Date] IS NOT NULL AND [CreatedAt] <> [Date];

UPDATE [dbo].[OutgoingLetters]
   SET [CreatedAt] = [DateSabt]
 WHERE [CreatedAt] >= @From AND [CreatedAt] < @To
   AND [DateSabt] IS NOT NULL AND [CreatedAt] <> [DateSabt];

UPDATE [dbo].[InnerLetters]
   SET [CreatedAt] = [DateSabt]
 WHERE [CreatedAt] >= @From AND [CreatedAt] < @To
   AND [DateSabt] IS NOT NULL AND [CreatedAt] <> [DateSabt];

COMMIT TRANSACTION;

-- ---------- ۳) بازبینی ----------
-- همان کوئری بخش ۱ را دوباره اجرا کنید؛ باید صفر شود.
-- سپس در برنامه، «شبیه‌ساز سهمیه» را اجرا کنید:
--     LicenseCheck --simulate --license <file.lic>
-- تا مطمئن شوید ماه جاری خالی شده و سقف واقعاً آزاد است.
