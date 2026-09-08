using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventory.Api.Data;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SystemDepartmentsController : ControllerBase
{
    private readonly AppDbContext _db;
    public SystemDepartmentsController(AppDbContext db) => _db = db;

    [HttpGet] public async Task<ActionResult> Get() => Ok(await _db.SystemDepartments.ToListAsync());
    [HttpGet("{id}")] public async Task<ActionResult> Get(int id) => Ok(await _db.SystemDepartments.FirstOrDefaultAsync(x => x.Id == id));
    [HttpPost] public async Task<ActionResult> Post([FromBody] SystemDepartment d) { _db.SystemDepartments.Add(d); await _db.SaveChangesAsync(); return Ok(new { id = d.Id }); }
    [HttpPut("{id}")] public async Task<ActionResult> Put(int id, [FromBody] SystemDepartment d) { var item = await _db.SystemDepartments.FirstOrDefaultAsync(x => x.Id == id); if (item == null) return NotFound(); item.Name = d.Name; item.CompanyId = d.CompanyId; item.IsActive = d.IsActive; await _db.SaveChangesAsync(); return Ok(); }
    [HttpDelete("{id}")] public async Task<ActionResult> Delete(int id) { var item = await _db.SystemDepartments.FirstOrDefaultAsync(x => x.Id == id); if (item == null) return NotFound(); _db.SystemDepartments.Remove(item); await _db.SaveChangesAsync(); return Ok(); }
}
