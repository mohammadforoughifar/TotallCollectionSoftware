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

    /// <summary>
    /// واحدهای قدیمیِ مشترک (CpInfo=null) را برای هر خدمات‌دهنده کپی می‌کند تا پس از تفکیک
    /// واحدها به‌ازای هر شرکت مودیان، هیچ خدمات‌دهنده‌ای بدون واحد نماند و کالاهای قبلی
    /// (که واحد را با نام نگه می‌دارند) مثل قبل کار کنند. Idempotent: بر اساس نام + خدمات‌دهنده.
    /// </summary>
    public static async Task EnsureUnitsPerProviderAsync(AppDbContext db)
    {
        try
        {
            if (!await db.Database.CanConnectAsync()) return;

            var templates = await db.UnitOfMeasurements
                .Where(u => u.CpInfo == null && !u.IsDelete).ToListAsync();
            if (templates.Count == 0) return;

            var providers = await db.CPInfos.Where(c => !c.IsDelete).Select(c => c.Id).ToListAsync();
            if (providers.Count == 0) return;

            var existing = await db.UnitOfMeasurements
                .Where(u => u.CpInfo != null && !u.IsDelete)
                .Select(u => new { u.CpInfo, u.Name }).ToListAsync();
            var have = existing.Select(x => $"{x.CpInfo}|{x.Name}").ToHashSet();

            var clones = new List<UnitOfMeasurement>();
            foreach (var cpId in providers)
                foreach (var t in templates)
                    if (have.Add($"{cpId}|{t.Name}"))
                        clones.Add(new UnitOfMeasurement { CpInfo = cpId, Name = t.Name, Code = t.Code, IsDelete = false });

            if (clones.Count == 0) return;
            await db.UnitOfMeasurements.AddRangeAsync(clones);
            await db.SaveChangesAsync();
            Console.WriteLine($"[DB] MoadianCompanyAssignment: {clones.Count} واحد برای خدمات‌دهنده‌ها کپی شد (تفکیک واحدها).");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[DB] MoadianCompanyAssignment.Units: " + ex.Message);
        }
    }
}
