using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Inventory.Api.Data;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SystemCompaniesController : ControllerBase
{
    private readonly AppDbContext _db;
    public SystemCompaniesController(AppDbContext db) => _db = db;

    [HttpGet] public async Task<ActionResult> Get() => Ok(await _db.SystemCompanies.ToListAsync());
    [HttpGet("{id}")] public async Task<ActionResult> Get(int id) => Ok(await _db.SystemCompanies.FirstOrDefaultAsync(x => x.Id == id));
    [HttpPost] public async Task<ActionResult> Post([FromBody] SystemCompany c) { _db.SystemCompanies.Add(c); await _db.SaveChangesAsync(); return Ok(new { id = c.Id }); }
    [HttpPut("{id}")] public async Task<ActionResult> Put(int id, [FromBody] SystemCompany c) { var item = await _db.SystemCompanies.FirstOrDefaultAsync(x => x.Id == id); if (item == null) return NotFound(); item.Name = c.Name; item.Code = c.Code; item.Phone = c.Phone; item.Address = c.Address; item.IsActive = c.IsActive; item.LetterheadFileName = c.LetterheadFileName; await _db.SaveChangesAsync(); return Ok(); }
    [HttpDelete("{id}")] public async Task<ActionResult> Delete(int id) { var item = await _db.SystemCompanies.FirstOrDefaultAsync(x => x.Id == id); if (item == null) return NotFound(); _db.SystemCompanies.Remove(item); await _db.SaveChangesAsync(); return Ok(); }
}
