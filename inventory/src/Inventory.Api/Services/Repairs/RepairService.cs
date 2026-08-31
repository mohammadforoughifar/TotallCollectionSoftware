using Db = Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>
/// سرویس تعمیرات — پذیرش دستگاه، ثبت کارها/قطعات، تعمیرکار و صدور فاکتور فروش یکپارچه.
/// صدور فاکتور از طریق IInventoryService.CreateOrderAsync انجام می‌شود تا موجودی انبار،
/// سود، پورسانت معرف و همه منطق‌های موجود بدون تکرار اعمال شوند (یکپارچگی کامل).
/// </summary>
public class RepairService : IRepairService
{
    private readonly Db.AppDbContext _db;
    private readonly IInventoryService _inventory;

    public RepairService(Db.AppDbContext db, IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    // =============================== تعمیرکارها ===============================

    public async Task<List<Technician>> GetTechniciansAsync(bool activeOnly = false)
    {
        var q = _db.Technicians.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(t => t.IsActive);
        var list = await q.OrderBy(t => t.Name).ToListAsync();

        var active = await _db.RepairOrders
            .Where(r => r.TechnicianId != null &&
                        r.Status != RepairStatus.Delivered &&
                        r.Status != RepairStatus.Cancelled)
            .GroupBy(r => r.TechnicianId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToListAsync();

        return list.Select(t => new Technician
        {
            Id = t.Id,
            Name = t.Name,
            Phone = t.Phone,
            Specialty = t.Specialty,
            IsActive = t.IsActive,
            CreatedAt = t.CreatedAt,
            ActiveRepairs = active.FirstOrDefault(a => a.Id == t.Id)?.Count ?? 0
        }).ToList();
    }

    public async Task<Technician> SaveTechnicianAsync(Technician dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("نام تعمیرکار را وارد کنید.");

        var name = dto.Name.Trim();
        var dup = await _db.Technicians.AnyAsync(t => t.Name == name && t.Id != dto.Id);
        if (dup) throw new InvalidOperationException("تعمیرکاری با این نام از قبل وجود دارد.");

        Db.Technician entity;
        if (dto.Id == 0)
        {
            entity = new Db.Technician { CreatedAt = DateTime.Now };
            _db.Technicians.Add(entity);
        }
        else
        {
            entity = await _db.Technicians.FindAsync(dto.Id)
                ?? throw new InvalidOperationException("تعمیرکار یافت نشد.");
        }

        entity.Name = name;
        entity.Phone = NullIfEmpty(dto.Phone);
        entity.Specialty = NullIfEmpty(dto.Specialty);
        entity.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();

        dto.Id = entity.Id;
        dto.CreatedAt = entity.CreatedAt;
        return dto;
    }

    public async Task DeleteTechnicianAsync(int id)
    {
        var used = await _db.RepairOrders.AnyAsync(r => r.TechnicianId == id);
        if (used) throw new InvalidOperationException("این تعمیرکار دارای سوابق پذیرش است و قابل حذف نیست؛ می‌توانید آن را غیرفعال کنید.");
        var t = await _db.Technicians.FindAsync(id);
        if (t is null) return;
        _db.Technicians.Remove(t);
        await _db.SaveChangesAsync();
    }

    // =============================== پذیرش تعمیر ===============================

    public async Task<PagedResult<RepairOrderDto>> GetRepairsAsync(string? search, RepairStatus? status, int? technicianId, int page, int pageSize)
    {
        var q = _db.RepairOrders.AsNoTracking().Include(r => r.Items).AsQueryable();

        if (status.HasValue) q = q.Where(r => r.Status == status.Value);
        if (technicianId is > 0) q = q.Where(r => r.TechnicianId == technicianId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            var partyIds = await _db.Parties.Where(p => p.Name.Contains(s)).Select(p => p.Id).ToListAsync();
            q = q.Where(r => r.Number.Contains(s) ||
                             r.DeviceType.Contains(s) ||
                             (r.DeviceModel != null && r.DeviceModel.Contains(s)) ||
                             (r.SerialNumber != null && r.SerialNumber.Contains(s)) ||
                             partyIds.Contains(r.PartyId));
        }

        var total = await q.CountAsync();
        if (pageSize <= 0) pageSize = 15;
        if (page <= 0) page = 1;

        var items = await q.OrderByDescending(r => r.ReceivedAt).ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        var dtos = new List<RepairOrderDto>();
        foreach (var r in items) dtos.Add(await ToDtoAsync(r));

        return new PagedResult<RepairOrderDto> { Items = dtos, TotalCount = total };
    }

    public async Task<RepairOrderDto?> GetRepairAsync(int id)
    {
        var r = await _db.RepairOrders.AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        return r is null ? null : await ToDtoAsync(r);
    }

    public async Task<RepairOrderDto> SaveRepairAsync(RepairOrderDto dto)
    {
        if (dto.PartyId <= 0) throw new InvalidOperationException("مشتری (صاحب دستگاه) را انتخاب کنید.");
        if (string.IsNullOrWhiteSpace(dto.DeviceType)) throw new InvalidOperationException("نوع دستگاه را وارد کنید.");
        if (dto.Items.Any(i => i.Quantity <= 0)) throw new InvalidOperationException("مقدار هر ردیف باید بزرگ‌تر از صفر باشد.");
        if (dto.Items.Any(i => string.IsNullOrWhiteSpace(i.Description) && i.ProductId is null or 0))
            throw new InvalidOperationException("برای هر ردیف، شرح کار یا کالای مصرفی را مشخص کنید.");

        _ = await _db.Parties.FindAsync(dto.PartyId)
            ?? throw new InvalidOperationException("مشتری یافت نشد.");

        if (dto.TechnicianId is > 0)
            _ = await _db.Technicians.FindAsync(dto.TechnicianId)
                ?? throw new InvalidOperationException("تعمیرکار یافت نشد.");

        Db.RepairOrder entity;
        if (dto.Id == 0)
        {
            entity = new Db.RepairOrder
            {
                Number = await GenerateNumberAsync(),
                CreatedAt = DateTime.Now
            };
            _db.RepairOrders.Add(entity);
        }
        else
        {
            entity = await _db.RepairOrders.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == dto.Id)
                ?? throw new InvalidOperationException("پذیرش یافت نشد.");
            if (entity.InvoiceTransactionId is > 0)
                throw new InvalidOperationException("برای این پذیرش فاکتور صادر شده و قابل ویرایش نیست.");
        }

        entity.PartyId = dto.PartyId;
        entity.TechnicianId = dto.TechnicianId is > 0 ? dto.TechnicianId : null;
        entity.DeviceType = dto.DeviceType.Trim();
        entity.DeviceModel = NullIfEmpty(dto.DeviceModel);
        entity.SerialNumber = NullIfEmpty(dto.SerialNumber);
        entity.ProblemDescription = NullIfEmpty(dto.ProblemDescription);
        entity.Accessories = NullIfEmpty(dto.Accessories);
        entity.QuotedPrice = dto.QuotedPrice;
        entity.Note = NullIfEmpty(dto.Note);
        entity.ReceivedAt = dto.ReceivedAt == default ? DateTime.Now : dto.ReceivedAt;
        if (dto.Id != 0) entity.Status = dto.Status;

        // بازنویسی ردیف‌های کار/قطعه
        entity.Items.Clear();
        foreach (var i in dto.Items)
        {
            string? productName = null;
            if (i.ProductId is > 0)
            {
                var p = await _db.Products.FindAsync(i.ProductId)
                    ?? throw new InvalidOperationException("کالای مصرفی یافت نشد.");
                productName = p.Name;
            }

            entity.Items.Add(new Db.RepairItem
            {
                Description = string.IsNullOrWhiteSpace(i.Description) ? (productName ?? "") : i.Description.Trim(),
                ProductId = i.ProductId is > 0 ? i.ProductId : null,
                Quantity = i.Quantity,
                Cost = i.Cost,
                Price = i.Price,
                CreatedAt = DateTime.Now
            });
        }

        await _db.SaveChangesAsync();
        return (await GetRepairAsync(entity.Id))!;
    }

    public async Task DeleteRepairAsync(int id)
    {
        var r = await _db.RepairOrders.FindAsync(id);
        if (r is null) return;
        if (r.InvoiceTransactionId is > 0)
            throw new InvalidOperationException("برای این پذیرش فاکتور صادر شده است؛ ابتدا فاکتور فروش مربوطه را حذف کنید.");
        _db.RepairOrders.Remove(r);
        await _db.SaveChangesAsync();
    }

    public async Task<RepairOrderDto> SetStatusAsync(int id, RepairStatus status)
    {
        var r = await _db.RepairOrders.FindAsync(id)
            ?? throw new InvalidOperationException("پذیرش یافت نشد.");

        if (r.InvoiceTransactionId is > 0)
            throw new InvalidOperationException("این پذیرش فاکتور شده و وضعیت آن قابل تغییر نیست.");
        if (status == RepairStatus.Delivered)
            throw new InvalidOperationException("تحویل فقط از طریق «صدور فاکتور» انجام می‌شود تا سود و انبار به‌درستی ثبت شوند.");

        r.Status = status;
        if (status == RepairStatus.Cancelled) r.DeliveredAt = DateTime.Now; // خروج بدون تعمیر
        await _db.SaveChangesAsync();
        return (await GetRepairAsync(id))!;
    }

    public async Task<RepairOrderDto> InvoiceAsync(int id, RepairInvoiceRequest request)
    {
        var r = await _db.RepairOrders.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("پذیرش یافت نشد.");

        if (r.InvoiceTransactionId is > 0)
            throw new InvalidOperationException("برای این پذیرش قبلاً فاکتور صادر شده است.");
        if (r.Status == RepairStatus.Cancelled)
            throw new InvalidOperationException("پذیرش انصرافی قابل فاکتور شدن نیست.");
        if (r.Items.Count == 0)
            throw new InvalidOperationException("حداقل یک ردیف کار/قطعه ثبت کنید.");
        if (r.Items.All(i => i.Price <= 0))
            throw new InvalidOperationException("مبلغ دریافتی ردیف‌ها صفر است؛ ابتدا مبالغ را ثبت کنید.");

        var warehouseId = request.WarehouseId > 0 ? request.WarehouseId : 1;

        // کالای خدماتی «اجرت تعمیرات» برای ردیف‌های کاری (بدون کالا) — خودکار ساخته می‌شود
        var laborLines = r.Items.Where(i => i.ProductId is null or 0 && i.Price > 0).ToList();
        int? laborProductId = null;
        if (laborLines.Count > 0)
            laborProductId = await GetOrCreateLaborServiceAsync();

        // ساخت فاکتور فروش از طریق سرویس موجود انبار (موجودی/سود/پورسانت معرف خودکار)
        var cmd = new OrderCommand
        {
            WarehouseId = warehouseId,
            PartyId = r.PartyId,
            Type = TransactionType.Sale,
            Date = DateTime.Now,
            Description = $"فاکتور تعمیرات — پذیرش {r.Number} ({r.DeviceType}{(string.IsNullOrWhiteSpace(r.DeviceModel) ? "" : " " + r.DeviceModel)})",
            Lines = new List<OrderLineInput>()
        };

        foreach (var i in r.Items.Where(x => x.Price > 0))
        {
            if (i.ProductId is > 0)
            {
                cmd.Lines.Add(new OrderLineInput { ProductId = i.ProductId.Value, Quantity = i.Quantity, Price = i.Price });
            }
            else
            {
                // ردیف اجرت: قیمت واحد = کل مبلغ ردیف، مقدار = ۱
                cmd.Lines.Add(new OrderLineInput { ProductId = laborProductId!.Value, Quantity = 1, Price = i.Price * i.Quantity });
            }
        }

        var order = await _inventory.CreateOrderAsync(cmd);

        r.InvoiceTransactionId = order.Id;
        r.Status = RepairStatus.Delivered;
        r.DeliveredAt = DateTime.Now;
        await _db.SaveChangesAsync();

        return (await GetRepairAsync(id))!;
    }

    // =============================== کمکی ===============================

    private async Task<RepairOrderDto> ToDtoAsync(Db.RepairOrder r)
    {
        var partyName = await _db.Parties.Where(p => p.Id == r.PartyId).Select(p => p.Name).FirstOrDefaultAsync();
        var techName = r.TechnicianId.HasValue
            ? await _db.Technicians.Where(t => t.Id == r.TechnicianId.Value).Select(t => t.Name).FirstOrDefaultAsync()
            : null;
        var invoiceNumber = r.InvoiceTransactionId.HasValue
            ? await _db.Transactions.Where(t => t.Id == r.InvoiceTransactionId.Value).Select(t => t.Number).FirstOrDefaultAsync()
            : null;

        var productIds = r.Items.Where(i => i.ProductId.HasValue).Select(i => i.ProductId!.Value).Distinct().ToList();
        var productNames = productIds.Count > 0
            ? await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name)
            : new Dictionary<int, string>();

        var items = r.Items.OrderBy(i => i.Id).Select(i => new RepairItemDto
        {
            Id = i.Id,
            Description = i.Description,
            ProductId = i.ProductId,
            ProductName = i.ProductId.HasValue && productNames.TryGetValue(i.ProductId.Value, out var pn) ? pn : null,
            Quantity = i.Quantity,
            Cost = i.Cost,
            Price = i.Price
        }).ToList();

        // جمع‌ها در حافظه (SQLite جمع decimal در SQL را پشتیبانی نمی‌کند)
        var totalPrice = items.Sum(i => i.Price * i.Quantity);
        var totalCost = items.Sum(i => i.Cost * i.Quantity);

        return new RepairOrderDto
        {
            Id = r.Id,
            Number = r.Number,
            PartyId = r.PartyId,
            PartyName = partyName,
            TechnicianId = r.TechnicianId,
            TechnicianName = techName,
            DeviceType = r.DeviceType,
            DeviceModel = r.DeviceModel,
            SerialNumber = r.SerialNumber,
            ProblemDescription = r.ProblemDescription,
            Accessories = r.Accessories,
            Status = r.Status,
            ReceivedAt = r.ReceivedAt,
            DeliveredAt = r.DeliveredAt,
            QuotedPrice = r.QuotedPrice,
            InvoiceTransactionId = r.InvoiceTransactionId,
            InvoiceNumber = invoiceNumber,
            Note = r.Note,
            CreatedAt = r.CreatedAt,
            Items = items,
            TotalPrice = totalPrice,
            TotalCost = totalCost,
            Profit = totalPrice - totalCost
        };
    }

    private async Task<string> GenerateNumberAsync()
    {
        var count = await _db.RepairOrders.CountAsync();
        return $"RP-{count + 1:0000}";
    }

    /// <summary>کالای خدماتی «اجرت تعمیرات» برای ردیف‌های کاری فاکتور — در صورت نبود ساخته می‌شود.</summary>
    private async Task<int> GetOrCreateLaborServiceAsync()
    {
        const string code = "S-REPAIR";
        var existing = await _db.Products.FirstOrDefaultAsync(p => p.Code == code);
        if (existing is not null) return existing.Id;

        var svc = new Db.Product
        {
            Code = code,
            Name = "اجرت تعمیرات",
            Unit = "مورد",
            IsService = true,
            SalePrice = 0,
            PurchasePrice = 0,
            IsActive = true,
            CreatedAt = DateTime.Now
        };
        _db.Products.Add(svc);
        await _db.SaveChangesAsync();
        return svc.Id;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
