using System.Data;
using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Repairs;

/// <summary>گارانتی مستقل از تعمیر عادی؛ مبلغ مشتری در هر دو تصمیم همیشه صفر است.</summary>
public class ShopWarrantyService
{
    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;
    public ShopWarrantyService(AppDbContext db, IInventoryService inventory) { _db = db; _inventory = inventory; }
    private static string? Optional(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    // Narrow shop-scoped lookups so repair staff do not need ERP/catalog management permissions.
    public async Task<ShopWarrantyLookupsDto> LookupsAsync() => new()
    {
        Customers = await _db.Parties.AsNoTracking().Where(p => p.Type == PartyType.Customer && p.IsActive)
            .OrderBy(p => p.Name).Select(p => new LookupItem { Id = p.Id, Name = p.Name }).ToListAsync(),
        Products = await _db.Products.AsNoTracking().Where(p => p.IsActive && !p.IsService)
            .OrderBy(p => p.Name).Select(p => new LookupItem { Id = p.Id, Name = p.Name }).ToListAsync(),
        Warehouses = await _db.Warehouses.AsNoTracking().Where(w => w.IsActive)
            .OrderBy(w => w.Name).Select(w => new LookupItem { Id = w.Id, Name = w.Name }).ToListAsync(),
        Technicians = await _db.Technicians.AsNoTracking().Where(t => t.IsActive)
            .OrderBy(t => t.Name).Select(t => new LookupItem { Id = t.Id, Name = t.Name }).ToListAsync()
    };

    private IQueryable<ShopWarranty> FullQuery() => _db.ShopWarranties.AsNoTracking()
        .Include(w => w.Party).Include(w => w.Technician).Include(w => w.Warehouse)
        .Include(w => w.Items).ThenInclude(i => i.Product)
        .Include(w => w.Devices)
        .Include(w => w.StockTransaction).ThenInclude(t => t!.Lines).AsSplitQuery();

    public async Task<List<ShopWarrantyDto>> ListAsync(DateTime? from = null, DateTime? to = null, bool finalizedOnly = false)
    {
        if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
            throw new InvalidOperationException("تاریخ شروع گزارش پس از تاریخ پایان است.");
        var query = FullQuery();
        if (finalizedOnly) query = query.Where(w => w.FinalizedAt.HasValue);
        if (from.HasValue) query = query.Where(w => w.FinalizedAt >= from.Value.Date);
        if (to.HasValue && to.Value.Date < DateTime.MaxValue.Date)
            query = query.Where(w => w.FinalizedAt < to.Value.Date.AddDays(1));
        var list = await query.OrderByDescending(w => w.ReceivedAt).ThenByDescending(w => w.Id).ToListAsync();
        return list.Select(ToDto).ToList();
    }

    private static ShopWarrantyDto ToDto(ShopWarranty w)
    {
        if (w.OperationId == Guid.Empty || w.Party.CompanyId != w.CompanyId || w.Warehouse.CompanyId != w.CompanyId ||
            (w.Technician is not null && w.Technician.CompanyId != w.CompanyId) || w.ReceivedAt == default ||
            (w.FinalizedAt.HasValue && w.FinalizedAt.Value.Date < w.ReceivedAt.Date) ||
            w.Devices.Any(d => d.CompanyId != w.CompanyId || d.Position <= 0 || string.IsNullOrWhiteSpace(d.DeviceType)) ||
            w.Items.Any(i => i.CompanyId != w.CompanyId || i.Quantity <= 0 || i.UnitCost < 0 ||
                i.Decision is not (WarrantyDecision.Accepted or WarrantyDecision.Rejected) ||
                (i.ProductId is > 0 && i.Product is null) ||
                (i.Product is not null && (i.Product.CompanyId != w.CompanyId || i.Product.IsService)) ||
                (i.Supplied && i.ProductId is not > 0)))
            throw new InvalidOperationException("سوابق پذیرش گارانتی معتبر نیست و نیازمند بررسی است.");
        var supplied = w.Items.Where(i => i.Supplied && i.ProductId is > 0).ToList();
        if (!w.FinalizedAt.HasValue && w.StockTransactionId.HasValue)
            throw new InvalidOperationException("پذیرش گارانتی نهایی‌نشده دارای گردش انبار است.");
        if (w.FinalizedAt.HasValue && supplied.Count > 0)
        {
            var movement = w.StockTransaction;
            if (movement is null || movement.CompanyId != w.CompanyId || movement.Type != TransactionType.WarrantyOut ||
                movement.PartyId != w.PartyId || movement.WarehouseId != w.WarehouseId || movement.Amount != 0 ||
                movement.CashAmount != 0 || movement.SettledAmount != 0 || movement.ShopCashLedgerEnabled ||
                movement.Lines.Count != supplied.Count || movement.Lines.Any(l => l.CompanyId != w.CompanyId || l.Price != 0) ||
                supplied.GroupBy(i => (i.ProductId, i.Quantity, i.UnitCost)).Any(g =>
                    movement.Lines.Count(l => l.ProductId == g.Key.ProductId && l.Quantity == g.Key.Quantity && l.UnitCostSnapshot == g.Key.UnitCost) != g.Count()))
                throw new InvalidOperationException("گردش انبار با قطعات تحویل‌شده گارانتی مطابقت ندارد.");
        }
        else if (w.FinalizedAt.HasValue && w.StockTransactionId.HasValue)
            throw new InvalidOperationException("پذیرش گارانتی بدون قطعه مصرفی نباید گردش انبار داشته باشد.");
        return new ShopWarrantyDto
        {
            Id = w.Id, OperationId = w.OperationId, Number = w.Number, PartyId = w.PartyId, PartyName = w.Party.Name,
            TechnicianId = w.TechnicianId, TechnicianName = w.Technician?.Name, WarehouseId = w.WarehouseId,
            DeviceType = w.DeviceType, DeviceModel = w.DeviceModel, SerialNumber = w.SerialNumber,
            ProblemDescription = w.ProblemDescription, Accessories = w.Accessories, Note = w.Note,
            ReceivedAt = w.ReceivedAt, FinalizedAt = w.FinalizedAt, StockTransactionId = w.StockTransactionId,
            Devices = w.Devices.Count == 0 ? new List<ShopWarrantyDeviceDto> { new() {
                DeviceType = w.DeviceType, DeviceModel = w.DeviceModel, SerialNumber = w.SerialNumber,
                ProblemDescription = w.ProblemDescription, Accessories = w.Accessories
            } } : w.Devices.OrderBy(d => d.Position).ThenBy(d => d.Id).Select(d => new ShopWarrantyDeviceDto {
                Id = d.Id, DeviceType = d.DeviceType, DeviceModel = d.DeviceModel, SerialNumber = d.SerialNumber,
                ProblemDescription = d.ProblemDescription, Accessories = d.Accessories
            }).ToList(),
            Items = w.Items.OrderBy(i => i.Id).Select(i => new ShopWarrantyItemDto
            {
                Id = i.Id, Description = i.Description, ProductId = i.ProductId, ProductName = i.Product?.Name,
                Quantity = i.Quantity, UnitCost = i.UnitCost, Decision = i.Decision, Supplied = i.Supplied
            }).ToList()
        };
    }

    private async Task<ShopWarrantyDto> GetAsync(int id)
    {
        var item = await FullQuery().FirstOrDefaultAsync(w => w.Id == id)
            ?? throw new InvalidOperationException("پذیرش گارانتی یافت نشد.");
        return ToDto(item);
    }

    public async Task<ShopWarrantyDto> SaveDraftAsync(ShopWarrantyDto dto, string? user)
    {
        ArgumentNullException.ThrowIfNull(dto);
        // Older clients send only the top-level device; keep them compatible.
        var devices = dto.Devices is { Count: > 0 } ? dto.Devices : new List<ShopWarrantyDeviceDto> { new() {
            DeviceType = dto.DeviceType, DeviceModel = dto.DeviceModel, SerialNumber = dto.SerialNumber,
            ProblemDescription = dto.ProblemDescription, Accessories = dto.Accessories
        } };
        if (dto.OperationId == Guid.Empty || dto.PartyId <= 0 || dto.WarehouseId <= 0 || dto.ReceivedAt == default ||
            dto.Note?.Trim().Length > 500 || user?.Length > 120 || devices.Count > 100 ||
            devices.Any(d => d is null || string.IsNullOrWhiteSpace(d.DeviceType) || d.DeviceType.Trim().Length > 100 ||
                d.DeviceModel?.Trim().Length > 200 || d.SerialNumber?.Trim().Length > 100 ||
                d.ProblemDescription?.Trim().Length > 1000 || d.Accessories?.Trim().Length > 500) ||
            dto.Items is null || dto.Items.Count == 0 || dto.Items.Count > 100)
            throw new InvalidOperationException("مشخصات پذیرش گارانتی معتبر نیست.");
        if (dto.Items.Any(i => string.IsNullOrWhiteSpace(i.Description) || i.Description.Trim().Length > 300 ||
            i.Quantity <= 0 || i.Quantity > ShopInvoiceAmounts.MaximumQuantity || i.Quantity != decimal.Round(i.Quantity, 2) ||
            i.UnitCost < 0 || i.UnitCost > 9999999999999999.99m || decimal.Round(i.UnitCost, 2) != i.UnitCost ||
            !ShopInvoiceAmounts.TryGross(i.Quantity, i.UnitCost, out _) ||
            i.Decision is not (WarrantyDecision.Accepted or WarrantyDecision.Rejected) || (i.Supplied && i.ProductId is not > 0)))
            throw new InvalidOperationException("شرح، مقدار، هزینه داخلی و تصمیم هر ردیف گارانتی را درست وارد کنید.");
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var party = await _db.Parties.FirstOrDefaultAsync(p => p.Id == dto.PartyId && p.Type == PartyType.Customer)
            ?? throw new InvalidOperationException("مشتری یافت نشد.");
        var warehouse = await _db.Warehouses.FirstOrDefaultAsync(w => w.Id == dto.WarehouseId)
            ?? throw new InvalidOperationException("انبار یافت نشد.");
        if (party.CompanyId != warehouse.CompanyId) throw new InvalidOperationException("شرکت مشتری و انبار یکسان نیست.");
        if (dto.TechnicianId is > 0 && !await _db.Technicians.AnyAsync(t => t.Id == dto.TechnicianId && t.CompanyId == party.CompanyId))
            throw new InvalidOperationException("تعمیرکار یافت نشد.");
        var productIds = dto.Items.Where(i => i.ProductId is > 0).Select(i => i.ProductId!.Value).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id) && !p.IsService && p.CompanyId == party.CompanyId).ToListAsync();
        if (products.Count != productIds.Count) throw new InvalidOperationException("کالای گارانتی معتبر یا متعلق به شرکت جاری نیست.");
        ShopWarranty record;
        if (dto.Id == 0)
        {
            var existing = await _db.ShopWarranties.IgnoreQueryFilters().FirstOrDefaultAsync(w => w.OperationId == dto.OperationId);
            if (existing is not null)
            {
                if (existing.CompanyId != party.CompanyId) throw new InvalidOperationException("شناسه عملیات قبلاً استفاده شده است.");
                var previous = await GetAsync(existing.Id);
                var same = previous.PartyId == dto.PartyId && previous.WarehouseId == dto.WarehouseId && previous.TechnicianId == dto.TechnicianId &&
                    previous.ReceivedAt.Date == dto.ReceivedAt.Date && previous.Devices.Count == devices.Count &&
                    previous.Devices.Zip(devices).All(pair => pair.First.DeviceType == pair.Second.DeviceType.Trim() &&
                        pair.First.DeviceModel == Optional(pair.Second.DeviceModel) && pair.First.SerialNumber == Optional(pair.Second.SerialNumber) &&
                        pair.First.ProblemDescription == Optional(pair.Second.ProblemDescription) && pair.First.Accessories == Optional(pair.Second.Accessories)) &&
                    previous.Note == Optional(dto.Note) && previous.Items.Count == dto.Items.Count &&
                    previous.Items.Zip(dto.Items).All(pair => pair.First.Description == pair.Second.Description.Trim() &&
                        pair.First.ProductId == (pair.Second.ProductId is > 0 ? pair.Second.ProductId : null) && pair.First.Quantity == pair.Second.Quantity &&
                        pair.First.UnitCost == pair.Second.UnitCost && pair.First.Decision == pair.Second.Decision && pair.First.Supplied == pair.Second.Supplied);
                if (!same) throw new InvalidOperationException("شناسه عملیات قبلاً با اطلاعات دیگری استفاده شده است.");
                await transaction.CommitAsync();
                return previous;
            }
            record = new ShopWarranty { OperationId = dto.OperationId, CreatedBy = user };
            _db.ShopWarranties.Add(record);
        }
        else
        {
            record = await _db.ShopWarranties.Include(w => w.Items).Include(w => w.Devices).AsSplitQuery().FirstOrDefaultAsync(w => w.Id == dto.Id)
                ?? throw new InvalidOperationException("پذیرش گارانتی یافت نشد.");
            if (record.FinalizedAt.HasValue || record.OperationId != dto.OperationId)
                throw new InvalidOperationException("پذیرش گارانتی نهایی‌شده قابل ویرایش نیست.");
            _db.ShopWarrantyItems.RemoveRange(record.Items);
            record.Items.Clear();
        }
        record.PartyId = party.Id; record.WarehouseId = warehouse.Id; record.TechnicianId = dto.TechnicianId is > 0 ? dto.TechnicianId : null;
        var primary = devices[0];
        record.DeviceType = primary.DeviceType.Trim(); record.DeviceModel = Optional(primary.DeviceModel); record.SerialNumber = Optional(primary.SerialNumber);
        record.ProblemDescription = Optional(primary.ProblemDescription); record.Accessories = Optional(primary.Accessories);
        record.Note = Optional(dto.Note); record.ReceivedAt = dto.ReceivedAt.Date;
        var existingDevices = record.Devices.OrderBy(d => d.Position).ToList();
        if (existingDevices.Where((d, index) => d.CompanyId != record.CompanyId || d.Position != index + 1).Any())
            throw new InvalidOperationException("ترتیب دستگاه‌های گارانتی معتبر نیست؛ ابتدا سوابق را بررسی کنید.");
        for (var index = 0; index < devices.Count; index++)
        {
            var d = devices[index];
            var target = index < existingDevices.Count ? existingDevices[index] : new ShopWarrantyDevice { Position = index + 1 };
            if (index >= existingDevices.Count) record.Devices.Add(target);
            target.DeviceType = d.DeviceType.Trim(); target.DeviceModel = Optional(d.DeviceModel); target.SerialNumber = Optional(d.SerialNumber);
            target.ProblemDescription = Optional(d.ProblemDescription); target.Accessories = Optional(d.Accessories);
        }
        foreach (var removed in existingDevices.Skip(devices.Count))
        {
            _db.ShopWarrantyDevices.Remove(removed);
            record.Devices.Remove(removed);
        }
        foreach (var i in dto.Items) record.Items.Add(new ShopWarrantyItem
        {
            Description = i.Description.Trim(), ProductId = i.ProductId is > 0 ? i.ProductId : null,
            Quantity = i.Quantity, UnitCost = i.UnitCost, Decision = i.Decision, Supplied = i.Supplied
        });
        await _db.SaveChangesAsync();
        if (string.IsNullOrEmpty(record.Number)) { record.Number = $"WR-{record.Id:0000}"; await _db.SaveChangesAsync(); }
        await transaction.CommitAsync();
        return await GetAsync(record.Id);
    }

    public async Task<ShopWarrantyDto> FinalizeAsync(int id, string? user)
    {
        if (user?.Length > 120) throw new InvalidOperationException("نام کاربر معتبر نیست.");
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var record = await _db.ShopWarranties.Include(w => w.Items).FirstOrDefaultAsync(w => w.Id == id)
            ?? throw new InvalidOperationException("پذیرش گارانتی یافت نشد.");
        if (record.FinalizedAt.HasValue) { await transaction.CommitAsync(); return await GetAsync(id); }
        _ = await GetAsync(id); // fail closed on malformed company, decision or inventory history
        if (record.ReceivedAt.Date > DateTime.Today) throw new InvalidOperationException("تاریخ پذیرش نمی‌تواند بعد از تاریخ تحویل باشد.");
        var now = DateTime.Now;
        var supplied = record.Items.Where(i => i.Supplied).Select(i => new ShopWarrantyItemDto
        {
            ProductId = i.ProductId, Description = i.Description, Quantity = i.Quantity, UnitCost = i.UnitCost,
            Decision = i.Decision, Supplied = true
        }).ToList();
        var stockId = await _inventory.ConsumeWarrantyStockAsync(record.WarehouseId, record.PartyId, record.Number, now, supplied);
        record.StockTransactionId = stockId; record.FinalizedAt = now; record.FinalizedBy = user;
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
        return await GetAsync(id);
    }
}
