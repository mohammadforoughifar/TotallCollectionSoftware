using Db = Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Inventory.Api.Services.Finance;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>سرویس هزینه‌ها — دسته‌های قابل مدیریت + اسناد هزینه.</summary>
public class ExpenseService : IExpenseService
{
    private readonly Db.AppDbContext _db;

    public ExpenseService(Db.AppDbContext db) => _db = db;

    // =============================== دسته‌ها ===============================

    public async Task<List<ExpenseCategoryDto>> GetCategoriesAsync(bool activeOnly = false, Paging.Request? pagination = null)
    {
        var q = _db.ExpenseCategories.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(c => c.IsActive);
        var cats = await q.OrderBy(c => c.Name).ThenBy(x => x.Id).ToPageListAsync(pagination);

        // آمار در حافظه (SQLite جمع decimal در SQL ندارد)
        var ids = cats.Select(c => c.Id).ToList();
        var expenses = await _db.Expenses.AsNoTracking().Where(e => ids.Contains(e.CategoryId)).ToListAsync();
        var stats = expenses.GroupBy(e => e.CategoryId)
            .ToDictionary(g => g.Key, g => new { Count = g.Count(), Total = g.Sum(x => x.Amount) });

        return cats.Select(c => new ExpenseCategoryDto
        {
            Id = c.Id,
            Name = c.Name,
            IsActive = c.IsActive,
            CreatedAt = c.CreatedAt,
            ExpenseCount = stats.TryGetValue(c.Id, out var s) ? s.Count : 0,
            TotalAmount = stats.TryGetValue(c.Id, out var s2) ? s2.Total : 0
        }).ToList();
    }

    public async Task<ExpenseCategoryDto> SaveCategoryAsync(ExpenseCategoryDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("نام دسته هزینه را وارد کنید.");

        var name = dto.Name.Trim();
        var dup = await _db.ExpenseCategories.AnyAsync(c => c.Name == name && c.Id != dto.Id);
        if (dup) throw new InvalidOperationException("دسته‌ای با این نام از قبل وجود دارد.");

        Db.ExpenseCategory entity;
        if (dto.Id == 0)
        {
            entity = new Db.ExpenseCategory { CreatedAt = DateTime.Now };
            _db.ExpenseCategories.Add(entity);
        }
        else
        {
            entity = await _db.ExpenseCategories.FindAsync(dto.Id)
                ?? throw new InvalidOperationException("دسته هزینه یافت نشد.");
        }

        entity.Name = name;
        entity.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();

        dto.Id = entity.Id;
        dto.CreatedAt = entity.CreatedAt;
        return dto;
    }

    public async Task DeleteCategoryAsync(int id)
    {
        var used = await _db.Expenses.AnyAsync(e => e.CategoryId == id);
        if (used) throw new InvalidOperationException("این دسته دارای سند هزینه است و قابل حذف نیست؛ می‌توانید آن را غیرفعال کنید.");
        var c = await _db.ExpenseCategories.FindAsync(id);
        if (c is null) return;
        _db.ExpenseCategories.Remove(c);
        await _db.SaveChangesAsync();
    }

    // =============================== اسناد هزینه ===============================

    public async Task<PagedResult<ExpenseDto>> GetExpensesAsync(string? search, int? categoryId, DateTime? from, DateTime? to, int page, int pageSize)
    {
        var q = _db.Expenses.AsNoTracking().AsQueryable();
        if (categoryId is > 0) q = q.Where(e => e.CategoryId == categoryId);
        if (from.HasValue) q = q.Where(e => e.Date >= from.Value.Date);
        if (to.HasValue) q = q.Where(e => e.Date < to.Value.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(e => e.Number.Contains(s) ||
                             (e.Payee != null && e.Payee.Contains(s)) ||
                             (e.Description != null && e.Description.Contains(s)));
        }

        var total = await q.CountAsync();
        if (pageSize <= 0) pageSize = 15;
        if (page <= 0) page = 1;

        var items = await q.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var catNames = await _db.ExpenseCategories.ToDictionaryAsync(c => c.Id, c => c.Name);

        var mapped = items.Select(e => new ExpenseDto
            {
                Id = e.Id,
                Number = e.Number,
                CategoryId = e.CategoryId,
                CategoryName = catNames.TryGetValue(e.CategoryId, out var n) ? n : "—",
                Amount = e.Amount,
                Date = e.Date,
                PayType = e.PayType,
                TrsAccountId = e.TrsAccountId,
                TrsAccountName = e.TrsAccountName,
                Payee = e.Payee,
                Description = e.Description,
                CreatedAt = e.CreatedAt
            }).ToList();
        await AttachChequeHistoryAsync(mapped);
        return new PagedResult<ExpenseDto> { Items = mapped, TotalCount = total };
    }

    private async Task AttachChequeHistoryAsync(List<ExpenseDto> rows)
    {
        var ids = rows.Select(r => r.Id).ToList();
        if (ids.Count == 0) return;
        var cheques = await _db.ShopExpenseCheques.AsNoTracking().Include(c => c.Expense)
            .Include(c => c.SettlementVoucher).ThenInclude(v => v!.Lines)
            .Where(c => ids.Contains(c.ExpenseId)).OrderBy(c => c.Id).ToListAsync();
        foreach (var cheque in cheques) ShopExpenseChequeIntegrity.Validate(cheque);
        var byExpense = cheques.ToLookup(c => c.ExpenseId);
        foreach (var row in rows) row.Cheques = byExpense[row.Id].Select(c => new ExpenseChequeDto
        {
            Id = c.Id, Kind = c.Kind, State = c.State, SourceChequeId = c.SourceChequeId, IssuingAccountId = c.IssuingAccountId,
            IssuingAccountName = c.Kind == ExpenseChequeKind.Issued ? c.BankName : null, Number = c.Number,
            BankName = c.BankName, Recipient = c.Recipient, Amount = c.Amount, DueDate = c.DueDate,
            HandedAt = c.HandedAt, SettledAt = c.SettledAt, Reason = c.Reason
        }).ToList();
    }

    public async Task<ExpenseReportDto> GetReportAsync(int? categoryId, DateTime? from, DateTime? to)
    {
        if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
            throw new InvalidOperationException("تاریخ شروع نمی‌تواند پس از تاریخ پایان باشد.");
        var q = _db.Expenses.AsNoTracking().AsQueryable();
        if (categoryId is > 0) q = q.Where(e => e.CategoryId == categoryId);
        if (from.HasValue) q = q.Where(e => e.Date >= from.Value.Date);
        if (to.HasValue && to.Value.Date < DateTime.MaxValue.Date)
            q = q.Where(e => e.Date < to.Value.Date.AddDays(1));
        // SQLite cannot SUM decimal natively. Project only report columns and total in memory;
        // the rows and totals come from the same filtered snapshot, never from a page.
        var rows = await q.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id)
            .Select(e => new ExpenseDto
            {
                Id = e.Id, Number = e.Number, CategoryId = e.CategoryId, Amount = e.Amount,
                Date = e.Date, PayType = e.PayType, TrsAccountId = e.TrsAccountId,
                TrsAccountName = e.TrsAccountName, Payee = e.Payee, Description = e.Description,
                CreatedAt = e.CreatedAt
            }).ToListAsync();
        var ids = rows.Select(r => r.CategoryId).Distinct().ToList();
        var names = await _db.ExpenseCategories.AsNoTracking().Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name);
        foreach (var row in rows)
            row.CategoryName = names.TryGetValue(row.CategoryId, out var name) ? name : "—";
        await AttachChequeHistoryAsync(rows);
        return new ExpenseReportDto
        {
            Count = rows.Count, TotalAmount = rows.Sum(r => r.Amount), Items = rows,
            Categories = rows.GroupBy(r => r.CategoryId).Select(g => new ExpenseReportCategoryDto
            {
                CategoryId = g.Key, Name = names.TryGetValue(g.Key, out var name) ? name : "—",
                Count = g.Count(), Amount = g.Sum(r => r.Amount)
            }).OrderByDescending(c => c.Amount).ThenBy(c => c.Name).ToList()
        };
    }

    public async Task<ExpenseDto> SaveExpenseAsync(ExpenseDto dto)
    {
        if (dto.Cheques is { Count: > 0 } || (dto.Id > 0 && await _db.ShopExpenseCheques.AnyAsync(c => c.ExpenseId == dto.Id)))
            throw new InvalidOperationException("هزینه دارای تاریخچه چک از مسیر معمول قابل ویرایش نیست.");
        if (dto.CategoryId <= 0)
            throw new InvalidOperationException("نوع هزینه را انتخاب کنید.");
        if (dto.Amount <= 0)
            throw new InvalidOperationException("مبلغ هزینه باید بزرگ‌تر از صفر باشد.");
        _ = await _db.ExpenseCategories.FindAsync(dto.CategoryId)
            ?? throw new InvalidOperationException("دسته هزینه یافت نشد.");

        Db.Expense entity;
        if (dto.Id == 0)
        {
            var count = await _db.Expenses.CountAsync();
            entity = new Db.Expense { Number = $"EX-{count + 1:0000}", CreatedAt = DateTime.Now };
            _db.Expenses.Add(entity);
        }
        else
        {
            entity = await _db.Expenses.FindAsync(dto.Id)
                ?? throw new InvalidOperationException("سند هزینه یافت نشد.");
        }

        entity.CategoryId = dto.CategoryId;
        entity.Amount = dto.Amount;
        entity.Date = dto.Date == default ? DateTime.Now : dto.Date;
        entity.PayType = dto.PayType;
        entity.TrsAccountId = dto.TrsAccountId;
        entity.TrsAccountName = dto.TrsAccountId is int accId
            ? (await _db.TrsAccounts.FindAsync(accId))?.Name
            : null;
        entity.Payee = string.IsNullOrWhiteSpace(dto.Payee) ? null : dto.Payee.Trim();
        entity.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        await _db.SaveChangesAsync();
        dto.TrsAccountId = entity.TrsAccountId;
        dto.TrsAccountName = entity.TrsAccountName;

        var cat = await _db.ExpenseCategories.FindAsync(entity.CategoryId);
        dto.Id = entity.Id;
        dto.Number = entity.Number;
        dto.CategoryName = cat?.Name;
        dto.CreatedAt = entity.CreatedAt;
        return dto;
    }

    public async Task DeleteExpenseAsync(int id)
    {
        var e = await _db.Expenses.FindAsync(id);
        if (e is null) return;
        if (await _db.ShopExpenseCheques.AnyAsync(c => c.ExpenseId == id))
            throw new InvalidOperationException("هزینه دارای سابقه چک برای حفظ تاریخچه قابل حذف نیست.");
        _db.Expenses.Remove(e);
        await _db.SaveChangesAsync();
    }

    public async Task<decimal> GetTotalAsync(DateTime from, DateTime to)
    {
        var list = await _db.Expenses.AsNoTracking()
            .Where(e => e.Date >= from && e.Date < to).ToListAsync();
        return list.Sum(e => e.Amount);
    }
}
