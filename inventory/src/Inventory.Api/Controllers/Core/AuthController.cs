using System.Security.Claims;
using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppDbContext = Inventory.Api.Data.AppDbContext;

namespace Inventory.Api.Controllers;

/// <summary>احراز هویت (ورود با JWT) — برای همه نقش‌ها.
/// (از ApiControllerBase ارث نمی‌برد چون آن کلاس مخصوص نقش مدیر است.)</summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly AppDbContext _db;
    private readonly FileStore _store;
    private readonly UserPhotoService _photos;

    public AuthController(IAuthService auth, AppDbContext db, FileStore store, UserPhotoService photos)
    {
        _auth = auth;
        _db = db;
        _store = store;
        _photos = photos;
    }

    /// <summary>ورود به سیستم.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        var result = await _auth.LoginAsync(request);
        return result is null
            ? Unauthorized(new { message = "نام کاربری یا رمز عبور اشتباه است." })
            : Ok(result);
    }

    /// <summary>تغییر رمز عبور توسط خود کاربر — هر نقشی (مدیر یا معرف).</summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
        await _auth.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword);
        return Ok(new { ok = true });
    }
    // ================== عکس کاربران لاگین (آواتار نوار بالا و...) ==================

    /// <summary>عکس کاربر لاگین — برای آواتارها (بدون احراز هویت).</summary>
    [HttpGet("users/photo/{id:int}")]
    [AllowAnonymous]
    public ActionResult UserPhoto(int id)
    {
        var path = _db.Users.AsNoTracking().FirstOrDefault(x => x.Id == id)?.PhotoPath;
        var (data, ct) = _photos.GetPhoto(path);
        return File(data, ct);
    }

    /// <summary>تغییر عکس کاربر لاگین — فقط ادمین.</summary>
    [HttpPost("users/photo/{id:int}")]
    [Authorize(Roles = "Admin")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> UploadUserPhoto(int id, IFormFile file)
    {
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == id);
        if (user == null) return NotFound(new { message = "کاربر پیدا نشد." });
        if (file == null || file.Length == 0) return BadRequest(new { message = "عکسی انتخاب نشده است." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;
        string newPath;
        try { newPath = await _photos.SaveUserPhotoAsync(id, ms, file.FileName); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }

        if (!string.IsNullOrWhiteSpace(user.PhotoPath)) _store.Delete(user.PhotoPath);
        user.PhotoPath = newPath;
        await _db.SaveChangesAsync();
        return Ok(new { photoPath = newPath });
    }
}
/// <summary>مدیریت کاربران — مدیر: کامل؛ اپراتور: بدون هیچ عملیاتی روی نقش/کاربر ادمین.</summary>
[Route("api/users")]
[Authorize(Roles = "Admin,Operator")]
public class UsersController : ApiControllerBase
{
    private readonly IAuthService _auth;

    public UsersController(IAuthService auth) => _auth = auth;

    private bool IsAdmin => User.IsInRole("Admin");

    /// <summary>فهرست کاربران.</summary>
    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> GetAll()
        => Ok(await _auth.GetUsersAsync());

    /// <summary>ایجاد یا ویرایش کاربر (رمز فقط در صورت پر بودن تغییر می‌کند).
    /// اپراتور نمی‌تواند کاربر ادمین بسازد، نقش کسی را به ادمین تغییر دهد یا کاربر ادمین را ویرایش کند.</summary>
    [HttpPost]
    public async Task<ActionResult<UserDto>> Save([FromBody] UserDto user)
        => Ok(await _auth.SaveUserAsync(user, callerIsAdmin: IsAdmin));

    /// <summary>حذف کاربر — اپراتور نمی‌تواند کاربر ادمین را حذف کند.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var currentId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
        await _auth.DeleteUserAsync(id, currentId, callerIsAdmin: IsAdmin);
        return Ok(new { ok = true });
    }
}

/// <summary>پنل معرف — هر معرف فقط اطلاعات خودش را می‌بیند.
/// (از ApiControllerBase ارث نمی‌برد چون آن کلاس مخصوص نقش مدیر است.)</summary>
[ApiController]
[Route("api/my")]
[Authorize(Roles = "Referrer")]
public class MyPanelController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly IInventoryService _inventory;
    private readonly AppDbContext _db;

    public MyPanelController(IAuthService auth, IInventoryService inventory, AppDbContext db)
    {
        _auth = auth;
        _inventory = inventory;
        _db = db;
    }

    private int MyReferrerId =>
        int.TryParse(User.FindFirstValue("referrerId"), out var v) ? v : 0;

    private int MyUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;

    /// <summary>آیا کاربر جاری از طریق نقش‌های RBAC این دسترسی را دارد؟</summary>
    private Task<bool> HasRbacAsync(string module, string action) =>
        _db.UserRoles.Where(ur => ur.UserId == MyUserId)
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp.PermissionId)
            .Join(_db.Permissions, pid => pid, p => p.Id, (pid, p) => p)
            .AnyAsync(p => p.Module == module && p.Action == action);

    /// <summary>داشبورد معرف جاری.</summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<ReferrerDashboard>> Dashboard()
    {
        if (MyReferrerId <= 0) return Forbid();
        return Ok(await _auth.GetReferrerDashboardAsync(MyReferrerId));
    }

    /// <summary>کالاهای موجود — فقط اگر مدیر دسترسی «مشاهده کالا» را برای این معرف فعال کرده باشد.</summary>
    [HttpGet("products")]
    public async Task<ActionResult<List<ReferrerProductItem>>> Products([FromQuery] string? search)
    {
        if (MyReferrerId <= 0) return Forbid();
        // مجوز مشاهده کالاها: یا پرمیشن RBAC (ReferrerPanel.MyProducts) یا فلگ مشاهده کالا روی خود معرف
        var hasRbac = await HasRbacAsync("ReferrerPanel", "MyProducts");
        return Ok(await _inventory.GetReferrerProductsAsync(MyReferrerId, search, bypassFlag: hasRbac));
    }

    /// <summary>اسناد پرداخت معرف جاری.</summary>
    [HttpGet("payments")]
    public async Task<ActionResult<List<ReferrerPayment>>> Payments()
    {
        if (MyReferrerId <= 0) return Forbid();
        return Ok(await _inventory.GetReferrerPaymentsAsync(MyReferrerId));
    }

}
