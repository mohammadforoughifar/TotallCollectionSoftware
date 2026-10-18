using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Data;

/// <summary>
/// وصل‌کردن خدمات‌دهنده‌های مودیانِ قدیمی (CPInfos بدون CompanyId) به شرکت داخلی سیستم.
/// طبق تصمیم کاربر: رکوردهای بدون شرکت موقتاً به «شرکت پیش‌فرض» (اولین شرکت فعال سیستم)
/// وصل می‌شوند تا از دید کاربران خارج نشوند و بعداً از فرم شرکت مودیان قابل اصلاح باشند.
/// Idempotent است: فقط وقتی رکورد بدون شرکت وجود داشته باشد کاری می‌کند.
/// </summary>
public static class MoadianCompanyAssignment
{
    public static async Task EnsureAsync(AppDbContext db)
    {
        try
        {
            if (!await db.Database.CanConnectAsync()) return;

            var unassigned = await db.CPInfos.Where(c => c.CompanyId == null || c.CompanyId == 0).ToListAsync();
            if (unassigned.Count == 0) return;

            var defaultCompanyId = await db.SystemCompanies.AsNoTracking()
                .Where(c => c.IsActive).OrderBy(c => c.Id)
                .Select(c => (int?)c.Id).FirstOrDefaultAsync();
            if (defaultCompanyId is null)
            {
                Console.WriteLine("[DB] MoadianCompanyAssignment: هیچ شرکت فعالی در سیستم نیست؛ تخصیص خودکار انجام نشد.");
                return;
            }

            foreach (var cp in unassigned) cp.CompanyId = defaultCompanyId;
            await db.SaveChangesAsync();
            Console.WriteLine($"[DB] MoadianCompanyAssignment: {unassigned.Count} خدمات‌دهندهٔ مودیان به شرکت پیش‌فرض (Id={defaultCompanyId}) وصل شد.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[DB] MoadianCompanyAssignment: " + ex.Message);
        }
    }
}
