using Inventory.Api.Data;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

// ============================================================
//  سرویس پیش‌نویس نامه — منطق بر اساس PishnevisService کارفرما
//  (هر کاربر فقط پیش‌نویس‌های خودش را می‌بیند/ویرایش می‌کند)
// ============================================================

public interface IPishnevisService
{
    /// <summary>فهرست پیش‌نویس‌های کاربر (بدون صفحه‌بندی — برای کمبو/مصرف داخلی).</summary>
    Task<List<PishnevisDto>> GetAllAsync(int userId, string? search);

    /// <summary>فهرست پیش‌نویس‌های کاربر با صفحه‌بندی — خروجی استاندارد GetAll.</summary>
    Task<PagedResult<PishnevisDto>> GetAllPagedAsync(int userId, string? search, int page, int pageSize);

    Task<PishnevisDto?> GetByIdAsync(int id, int userId);
    Task<int> AddAsync(PishnevisDto dto, int userId);
    Task EditAsync(PishnevisDto dto, int userId);
    Task DeleteAsync(int id, int userId);
}

public class PishnevisService : IPishnevisService
{
    /// <summary>اندازهٔ صفحهٔ پیش‌فرض فهرست پیش‌نویس‌ها.</summary>
    public const int DefaultPageSize = 20;

    private readonly AppDbContext _db;
    public PishnevisService(AppDbContext db) => _db = db;

    /// <summary>پرس‌وجوی مرتب‌شدهٔ پیش‌نویس‌ها (بدون Skip/Take) — مشترک بین نسخهٔ لیستی و صفحه‌بندی‌شده.</summary>
    private IQueryable<PishnevisDto> Query(int userId, string? search)
    {
        var q = _db.PishnevisLetters.AsNoTracking()
            .Where(p => p.UserId == userId && !p.IsDelete);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(p => p.Title.Contains(s));
        }

        return q
            .OrderByDescending(p => p.IsNeshan)
            .ThenByDescending(p => p.PishnevisId)
            .Select(p => new PishnevisDto
            {
                PishnevisId = p.PishnevisId,
                Title = p.Title,
                Text = p.Text,
                IsNeshan = p.IsNeshan
            });
    }

    public async Task<List<PishnevisDto>> GetAllAsync(int userId, string? search)
        => await Query(userId, search).ToListAsync();

    public async Task<PagedResult<PishnevisDto>> GetAllPagedAsync(int userId, string? search, int page, int pageSize)
    {
        var (p, size) = Pager.Normalize(page, pageSize, DefaultPageSize);
        var q = Query(userId, search);
        var total = await q.CountAsync();
        var items = await q.Skip((p - 1) * size).Take(size).ToListAsync();
        return new PagedResult<PishnevisDto> { TotalCount = total, Page = p, PageSize = size, Items = items };
    }

    public Task<PishnevisDto?> GetByIdAsync(int id, int userId) =>
        _db.PishnevisLetters.AsNoTracking()
            .Where(p => p.PishnevisId == id && p.UserId == userId && !p.IsDelete)
            .Select(p => new PishnevisDto
            {
                PishnevisId = p.PishnevisId,
                Title = p.Title,
                Text = p.Text,
                IsNeshan = p.IsNeshan
            })
            .FirstOrDefaultAsync();

    public async Task<int> AddAsync(PishnevisDto dto, int userId)
    {
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new Exception("عنوان پیش‌نویس الزامی است.");

        var p = new PishnevisLetter
        {
            Title = dto.Title.Trim(),
            Text = dto.Text ?? "",
            UserId = userId,
            IsNeshan = dto.IsNeshan,
            IsDelete = false
        };
        _db.PishnevisLetters.Add(p);
        await _db.SaveChangesAsync();
        return p.PishnevisId;
    }

    public async Task EditAsync(PishnevisDto dto, int userId)
    {
        var p = await _db.PishnevisLetters
            .FirstOrDefaultAsync(x => x.PishnevisId == dto.PishnevisId && x.UserId == userId && !x.IsDelete)
            ?? throw new Exception("پیش‌نویس پیدا نشد.");

        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new Exception("عنوان پیش‌نویس الزامی است.");

        p.Title = dto.Title.Trim();
        p.Text = dto.Text ?? "";
        p.IsNeshan = dto.IsNeshan;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id, int userId)
    {
        var p = await _db.PishnevisLetters
            .FirstOrDefaultAsync(x => x.PishnevisId == id && x.UserId == userId && !x.IsDelete)
            ?? throw new Exception("پیش‌نویس پیدا نشد.");
        p.IsDelete = true;
        await _db.SaveChangesAsync();
    }
}
