using Inventory.Api.Services;
using Inventory.Api.Services.Finance;
using Inventory.Api.Infrastructure;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>اسناد هزینه.</summary>
[Route("api/expenses")]
[Inventory.Api.Infrastructure.ModulePermission("Expenses")]
public class ExpensesController : ApiControllerBase
{
    private readonly IExpenseService _svc;
    private readonly ShopExpenseChequeService _cheques;
    public ExpensesController(IExpenseService svc, ShopExpenseChequeService cheques) { _svc = svc; _cheques = cheques; }
    private string? Actor => User.Identity?.Name ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>فهرست اسناد هزینه با جستجو/فیلتر/صفحه‌بندی.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ExpenseDto>>> GetAll(
        [FromQuery] string? search, [FromQuery] int? categoryId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 15, [FromQuery] int? skip = null, [FromQuery] int? take = null)
        => Ok(await _svc.GetExpensesAsync(search, categoryId, from, to, Paging.ToPage(skip, take, page), Paging.ToPageSize(take, pageSize)));

    /// <summary>گزارش همه اسناد هزینه مطابق فیلتر، بدون دوباره‌شماری تسویه‌ها.</summary>
    [HttpGet("report")]
    public async Task<ActionResult<ExpenseReportDto>> Report([FromQuery] int? categoryId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        => Ok(await _svc.GetReportAsync(categoryId, from, to));

    [HttpGet("cheque-banks")]
    public async Task<IActionResult> ChequeBanks() => Ok(await _cheques.BanksAsync());

    [HttpGet("available-received-cheques")]
    [RequirePermission("Orders", "Read")]
    public async Task<IActionResult> AvailableReceivedCheques() => Ok(await _cheques.EligibleReceivedAsync());

    [HttpPost("issued-cheque")]
    public async Task<IActionResult> CreateIssuedCheque([FromBody] ExpenseChequeCreateCommand cmd)
        => Ok(new { id = await _cheques.CreateAsync(cmd, ExpenseChequeKind.Issued, Actor) });

    [HttpPost("received-cheque")]
    [RequirePermission("Orders", "Update")]
    public async Task<IActionResult> CreateReceivedCheque([FromBody] ExpenseChequeCreateCommand cmd)
        => Ok(new { id = await _cheques.CreateAsync(cmd, ExpenseChequeKind.Received, Actor) });

    [HttpPost("cheques/{id:int}/issued-status")]
    public async Task<IActionResult> IssuedChequeStatus(int id, [FromBody] ExpenseChequeStatusCommand cmd)
    { await _cheques.ChangeStatusAsync(id, cmd, ExpenseChequeKind.Issued, Actor); return Ok(new { ok = true }); }

    [HttpPost("cheques/{id:int}/received-status")]
    [RequirePermission("Orders", "Update")]
    public async Task<IActionResult> ReceivedChequeStatus(int id, [FromBody] ExpenseChequeStatusCommand cmd)
    { await _cheques.ChangeStatusAsync(id, cmd, ExpenseChequeKind.Received, Actor); return Ok(new { ok = true }); }

    /// <summary>ایجاد / ویرایش سند هزینه.</summary>
    [HttpPost]
    public async Task<ActionResult<ExpenseDto>> Save([FromBody] ExpenseDto expense)
        => Ok(await _svc.SaveExpenseAsync(expense));

    /// <summary>حذف سند هزینه.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _svc.DeleteExpenseAsync(id);
        return Ok(new { ok = true });
    }
}

/// <summary>دسته‌های هزینه (قابل مدیریت).</summary>
[Route("api/expense-categories")]
[Inventory.Api.Infrastructure.ModulePermission("Expenses")]
public class ExpenseCategoriesController : ApiControllerBase
{
    private readonly IExpenseService _svc;

    public ExpenseCategoriesController(IExpenseService svc) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<List<ExpenseCategoryDto>>> GetAll([FromQuery] bool activeOnly = false, [FromQuery] int skip = 0, [FromQuery] int? take = null)
        => Ok(await Paging.ResultAsync(async pagination => await _svc.GetCategoriesAsync(activeOnly, pagination: pagination), skip, take));

    [HttpPost]
    public async Task<ActionResult<ExpenseCategoryDto>> Save([FromBody] ExpenseCategoryDto category)
        => Ok(await _svc.SaveCategoryAsync(category));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _svc.DeleteCategoryAsync(id);
        return Ok(new { ok = true });
    }
}
