using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventory.Api.Data;
using Inventory.Shared.Entities;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RolesController : ControllerBase
{
    // اکشن‌های استاندارد CRUD — وقتی یکی از این ۴ برای یک ماژول به یک نقش داده شود، چهارتـانکمهم باید وجود داشته باشد
    private static readonly string[] CrudActions = { "Create", "Read", "Update", "Delete" };

    private readonly AppDbContext _db;

    public RolesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int skip = 0, [FromQuery] int? take = null) =>
        Ok(await Paging.ResultAsync(_db.Roles.AsNoTracking().OrderBy(r => r.Id), skip, take));

    [HttpGet("{id}/permissions")]
    public async Task<IActionResult> GetPermissions(int id, [FromQuery] int skip = 0, [FromQuery] int? take = null)
    {
        if (!await _db.Roles.AnyAsync(r => r.Id == id)) return NotFound();
        return Ok(await Paging.ResultAsync(_db.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == id).OrderBy(rp => rp.PermissionId)
            .Select(rp => rp.PermissionId), skip, take));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RoleDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "نام نقش الزامی است." });

        var role = new Role
        {
            Name = dto.Name.Trim(),
            Description = dto.Description,
            IsActive = dto.IsActive
        };

        _db.Roles.Add(role);
        await _db.SaveChangesAsync();

        // Assign permissions — با اجرای قانون گروه‌بندی CRUD
        var finalPermIds = EnforceCrudGrouping(dto.PermissionIds ?? new List<int>());

        if (finalPermIds.Any())
        {
            foreach (var permId in finalPermIds)
            {
                _db.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.Id,
                    PermissionId = permId
                });
            }
            await _db.SaveChangesAsync();
        }

        return Ok(new { id = role.Id });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] RoleDto dto)
    {
        var role = await _db.Roles.FindAsync(id);
        if (role == null) return NotFound();

        role.Name = dto.Name.Trim();
        role.Description = dto.Description;
        role.IsActive = dto.IsActive;

        // Remove old permissions
        var oldPerms = await _db.RolePermissions.Where(rp => rp.RoleId == id).ToListAsync();
        _db.RolePermissions.RemoveRange(oldPerms);

        // Add new permissions — با اجرای قانون گروه‌بندی CRUD
        var finalPermIds = EnforceCrudGrouping(dto.PermissionIds ?? new List<int>());

        foreach (var permId in finalPermIds)
        {
            _db.RolePermissions.Add(new RolePermission
            {
                RoleId = id,
                PermissionId = permId
            });
        }

        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var role = await _db.Roles.FindAsync(id);
        if (role == null) return NotFound();

        _db.Roles.Remove(role);
        await _db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>
    /// اگر هر کدام از ۴ مجوز CRUD (Create/Read/Update/Delete) برای یک ماژول انتخاب شده باشد،
    ///剩余 الثلاث مجوز CRUD هم همان ماژول به لیست اضافه می‌شود.
    /// </summary>
    private List<int> EnforceCrudGrouping(List<int> permissionIds)
    {
        if (!permissionIds.Any()) return permissionIds;

        var allPerms = _db.Permissions
            .Where(p => CrudActions.Contains(p.Action))
            .ToList();

        var result = new HashSet<int>(permissionIds);

        // پیدا کردن ماژول‌هایی که حداقل یک مجوز CRUD از آن‌ها انتخاب شده
        var modulesWithCrud = allPerms
            .Where(p => result.Contains(p.Id))
            .Select(p => p.Module)
            .Distinct();

        foreach (var module in modulesWithCrud)
        {
            var moduleCrudPerms = allPerms
                .Where(p => p.Module == module)
                .Select(p => p.Id);

            foreach (var permId in moduleCrudPerms)
                result.Add(permId);
        }

        return result.ToList();
    }

    public class RoleDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public List<int> PermissionIds { get; set; } = new();
    }
}