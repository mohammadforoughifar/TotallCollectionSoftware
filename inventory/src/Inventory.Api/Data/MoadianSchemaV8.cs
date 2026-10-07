using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// وسعت‌بخشی ستون‌های متنی صورتحساب/قلم به اندازهٔ واقعی داده‌ها — پیشگیری از خطای
/// «String or binary data would be truncated» (که به‌صورت 409 مبهم ظاهر می‌شد):
///   • TaxId / EconomicCode / BuyerTaxId / BuyerPostalCode / BuyerPhone → nvarchar(50)
///   • SstId و UnitCode (جدول قلم) → nvarchar(50)
/// دلایل: فرم ثبت maxlength=50 برای شناسهٔ خریدار دارد و با انتخاب مشتری، مقادیر تا ۵۰
/// نویسه‌ای از «اطلاعات پایه» (NationalID/Phone/PostalCode/UniqueIdentifier/UnitOfMeasurement
/// همگی ۵۰ نویسه) بدون محدودیت به فرم تزریق می‌شود؛ درحالی‌که ستون‌ها ۲۰ تا ۰ نویسه بودند.
/// روی SQLite ستون‌ها TEXT و بدون محدودیت طول هستند، پس کاری انجام نمی‌شود.
/// عملیات idempotent است (فقط ستون‌های کوتاه‌تر از هدف وسیع می‌شوند).
/// </summary>
public static class MoadianSchemaV8
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite()) return;

        // COL_LENGTH برای nvarchar برابر (تعداد نویسه × ۲) است؛ هدف ۵۰ نویسه = ۱۰۰.
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH(N'dbo.MoadianInvoices', N'TaxId') IS NOT NULL AND COL_LENGTH(N'dbo.MoadianInvoices', N'TaxId') < 100
    ALTER TABLE dbo.MoadianInvoices ALTER COLUMN TaxId nvarchar(50) NOT NULL;
IF COL_LENGTH(N'dbo.MoadianInvoices', N'EconomicCode') IS NOT NULL AND COL_LENGTH(N'dbo.MoadianInvoices', N'EconomicCode') < 100
    ALTER TABLE dbo.MoadianInvoices ALTER COLUMN EconomicCode nvarchar(50) NULL;
IF COL_LENGTH(N'dbo.MoadianInvoices', N'BuyerTaxId') IS NOT NULL AND COL_LENGTH(N'dbo.MoadianInvoices', N'BuyerTaxId') < 100
    ALTER TABLE dbo.MoadianInvoices ALTER COLUMN BuyerTaxId nvarchar(50) NULL;
IF COL_LENGTH(N'dbo.MoadianInvoices', N'BuyerPostalCode') IS NOT NULL AND COL_LENGTH(N'dbo.MoadianInvoices', N'BuyerPostalCode') < 100
    ALTER TABLE dbo.MoadianInvoices ALTER COLUMN BuyerPostalCode nvarchar(50) NULL;
IF COL_LENGTH(N'dbo.MoadianInvoices', N'BuyerPhone') IS NOT NULL AND COL_LENGTH(N'dbo.MoadianInvoices', N'BuyerPhone') < 100
    ALTER TABLE dbo.MoadianInvoices ALTER COLUMN BuyerPhone nvarchar(50) NULL;
IF COL_LENGTH(N'dbo.MoadianInvoiceLines', N'SstId') IS NOT NULL AND COL_LENGTH(N'dbo.MoadianInvoiceLines', N'SstId') < 100
    ALTER TABLE dbo.MoadianInvoiceLines ALTER COLUMN SstId nvarchar(50) NOT NULL;
IF COL_LENGTH(N'dbo.MoadianInvoiceLines', N'UnitCode') IS NOT NULL AND COL_LENGTH(N'dbo.MoadianInvoiceLines', N'UnitCode') < 100
    ALTER TABLE dbo.MoadianInvoiceLines ALTER COLUMN UnitCode nvarchar(50) NULL;
");
    }
}
