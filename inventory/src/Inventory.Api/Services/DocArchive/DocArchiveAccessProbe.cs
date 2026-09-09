using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.DocArchive;

/// <summary>
/// «آیا این کاربر اصلاً جایی در آرشیو اسناد دسترسی دارد؟»
///
/// دسترسی آرشیو <b>به‌ازای هر پوشه و هر مدرک</b> جداگانه تعریف می‌شود ( grant مستقیم روی
/// پوشه/مدرک، دسترسی گروهی/نقشی، «نمایش برای همه»، یا سازنده مدرک). بنابراین مجوز ماژول
/// در «تنظیمات ← نقش‌ها و دسترسی‌ها» (DocArchive.Read) فقط یک <b>دسترسی سراسری</b> است و
/// نداشتن آن نباید کاربری را که روی یک پوشه/مدرک مشخص به او دسترسی داده‌ایم از کل آرشیو
/// محروم کند. این متد همان شرط «حداقل یک مورد قابل دسترسی» را یک‌جا بررسی می‌کند.
///
/// توجه: این فقط gate ورود به ماژول است؛ سطح واقعی دسترسی هر پوشه/مدرک همچنان در
/// <c>DocAccessService</c> و داخل هر endpoint محاسبه و چک می‌شود.
/// </summary>
public static class DocArchiveAccessProbe
{
    public static async Task<bool> AnyAsync(AppDbContext db, int userId)
    {
        if (userId <= 0) return false;

        // ۱) پوشه یا مدرک «نمایش برای همه»
        if (await db.DocFolders.AnyAsync(f => f.IsPublic)) return true;
        if (await db.Documents.AnyAsync(d => d.IsPublic)) return true;

        // ۲) مدرک ساخته‌ی خود کاربر (سازنده همیشه دسترسی کامل دارد)
        if (await db.Documents.AnyAsync(d => d.CreatedByUserId == userId)) return true;

        // ۳) grant مستقیم روی پوشه یا مدرک — فردی (UserId) یا گروهی/نقشی (RoleId)
        var roleIds = await db.UserRoles.AsNoTracking()
            .Where(r => r.UserId == userId)
            .Select(r => r.RoleId)
            .ToListAsync();

        if (await db.DocFolderPermissions.AnyAsync(p =>
                p.UserId == userId || (p.RoleId != 0 && roleIds.Contains(p.RoleId)))) return true;

        if (await db.DocumentPermissions.AnyAsync(p =>
                p.UserId == userId || (p.RoleId != 0 && roleIds.Contains(p.RoleId)))) return true;

        return false;
    }
}
