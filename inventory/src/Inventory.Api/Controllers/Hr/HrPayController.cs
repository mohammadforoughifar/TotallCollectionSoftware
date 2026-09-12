using System.Security.Claims;
using Inventory.Api.Data;
using Inventory.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>API حقوق و دستمزد: آیتم/فرمول، دوره‌ها، فیش، وام، خروجی‌ها، تسویه</summary>
[ApiController]
[Route("api/hr-pay")]
[Authorize]
public class HrPayController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly HrPayService _svc;

    public HrPayController(AppDbContext db, HrPayService svc) { _db = db; _svc = svc; }

    private int MyUserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var v) ? v : 0;
    private string MyName => User.FindFirstValue(ClaimTypes.Name) ?? "";

    private bool? _mgr; private bool? _rd;
    private async Task<bool> IsManagerAsync()
    {
        if (_mgr.HasValue) return _mgr.Value;
        if (User.IsInRole("Admin")) { _mgr = true; return true; }
        if (User.HasClaim("permission", "HrPay.Manage")) { _mgr = true; return true; }
        _mgr = await HasPermAsync("Manage");
        return _mgr.Value;
    }
    private async Task<bool> CanReadAsync()
    {
        if (_rd.HasValue) return _rd.Value;
        if (await IsManagerAsync()) { _rd = true; return true; }
        if (User.HasClaim("permission", "HrPay.Read")) { _rd = true; return true; }
        _rd = await HasPermAsync("Read");
        return _rd.Value;
    }
    private async Task<bool> HasPermAsync(string action)
        => await _db.UserRoles.Where(ur => ur.UserId == MyUserId)
            .Join(_db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (ur, rp) => rp)
            .Join(_db.Permissions, rp => rp.PermissionId, pm => pm.Id, (rp, pm) => pm)
            .AnyAsync(pm => pm.Module == "HrPay" && pm.Action == action);

    // ================= آیتم‌ها =================

    [HttpGet("items")]
    public async Task<IActionResult> Items()
    {
        if (!await CanReadAsync()) return Forbid();
        await _svc.EnsureSeedAsync();
        return Ok(await _db.HrPayItems.AsNoTracking().OrderBy(i => i.SortOrder).ToListAsync());
    }

    public class ItemInput
    {
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public string Kind { get; set; } = "Earning";
        public string Category { get; set; } = "Mazaya";
        public string? Formula { get; set; }
        public decimal DefaultAmount { get; set; }
        public bool IsTaxable { get; set; } = true;
        public bool IsInsuranceable { get; set; } = true;
        public bool IsAccrual { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
    }

    [HttpPost("items")]
    public async Task<IActionResult> CreateItem([FromBody] ItemInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        var code = (input.Code ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code) || string.IsNullOrWhiteSpace(input.Title))
            return BadRequest(new { message = "کد و عنوان آیتم الزامی است." });
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z][A-Z0-9_]*$"))
            return BadRequest(new { message = "کد باید انگلیسی و بدون فاصله باشد (مثل BONUS)." });
        if (await _db.HrPayItems.AnyAsync(i => i.Code == code))
            return BadRequest(new { message = "این کد تکراری است." });
        if (!string.IsNullOrWhiteSpace(input.Formula))
        {
            try { HrPayFormula.Eval(input.Formula, SampleVars()); }
            catch (Exception ex) { return BadRequest(new { message = "خطای فرمول: " + ex.Message }); }
        }
        var it = new HrPayItem
        {
            Code = code, Title = input.Title.Trim(),
            Kind = input.Kind == "Deduction" ? "Deduction" : "Earning",
            Category = input.Category, Formula = string.IsNullOrWhiteSpace(input.Formula) ? null : input.Formula.Trim(),
            DefaultAmount = input.DefaultAmount, IsTaxable = input.IsTaxable,
            IsInsuranceable = input.IsInsuranceable, IsAccrual = input.IsAccrual,
            IsActive = input.IsActive, SortOrder = input.SortOrder,
        };
        _db.HrPayItems.Add(it);
        await _db.SaveChangesAsync();
        return Ok(it);
    }

    [HttpPut("items/{id:int}")]
    public async Task<IActionResult> UpdateItem(int id, [FromBody] ItemInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        var it = await _db.HrPayItems.FirstOrDefaultAsync(i => i.Id == id);
        if (it == null) return NotFound();
        if (!string.IsNullOrWhiteSpace(input.Formula))
        {
            try { HrPayFormula.Eval(input.Formula, SampleVars()); }
            catch (Exception ex) { return BadRequest(new { message = "خطای فرمول: " + ex.Message }); }
        }
        it.Title = input.Title.Trim();
        it.Kind = input.Kind == "Deduction" ? "Deduction" : "Earning";
        it.Category = input.Category;
        it.Formula = string.IsNullOrWhiteSpace(input.Formula) ? null : input.Formula.Trim();
        it.DefaultAmount = input.DefaultAmount;
        it.IsTaxable = input.IsTaxable; it.IsInsuranceable = input.IsInsuranceable;
        it.IsAccrual = input.IsAccrual; it.IsActive = input.IsActive; it.SortOrder = input.SortOrder;
        await _db.SaveChangesAsync();
        return Ok(it);
    }

    [HttpDelete("items/{id:int}")]
    public async Task<IActionResult> DeleteItem(int id)
    {
        if (!await IsManagerAsync()) return Forbid();
        var it = await _db.HrPayItems.FindAsync(id);
        if (it == null) return NotFound();
        _db.HrPayEmployeeItems.RemoveRange(_db.HrPayEmployeeItems.Where(x => x.ItemId == id));
        _db.HrPayItems.Remove(it);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    private static Dictionary<string, decimal> SampleVars() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["BASE"] = 100000000, ["DAILY_WAGE"] = 3333333, ["HOURLY_WAGE"] = 454545,
        ["MIN_WAGE"] = 103909270, ["MIN_DAILY"] = 3463642,
        ["DAYS_PAID"] = 30, ["DAYS_WORKED"] = 26, ["OT_H"] = 10, ["OT_H_HOL"] = 0,
        ["OT_H_NIGHT"] = 0, ["MISSION_DAYS"] = 0, ["LEAVE_UNPAID"] = 0, ["CHILDREN"] = 1,
        ["BON"] = 22000000, ["MASKAN"] = 9000000, ["EYAB"] = 0,
        ["OT_COEF_N"] = 1.4m, ["OT_COEF_H"] = 1.4m, ["OT_COEF_NT"] = 1.35m,
    };

    public class TestInput { public string Formula { get; set; } = ""; }

    [HttpPost("items/test")]
    public async Task<IActionResult> TestFormula([FromBody] TestInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        try { return Ok(new { ok = true, value = HrPayFormula.Eval(input.Formula ?? "", SampleVars()) }); }
        catch (Exception ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ================= مبالغ اختصاصی =================

    [HttpGet("empitems")]
    public async Task<IActionResult> EmpItems([FromQuery] int? employeeId)
    {
        if (!await CanReadAsync()) return Forbid();
        var q = _db.HrPayEmployeeItems.AsNoTracking().AsQueryable();
        if (employeeId != null) q = q.Where(x => x.EmployeeId == employeeId);
        var rows = await q.Take(1000).ToListAsync();
        var items = await _db.HrPayItems.AsNoTracking().ToDictionaryAsync(i => i.Id);
        return Ok(rows.Select(r => new
        {
            r.Id, r.EmployeeId, r.ItemId, r.Amount,
            ItemCode = items.TryGetValue(r.ItemId, out var it) ? it.Code : null,
            ItemTitle = items.TryGetValue(r.ItemId, out var it2) ? it2.Title : null,
        }));
    }

    public class EmpItemInput { public int EmployeeId { get; set; } public int ItemId { get; set; } public decimal Amount { get; set; } }

    [HttpPost("empitems")]
    public async Task<IActionResult> SaveEmpItem([FromBody] EmpItemInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        if (!await _db.HrEmployees.AnyAsync(e => e.Id == input.EmployeeId)) return BadRequest(new { message = "پرسنل معتبر نیست." });
        if (!await _db.HrPayItems.AnyAsync(i => i.Id == input.ItemId)) return BadRequest(new { message = "آیتم معتبر نیست." });
        var ex = await _db.HrPayEmployeeItems.FirstOrDefaultAsync(x => x.EmployeeId == input.EmployeeId && x.ItemId == input.ItemId);
        if (ex == null) _db.HrPayEmployeeItems.Add(new HrPayEmployeeItem { EmployeeId = input.EmployeeId, ItemId = input.ItemId, Amount = input.Amount });
        else ex.Amount = input.Amount;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpDelete("empitems/{id:int}")]
    public async Task<IActionResult> DeleteEmpItem(int id)
    {
        if (!await IsManagerAsync()) return Forbid();
        var r = await _db.HrPayEmployeeItems.FindAsync(id);
        if (r == null) return NotFound();
        _db.HrPayEmployeeItems.Remove(r);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ================= پروفایل =================

    [HttpGet("profiles")]
    public async Task<IActionResult> Profiles([FromQuery] int? employeeId)
    {
        if (!await CanReadAsync()) return Forbid();
        var q = _db.HrPayProfiles.AsNoTracking().AsQueryable();
        if (employeeId != null) q = q.Where(p => p.EmployeeId == employeeId);
        return Ok(await q.Take(2000).ToListAsync());
    }

    public class ProfileInput
    {
        public int EmployeeId { get; set; }
        public int ChildrenCount { get; set; }
        public string? InsuranceNo { get; set; }
        public string? BankName { get; set; }
        public string? Iban { get; set; }
        public string? AccountNo { get; set; }
        public decimal ExtraTaxExempt { get; set; }
        public bool IsHardJob { get; set; }
    }

    [HttpPost("profiles")]
    public async Task<IActionResult> SaveProfile([FromBody] ProfileInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        if (!await _db.HrEmployees.AnyAsync(e => e.Id == input.EmployeeId)) return BadRequest(new { message = "پرسنل معتبر نیست." });
        var p = await _db.HrPayProfiles.FirstOrDefaultAsync(x => x.EmployeeId == input.EmployeeId);
        if (p == null) { p = new HrPayProfile { EmployeeId = input.EmployeeId }; _db.HrPayProfiles.Add(p); }
        p.ChildrenCount = Math.Max(0, input.ChildrenCount);
        p.InsuranceNo = input.InsuranceNo; p.BankName = input.BankName;
        p.Iban = input.Iban; p.AccountNo = input.AccountNo;
        p.ExtraTaxExempt = Math.Max(0, input.ExtraTaxExempt);
        p.IsHardJob = input.IsHardJob;
        await _db.SaveChangesAsync();
        return Ok(p);
    }

    // ================= پلکان مالیات =================

    [HttpGet("brackets")]
    public async Task<IActionResult> Brackets([FromQuery] int? year)
    {
        if (!await CanReadAsync()) return Forbid();
        var y = year ?? 1405;
        await _svc.EnsureTaxSeedAsync(y);
        var rows = await _db.HrPayTaxBrackets.AsNoTracking().Where(b => b.Year == y).ToListAsync();
        return Ok(rows.OrderBy(b => b.FromAmount).ToList());
    }

    public class BracketInput { public int Year { get; set; } public decimal FromAmount { get; set; } public decimal ToAmount { get; set; } public decimal Rate { get; set; } }

    [HttpPost("brackets")]
    public async Task<IActionResult> CreateBracket([FromBody] BracketInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        var b = new HrPayTaxBracket { Year = input.Year, FromAmount = input.FromAmount, ToAmount = input.ToAmount, Rate = input.Rate };
        _db.HrPayTaxBrackets.Add(b);
        await _db.SaveChangesAsync();
        return Ok(b);
    }

    [HttpPut("brackets/{id:int}")]
    public async Task<IActionResult> UpdateBracket(int id, [FromBody] BracketInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        var b = await _db.HrPayTaxBrackets.FindAsync(id);
        if (b == null) return NotFound();
        b.FromAmount = input.FromAmount; b.ToAmount = input.ToAmount; b.Rate = input.Rate;
        await _db.SaveChangesAsync();
        return Ok(b);
    }

    [HttpDelete("brackets/{id:int}")]
    public async Task<IActionResult> DeleteBracket(int id)
    {
        if (!await IsManagerAsync()) return Forbid();
        var b = await _db.HrPayTaxBrackets.FindAsync(id);
        if (b == null) return NotFound();
        _db.HrPayTaxBrackets.Remove(b);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ================= قوانین =================

    [HttpGet("rules")]
    public async Task<IActionResult> Rules()
    {
        if (!await CanReadAsync()) return Forbid();
        return Ok(await _svc.GetPayRulesAsync());
    }

    [HttpPut("rules")]
    public async Task<IActionResult> SaveRules([FromBody] Dictionary<string, string> values)
    {
        if (!await IsManagerAsync()) return Forbid();
        await _svc.SavePayRulesAsync(values ?? new());
        return Ok(new { ok = true });
    }

    // ================= وام =================

    [HttpGet("loans")]
    public async Task<IActionResult> Loans([FromQuery] int? employeeId, [FromQuery] string? status)
    {
        if (!await CanReadAsync()) return Forbid();
        var q = _db.HrPayLoans.AsNoTracking().AsQueryable();
        if (employeeId != null) q = q.Where(l => l.EmployeeId == employeeId);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(l => l.Status == status);
        var rows = await q.OrderByDescending(l => l.Id).Take(1000).ToListAsync();
        var emps = await _db.HrEmployees.AsNoTracking().ToDictionaryAsync(e => e.Id);
        return Ok(rows.Select(l => new
        {
            l.Id, l.EmployeeId, l.Kind, l.Amount, l.Installments, l.MonthlyAmount, l.PaidCount,
            l.StartYear, l.StartMonth, l.Status, l.Description, l.CreatedAt,
            EmployeeName = emps.TryGetValue(l.EmployeeId, out var e) ? $"{e.FirstName} {e.LastName}".Trim() : null,
        }));
    }

    public class LoanInput
    {
        public int EmployeeId { get; set; }
        public string Kind { get; set; } = "Loan";
        public decimal Amount { get; set; }
        public int Installments { get; set; } = 1;
        public decimal MonthlyAmount { get; set; }
        public int StartYear { get; set; }
        public int StartMonth { get; set; }
        public string? Description { get; set; }
    }

    [HttpPost("loans")]
    public async Task<IActionResult> CreateLoan([FromBody] LoanInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        if (!await _db.HrEmployees.AnyAsync(e => e.Id == input.EmployeeId)) return BadRequest(new { message = "پرسنل معتبر نیست." });
        if (input.Amount <= 0) return BadRequest(new { message = "مبلغ نامعتبر است." });
        var n = input.Kind == "Advance" ? 1 : Math.Max(1, input.Installments);
        var monthly = input.MonthlyAmount > 0 ? input.MonthlyAmount : Math.Round(input.Amount / n);
        var ln = new HrPayLoan
        {
            EmployeeId = input.EmployeeId, Kind = input.Kind == "Advance" ? "Advance" : "Loan",
            Amount = input.Amount, Installments = n, MonthlyAmount = monthly,
            StartYear = input.StartYear, StartMonth = input.StartMonth,
            Status = "Active", Description = input.Description,
        };
        _db.HrPayLoans.Add(ln);
        await _db.SaveChangesAsync();
        return Ok(ln);
    }

    [HttpPost("loans/{id:int}/cancel")]
    public async Task<IActionResult> CancelLoan(int id)
    {
        if (!await IsManagerAsync()) return Forbid();
        var ln = await _db.HrPayLoans.FindAsync(id);
        if (ln == null) return NotFound();
        ln.Status = "Cancelled";
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ================= معوقه / علی‌الحساب =================

    [HttpGet("arrears")]
    public async Task<IActionResult> Arrears([FromQuery] int? year, [FromQuery] int? month)
    {
        if (!await CanReadAsync()) return Forbid();
        var q = _db.HrPayArrears.AsNoTracking().AsQueryable();
        if (year != null) q = q.Where(a => a.Year == year);
        if (month != null) q = q.Where(a => a.Month == month);
        var rows = await q.OrderByDescending(a => a.Id).Take(500).ToListAsync();
        var emps = await _db.HrEmployees.AsNoTracking().ToDictionaryAsync(e => e.Id);
        return Ok(rows.Select(a => new
        {
            a.Id, a.EmployeeId, a.Year, a.Month, a.Amount, a.Title, a.Status,
            EmployeeName = emps.TryGetValue(a.EmployeeId, out var e) ? $"{e.FirstName} {e.LastName}".Trim() : null,
        }));
    }

    public class ArrearInput { public int EmployeeId { get; set; } public int Year { get; set; } public int Month { get; set; } public decimal Amount { get; set; } public string? Title { get; set; } }

    [HttpPost("arrears")]
    public async Task<IActionResult> CreateArrear([FromBody] ArrearInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        if (!await _db.HrEmployees.AnyAsync(e => e.Id == input.EmployeeId)) return BadRequest(new { message = "پرسنل معتبر نیست." });
        if (input.Amount <= 0) return BadRequest(new { message = "مبلغ نامعتبر است." });
        var a = new HrPayArrear { EmployeeId = input.EmployeeId, Year = input.Year, Month = input.Month, Amount = input.Amount, Title = input.Title, Status = "Pending" };
        _db.HrPayArrears.Add(a);
        await _db.SaveChangesAsync();
        return Ok(a);
    }

    [HttpDelete("arrears/{id:int}")]
    public async Task<IActionResult> DeleteArrear(int id)
    {
        if (!await IsManagerAsync()) return Forbid();
        var a = await _db.HrPayArrears.FindAsync(id);
        if (a == null) return NotFound();
        if (a.Status != "Pending") return BadRequest(new { message = "این معوقه اعمال شده و قابل حذف نیست." });
        _db.HrPayArrears.Remove(a);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpGet("onaccounts")]
    public async Task<IActionResult> OnAccounts([FromQuery] int? year, [FromQuery] int? month)
    {
        if (!await CanReadAsync()) return Forbid();
        var q = _db.HrPayOnAccounts.AsNoTracking().AsQueryable();
        if (year != null) q = q.Where(a => a.Year == year);
        if (month != null) q = q.Where(a => a.Month == month);
        var rows = await q.OrderByDescending(a => a.Id).Take(500).ToListAsync();
        var emps = await _db.HrEmployees.AsNoTracking().ToDictionaryAsync(e => e.Id);
        return Ok(rows.Select(a => new
        {
            a.Id, a.EmployeeId, a.Year, a.Month, a.Amount, a.PaidDate, a.Status,
            EmployeeName = emps.TryGetValue(a.EmployeeId, out var e) ? $"{e.FirstName} {e.LastName}".Trim() : null,
        }));
    }

    [HttpPost("onaccounts")]
    public async Task<IActionResult> CreateOnAccount([FromBody] ArrearInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        if (!await _db.HrEmployees.AnyAsync(e => e.Id == input.EmployeeId)) return BadRequest(new { message = "پرسنل معتبر نیست." });
        if (input.Amount <= 0) return BadRequest(new { message = "مبلغ نامعتبر است." });
        var o = new HrPayOnAccount { EmployeeId = input.EmployeeId, Year = input.Year, Month = input.Month, Amount = input.Amount, Status = "Pending" };
        _db.HrPayOnAccounts.Add(o);
        await _db.SaveChangesAsync();
        return Ok(o);
    }

    [HttpDelete("onaccounts/{id:int}")]
    public async Task<IActionResult> DeleteOnAccount(int id)
    {
        if (!await IsManagerAsync()) return Forbid();
        var o = await _db.HrPayOnAccounts.FindAsync(id);
        if (o == null) return NotFound();
        if (o.Status != "Pending") return BadRequest(new { message = "این علی‌الحساب اعمال شده و قابل حذف نیست." });
        _db.HrPayOnAccounts.Remove(o);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ================= دوره‌ها =================

    [HttpGet("runs")]
    public async Task<IActionResult> Runs()
    {
        if (!await CanReadAsync()) return Forbid();
        return Ok(await _db.HrPayRuns.AsNoTracking().OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).Take(60).ToListAsync());
    }

    public class RunInput { public int Year { get; set; } public int Month { get; set; } public string? Kind { get; set; } }

    [HttpPost("runs")]
    public async Task<IActionResult> CreateRun([FromBody] RunInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        try { return Ok(await _svc.CreateRunAsync(input.Year, input.Month, MyName, input.Kind ?? "Monthly")); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    public class EydiInput { public int Year { get; set; } }

    [HttpPost("runs/eydi")]
    public async Task<IActionResult> CreateEydi([FromBody] EydiInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        try
        {
            var run = await _svc.CreateRunAsync(input.Year, 12, MyName, "Eydi");
            return Ok(await _svc.CalculateRunAsync(run.Id));
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("runs/{id:int}/calculate")]
    public async Task<IActionResult> Calculate(int id)
    {
        if (!await IsManagerAsync()) return Forbid();
        try { return Ok(await _svc.CalculateRunAsync(id)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    public class StatusInput { public string Status { get; set; } = ""; }

    [HttpPost("runs/{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, [FromBody] StatusInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        try { return Ok(await _svc.SetStatusAsync(id, input.Status)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("runs/{id:int}/compare")]
    public async Task<IActionResult> Compare(int id)
    {
        if (!await CanReadAsync()) return Forbid();
        try { return Ok(await _svc.CompareAsync(id)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("runs/{id:int}/voucher")]
    public async Task<IActionResult> IssueVoucher(int id)
    {
        if (!await IsManagerAsync()) return Forbid();
        try
        {
            var v = await _svc.IssueVoucherAsync(id, MyName);
            return Ok(new { ok = true, voucherId = v.Id, number = v.Number });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ================= فیش‌ها =================

    [HttpGet("runs/{id:int}/slips")]
    public async Task<IActionResult> Slips(int id)
    {
        if (!await CanReadAsync()) return Forbid();
        return Ok(await _db.HrPaySlips.AsNoTracking().Where(s => s.RunId == id).OrderBy(s => s.EmployeeName).ToListAsync());
    }

    [HttpGet("slips/{id:int}")]
    public async Task<IActionResult> Slip(int id)
    {
        var s = await _db.HrPaySlips.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (s == null) return NotFound();
        if (s.UserId != MyUserId && !await CanReadAsync()) return Forbid();
        var run = await _db.HrPayRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == s.RunId);
        return Ok(new
        {
            slip = s,
            runYear = run?.Year, runMonth = run?.Month, runStatus = run?.Status,
            lines = System.Text.Json.JsonSerializer.Deserialize<List<object>>(s.DetailsJson),
        });
    }

    [HttpGet("slips/my")]
    public async Task<IActionResult> MySlips()
    {
        var rows = await _db.HrPaySlips.AsNoTracking().Where(s => s.UserId == MyUserId).OrderByDescending(s => s.Id).Take(24).ToListAsync();
        var runIds = rows.Select(s => s.RunId).Distinct().ToList();
        var runs = await _db.HrPayRuns.AsNoTracking().Where(r => runIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id);
        return Ok(rows.Select(s => new
        {
            s.Id, s.RunId, s.EmployeeName, s.DaysPaid, s.GrossEarnings, s.TaxAmount, s.InsuranceAmount, s.OtherDeductions, s.NetPay,
            Year = runs.TryGetValue(s.RunId, out var r) ? r.Year : 0,
            Month = runs.TryGetValue(s.RunId, out var r2) ? r2.Month : 0,
        }));
    }

    // ================= خروجی‌ها =================

    [HttpGet("exports/insurance")]
    public async Task<IActionResult> Insurance([FromQuery] int runId, [FromQuery] string format = "txt")
    {
        if (!await CanReadAsync()) return Forbid();
        try
        {
            var (bytes, name, ct) = await _svc.InsuranceFileAsync(runId, format == "csv" ? "csv" : "txt");
            return File(bytes, ct, name);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("exports/tax")]
    public async Task<IActionResult> Tax([FromQuery] int runId, [FromQuery] string format = "txt")
    {
        if (!await CanReadAsync()) return Forbid();
        try
        {
            var (bytes, name, ct) = await _svc.TaxFileAsync(runId, format == "csv" ? "csv" : "txt");
            return File(bytes, ct, name);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("exports/bank")]
    public async Task<IActionResult> Bank([FromQuery] int runId, [FromQuery] string format = "xlsx")
    {
        if (!await CanReadAsync()) return Forbid();
        try
        {
            var (bytes, name, ct) = await _svc.BankFileAsync(runId, format == "csv" ? "csv" : "xlsx");
            return File(bytes, ct, name);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ================= تسویه =================

    [HttpGet("settlements")]
    public async Task<IActionResult> Settlements([FromQuery] int? employeeId)
    {
        if (!await CanReadAsync()) return Forbid();
        var q = _db.HrPaySettlements.AsNoTracking().AsQueryable();
        if (employeeId != null) q = q.Where(s => s.EmployeeId == employeeId);
        var rows = await q.OrderByDescending(s => s.Id).Take(500).ToListAsync();
        var emps = await _db.HrEmployees.AsNoTracking().ToDictionaryAsync(e => e.Id);
        return Ok(rows.Select(s => new
        {
            s.Id, s.EmployeeId, s.LeaveDate, s.YearsOfService, s.LastBase, s.UnusedLeaveDays,
            s.SeveranceAmount, s.LeaveRefund, s.EydiProrata, s.TotalAmount, s.Status, s.CreatedAt,
            EmployeeName = emps.TryGetValue(s.EmployeeId, out var e) ? $"{e.FirstName} {e.LastName}".Trim() : null,
        }));
    }

    public class SettlementInput { public int EmployeeId { get; set; } public DateTime LeaveDate { get; set; } public double? UnusedLeaveDays { get; set; } }

    [HttpPost("settlements/calculate")]
    public async Task<IActionResult> CalcSettlement([FromBody] SettlementInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        try { return Ok(await _svc.CalculateSettlementAsync(input.EmployeeId, input.LeaveDate, input.UnusedLeaveDays)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("settlements/{id:int}/status")]
    public async Task<IActionResult> SettlementStatus(int id, [FromBody] StatusInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        var s = await _db.HrPaySettlements.FindAsync(id);
        if (s == null) return NotFound();
        if (input.Status is not ("Approved" or "Paid" or "Draft"))
            return BadRequest(new { message = "وضعیت نامعتبر." });
        s.Status = input.Status;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpGet("employees")]
    public async Task<IActionResult> Employees()
    {
        if (!await CanReadAsync()) return Forbid();
        return Ok(await _db.HrEmployees.AsNoTracking().OrderBy(e => e.FirstName).ThenBy(e => e.LastName)
            .Select(e => new { e.Id, e.Code, e.FirstName, e.LastName, e.NationalCode, e.BaseSalary, e.Status, e.HireDate })
            .Take(2000).ToListAsync());
    }

    // ================= کارکرد ماه (نمایش در فیش/گزارش) =================

    [HttpGet("attendance-month")]
    public async Task<IActionResult> AttendanceMonth([FromQuery] int employeeId, [FromQuery] int year, [FromQuery] int month)
    {
        if (!await CanReadAsync()) return Forbid();
        var emp = await _db.HrEmployees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound();
        var uid = await _db.HrUserLinks.AsNoTracking().Where(x => x.EmployeeId == employeeId).Select(x => (int?)x.UserId).FirstOrDefaultAsync();
        return Ok(await _svc.MonthAggAsync(uid, year, month));
    }

    // ================= فاز ۳: بایگانی ارسال لیست‌های قانونی =================

    [HttpGet("filings/preflight")]
    public async Task<IActionResult> FilingPreflight([FromQuery] int runId, [FromQuery] string kind)
    {
        if (!await CanReadAsync()) return Forbid();
        try
        {
            var r = await _svc.PreflightAsync(runId, kind ?? "");
            return Ok(new { errors = r.Errors, warnings = r.Warnings, canFile = r.Errors.Count == 0 });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    public class FileInput
    {
        public int RunId { get; set; }
        public string Kind { get; set; } = "";
        public string? ReceiptNo { get; set; }
        public string? Note { get; set; }
    }

    [HttpPost("filings")]
    public async Task<IActionResult> FileList([FromBody] FileInput input)
    {
        if (!await IsManagerAsync()) return Forbid();
        try { return Ok(await _svc.FileAsync(input.RunId, input.Kind ?? "", MyName, input.ReceiptNo, input.Note)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("filings")]
    public async Task<IActionResult> Filings([FromQuery] int? runId)
    {
        if (!await CanReadAsync()) return Forbid();
        return Ok(await _svc.FilingsAsync(runId));
    }

    [HttpGet("filings/verify")]
    public async Task<IActionResult> VerifyFiling([FromQuery] int runId, [FromQuery] string kind)
    {
        if (!await CanReadAsync()) return Forbid();
        try { return Ok(await _svc.VerifyFilingAsync(runId, kind ?? "")); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }
}
