using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// V13 — ایندکس‌های یکتای سال/دورهٔ مالی به‌ازای هر خدمات‌دهنده:
///  • ایندکس یکتای قدیمی MoadianFiscalYears فقط روی (Year) بود؛ یعنی کل سیستم فقط یک سال مالی
///    برای هر سال شمسی داشت و خدمات‌دهندهٔ دوم نمی‌توانست همان سال را داشته باشد.
///    جایگزین: (ServiceProviderId, Year)
///  • ایندکس یکتای قدیمی MoadianFiscalPeriods روی (Year, Month) بود؛ جایگزین: (ServiceProviderId, Year, Month)
/// (ستون ServiceProviderId روی هر دو جدول از V12 وجود دارد.) مثل سایر Schemaها idempotent است.
/// </summary>
public static class MoadianSchemaV13
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite())
        {
            await db.Database.ExecuteSqlRawAsync(@"
DROP INDEX IF EXISTS IX_MoadianFiscalYears_Year;
CREATE UNIQUE INDEX IF NOT EXISTS IX_MoadianFiscalYears_ProviderYear
    ON MoadianFiscalYears (ServiceProviderId, Year);
DROP INDEX IF EXISTS IX_MoadianFiscalPeriods_Year_Month;
CREATE UNIQUE INDEX IF NOT EXISTS IX_MoadianFiscalPeriods_ProviderYearMonth
    ON MoadianFiscalPeriods (ServiceProviderId, Year, Month);");
            return;
        }

        await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.MoadianFiscalYears', N'U') IS NOT NULL
AND EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MoadianFiscalYears_Year' AND object_id = OBJECT_ID(N'dbo.MoadianFiscalYears'))
    EXEC(N'DROP INDEX IX_MoadianFiscalYears_Year ON dbo.MoadianFiscalYears');
IF OBJECT_ID(N'dbo.MoadianFiscalYears', N'U') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MoadianFiscalYears_ProviderYear' AND object_id = OBJECT_ID(N'dbo.MoadianFiscalYears'))
    EXEC(N'CREATE UNIQUE INDEX IX_MoadianFiscalYears_ProviderYear ON dbo.MoadianFiscalYears(ServiceProviderId, Year)');
IF OBJECT_ID(N'dbo.MoadianFiscalPeriods', N'U') IS NOT NULL
AND EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MoadianFiscalPeriods_Year_Month' AND object_id = OBJECT_ID(N'dbo.MoadianFiscalPeriods'))
    EXEC(N'DROP INDEX IX_MoadianFiscalPeriods_Year_Month ON dbo.MoadianFiscalPeriods');
IF OBJECT_ID(N'dbo.MoadianFiscalPeriods', N'U') IS NOT NULL
AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MoadianFiscalPeriods_ProviderYearMonth' AND object_id = OBJECT_ID(N'dbo.MoadianFiscalPeriods'))
    EXEC(N'CREATE UNIQUE INDEX IX_MoadianFiscalPeriods_ProviderYearMonth ON dbo.MoadianFiscalPeriods(ServiceProviderId, Year, Month)');");
    }
}
