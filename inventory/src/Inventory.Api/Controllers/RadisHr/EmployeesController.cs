using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadisHr.Api.Data;
using RadisHr.Shared.Contracts;
using RadisHr.Shared.Models;

namespace RadisHr.Api.Controllers;

[ApiController]
[Authorize(Policy = "RadisHrAccess")]
[Route("api/employees")]
public class EmployeesController : ControllerBase
{
    private readonly AppDbContext _db;
    public EmployeesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<Employee>>> GetAll([FromQuery] string? search, [FromQuery] string? unit)
    {
        var query = _db.Employees.Include(e => e.Contracts).AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(unit))
            query = query.Where(e => e.Unit == unit);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(e =>
                e.First.Contains(s) || e.Last.Contains(s) ||
                e.Code.Contains(s) || e.Nid.Contains(s) ||
                e.PositionTitle.Contains(s) || e.Unit.Contains(s));
        }

        return await query.OrderBy(e => e.Code).ToListAsync();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Employee>> Get(int id)
    {
        var employee = await _db.Employees.Include(e => e.Contracts)
            .FirstOrDefaultAsync(e => e.Id == id);
        return employee == null ? NotFound(new ApiMessage(false, "پرسنل یافت نشد.")) : employee;
    }

    [HttpGet("by-code/{code}")]
    public async Task<ActionResult<Employee>> GetByCode(string code)
    {
        var employee = await _db.Employees.Include(e => e.Contracts)
            .FirstOrDefaultAsync(e => e.Code == code);
        return employee == null ? NotFound(new ApiMessage(false, "پرسنل یافت نشد.")) : employee;
    }

    [HttpPost]
    public async Task<ActionResult<IdResponse>> Create(Employee employee)
    {
        if (string.IsNullOrWhiteSpace(employee.Code))
            return BadRequest(new ApiMessage(false, "کد پرسنلی الزامی است."));

        if (await _db.Employees.AnyAsync(e => e.Code == employee.Code))
            return Conflict(new ApiMessage(false, "کد پرسنلی تکراری است."));

        employee.Id = 0;
        foreach (var c in employee.Contracts) c.Id = 0;
        _db.Employees.Add(employee);
        await _db.SaveChangesAsync();
        return new IdResponse(employee.Id, "اطلاعات پرسنل ثبت شد.");
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ApiMessage>> Update(int id, Employee input)
    {
        var employee = await _db.Employees.Include(e => e.Contracts)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (employee == null) return NotFound(new ApiMessage(false, "پرسنل یافت نشد."));

        if (await _db.Employees.AnyAsync(e => e.Code == input.Code && e.Id != id))
            return Conflict(new ApiMessage(false, "کد پرسنلی تکراری است."));

        _db.Entry(employee).CurrentValues.SetValues(input);

        _db.EmployeeContracts.RemoveRange(employee.Contracts);
        foreach (var c in input.Contracts)
            _db.EmployeeContracts.Add(new EmployeeContract
            {
                EmployeeId = id, Name = c.Name, Month = c.Month, FileId = c.FileId
            });

        await _db.SaveChangesAsync();
        return new ApiMessage(true, "اطلاعات پرسنل به‌روزرسانی شد.");
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult<ApiMessage>> Delete(int id)
    {
        var employee = await _db.Employees.FindAsync(id);
        if (employee == null) return NotFound(new ApiMessage(false, "پرسنل یافت نشد."));
        _db.Employees.Remove(employee);
        await _db.SaveChangesAsync();
        return new ApiMessage(true, "پرسنل حذف شد.");
    }

    /// <summary>ورود گروهی اطلاعات پرسنل (جایگزین بارگذاری اکسل نسخهٔ قدیم)</summary>
    [HttpPost("import")]
    public async Task<ActionResult<ImportEmployeesResult>> Import(ImportEmployeesRequest request)
    {
        int inserted = 0, updated = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var incoming in request.Employees)
        {
            if (string.IsNullOrWhiteSpace(incoming.Code))
            {
                errors.Add($"ردیف بدون کد پرسنلی: {incoming.First} {incoming.Last}");
                skipped++;
                continue;
            }

            var existing = await _db.Employees.FirstOrDefaultAsync(e => e.Code == incoming.Code);
            if (existing == null)
            {
                incoming.Id = 0;
                foreach (var c in incoming.Contracts) c.Id = 0;
                _db.Employees.Add(incoming);
                inserted++;
            }
            else if (request.Overwrite)
            {
                incoming.Id = existing.Id;
                _db.Entry(existing).CurrentValues.SetValues(incoming);
                updated++;
            }
            else skipped++;
        }

        await _db.SaveChangesAsync();
        return new ImportEmployeesResult(inserted, updated, skipped, errors);
    }
}
