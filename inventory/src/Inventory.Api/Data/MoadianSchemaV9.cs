using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// اتصال سوابق صورتحساب به سال مالی متناظر (ترمیم اثر بک‌فیل V6):
/// بک‌فیل شمارهٔ سند (V6) ستون FiscalYearId را خالی می‌گذاشت؛ در نتیجه شمارش
/// «بزرگ‌ترین سریال سال» این سوابق را نمی‌دید و ثبت جدید با تکرار
/// DocumentNumber (محدودیت یکتای IX_MoadianInvoices_DocumentNumber) خطا می‌داد.
/// سال از خود DocumentNumber (فرمت «سال/سریال») و در نبود آن از دورهٔ مالیاتی
/// (سال شمسی) برداشت می‌شود و فقط اگر سال مالی آن سال موجود باشد گره می‌خورد.
/// Idempotent: فقط رکوردهای FiscalYearId خالی را به‌روزرسانی می‌کند.
/// </summary>
public static class MoadianSchemaV9
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            await db.Database.ExecuteSqlRawAsync(@"
UPDATE MoadianInvoices
SET FiscalYearId = (
    SELECT y.Id FROM MoadianFiscalYears y
    WHERE y.Year = CAST(SUBSTR(MoadianInvoices.DocumentNumber, 1, INSTR(MoadianInvoices.DocumentNumber, '/') - 1) AS INTEGER)
)
WHERE FiscalYearId IS NULL
  AND DocumentNumber IS NOT NULL
  AND INSTR(DocumentNumber, '/') > 5
  AND SUBSTR(DocumentNumber, 1, INSTR(DocumentNumber, '/') - 1) GLOB '[0-9][0-9][0-9][0-9]';

UPDATE MoadianInvoices
SET FiscalYearId = (
    SELECT y.Id FROM MoadianFiscalYears y
    JOIN MoadianFiscalPeriods p ON p.Year = y.Year
    WHERE p.Id = MoadianInvoices.FiscalPeriodId
)
WHERE FiscalYearId IS NULL;
");
        }
        else
        {
            await db.Database.ExecuteSqlRawAsync(@"
-- ۱) سال از پیشوند DocumentNumber (فرمت «سال/سریال»)
UPDATE i SET i.FiscalYearId = y.Id
FROM dbo.MoadianInvoices i
JOIN dbo.MoadianFiscalYears y
  ON y.Year = CAST(LEFT(i.DocumentNumber, CHARINDEX(N'/', i.DocumentNumber) - 1) AS int)
WHERE i.FiscalYearId IS NULL
  AND i.DocumentNumber IS NOT NULL
  AND CHARINDEX(N'/', i.DocumentNumber) > 5
  AND LEFT(i.DocumentNumber, CHARINDEX(N'/', i.DocumentNumber) - 1) LIKE N'[0-9][0-9][0-9][0-9]';

-- ۲) سال از دورهٔ مالیاتی (سال شمسیِ دقیق تاریخ فاکتور)
UPDATE i SET i.FiscalYearId = y.Id
FROM dbo.MoadianInvoices i
JOIN dbo.MoadianFiscalPeriods p ON p.Id = i.FiscalPeriodId
JOIN dbo.MoadianFiscalYears y ON y.Year = p.Year
WHERE i.FiscalYearId IS NULL;
");
        }
    }
}
