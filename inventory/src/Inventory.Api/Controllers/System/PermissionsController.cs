using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventory.Api.Data;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PermissionsController : ControllerBase
{
    private readonly AppDbContext _db;

    public PermissionsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var query = _db.Permissions.AsNoTracking().OrderBy(p => p.Module).ThenBy(p => p.Action);
        var total = await query.CountAsync();
        page = page < 1 ? 1 : page; pageSize = pageSize is < 1 or > 200 ? 20 : pageSize;
        var items = await query.Skip((page - 1) * pageSize)
            .Take(pageSize).Select(p => new PermissionItem { Id = p.Id, Module = p.Module, Action = p.Action }).ToListAsync();
        return Ok(new PagedResult<PermissionItem> { Items = items, TotalCount = total });
    }

    private sealed class PermissionItem
    {
        public int Id { get; set; }
        public string Module { get; set; } = "";
        public string Action { get; set; } = "";
    }
}