using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Catalog;

/// <summary>سرویس حواله تحویل کالا (فقط مقدار — بدون قیمت).</summary>
public interface IGoodsIssueService
{
    Task<List<GoodsIssueDto>> GetAsync(int? partyId = null, DateTime? from = null, DateTime? to = null, string? search = null, CancellationToken ct = default);
    Task<GoodsIssueDto?> GetAsync(int id);

    /// <summary>ثبت یا ویرایش حواله. شماره فقط هنگام ثبت جدید ساخته می‌شود.</summary>
    Task<GoodsIssueDto> SaveAsync(GoodsIssueCommand cmd);
    Task DeleteAsync(int id);

    /// <summary>
    /// کالاهای انباریِ شرکت فعال با موجودی — برای انتخاب در سطرهای بیجک.
    /// ورود نام آزاد (دستی) همچنان مجاز است؛ این فهرست فقط انتخاب را سریع می‌کند.
    /// </summary>
    Task<List<GoodsIssueProductOptionDto>> GetProductOptionsAsync();
    Task<List<LookupItem>> GetWarehouseOptionsAsync();
}

public class GoodsIssueService : IGoodsIssueService
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;

    public GoodsIssueService(AppDbContext db, IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    public Task<List<LookupItem>> GetWarehouseOptionsAsync() => _db.Warehouses.AsNoTracking()
        .Where(w => w.IsActive).OrderBy(w => w.Name).ThenBy(w => w.Id)
        .Select(w => new LookupItem { Id = w.Id, Name = w.Name }).ToListAsync();

    /// <summary>
    /// فهرست کالاهای انباری با موجودی کل. خدمات (بدون موجودی) کنار گذاشته می‌شوند چون
    /// بیجک برای تحویل کالای فیزیکی است. کالای غیرفعال هم می‌ماند تا حوالهٔ کالای
    /// کم‌گردش ولی موجود همچنان ثبت شود و در فهرست با برچسب «غیرفعال» دیده شود.
    /// </summary>
    public async Task<List<GoodsIssueProductOptionDto>> GetProductOptionsAsync()
    {
        var page = await _inventory.GetProductsAsync(null, false, 1, MaxProductOptions);
        return page.Items
            .Where(p => !p.IsService)
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Select(p => new GoodsIssueProductOptionDto
            {
                Id = p.Id,
                Code = p.Code,
                Name = p.Name,
                Unit = p.Unit,
                TotalStock = p.TotalStock,
                WarehouseName = p.WarehouseName,
                AllowsFractionalQuantity = p.AllowsFractionalQuantity,
                IsActive = p.IsActive
            })
            .ToList();
    }

    /// <summary>سقف فهرست انتخاب کالا (هم‌اندازهٔ سقف صفحه‌بندی کالاها).</summary>
    private const int MaxProductOptions = 10000;

    /// <summary>سقف فهرست حواله‌ها — فقط تازه‌ترین‌ها؛ بقیه با تنگ‌تر کردن فیلتر پیدا می‌شوند.</summary>
    private const int MaxListResults = 200;

    public async Task<List<GoodsIssueDto>> GetAsync(int? partyId = null, DateTime? from = null, DateTime? to = null, string? search = null, CancellationToken ct = default)
    {
        var q = _db.GoodsIssues.AsNoTracking().AsQueryable();

        if (partyId is > 0) q = q.Where(g => g.PartyId == partyId);
        if (from.HasValue) q = q.Where(g => g.Date >= from.Value.Date);
        if (to.HasValue) q = q.Where(g => g.Date <= to.Value.Date.AddDays(1).AddTicks(-1));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(g => g.Number.Contains(s) || g.PartyName.Contains(s)
                || (g.Description != null && g.Description.Contains(s))
                || g.Lines.Any(l => l.ProductName.Contains(s))
                || _db.Parties.MatchingPhone(s).Any(p => p.Id == g.PartyId));
        }

        // دو مرحله‌ای: اول فقط شناسه‌های ۲۰۰ تای آخر (بدون Include)، بعد همان‌ها با سطرها.
        // Include روی کل جدول، همهٔ حواله‌ها و همهٔ سطرهایشان را یک‌جا می‌خواند و
        // فهرست را کند و پاسخ را سنگین می‌کرد (اسپینرِ بی‌پایان پس از اعمال فیلتر).
        var ids = await q.OrderByDescending(g => g.Date).ThenByDescending(g => g.Id)
            .Select(g => g.Id).Take(MaxListResults).ToListAsync(ct);
        if (ids.Count == 0) return new List<GoodsIssueDto>();

        var list = await _db.GoodsIssues.Include(g => g.Lines).AsNoTracking()
            .Where(g => ids.Contains(g.Id)).ToListAsync(ct);
        var byId = list.ToDictionary(g => g.Id);
        return ids.Where(byId.ContainsKey).Select(id => ToDto(byId[id])).ToList();
    }

    public async Task<GoodsIssueDto?> GetAsync(int id)
    {
        var g = await _db.GoodsIssues.Include(x => x.Lines).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return g is null ? null : ToDto(g);
    }

    public async Task<GoodsIssueDto> SaveAsync(GoodsIssueCommand cmd)
    {
        if (cmd.PartyId <= 0) throw new InvalidOperationException("مشتری را انتخاب کنید.");
        if (cmd.Lines.Count == 0) throw new InvalidOperationException("حداقل یک کالا با مقدار وارد کنید.");
        if (cmd.Lines.Any(l => l.Quantity <= 0)) throw new InvalidOperationException("مقدار هر سطر باید بزرگ‌تر از صفر باشد.");

        var party = await _db.Parties.FirstOrDefaultAsync(p => p.Id == cmd.PartyId)
            ?? throw new InvalidOperationException("مشتری انتخاب‌شده یافت نشد؛ از فهرست مشتری‌ها دوباره انتخاب کنید.");

        // سطر «انتخاب از انبار» شناسهٔ کالا دارد و سطر «دستی» ندارد (ProductId = 0).
        // نام snapshot از خود کالا خوانده می‌شود تا متن و شناسه هرگز از هم جدا نشوند.
        var chosenIds = cmd.Lines.Where(l => l.ProductId > 0).Select(l => l.ProductId).Distinct().ToList();
        if (chosenIds.Count > 0)
        {
            var chosenProducts = await _db.Products.AsNoTracking()
                .Where(p => chosenIds.Contains(p.Id) && !p.IsService)
                .ToDictionaryAsync(p => p.Id, p => p.Name);
            foreach (var line in cmd.Lines.Where(l => l.ProductId > 0))
                line.ProductName = chosenProducts.TryGetValue(line.ProductId, out var productName)
                    ? productName
                    : throw new InvalidOperationException("کالای انتخاب‌شده از انبار یافت نشد؛ از فهرست دوباره انتخاب کنید.");
        }

        // سطر دستی باید نام داشته باشد؛ سطر انبار نامش از خود کالا پر شد.
        if (cmd.Lines.Any(l => string.IsNullOrWhiteSpace(l.ProductName)))
            throw new InvalidOperationException("نام کالای هر سطر را وارد کنید.");

        var warehouse = cmd.WarehouseId is > 0
            ? await _db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == cmd.WarehouseId)
                ?? throw new InvalidOperationException("انبار انتخاب‌شده یافت نشد؛ دوباره انتخاب کنید.")
            : null;

        // Serialize number allocation with the insert; the unique index remains the final guard.
        await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        GoodsIssue entity;
        if (cmd.Id == 0)
        {
            entity = new GoodsIssue { Number = await GenerateNumberAsync(), CreatedAt = DateTime.Now };
            _db.GoodsIssues.Add(entity);
        }
        else
        {
            entity = await _db.GoodsIssues.Include(g => g.Lines).FirstOrDefaultAsync(g => g.Id == cmd.Id)
                ?? throw new InvalidOperationException("حواله یافت نشد.");
            _db.GoodsIssueLines.RemoveRange(entity.Lines);
            entity.Lines.Clear();
        }

        entity.Date = cmd.Date == default ? DateTime.Now : cmd.Date;
        entity.PartyId = party.Id;
        entity.PartyName = party.Name;
        entity.WarehouseId = warehouse?.Id;
        entity.WarehouseName = warehouse?.Name;
        entity.Description = string.IsNullOrWhiteSpace(cmd.Description) ? null : cmd.Description.Trim();

        foreach (var l in cmd.Lines)
            entity.Lines.Add(new GoodsIssueLine
            {
                ProductId = l.ProductId,               // 0 = سطر دستی، > 0 = انتخاب‌شده از انبار
                ProductName = l.ProductName.Trim(),    // snapshot نام در لحظهٔ ثبت
                Quantity = l.Quantity,
                Note = string.IsNullOrWhiteSpace(l.Note) ? null : l.Note.Trim()
            });

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        // پس از ذخیره، Id سطرها هم پر شده است
        var saved = await _db.GoodsIssues.Include(g => g.Lines).AsNoTracking().FirstAsync(g => g.Id == entity.Id);
        return ToDto(saved);
    }

    public async Task DeleteAsync(int id)
    {
        var g = await _db.GoodsIssues.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (g is null) return;
        _db.GoodsIssues.Remove(g);   // سطرها با Cascade حذف می‌شوند
        await _db.SaveChangesAsync();
    }

    private static GoodsIssueDto ToDto(GoodsIssue g) => new()
    {
        Id = g.Id,
        Number = g.Number,
        Date = g.Date,
        PartyId = g.PartyId,
        PartyName = g.PartyName,
        WarehouseId = g.WarehouseId,
        WarehouseName = g.WarehouseName,
        Description = g.Description,
        Lines = g.Lines.Select(l => new GoodsIssueLineDto
        {
            Id = l.Id,
            ProductId = l.ProductId,
            ProductName = l.ProductName,
            Quantity = l.Quantity,
            Note = l.Note
        }).ToList()
    };

    /// <summary>شمارهٔ حواله: <c>HI-1405-0001</c> — سال شمسی + شمارهٔ متوالی در همان سال.</summary>
    private async Task<string> GenerateNumberAsync()
    {
        var (jy, _, _) = PersianDate.FromGregorian(DateTime.Now);
        var prefix = $"HI-{jy}-";
        // Older installations may still have a global unique index. Allocate across
        // companies as well, and use the largest suffix rather than a row count:
        // deleting an earlier issue must never reuse an existing number.
        var numbers = await _db.GoodsIssues.IgnoreQueryFilters().AsNoTracking()
            .Where(g => g.Number.StartsWith(prefix)).Select(g => g.Number).ToListAsync();
        var max = 0;
        foreach (var number in numbers)
            if (int.TryParse(number.Substring(prefix.Length), out var n) && n > max) max = n;
        return $"{prefix}{checked(max + 1):0000}";
    }
}
