using Db = Inventory.Api.Data;
using Inventory.Api.Services.Accounting;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers.Accounting;

// =====================================================================
// بودجه و کنترل بودجه
//   api/acc/budgets            بودجه‌ها و اقلام
//   api/acc/budgets/dashboard  داشبورد مصرف
//   api/acc/budgets/transactions  رویدادهای تعهد/مصرف
// =====================================================================

/// <summary>بودجه‌ها و اقلام.</summary>
[Route("api/acc/budgets")]
public class BudgetsController : RbacControllerBase
{
    private readonly IBudgetService _svc;
    public BudgetsController(Db.AppDbContext db, IBudgetService svc) : base(db) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<List<Budget>>> GetAll([FromQuery] bool activeOnly = false)
        => Ok(await _svc.GetBudgetsAsync(activeOnly));

    [HttpGet("dashboard")]
    public async Task<ActionResult<BudgetDashboard>> GetDashboard()
        => Ok(await _svc.GetDashboardAsync());

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Budget>> Get(int id) => Ok(await _svc.GetBudgetAsync(id));

    [HttpPost]
    public async Task<ActionResult<Budget>> Save([FromBody] Budget dto)
    {
        if (await ForbiddenUnlessAsync("Budgets", dto.Id == 0 ? "Create" : "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.SaveBudgetAsync(dto));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync("Budgets", "Delete") is ObjectResult forbidden) return forbidden;
        await _svc.DeleteBudgetAsync(id);
        return Ok(new { ok = true });
    }

    // -------------------- رویدادها --------------------
    [HttpGet("{budgetId:int}/transactions")]
    public async Task<ActionResult<List<BudgetTransaction>>> GetTransactions(int budgetId, [FromQuery] int? budgetItemId = null)
        => Ok(await _svc.GetTransactionsAsync(budgetId, budgetItemId));

    [HttpPost("transactions")]
    public async Task<ActionResult<BudgetTransaction>> AddTransaction([FromBody] BudgetTransaction dto)
    {
        if (await ForbiddenUnlessAsync("Budgets", dto.Id == 0 ? "Create" : "Update") is ObjectResult forbidden) return forbidden;
        return Ok(await _svc.AddTransactionAsync(dto));
    }

    [HttpDelete("transactions/{id:int}")]
    public async Task<IActionResult> DeleteTransaction(int id)
    {
        if (await ForbiddenUnlessAsync("Budgets", "Delete") is ObjectResult forbidden) return forbidden;
        await _svc.DeleteTransactionAsync(id);
        return Ok(new { ok = true });
    }
}
