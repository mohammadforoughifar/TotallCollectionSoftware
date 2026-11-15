using Db = Inventory.Api.Data;
using System.Data;
using Inventory.Api.Services.Repairs;
using Inventory.Api.Services.ItAssets;
using Microsoft.Extensions.Options;
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
    private readonly IKavenegarSmsGateway _smsGateway;
    private readonly KavenegarSmsOptions _smsOptions;

    public RepairService(Db.AppDbContext db, IInventoryService inventory, IKavenegarSmsGateway smsGateway, IOptions<KavenegarSmsOptions> smsOptions)
    {
        _db = db;
        _inventory = inventory;
        _smsGateway = smsGateway;
        _smsOptions = smsOptions.Value;
    }

    // =============================== تعمیرکارها ===============================

    public async Task<List<Technician>> GetTechniciansAsync(bool activeOnly = false, Paging.Request? pagination = null)
    {
        var q = _db.Technicians.AsNoTracking().AsQueryable();
        if (activeOnly) q = q.Where(t => t.IsActive);
        var list = await q.OrderBy(t => t.Name).ThenBy(x => x.Id).ToPageListAsync(pagination);

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
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
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
                entity = await _db.Technicians.FirstOrDefaultAsync(t => t.Id == dto.Id)
                    ?? throw new InvalidOperationException("تعمیرکار یافت نشد.");
            }

            entity.Name = name;
            entity.Phone = NullIfEmpty(dto.Phone);
            entity.Specialty = NullIfEmpty(dto.Specialty);
            entity.IsActive = dto.IsActive;
            await _db.SaveChangesAsync();

            dto.Id = entity.Id;
            dto.CreatedAt = entity.CreatedAt;
            await transaction.CommitAsync();
            return dto;
        }
        catch
        {
            try { if (_db.Database.CurrentTransaction is not null) await transaction.RollbackAsync(); } catch { }
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task DeleteTechnicianAsync(int id)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var used = await _db.RepairOrders.AnyAsync(r => r.TechnicianId == id) ||
                await _db.ShopWarranties.AnyAsync(w => w.TechnicianId == id);
            if (used) throw new InvalidOperationException("این تعمیرکار دارای سوابق پذیرش است و قابل حذف نیست؛ می‌توانید آن را غیرفعال کنید.");
            var t = await _db.Technicians.FirstOrDefaultAsync(x => x.Id == id);
            if (t is null) { await transaction.CommitAsync(); return; }
            _db.Technicians.Remove(t);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            try { if (_db.Database.CurrentTransaction is not null) await transaction.RollbackAsync(); } catch { }
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    // Narrow, read-only repair lookups: no ERP or general catalog permissions are needed.
    private static Product RepairProductView(Product p) => new()
    {
        Id = p.Id, Name = p.Name, Code = p.Code, Unit = p.Unit, IsService = p.IsService,
        IsActive = p.IsActive, AvgCost = p.AvgCost, SalePrice = p.SalePrice,
        AllowsFractionalQuantity = p.AllowsFractionalQuantity
    };

    public async Task<PagedResult<Product>> RepairProductsAsync(string? search, int page, int pageSize)
    {
        var result = await _inventory.GetProductsAsync(search, false, Math.Max(1, page), Math.Clamp(pageSize, 1, 50));
        result.Items = result.Items.Select(RepairProductView).ToList();
        return result;
    }

    public async Task<Product?> RepairProductAsync(int id)
    {
        // FindAsync can return a tracked entity without applying the tenant filter.
        if (!await _db.Products.AsNoTracking().AnyAsync(p => p.Id == id)) return null;
        var product = await _inventory.GetProductAsync(id);
        return product is null ? null : RepairProductView(product);
    }

    public async Task<List<Warehouse>> RepairWarehousesAsync() => await _db.Warehouses.AsNoTracking()
        .Where(w => w.IsActive).OrderBy(w => w.Name).Select(w => new Warehouse { Id = w.Id, Name = w.Name }).ToListAsync();

    public Task<List<TrsAccount>> RepairAccountsAsync() => _inventory.GetActiveCompanyTreasuryAccountsAsync();

    // =============================== پذیرش تعمیر ===============================

    public async Task<PagedResult<RepairOrderDto>> GetRepairsAsync(string? search, RepairStatus? status, int? technicianId, int page, int pageSize)
    {
        var q = _db.RepairOrders.AsNoTracking()
            .Include(r => r.Items)
            .Include(r => r.Devices)
            .AsSplitQuery()
            .AsQueryable();

        if (status.HasValue) q = q.Where(r => r.Status == status.Value);
        if (technicianId is > 0) q = q.Where(r => r.TechnicianId == technicianId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(r => r.Number.Contains(s) ||
                             r.DeviceType.Contains(s) ||
                             (r.DeviceModel != null && r.DeviceModel.Contains(s)) ||
                             (r.SerialNumber != null && r.SerialNumber.Contains(s)) ||
                             _db.RepairDevices.Any(d => d.RepairOrderId == r.Id &&
                                 (d.DeviceType.Contains(s) ||
                                  (d.DeviceModel != null && d.DeviceModel.Contains(s)) ||
                                  (d.SerialNumber != null && d.SerialNumber.Contains(s)))) ||
                             _db.Parties.MatchingPhone(s).Any(p => p.Id == r.PartyId));
        }

        // شمارش، فیلتر و صفحه‌بندی همگی در SQL انجام می‌شوند؛ هیچ‌وقت کل پذیرش‌ها به حافظهٔ API/کلاینت نمی‌آیند.
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize <= 0 ? 15 : pageSize, 1, 100);
        var total = await q.CountAsync();
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        if (page > pageCount) page = pageCount;

        var items = await q.OrderByDescending(r => r.ReceivedAt).ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        var dtos = new List<RepairOrderDto>();
        foreach (var r in items) dtos.Add(await ToDtoAsync(r));

        return new PagedResult<RepairOrderDto>
        {
            Items = dtos,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<RepairOrderDto?> GetRepairAsync(int id)
    {
        var r = await _db.RepairOrders.AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Devices)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id);
        return r is null ? null : await ToDtoAsync(r);
    }

    public async Task<RepairOrderDto> SaveRepairAsync(RepairOrderDto dto)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            if (dto.PartyId <= 0) throw new InvalidOperationException("مشتری (صاحب دستگاه) را انتخاب کنید.");
            if (dto.Status is not (RepairStatus.Received or RepairStatus.InProgress or RepairStatus.Ready or RepairStatus.Cancelled))
                throw new InvalidOperationException("تحویل فقط از طریق صدور فاکتور یا تحویل گارانتی انجام می‌شود.");

            // APIهای قدیمی هنوز مشخصات تک‌دستگاهی را می‌فرستند؛ آن‌ها را به کارت اول تبدیل می‌کنیم.
            var deviceInputs = dto.Devices is { Count: > 0 }
                ? dto.Devices
                : new List<RepairDeviceDto>
                {
                    new()
                    {
                        DeviceType = dto.DeviceType,
                        DeviceModel = dto.DeviceModel,
                        SerialNumber = dto.SerialNumber,
                        ProblemDescription = dto.ProblemDescription,
                        Accessories = dto.Accessories,
                        QuotedPrice = dto.QuotedPrice
                    }
                };
            if (dto.Items.Any(i => i.Quantity <= 0)) throw new InvalidOperationException("مقدار هر ردیف باید بزرگ‌تر از صفر باشد.");
            if (dto.Items.Any(i => i.WarrantyDecision is not (WarrantyDecision.Unspecified or WarrantyDecision.Accepted or WarrantyDecision.Rejected)))
                throw new InvalidOperationException("تصمیم گارانتی ردیف معتبر نیست.");
            if (dto.Items.Any(i => i.WarrantyDecision != WarrantyDecision.Unspecified &&
                (i.Quantity > ShopInvoiceAmounts.MaximumQuantity || i.Quantity != decimal.Round(i.Quantity, 2) ||
                 i.Cost < 0 || i.Cost > OrderLineAmounts.MaxVatValue || i.Cost != decimal.Round(i.Cost, 2) ||
                 !ShopInvoiceAmounts.IsWholePrice(i.Price) || !ShopInvoiceAmounts.TryGross(i.Quantity, i.Price, out _) ||
                 !ShopInvoiceAmounts.TryGross(i.Quantity, i.Cost, out _))))
                throw new InvalidOperationException("مقدار، هزینه داخلی یا قیمت ریالی ردیف گارانتی معتبر نیست.");
            if (dto.Items.Any(i => string.IsNullOrWhiteSpace(i.Description) && i.ProductId is null or 0))
                throw new InvalidOperationException("برای هر ردیف، شرح کار یا کالای مصرفی را مشخص کنید.");

            var party = await _db.Parties.FirstOrDefaultAsync(p => p.Id == dto.PartyId)
                ?? throw new InvalidOperationException("مشتری یافت نشد.");
            if (dto.Items.Any(i => i.WarrantyDecision != WarrantyDecision.Unspecified) && party.Type != PartyType.Customer)
                throw new InvalidOperationException("پذیرش گارانتی باید برای مشتری ثبت شود.");

            if (dto.TechnicianId is > 0 && !await _db.Technicians.AnyAsync(t => t.Id == dto.TechnicianId && t.CompanyId == party.CompanyId))
                throw new InvalidOperationException("تعمیرکار در شرکت جاری یافت نشد.");

            var isNew = dto.Id == 0;
            Db.RepairOrder entity;
            if (isNew)
            {
                entity = new Db.RepairOrder
                {
                    Number = await GenerateNumberAsync(),
                    CompanyId = party.CompanyId,
                    CreatedAt = DateTime.Now
                };
                _db.RepairOrders.Add(entity);
            }
            else
            {
                entity = await _db.RepairOrders
                    .Include(r => r.Items)
                    .Include(r => r.Devices)
                    .AsSplitQuery()
                    .FirstOrDefaultAsync(r => r.Id == dto.Id)
                    ?? throw new InvalidOperationException("پذیرش یافت نشد.");
                if (entity.InvoiceTransactionId is > 0 || entity.WarrantyProcessed)
                    throw new InvalidOperationException("این پذیرش تحویل یا فاکتور شده و قابل ویرایش نیست.");
                if (entity.CompanyId != party.CompanyId)
                    throw new InvalidOperationException("مشتری و پذیرش باید متعلق به یک شرکت باشند.");

                // کلاینت قدیمی فهرست Devices را نمی‌فرستد؛ اگر پذیرش چند دستگاه دارد،
                // هنگام ویرایش اطلاعات تختِ قدیمی نباید کارت‌های دیگر را حذف کند.
                if (dto.Devices is not { Count: > 0 } && entity.Devices.Count > 1)
                {
                    deviceInputs = entity.Devices.OrderBy(d => d.Id).Select(d => new RepairDeviceDto
                    {
                        Id = d.Id,
                        DeviceType = d.DeviceType,
                        DeviceModel = d.DeviceModel,
                        SerialNumber = d.SerialNumber,
                        ProblemDescription = d.ProblemDescription,
                        Accessories = d.Accessories,
                        QuotedPrice = d.QuotedPrice
                    }).ToList();
                }
            }

            for (var i = 0; i < deviceInputs.Count; i++)
                if (string.IsNullOrWhiteSpace(deviceInputs[i].DeviceType))
                    throw new InvalidOperationException($"نوع دستگاه کارت شمارهٔ {i + 1} را وارد کنید.");

            var previousStatus = entity.Status;
            entity.PartyId = dto.PartyId;
            entity.TechnicianId = dto.TechnicianId is > 0 ? dto.TechnicianId : null;

            // بازنویسی کارت‌های دستگاه؛ فیلدهای قدیمیِ سربرگ با کارت اول/جمع برآورد همگام می‌مانند.
            entity.Devices.Clear();
            foreach (var d in deviceInputs)
            {
                entity.Devices.Add(new Db.RepairDevice
                {
                    DeviceType = d.DeviceType.Trim(),
                    DeviceModel = NullIfEmpty(d.DeviceModel),
                    SerialNumber = NullIfEmpty(d.SerialNumber),
                    ProblemDescription = NullIfEmpty(d.ProblemDescription),
                    Accessories = NullIfEmpty(d.Accessories),
                    QuotedPrice = d.QuotedPrice
                });
            }

            var primaryDevice = deviceInputs[0];
            entity.DeviceType = primaryDevice.DeviceType.Trim();
            entity.DeviceModel = NullIfEmpty(primaryDevice.DeviceModel);
            entity.SerialNumber = NullIfEmpty(primaryDevice.SerialNumber);
            entity.ProblemDescription = NullIfEmpty(primaryDevice.ProblemDescription);
            entity.Accessories = NullIfEmpty(primaryDevice.Accessories);
            entity.QuotedPrice = deviceInputs.Sum(d => d.QuotedPrice);
            entity.Note = NullIfEmpty(dto.Note);
            entity.ReceivedAt = dto.ReceivedAt == default ? DateTime.Now : dto.ReceivedAt;
            if (dto.Id != 0) entity.Status = dto.Status;

            // بازنویسی ردیف‌های کار/قطعه
            entity.Items.Clear();
            foreach (var i in dto.Items)
            {
                string? productName = null;
                var cost = i.Cost;
                if (i.ProductId is > 0)
                {
                    var p = await _db.Products.FirstOrDefaultAsync(p => p.Id == i.ProductId && p.CompanyId == party.CompanyId)
                        ?? throw new InvalidOperationException("کالای مصرفی در شرکت جاری یافت نشد.");
                    productName = p.Name;
                    // اگر هزینهٔ قطعه خالی مانده باشد، بهای خرید کالا مبنا می‌شود تا
                    // گزارش «سود تعمیرات» هزینهٔ صفر و سودِ کاذب نشان ندهد.
                    if (i.WarrantyDecision == WarrantyDecision.Unspecified && cost <= 0 && p.PurchasePrice > 0)
                        cost = p.PurchasePrice; // preserve the legacy ordinary-repair default only
                }

                entity.Items.Add(new Db.RepairItem
                {
                    Description = string.IsNullOrWhiteSpace(i.Description) ? (productName ?? "") : i.Description.Trim(),
                    ProductId = i.ProductId is > 0 ? i.ProductId : null,
                    Quantity = i.Quantity,
                    Cost = cost,
                    Price = i.Price,
                    WarrantyDecision = i.WarrantyDecision,
                    WarrantySupplied = i.WarrantySupplied,
                    WarrantyPerformed = i.WarrantyPerformed,
                    CreatedAt = DateTime.Now
                });
            }

            await _db.SaveChangesAsync();
            var smsEvent = RepairSmsPolicy.EventFor(isNew, previousStatus, entity.Status);
            if (smsEvent.HasValue)
            {
                await RepairSmsQueue.StageAsync(_db, _smsGateway, _smsOptions, entity, party, smsEvent.Value);
                await _db.SaveChangesAsync();
            }
            var result = (await GetRepairAsync(entity.Id))!;
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            try { if (_db.Database.CurrentTransaction is not null) await transaction.RollbackAsync(); } catch { }
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task DeleteRepairAsync(int id)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var r = await _db.RepairOrders.FirstOrDefaultAsync(x => x.Id == id);
            if (r is not null)
            {
                if (r.InvoiceTransactionId is > 0 || r.WarrantyProcessed)
                    throw new InvalidOperationException("پذیرش تحویل یا فاکتور شده قابل حذف نیست.");
                _db.RepairOrders.Remove(r);
                await _db.SaveChangesAsync();
            }
            await transaction.CommitAsync();
        }
        catch
        {
            try { if (_db.Database.CurrentTransaction is not null) await transaction.RollbackAsync(); } catch { }
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<RepairOrderDto> SetStatusAsync(int id, RepairStatus status)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var r = await _db.RepairOrders.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new InvalidOperationException("پذیرش یافت نشد.");
            if (r.InvoiceTransactionId is > 0 || r.WarrantyProcessed)
                throw new InvalidOperationException("این پذیرش تحویل یا فاکتور شده و وضعیت آن قابل تغییر نیست.");
            if (status is not (RepairStatus.Received or RepairStatus.InProgress or RepairStatus.Ready or RepairStatus.Cancelled))
                throw new InvalidOperationException("تحویل فقط از طریق صدور فاکتور یا تحویل گارانتی انجام می‌شود.");
            var previousStatus = r.Status;
            r.Status = status;
            if (status == RepairStatus.Cancelled) r.DeliveredAt = DateTime.Now;
            await _db.SaveChangesAsync();
            var smsEvent = RepairSmsPolicy.EventFor(false, previousStatus, r.Status);
            if (smsEvent.HasValue)
            {
                var party = await _db.Parties.FirstOrDefaultAsync(p => p.Id == r.PartyId && p.CompanyId == r.CompanyId);
                await RepairSmsQueue.StageAsync(_db, _smsGateway, _smsOptions, r, party, smsEvent.Value);
                await _db.SaveChangesAsync();
            }
            var result = (await GetRepairAsync(id))!;
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            try { if (_db.Database.CurrentTransaction is not null) await transaction.RollbackAsync(); } catch { }
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<RepairOrderDto> InvoiceAsync(int id, RepairInvoiceRequest request, string? user = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var r = await _db.RepairOrders.Include(x => x.Items).Include(x => x.Devices).AsSplitQuery()
                .FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new InvalidOperationException("پذیرش یافت نشد.");
            // Returning the existing result makes a repeated delivery request safe.
            if (r.InvoiceTransactionId is > 0 || r.WarrantyProcessed)
            {
                if (r.Status != RepairStatus.Delivered) throw new InvalidOperationException("وضعیت پذیرش تحویل‌شده معتبر نیست.");
                var previous = await ToDtoAsync(r);
                await transaction.CommitAsync();
                return previous;
            }
            if (r.Status == RepairStatus.Cancelled)
                throw new InvalidOperationException("پذیرش انصرافی قابل فاکتور شدن نیست.");
            if (r.Items.Count == 0) throw new InvalidOperationException("حداقل یک ردیف کار/قطعه ثبت کنید.");
            if (r.Items.Any(i => i.WarrantyDecision is not (WarrantyDecision.Unspecified or WarrantyDecision.Accepted or WarrantyDecision.Rejected)))
                throw new InvalidOperationException("تصمیم گارانتی ردیف معتبر نیست.");

            var hasWarranty = r.Items.Any(i => i.WarrantyDecision != WarrantyDecision.Unspecified);
            if (hasWarranty && r.ReceivedAt.Date > DateTime.Today)
                throw new InvalidOperationException("تاریخ پذیرش نمی‌تواند بعد از تاریخ تحویل باشد.");
            var productIds = r.Items.Where(i => i.ProductId is > 0).Select(i => i.ProductId!.Value).Distinct().ToList();
            var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
            if (products.Count != productIds.Count || products.Values.Any(p => p.CompanyId != r.CompanyId))
                throw new InvalidOperationException("کالای پذیرش متعلق به شرکت جاری نیست یا یافت نشد.");
            bool IsPart(Db.RepairItem i) => i.ProductId is > 0 && !products[i.ProductId.Value].IsService;
            bool Done(Db.RepairItem i) => i.WarrantyDecision == WarrantyDecision.Unspecified ||
                (IsPart(i) ? i.WarrantySupplied : i.WarrantyPerformed);
            bool Charge(Db.RepairItem i) => Done(i) && i.WarrantyDecision != WarrantyDecision.Accepted && i.Price > 0;
            if (hasWarranty && r.Items.Any(i => i.Quantity <= 0 || i.Quantity > ShopInvoiceAmounts.MaximumQuantity ||
                    i.Quantity != decimal.Round(i.Quantity, 2) || i.Cost < 0 || i.Cost > OrderLineAmounts.MaxVatValue ||
                    i.Cost != decimal.Round(i.Cost, 2) || !ShopInvoiceAmounts.TryGross(i.Quantity, i.Cost, out _) ||
                    !ShopInvoiceAmounts.IsWholePrice(i.Price) || !ShopInvoiceAmounts.TryGross(i.Quantity, i.Price, out _)))
                throw new InvalidOperationException("مقدار، هزینه یا قیمت ریالی ردیف تعمیرات معتبر نیست.");
            if (r.Items.Any(i => i.WarrantyDecision == WarrantyDecision.Rejected && Done(i) &&
                    (i.Price <= 0 || !ShopInvoiceAmounts.TryGross(i.Quantity, i.Price, out var gross) || gross <= 0)))
                throw new InvalidOperationException("برای کار انجام‌شده یا قطعه مصرف‌شده ردشده، قیمت مشتری بزرگ‌تر از صفر وارد کنید.");
            var chargeable = r.Items.OrderBy(i => i.Id).Where(Charge).ToList();
            if (!hasWarranty && chargeable.Count == 0)
                throw new InvalidOperationException("مبلغ دریافتی ردیف‌ها صفر است؛ ابتدا مبالغ را ثبت کنید.");
            var warrantyTotal = hasWarranty ? chargeable.Sum(i => ShopInvoiceAmounts.Gross(i.Quantity, i.Price)) : 0m;
            if (hasWarranty && warrantyTotal > OrderLineAmounts.MaxVatValue)
                throw new InvalidOperationException("جمع مبلغ قابل دریافت از محدوده مجاز بیشتر است.");
            if (hasWarranty && (request.PaymentMethod is not (PaymentMethod.Cash or PaymentMethod.Credit) ||
                !ShopInvoiceAmounts.IsWholePrice(request.CashAmount) ||
                (request.PaymentMethod == PaymentMethod.Cash && request.CashAmount != 0m) ||
                (request.PaymentMethod == PaymentMethod.Credit && warrantyTotal > 0m && request.CashAmount >= warrantyTotal) ||
                (warrantyTotal == 0m && request.CashAmount != 0m)))
                throw new InvalidOperationException("روش پرداخت یا مبلغ پیش‌دریافت گارانتی معتبر نیست و باید به ریال صحیح باشد.");

            var warehouseId = request.WarehouseId > 0 ? request.WarehouseId : 1;
            if (hasWarranty && (!await _db.Warehouses.AnyAsync(w => w.Id == warehouseId && w.CompanyId == r.CompanyId && w.IsActive) ||
                !await _db.Parties.AnyAsync(p => p.Id == r.PartyId && p.CompanyId == r.CompanyId && p.Type == PartyType.Customer) ||
                r.Items.Any(i => i.CompanyId != r.CompanyId) || r.Devices.Any(d => d.CompanyId != r.CompanyId)))
                throw new InvalidOperationException("مشتری، انبار یا ردیف‌های پذیرش متعلق به شرکت جاری نیستند.");
            var now = DateTime.Now;
            int? stockId = null;
            if (hasWarranty)
            {
                var acceptedParts = r.Items.Where(i => i.WarrantyDecision == WarrantyDecision.Accepted && IsPart(i) && i.WarrantySupplied)
                    .Select(i => new ShopWarrantyItemDto { ProductId = i.ProductId, Description = i.Description, Quantity = i.Quantity,
                        UnitCost = i.Cost, Decision = WarrantyDecision.Accepted, Supplied = true }).ToList();
                stockId = await _inventory.ConsumeWarrantyStockAsync(warehouseId, r.PartyId, r.Number, now, acceptedParts);
            }

            int? invoiceId = null;
            if (chargeable.Count > 0)
            {
                int? laborProductId = null;
                if (chargeable.Any(i => i.ProductId is null or 0))
                    laborProductId = await RepairLaborProduct.GetOrCreateAsync(_db, r.CompanyId);
                var payMethod = request.PaymentMethod;
                var cmd = new OrderCommand
                {
                    WarehouseId = warehouseId, PartyId = r.PartyId, Type = TransactionType.Sale, Date = now,
                    Description = $"فاکتور تعمیرات — پذیرش {r.Number} ({DeviceSummary(r.Devices, r.DeviceType, r.DeviceModel)})",
                    PaymentMethod = payMethod, IsWarrantyInvoice = hasWarranty,
                    CashType = payMethod == PaymentMethod.Cash ? request.CashType ?? CashType.CardReader : (request.CashAmount > 0 ? request.CashType ?? CashType.CardReader : null),
                    TrsAccountId = request.TrsAccountId > 0 ? request.TrsAccountId : null,
                    DueDate = payMethod == PaymentMethod.Credit ? request.DueDate ?? now.AddDays(30) : null,
                    CashAmount = payMethod == PaymentMethod.Credit ? Math.Max(0, request.CashAmount) : 0,
                    Lines = chargeable.Select(i => new OrderLineInput { ProductId = i.ProductId ?? laborProductId!.Value,
                        Quantity = i.Quantity, Price = i.Price, UnitCostSnapshot = i.Cost,
                        SourceDescription = hasWarranty ? i.Description : null }).ToList()
                };
                // Historical ordinary repairs retain their existing payment path. New
                // warranty/mixed repairs use the shop cash and credit ledger for real payments.
                invoiceId = (await _inventory.CreateOrderAsync(cmd, user, trackShopCash: hasWarranty)).Id;
            }
            r.InvoiceTransactionId = invoiceId;
            r.WarrantyStockTransactionId = stockId;
            r.WarrantyProcessed = hasWarranty;
            r.Status = RepairStatus.Delivered;
            r.DeliveredAt = now;
            await _db.SaveChangesAsync();
            var result = await ToDtoAsync(r);
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            try { if (_db.Database.CurrentTransaction is not null) await transaction.RollbackAsync(); } catch { }
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<int> WarrantyInvoiceIdAsync(int id)
    {
        var repair = await GetRepairAsync(id) ?? throw new InvalidOperationException("پذیرش یافت نشد.");
        if (!repair.WarrantyProcessed || repair.Status != RepairStatus.Delivered || repair.InvoiceTransactionId is not > 0)
            throw new InvalidOperationException("فاکتور گارانتی تحویل‌شده برای این پذیرش یافت نشد.");
        return repair.InvoiceTransactionId.Value;
    }

    public async Task<ShopOrderCreditState> WarrantyCreditStateAsync(int id) =>
        await _inventory.GetOrderCreditStateAsync(await WarrantyInvoiceIdAsync(id));

    public async Task ReceiveWarrantyCreditAsync(int id, ShopOrderCreditReceiptCommand cmd, string? user) =>
        await _inventory.ReceiveOrderCreditAsync(await WarrantyInvoiceIdAsync(id), cmd, user);

    public async Task ReverseWarrantyCreditAsync(int id, int receiptId, ShopOrderCreditReversalCommand cmd, string? user) =>
        await _inventory.ReverseOrderCreditAsync(await WarrantyInvoiceIdAsync(id), receiptId, cmd, user);

    public async Task<Order> LinkedInvoiceAsync(int id)
    {
        var repair = await GetRepairAsync(id) ?? throw new InvalidOperationException("پذیرش یافت نشد.");
        if (repair.InvoiceTransactionId is not > 0) throw new InvalidOperationException("فاکتور پذیرش یافت نشد.");
        var order = await _inventory.GetOrderAsync(repair.InvoiceTransactionId.Value)
            ?? throw new InvalidOperationException("فاکتور پذیرش یافت نشد.");
        // This repair-scoped endpoint serves customer invoice printing, not the
        // independently permissioned Orders.ViewProfit/commission data.
        order.TotalProfit = null; order.NetProfit = null; order.CommissionAmount = null;
        order.ReferrerGoodsCommissionPercent = null; order.ReferrerServiceCommissionPercent = null;
        order.CommissionRatesAreHistorical = false; order.UsesLegacyCommissionPolicy = false;
        order.HasEstimatedSaleCost = false;
        foreach (var line in order.Lines)
        {
            line.UnitCost = null; line.Profit = null; line.Commission = null;
        }
        return order;
    }

    // =============================== کمکی ===============================

    private async Task<RepairOrderDto> ToDtoAsync(Db.RepairOrder r)
    {
        var party = await _db.Parties.AsNoTracking()
            .Where(p => p.Id == r.PartyId)
            .Select(p => new { p.Name, p.Phone, p.Mobile })
            .FirstOrDefaultAsync();
        var tech = r.TechnicianId.HasValue
            ? await _db.Technicians.AsNoTracking()
                .Where(t => t.Id == r.TechnicianId.Value)
                .Select(t => new { t.Name, t.Phone })
                .FirstOrDefaultAsync()
            : null;
        var invoiceNumber = r.InvoiceTransactionId.HasValue
            ? await _db.Transactions.Where(t => t.Id == r.InvoiceTransactionId.Value).Select(t => t.Number).FirstOrDefaultAsync()
            : null;

        var productIds = r.Items.Where(i => i.ProductId.HasValue).Select(i => i.ProductId!.Value).Distinct().ToList();
        var productInfo = await _db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.IsService, p.CompanyId }).ToDictionaryAsync(p => p.Id);
        if (r.WarrantyProcessed && (productInfo.Count != productIds.Count || productInfo.Values.Any(p => p.CompanyId != r.CompanyId)))
            throw new InvalidOperationException("کالای پذیرش گارانتی متعلق به شرکت جاری نیست.");

        var items = r.Items.OrderBy(i => i.Id).Select(i => new RepairItemDto
        {
            Id = i.Id,
            Description = i.Description,
            ProductId = i.ProductId,
            ProductName = i.ProductId.HasValue && productInfo.TryGetValue(i.ProductId.Value, out var pn) ? pn.Name : null,
            ProductIsService = i.ProductId.HasValue && productInfo.TryGetValue(i.ProductId.Value, out var pi) && pi.IsService,
            Quantity = i.Quantity,
            Cost = i.Cost,
            Price = i.Price,
            WarrantyDecision = i.WarrantyDecision,
            WarrantySupplied = i.WarrantySupplied,
            WarrantyPerformed = i.WarrantyPerformed
        }).ToList();

        // جمع‌ها در حافظه (SQLite جمع decimal در SQL را پشتیبانی نمی‌کند)
        var warrantyRows = items.Any(i => i.WarrantyDecision != WarrantyDecision.Unspecified);
        var totalPrice = items.Sum(i => warrantyRows ? ShopInvoiceAmounts.Gross(i.Quantity, i.CustomerUnitPrice) : i.Price * i.Quantity);
        var totalCost = items.Sum(i => warrantyRows ? ShopInvoiceAmounts.Gross(i.Quantity, i.EffectiveUnitCost) : i.Cost * i.Quantity);
        if (r.WarrantyProcessed) await ValidateWarrantyLinksAsync(r, items, totalPrice);

        var devices = (r.Devices ?? new List<Db.RepairDevice>())
            .OrderBy(d => d.Id)
            .Select(d => new RepairDeviceDto
            {
                Id = d.Id,
                DeviceType = d.DeviceType,
                DeviceModel = d.DeviceModel,
                SerialNumber = d.SerialNumber,
                ProblemDescription = d.ProblemDescription,
                Accessories = d.Accessories,
                QuotedPrice = d.QuotedPrice
            })
            .ToList();
        if (devices.Count == 0)
        {
            // پشتیبان برای رکوردی که پیش از اجرای خودتعمیرِ schema خوانده شود.
            devices.Add(new RepairDeviceDto
            {
                DeviceType = r.DeviceType,
                DeviceModel = r.DeviceModel,
                SerialNumber = r.SerialNumber,
                ProblemDescription = r.ProblemDescription,
                Accessories = r.Accessories,
                QuotedPrice = r.QuotedPrice
            });
        }
        var primaryDevice = devices[0];
        var smsNotifications = await _db.RepairSmsNotifications.AsNoTracking()
            .Where(n => n.RepairOrderId == r.Id && n.CompanyId == r.CompanyId)
            .OrderBy(n => n.Id).Select(n => new RepairSmsNotificationDto
            {
                EventType = n.EventType, Status = n.Status, ProviderMessageId = n.ProviderMessageId,
                ProviderCode = n.ProviderCode, CreatedAtUtc = n.CreatedAtUtc, CompletedAtUtc = n.CompletedAtUtc
            }).ToListAsync();

        return new RepairOrderDto
        {
            Id = r.Id,
            Number = r.Number,
            PartyId = r.PartyId,
            PartyName = party?.Name,
            PartyPhone = party?.Phone,
            PartyMobile = party?.Mobile,
            TechnicianId = r.TechnicianId,
            TechnicianName = tech?.Name,
            TechnicianPhone = tech?.Phone,
            // فیلدهای تخت برای سازگاری APIهای قبلی از کارت اول خوانده می‌شوند.
            DeviceType = primaryDevice.DeviceType,
            DeviceModel = primaryDevice.DeviceModel,
            SerialNumber = primaryDevice.SerialNumber,
            ProblemDescription = primaryDevice.ProblemDescription,
            Accessories = primaryDevice.Accessories,
            Status = r.Status,
            ReceivedAt = r.ReceivedAt,
            DeliveredAt = r.DeliveredAt,
            QuotedPrice = devices.Sum(d => d.QuotedPrice),
            InvoiceTransactionId = r.InvoiceTransactionId,
            WarrantyProcessed = r.WarrantyProcessed,
            WarrantyStockTransactionId = r.WarrantyStockTransactionId,
            InvoiceNumber = invoiceNumber,
            Note = r.Note,
            CreatedAt = r.CreatedAt,
            Devices = devices,
            Items = items,
            SmsNotifications = smsNotifications,
            TotalPrice = totalPrice,
            TotalCost = totalCost,
            Profit = totalPrice - totalCost
        };
    }

    private async Task ValidateWarrantyLinksAsync(Db.RepairOrder repair, List<RepairItemDto> items, decimal totalPrice)
    {
        if (repair.Status != RepairStatus.Delivered || !repair.DeliveredAt.HasValue ||
            items.All(i => i.WarrantyDecision == WarrantyDecision.Unspecified) ||
            items.Any(i => i.WarrantyDecision is not (WarrantyDecision.Unspecified or WarrantyDecision.Accepted or WarrantyDecision.Rejected)) ||
            repair.Items.Any(i => i.CompanyId != repair.CompanyId))
            throw new InvalidOperationException("وضعیت یا ردیف‌های پذیرش گارانتی معتبر نیست.");
        var acceptedParts = items.Where(i => i.WarrantyDecision == WarrantyDecision.Accepted &&
            i.ProductId is > 0 && !i.ProductIsService && i.WarrantySupplied).ToList();
        var movement = repair.WarrantyStockTransactionId.HasValue
            ? await _db.Transactions.AsNoTracking().Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == repair.WarrantyStockTransactionId)
            : null;
        if (acceptedParts.Count == 0 && repair.WarrantyStockTransactionId.HasValue || acceptedParts.Count > 0 &&
            (movement is null || movement.CompanyId != repair.CompanyId || movement.PartyId != repair.PartyId ||
             movement.Type != TransactionType.WarrantyOut || movement.Amount != 0 || movement.CashAmount != 0 ||
             movement.SettledAmount != 0 || movement.ShopCashLedgerEnabled || movement.Lines.Count != acceptedParts.Count ||
             acceptedParts.GroupBy(i => (i.ProductId, i.Quantity, i.Cost, i.Description)).Any(g =>
                movement.Lines.Count(l => l.CompanyId == repair.CompanyId && l.ProductId == g.Key.ProductId &&
                    l.Quantity == g.Key.Quantity && l.UnitCostSnapshot == g.Key.Cost && l.Description == g.Key.Description && l.Price == 0) != g.Count())))
            throw new InvalidOperationException("خروج قطعات پذیرفته‌شده با پذیرش تعمیرات مطابقت ندارد.");

        var charged = items.Where(i => i.CustomerUnitPrice > 0).ToList();
        if (items.Any(i => i.WarrantyDecision == WarrantyDecision.Rejected && !i.IsUnusedWarrantyPart &&
                !i.IsUnperformedWarrantyWork && (i.Price <= 0 || !ShopInvoiceAmounts.IsWholePrice(i.Price) ||
                !ShopInvoiceAmounts.TryGross(i.Quantity, i.Price, out var gross) || gross <= 0)))
            throw new InvalidOperationException("قیمت ردیف ردشده پذیرش معتبر نیست.");
        var sale = repair.InvoiceTransactionId.HasValue
            ? await _db.Transactions.AsNoTracking().Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == repair.InvoiceTransactionId)
            : null;
        if (charged.Count == 0 && repair.InvoiceTransactionId.HasValue)
            throw new InvalidOperationException("پذیرش بدون مبلغ نباید فاکتور داشته باشد.");
        if (charged.Count > 0)
        {
            if (sale is null || sale.CompanyId != repair.CompanyId || sale.PartyId != repair.PartyId ||
                sale.Type != TransactionType.Sale || !sale.ShopCashLedgerEnabled ||
                sale.Date.Date != repair.DeliveredAt.Value.Date || sale.Amount != totalPrice ||
                sale.SettledAmount < 0 || sale.SettledAmount > sale.Amount || sale.Lines.Count != charged.Count ||
                (movement is not null && movement.WarehouseId != sale.WarehouseId))
                throw new InvalidOperationException("فاکتور با پذیرش گارانتی تعمیرات مطابقت ندارد.");
            var serviceIds = (await _db.Products.AsNoTracking().Where(p => p.CompanyId == repair.CompanyId && p.IsService)
                .Select(p => p.Id).ToListAsync()).ToHashSet();
            var unmatched = sale.Lines.ToList();
            foreach (var item in charged)
            {
                var index = unmatched.FindIndex(l => l.CompanyId == repair.CompanyId &&
                    (item.ProductId is > 0 ? l.ProductId == item.ProductId : serviceIds.Contains(l.ProductId)) &&
                    l.Quantity == item.Quantity && l.Price == item.Price && l.UnitCostSnapshot == item.Cost &&
                    l.Description == item.Description && l.GrossAmount == ShopInvoiceAmounts.Gross(item.Quantity, item.Price) &&
                    l.DiscountAmount == 0 && l.VatAmount == 0);
                if (index < 0) throw new InvalidOperationException("ردیف فاکتور با پذیرش تعمیرات مطابقت ندارد.");
                unmatched.RemoveAt(index);
            }
        }
    }

    private async Task<string> GenerateNumberAsync()
    {
        var count = await _db.RepairOrders.CountAsync();
        return $"RP-{count + 1:0000}";
    }

    private static string DeviceSummary(IEnumerable<Db.RepairDevice> devices, string fallbackType, string? fallbackModel)
    {
        var labels = devices.OrderBy(d => d.Id)
            .Select(d => DeviceLabel(d.DeviceType, d.DeviceModel))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        if (labels.Count == 0)
            labels.Add(DeviceLabel(fallbackType, fallbackModel));
        if (labels.Count == 0) return "دستگاه";
        return labels.Count == 1
            ? labels[0]
            : $"{labels[0]} و {labels.Count - 1} دستگاه دیگر";
    }

    private static string DeviceLabel(string? deviceType, string? deviceModel)
    {
        var type = (deviceType ?? "").Trim();
        var model = (deviceModel ?? "").Trim();
        return string.IsNullOrWhiteSpace(model) ? type : $"{type} {model}".Trim();
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
