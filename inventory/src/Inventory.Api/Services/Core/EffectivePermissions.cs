using System.Security.Claims;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Core;

// =====================================================================
//  دسترسی‌های مؤثر کاربر — منبع واحد برای کنترلرهای داشبورد/گزارش‌ساز
//
//  مجوزها هنگام لاگین به‌صورت claim با نوع «permission» داخل JWT
//  گذاشته می‌شوند (همان چیزی که منوی کلاینت با آن آیتم‌ها را
//  نشان/پنهان می‌کند). پس ابتدا از توکن خوانده می‌شود تا سرور و منو
//  هیچ‌وقت از هم واگرا نشوند؛ اگر توکن قدیمی و بدون claim بود، از
//  دیتابیس بازسازی می‌شود.
// =====================================================================

public interface IEffectivePermissions
{
    /// <summary>مجموعهٔ «Module.Action» برای کاربر جاری.</summary>
    Task<HashSet<string>> GetAsync(ClaimsPrincipal user);

    /// <summary>مدیر قدیمی بدون نقش RBAC (سازگاری عقب‌رو).</summary>
    Task<bool> IsLegacyAdminAsync(ClaimsPrincipal user);

    /// <summary>آیا کاربر مجوز مشاهدهٔ ماژول را دارد؟ (Read/View/Access)</summary>
    Task<bool> CanSeeModuleAsync(ClaimsPrincipal user, string module);

    /// <summary>شناسهٔ نقش‌های RBAC کاربر (برای اشتراک‌گذاری گزارش).</summary>
    Task<List<int>> MyRoleIdsAsync(int userId);
}

public sealed class EffectivePermissions : IEffectivePermissions
{
    private const string ClaimType = "permission";
    private readonly AppDbContext _db;

    public EffectivePermissions(AppDbContext db) => _db = db;

    private static int UserId(ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;

    public async Task<HashSet<string>> GetAsync(ClaimsPrincipal user)
    {
        var fromToken = user.FindAll(ClaimType).Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        // مجوزهای نقش از دیتابیس خوانده می‌شوند؛ claimهای JWT ممکن است بعد از
        // تغییر نقش قدیمی باشند و باعث انتقال دسترسی یک ماژول به ماژول دیگر شوند.
        var uid = UserId(user);
        var roleIds = await _db.UserRoles.Where(ur => ur.UserId == uid).Select(ur => ur.RoleId).Distinct().ToListAsync();
        List<string> perms;
        if (roleIds.Count > 0)
        {
            perms = await _db.RolePermissions.Where(rp => roleIds.Contains(rp.RoleId))
                .Join(_db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Module + "." + p.Action)
                .Distinct().ToListAsync();
        }
        else if (fromToken.Count > 0)
        {
            perms = fromToken;
        }
        else
        {
            perms = user.FindFirstValue(ClaimTypes.Role) switch
            {
                "Admin" => await _db.Permissions.Select(p => p.Module + "." + p.Action).ToListAsync(),
                "Operator" or "Accountant" => await _db.Permissions
                    .Where(p => (p.Action == "Read" || p.Action == "View" || p.Action == "Create")
                                && p.Module != "SystemUsers" && p.Module != "Settings" && p.Module != "DocArchive")
                    .Select(p => p.Module + "." + p.Action).ToListAsync(),
                _ => await _db.Permissions
                    .Where(p => p.Module == "ReferrerPanel" || p.Module == "MyCartable"
                                || p.Module == "MyArchive" || p.Module == "MyDashboards")
                    .Select(p => p.Module + "." + p.Action).ToListAsync()
            };
        }
        return new HashSet<string>(perms, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> IsLegacyAdminAsync(ClaimsPrincipal user)
    {
        if (user.FindFirstValue(ClaimTypes.Role) != "Admin") return false;
        return !await _db.UserRoles.AnyAsync(ur => ur.UserId == UserId(user));
    }

    public async Task<bool> CanSeeModuleAsync(ClaimsPrincipal user, string module)
    {
        if (string.IsNullOrEmpty(module)) return true;
        if (await IsLegacyAdminAsync(user)) return true;
        var perms = await GetAsync(user);
        return perms.Contains($"{module}.Read") || perms.Contains($"{module}.View")
               || perms.Contains($"{module}.Access");
    }

    public Task<List<int>> MyRoleIdsAsync(int userId) =>
        _db.UserRoles.Where(ur => ur.UserId == userId)
            .Join(_db.Roles.Where(r => r.IsActive), ur => ur.RoleId, r => r.Id, (ur, r) => ur.RoleId)
            .Distinct().ToListAsync();
}
