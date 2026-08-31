using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>اسناد هزینه.</summary>
[Route("api/expenses")]
public class ExpensesController : ApiControllerBase
{
    private readonly IExpenseService _svc;

    public ExpensesController(IExpenseService svc) => _svc = svc;

    /// <summary>فهرست اسناد هزینه با جستجو/فیلتر/صفحه‌بندی.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ExpenseDto>>> GetAll(
        [FromQuery] string? search, [FromQuery] int? categoryId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 15)
        => Ok(await _svc.GetExpensesAsync(search, categoryId, from, to, page, pageSize));

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
public class ExpenseCategoriesController : ApiControllerBase
{
    private readonly IExpenseService _svc;

    public ExpenseCategoriesController(IExpenseService svc) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<List<ExpenseCategoryDto>>> GetAll([FromQuery] bool activeOnly = false)
        => Ok(await _svc.GetCategoriesAsync(activeOnly));

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
