using System.Security.Claims;
using Inventory.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>
/// کنترلر پایه برای ماژول‌هایی که چک دسترسی دقیق (ماژول.اکشن) لازم دارند —
/// مثل WorkOrdersController: اگر کاربر نقش RBAC دارد از جداول RBAC چک می‌شود،
/// وگرنه نقش قدیمی (Admin=همه‌چیز، Operator=خواندنی/ایجاد) فال‌بک است.
/// </summary>
public abstract class RbacControllerBase : ApiControllerBase
{
    protected readonly AppDbContext Db;

    protected RbacControllerBase(AppDbContext db) => Db = db;

    protected int MyUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;

    protected string MyUsername => User.FindFirstValue(ClaimTypes.Name) ?? "";

    /// <summary>آیا کاربر جاری این مجوز را دارد؟ (مثال: HasAsync("Projects", "Create"))</summary>
    protected async Task<bool> HasAsync(string module, string action)
    {
        var hasRoles = await Db.UserRoles.AnyAsync(ur => ur.UserId == MyUserId);
        if (!hasRoles)
        {
            var legacy = User.FindFirstValue(ClaimTypes.Role);
            if (legacy == "Admin") return true;
            if (legacy == "Operator") return action is "Create" or "Read" or "Export";
            return false;
        }
        return await Db.UserRoles.Where(ur => ur.UserId == MyUserId)
            .Join(Db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
            .Join(Db.Permissions, pid => pid, p => p.Id, (pid, p) => p)
            .AnyAsync(p => p.Module == module && p.Action == action);
    }

    /// <summary>اگر مجوز را نداشت 403 می‌دهد.</summary>
    protected async Task<IActionResult?> ForbiddenUnlessAsync(string module, string action)
        => await HasAsync(module, action) ? null
           : StatusCode(403, new { message = "شما به این بخش دسترسی ندارید." });

    /// <summary>
    /// شناسهٔ کاربران دارای یک مجوز خاص (مثل ProjectCartable.Manager) — برای ارسال اعلان به گروه مجاز.
    /// کاربران ادمین قدیمی (بدون نقش RBAC) هم لحاظ می‌شوند. خود کاربرِ رخداددهنده حذف می‌شود تا به خودش اعلان نزند.
    /// </summary>
    /// <param name="excludeSelf">اگر true باشد کاربر جاری از نتیجه حذف می‌شود</param>
    protected async Task<List<int>> UsersWithPermissionAsync(string module, string action, bool excludeSelf = false)
    {
        var rbac = await Db.UserRoles
            .Join(Db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => new { ur.UserId, rp.PermissionId })
            .Join(Db.Permissions, x => x.PermissionId, p => p.Id, (x, p) => new { x.UserId, p.Module, p.Action })
            .Where(x => x.Module == module && x.Action == action)
            .Select(x => x.UserId).Distinct().ToListAsync();
        // ادمین‌های قدیمی بدون نقش RBAC — به همهٔ مجوزها دسترسی دارند
        var legacyAdmins = await Db.Users
            .Where(u => u.Role == "Admin" && u.IsActive && !Db.UserRoles.Any(ur => ur.UserId == u.Id))
            .Select(u => u.Id).ToListAsync();
        var ids = rbac.Concat(legacyAdmins).Distinct();
        if (excludeSelf) ids = ids.Where(id => id != MyUserId);
        return ids.ToList();
    }
}
