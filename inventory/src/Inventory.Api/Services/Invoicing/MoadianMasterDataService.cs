using System.Globalization;
using ClosedXML.Excel;
using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Invoicing;

/// <summary>
/// CRUD for the CPInfo/ConnectInfo-style Moadian records and provider-scoped catalogs.
/// This service deliberately does not participate in invoice intake, queueing, or sending.
/// </summary>
public interface IMoadianMasterDataService
{
    Task<List<MoadianServiceProviderProfileDto>> GetProvidersAsync();
    Task<MoadianServiceProviderProfileDto> CreateProviderAsync(MoadianServiceProviderProfileDto dto);
    Task<MoadianServiceProviderProfileDto> UpdateProviderAsync(int id, MoadianServiceProviderProfileDto dto);
    Task SetProviderDeletedAsync(int id, bool deleted);

    Task<List<MoadianProviderConnectionProfileDto>> GetConnectionsAsync(int? serviceProviderId = null);
    Task<MoadianProviderConnectionProfileDto> CreateConnectionAsync(MoadianProviderConnectionProfileDto dto);
    Task SetConnectionDeletedAsync(int id, bool deleted);

    /// <summary>حذف قطعی اتصال (سطر پایگاه‌داده). فایل کلید خصوصی توسط کنترلر و سرویس کلید پاک می‌شود.</summary>
    Task DeleteConnectionAsync(int id);

    Task<List<MoadianCustomerProfileDto>> GetCustomersAsync(int? serviceProviderId = null);
    Task<MoadianCustomerProfileDto> CreateCustomerAsync(MoadianCustomerProfileDto dto);
    Task<MoadianCustomerProfileDto> UpdateCustomerAsync(int id, MoadianCustomerProfileDto dto);
    Task SetCustomerDeletedAsync(int id, bool deleted);

    Task<List<MoadianGoodsOrServiceProfileDto>> GetGoodsOrServicesAsync(int? serviceProviderId = null);
    Task<MoadianGoodsOrServiceProfileDto> CreateGoodsOrServiceAsync(MoadianGoodsOrServiceProfileDto dto);
    Task<MoadianGoodsOrServiceProfileDto> UpdateGoodsOrServiceAsync(int id, MoadianGoodsOrServiceProfileDto dto);
    Task SetGoodsOrServiceDeletedAsync(int id, bool deleted);

    /// <summary>ورود گروهی «کالا و خدمت» از فایل اکسل (xlsx). اتصال به مشتری/خدمات‌دهندهٔ نامعتبر، خطای همان سطر است.</summary>
    Task<ExcelImportResult> ImportGoodsOrServicesAsync(Stream excelStream);

    /// <summary>ساخت فایل اکسل نمونهٔ «کالا و خدمت» همراه کاربرگ راهنما و فهرست واحدهای موجود.</summary>
    Task<byte[]> BuildGoodsOrServicesTemplateAsync();

    Task<List<MoadianUnitOfMeasurementDto>> GetUnitsAsync();
    Task<MoadianUnitOfMeasurementDto> CreateUnitAsync(MoadianUnitOfMeasurementDto dto);
    Task<MoadianUnitOfMeasurementDto> UpdateUnitAsync(int id, MoadianUnitOfMeasurementDto dto);
    Task SetUnitDeletedAsync(int id, bool deleted);
}

public sealed class MoadianMasterDataService : IMoadianMasterDataService
{
    private readonly AppDbContext _db;

    public MoadianMasterDataService(AppDbContext db) => _db = db;

    // CPInfo / service-provider profile
    public async Task<List<MoadianServiceProviderProfileDto>> GetProvidersAsync()
        => await _db.MoadianServiceProviderProfiles.AsNoTracking()
            .OrderBy(x => x.PersianName).ThenBy(x => x.EnglishName).ThenBy(x => x.NationalID)
            .Select(x => new MoadianServiceProviderProfileDto
            {
                Id = x.Id,
                Type = x.Type,
                EconomicNumber = x.EconomicNumber,
                PersianName = x.PersianName,
                EnglishName = x.EnglishName,
                NationalId = x.NationalID,
                PostalCode = x.PostalCode,
                IsDeleted = x.IsDeleted
            }).ToListAsync();

    public async Task<MoadianServiceProviderProfileDto> CreateProviderAsync(MoadianServiceProviderProfileDto dto)
    {
        ValidateProvider(dto);
        var provider = new MoadianServiceProviderProfile();
        ApplyProvider(provider, dto);
        _db.MoadianServiceProviderProfiles.Add(provider);
        await _db.SaveChangesAsync();
        return ToProviderDto(provider);
    }

    public async Task<MoadianServiceProviderProfileDto> UpdateProviderAsync(int id, MoadianServiceProviderProfileDto dto)
    {
        ValidateProvider(dto);
        var provider = await _db.MoadianServiceProviderProfiles.FindAsync(id)
            ?? throw new InvalidOperationException("خدمات‌دهنده یافت نشد.");
        ApplyProvider(provider, dto);
        await _db.SaveChangesAsync();
        return ToProviderDto(provider);
    }

    public async Task SetProviderDeletedAsync(int id, bool deleted)
    {
        var provider = await _db.MoadianServiceProviderProfiles.FindAsync(id)
            ?? throw new InvalidOperationException("خدمات‌دهنده یافت نشد.");
        provider.IsDeleted = deleted;

        // Deleting a provider deactivates its currently active connection versions;
        // restoring the provider does not silently reactivate a historical connection.
        if (deleted)
        {
            var activeConnections = await _db.MoadianProviderConnections
                .Where(x => x.ServiceProviderId == id && !x.IsDeleted)
                .ToListAsync();
            foreach (var connection in activeConnections) connection.IsDeleted = true;
        }
        await _db.SaveChangesAsync();
    }

    // ConnectInfo / provider connection profile
    public async Task<List<MoadianProviderConnectionProfileDto>> GetConnectionsAsync(int? serviceProviderId = null)
    {
        var query = from connection in _db.MoadianProviderConnections.AsNoTracking()
                    join provider in _db.MoadianServiceProviderProfiles.AsNoTracking()
                        on connection.ServiceProviderId equals provider.Id
                    select new { connection, provider };
        if (serviceProviderId.HasValue)
            query = query.Where(x => x.connection.ServiceProviderId == serviceProviderId.Value);

        return await query.OrderBy(x => x.provider.PersianName)
            .ThenByDescending(x => x.connection.Id)
            .Select(x => new MoadianProviderConnectionProfileDto
            {
                Id = x.connection.Id,
                ServiceProviderId = x.connection.ServiceProviderId,
                ProviderName = x.provider.PersianName ?? x.provider.EnglishName ?? x.provider.NationalID,
                WebServiceAddress = x.connection.WebServiceAddress,
                TaxMemoryId = x.connection.TaxMemoryID,
                HasPrivateKey = !string.IsNullOrWhiteSpace(x.connection.PrivateKeyPath),
                IsDeleted = x.connection.IsDeleted,
                LastTestAt = x.connection.LastConnectionTestAt,
                LastTestSucceeded = x.connection.LastConnectionTestSucceeded,
                LastTestMessage = x.connection.LastConnectionTestMessage
            }).ToListAsync();
    }

    public async Task<MoadianProviderConnectionProfileDto> CreateConnectionAsync(MoadianProviderConnectionProfileDto dto)
    {
        await ValidateConnectionAsync(dto);

        // Create an inactive draft first. The client uploads the required private key,
        // then activates this version; only activation deactivates the previous version.
        var connection = new MoadianProviderConnectionProfile
        {
            ServiceProviderId = dto.ServiceProviderId,
            IsDeleted = true
        };
        ApplyConnection(connection, dto);
        _db.MoadianProviderConnections.Add(connection);
        await _db.SaveChangesAsync();
        return await LoadConnectionDtoAsync(connection.Id);
    }

    public async Task SetConnectionDeletedAsync(int id, bool deleted)
    {
        var connection = await _db.MoadianProviderConnections.FindAsync(id)
            ?? throw new InvalidOperationException("اتصال خدمات‌دهنده یافت نشد.");
        if (!deleted)
        {
            if (!await _db.MoadianServiceProviderProfiles
                    .AnyAsync(x => x.Id == connection.ServiceProviderId && !x.IsDeleted))
                throw new InvalidOperationException("ابتدا خدمات‌دهنده را بازیابی کنید.");
            if (string.IsNullOrWhiteSpace(connection.PrivateKeyPath))
                throw new InvalidOperationException("ابتدا فایل کلید خصوصی .key را بارگذاری کنید.");

            var previousActiveConnections = await _db.MoadianProviderConnections
                .Where(x => x.ServiceProviderId == connection.ServiceProviderId
                    && x.Id != connection.Id && !x.IsDeleted)
                .ToListAsync();
            if (previousActiveConnections.Count > 0)
            {
                // Save deactivation first inside a transaction so the filtered unique
                // active-provider index never sees two active rows, while preserving atomicity.
                await using var transaction = await _db.Database.BeginTransactionAsync();
                foreach (var previous in previousActiveConnections) previous.IsDeleted = true;
                await _db.SaveChangesAsync();
                connection.IsDeleted = false;
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
                return;
            }
        }

        connection.IsDeleted = deleted;
        await _db.SaveChangesAsync();
    }

    /// <summary>حذف قطعی اتصال از پایگاه‌داده (کلید خصوصی پیش از این توسط کنترلر پاک شده است).</summary>
    public async Task DeleteConnectionAsync(int id)
    {
        var connection = await _db.MoadianProviderConnections.FindAsync(id)
            ?? throw new InvalidOperationException("اتصال خدمات‌دهنده یافت نشد.");
        _db.MoadianProviderConnections.Remove(connection);
        await _db.SaveChangesAsync();
    }

    // Customers / buyers
    public async Task<List<MoadianCustomerProfileDto>> GetCustomersAsync(int? serviceProviderId = null)
    {
        var query = from customer in _db.MoadianCustomerProfiles.AsNoTracking()
                    join provider in _db.MoadianServiceProviderProfiles.AsNoTracking()
                        on customer.ServiceProviderId equals provider.Id
                    select new { customer, provider };
        if (serviceProviderId.HasValue)
            query = query.Where(x => x.customer.ServiceProviderId == serviceProviderId.Value);

        return await query.OrderBy(x => x.customer.Name)
            .Select(x => new MoadianCustomerProfileDto
            {
                Id = x.customer.Id,
                ServiceProviderId = x.customer.ServiceProviderId,
                ProviderName = x.provider.PersianName ?? x.provider.EnglishName ?? x.provider.NationalID,
                Type = x.customer.Type,
                Name = x.customer.Name,
                NationalId = x.customer.NationalID,
                EconomicNumber = x.customer.EconomicNumber,
                Phone = x.customer.Phone,
                PostalCode = x.customer.PostalCode,
                Address = x.customer.Address,
                IsDeleted = x.customer.IsDeleted
            }).ToListAsync();
    }

    public async Task<MoadianCustomerProfileDto> CreateCustomerAsync(MoadianCustomerProfileDto dto)
    {
        await ValidateCustomerAsync(dto);
        var customer = new MoadianCustomerProfile { ServiceProviderId = dto.ServiceProviderId };
        ApplyCustomer(customer, dto);
        _db.MoadianCustomerProfiles.Add(customer);
        await _db.SaveChangesAsync();
        return await LoadCustomerDtoAsync(customer.Id);
    }

    public async Task<MoadianCustomerProfileDto> UpdateCustomerAsync(int id, MoadianCustomerProfileDto dto)
    {
        await ValidateCustomerAsync(dto);
        var customer = await _db.MoadianCustomerProfiles.FindAsync(id)
            ?? throw new InvalidOperationException("مشتری یافت نشد.");
        customer.ServiceProviderId = dto.ServiceProviderId;
        ApplyCustomer(customer, dto);
        await _db.SaveChangesAsync();
        return await LoadCustomerDtoAsync(customer.Id);
    }

    public async Task SetCustomerDeletedAsync(int id, bool deleted)
    {
        var customer = await _db.MoadianCustomerProfiles.FindAsync(id)
            ?? throw new InvalidOperationException("مشتری یافت نشد.");
        customer.IsDeleted = deleted;
        await _db.SaveChangesAsync();
    }

    // Goods / services
    public async Task<List<MoadianGoodsOrServiceProfileDto>> GetGoodsOrServicesAsync(int? serviceProviderId = null)
    {
        var query = from item in _db.MoadianGoodsOrServices.AsNoTracking()
                    join provider in _db.MoadianServiceProviderProfiles.AsNoTracking()
                        on item.ServiceProviderId equals provider.Id
                    join unitRow in _db.MoadianUnitsOfMeasurement.AsNoTracking()
                        on item.UnitOfMeasurement equals unitRow.Code into units
                    from unit in units.DefaultIfEmpty()
                    select new { item, provider, unit };
        if (serviceProviderId.HasValue)
            query = query.Where(x => x.item.ServiceProviderId == serviceProviderId.Value);

        return await query.OrderBy(x => x.item.Name)
            .Select(x => new MoadianGoodsOrServiceProfileDto
            {
                Id = x.item.Id,
                ServiceProviderId = x.item.ServiceProviderId,
                ProviderName = x.provider.PersianName ?? x.provider.EnglishName ?? x.provider.NationalID,
                Name = x.item.Name,
                UniqueIdentifier = x.item.UniqueIdentifier,
                UnitOfMeasurement = x.item.UnitOfMeasurement,
                UnitName = x.unit == null ? null : x.unit.Name,
                ValueAddedPercentage = x.item.ValueAddedPercentage,
                Price = x.item.Price,
                IsDeleted = x.item.IsDeleted
            }).ToListAsync();
    }

    public async Task<MoadianGoodsOrServiceProfileDto> CreateGoodsOrServiceAsync(MoadianGoodsOrServiceProfileDto dto)
    {
        await ValidateGoodsOrServiceAsync(dto);
        var item = new MoadianGoodsOrServiceProfile { ServiceProviderId = dto.ServiceProviderId };
        ApplyGoodsOrService(item, dto);
        _db.MoadianGoodsOrServices.Add(item);
        await _db.SaveChangesAsync();
        return await LoadGoodsOrServiceDtoAsync(item.Id);
    }

    public async Task<MoadianGoodsOrServiceProfileDto> UpdateGoodsOrServiceAsync(int id, MoadianGoodsOrServiceProfileDto dto)
    {
        await ValidateGoodsOrServiceAsync(dto);
        var item = await _db.MoadianGoodsOrServices.FindAsync(id)
            ?? throw new InvalidOperationException("کالا/خدمت یافت نشد.");
        item.ServiceProviderId = dto.ServiceProviderId;
        ApplyGoodsOrService(item, dto);
        await _db.SaveChangesAsync();
        return await LoadGoodsOrServiceDtoAsync(item.Id);
    }

    public async Task SetGoodsOrServiceDeletedAsync(int id, bool deleted)
    {
        var item = await _db.MoadianGoodsOrServices.FindAsync(id)
            ?? throw new InvalidOperationException("کالا/خدمت یافت نشد.");
        item.IsDeleted = deleted;
        await _db.SaveChangesAsync();
    }

    // Units
    public async Task<List<MoadianUnitOfMeasurementDto>> GetUnitsAsync()
        => await _db.MoadianUnitsOfMeasurement.AsNoTracking()
            .OrderBy(x => x.Code)
            .Select(x => new MoadianUnitOfMeasurementDto
            {
                Id = x.Id, Code = x.Code, Name = x.Name, IsDeleted = x.IsDeleted
            }).ToListAsync();

    public async Task<MoadianUnitOfMeasurementDto> CreateUnitAsync(MoadianUnitOfMeasurementDto dto)
    {
        ValidateUnit(dto);
        var code = dto.Code.Trim();
        if (await _db.MoadianUnitsOfMeasurement.AnyAsync(x => x.Code == code))
            throw new InvalidOperationException("کد این واحد قبلاً ثبت شده است.");

        var unit = new MoadianUnitOfMeasurement { Code = code, Name = dto.Name.Trim() };
        _db.MoadianUnitsOfMeasurement.Add(unit);
        await _db.SaveChangesAsync();
        return ToUnitDto(unit);
    }

    public async Task<MoadianUnitOfMeasurementDto> UpdateUnitAsync(int id, MoadianUnitOfMeasurementDto dto)
    {
        ValidateUnit(dto);
        var unit = await _db.MoadianUnitsOfMeasurement.FindAsync(id)
            ?? throw new InvalidOperationException("واحد اندازه‌گیری یافت نشد.");
        var code = dto.Code.Trim();
        if (await _db.MoadianUnitsOfMeasurement.AnyAsync(x => x.Id != id && x.Code == code))
            throw new InvalidOperationException("کد این واحد قبلاً ثبت شده است.");
        if (!string.Equals(unit.Code, code, StringComparison.Ordinal)
            && await _db.MoadianGoodsOrServices.AnyAsync(x => x.UnitOfMeasurement == unit.Code))
            throw new InvalidOperationException("کد این واحد به کالا/خدمت تخصیص یافته است؛ ابتدا رکوردهای مرتبط را اصلاح کنید.");

        unit.Code = code;
        unit.Name = dto.Name.Trim();
        await _db.SaveChangesAsync();
        return ToUnitDto(unit);
    }

    public async Task SetUnitDeletedAsync(int id, bool deleted)
    {
        var unit = await _db.MoadianUnitsOfMeasurement.FindAsync(id)
            ?? throw new InvalidOperationException("واحد اندازه‌گیری یافت نشد.");
        if (deleted && await _db.MoadianGoodsOrServices.AnyAsync(x => x.UnitOfMeasurement == unit.Code))
            throw new InvalidOperationException("ابتدا اقلام مرتبط را به واحد دیگری منتقل کنید.");
        unit.IsDeleted = deleted;
        await _db.SaveChangesAsync();
    }

    private async Task ValidateConnectionAsync(MoadianProviderConnectionProfileDto dto)
    {
        if (dto.ServiceProviderId <= 0
            || !await _db.MoadianServiceProviderProfiles.AnyAsync(x => x.Id == dto.ServiceProviderId && !x.IsDeleted))
            throw new InvalidOperationException("خدمات‌دهندهٔ فعال را انتخاب کنید.");

        AddLengthError(dto.WebServiceAddress, 2048, "نشانی وب‌سرویس");
        AddLengthError(dto.TaxMemoryId, 50, "TaxMemoryID");

        if (string.IsNullOrWhiteSpace(dto.WebServiceAddress)
            || !Uri.TryCreate(dto.WebServiceAddress.Trim(), UriKind.Absolute, out var baseUri)
            || baseUri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(baseUri.UserInfo)
            || !string.IsNullOrEmpty(baseUri.Query)
            || !string.IsNullOrEmpty(baseUri.Fragment))
            throw new InvalidOperationException("نشانی وب‌سرویس باید HTTPS مطلق و بدون credentials، query یا fragment باشد.");
        if (string.IsNullOrWhiteSpace(dto.TaxMemoryId)) throw new InvalidOperationException("TaxMemoryID الزامی است.");
    }

    private async Task ValidateCustomerAsync(MoadianCustomerProfileDto dto)
    {
        if (dto.ServiceProviderId <= 0
            || !await _db.MoadianServiceProviderProfiles.AnyAsync(x => x.Id == dto.ServiceProviderId && !x.IsDeleted))
            throw new InvalidOperationException("خدمات‌دهندهٔ فعال را انتخاب کنید.");
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("نام مشتری الزامی است.");
        AddLengthError(dto.Type, 50, "نوع مشتری");
        AddLengthError(dto.Name, 50, "نام مشتری");
        AddLengthError(dto.NationalId, 50, "شناسه ملی مشتری");
        AddLengthError(dto.EconomicNumber, 50, "شماره اقتصادی مشتری");
        AddLengthError(dto.Phone, 50, "تلفن");
        AddLengthError(dto.PostalCode, 50, "کدپستی مشتری");
    }

    // ===================== ورود/خروج اکسل «کالا و خدمت» =====================

    private static readonly string[] GoodsImportHeaders =
    {
        "خدمات‌دهنده", "نام کالا/خدمت", "شناسه کالا/خدمت", "واحد کالا و خدمت", "درصد ارزش افزوده", "قیمت واحد (ریال)"
    };

    public async Task<ExcelImportResult> ImportGoodsOrServicesAsync(Stream excelStream)
    {
        var result = new ExcelImportResult();

        using var wb = new XLWorkbook(excelStream);
        var ws = wb.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("فایل اکسل فاقد کاربرگ (Sheet) است.");

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        if (lastRow < 2)
            throw new InvalidOperationException("فایل اکسل خالی است؛ داده‌ها باید از سطر ۲ (بعد از سطر عنوان) شروع شوند.");

        var providers = await _db.MoadianServiceProviderProfiles.AsNoTracking()
            .Where(x => !x.IsDeleted).ToListAsync();
        if (providers.Count == 0)
            throw new InvalidOperationException("ابتدا یک خدمات‌دهندهٔ فعال در زبانهٔ «خدمات‌دهنده» تعریف کنید.");

        var units = await _db.MoadianUnitsOfMeasurement.AsNoTracking()
            .Where(x => !x.IsDeleted).ToListAsync();

        var existingGoods = await _db.MoadianGoodsOrServices
            .Where(x => !x.IsDeleted).ToListAsync();

        for (var r = 2; r <= lastRow; r++)
        {
            var row = ws.Row(r);
            if (row.IsEmpty()) continue;

            try
            {
                var providerText = CellText(row.Cell(1));
                var name = CellText(row.Cell(2));
                var uniqueIdentifier = CellText(row.Cell(3));
                var unitText = CellText(row.Cell(4));
                var vatText = CellText(row.Cell(5));
                var price = ReadDecimal(row.Cell(6));

                if (string.IsNullOrWhiteSpace(name))
                    throw new InvalidOperationException("نام کالا/خدمت خالی است.");

                // خدمات‌دهنده: خالی ⇒ نخستین خدمات‌دهندهٔ فعال؛ در غیر این صورت تطابق نام/شناسه
                var provider = string.IsNullOrWhiteSpace(providerText)
                    ? providers.OrderBy(x => x.Id).First()
                    : providers.FirstOrDefault(x => Match(providerText, x.PersianName)
                                                    || Match(providerText, x.EnglishName)
                                                    || Match(providerText, x.NationalID))
                      ?? throw new InvalidOperationException(
                          $"خدمات‌دهندهٔ «{providerText}» در فهرست خدمات‌دهندگان فعال نیست (کاربرگ «خدمات‌دهنده‌ها» را ببینید).");

                // واحد: می‌تواند کد یا نام باشد؛ برای جلوگیری از کد نادرست، فقط مقادیر موجود پذیرفته می‌شوند.
                if (string.IsNullOrWhiteSpace(unitText))
                    throw new InvalidOperationException("واحد کالا و خدمت خالی است (کد یا نام واحد از کاربرگ «واحدهای موجود»).");
                var unit = units.FirstOrDefault(x => Match(unitText, x.Code))
                           ?? units.FirstOrDefault(x => Match(unitText, x.Name))
                           ?? throw new InvalidOperationException(
                               $"واحد «{unitText}» در فهرست واحدهای فعال نیست (کاربرگ «واحدهای موجود» را ببینید).");

                var dto = new MoadianGoodsOrServiceProfileDto
                {
                    ServiceProviderId = provider.Id,
                    Name = name,
                    UniqueIdentifier = string.IsNullOrWhiteSpace(uniqueIdentifier) ? null : uniqueIdentifier,
                    UnitOfMeasurement = unit.Code,
                    ValueAddedPercentage = ParsePercent(vatText),
                    Price = price
                };

                await ValidateGoodsOrServiceAsync(dto);

                // به‌روزرسانی در صورت تکرار (ابتدا با شناسهٔ کالا/خدمت، سپس با نام)
                var existing = (!string.IsNullOrWhiteSpace(dto.UniqueIdentifier)
                        ? existingGoods.FirstOrDefault(x => x.ServiceProviderId == provider.Id
                            && Match(x.UniqueIdentifier, dto.UniqueIdentifier))
                        : null)
                    ?? existingGoods.FirstOrDefault(x => x.ServiceProviderId == provider.Id && Match(x.Name, dto.Name));

                if (existing is not null)
                {
                    existing.Name = dto.Name.Trim();
                    existing.UniqueIdentifier = dto.UniqueIdentifier;
                    existing.UnitOfMeasurement = dto.UnitOfMeasurement;
                    existing.ValueAddedPercentage = checked((byte)dto.ValueAddedPercentage);
                    existing.Price = dto.Price;
                }
                else
                {
                    var entity = new MoadianGoodsOrServiceProfile { ServiceProviderId = provider.Id };
                    ApplyGoodsOrService(entity, dto);
                    _db.MoadianGoodsOrServices.Add(entity);
                    existingGoods.Add(entity);
                }

                await _db.SaveChangesAsync();
                result.Imported++;
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Errors.Add($"سطر {r}: {ex.Message}");
            }
        }

        return result;
    }

    public async Task<byte[]> BuildGoodsOrServicesTemplateAsync()
    {
        var units = await _db.MoadianUnitsOfMeasurement.AsNoTracking()
            .Where(x => !x.IsDeleted).OrderBy(x => x.Name).ToListAsync();
        var providers = await _db.MoadianServiceProviderProfiles.AsNoTracking()
            .Where(x => !x.IsDeleted).OrderBy(x => x.PersianName).ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("کالا و خدمت");
        ws.RightToLeft = true;

        for (var i = 0; i < GoodsImportHeaders.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = GoodsImportHeaders[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBF7");
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
        ws.SheetView.FreezeRows(1);

        var sampleProvider = providers.FirstOrDefault()?.PersianName ?? "نام خدمات‌دهنده";
        var sampleUnit = units.FirstOrDefault();
        ws.Cell(2, 1).Value = sampleProvider;
        ws.Cell(2, 2).Value = "روغن ترمز DOT4";
        ws.Cell(2, 3).Value = "1234567890123";
        ws.Cell(2, 4).Value = sampleUnit?.Code ?? "کد واحد";
        ws.Cell(2, 5).Value = 9;
        ws.Cell(2, 6).Value = 590000;
        ws.Cell(3, 1).Value = sampleProvider;
        ws.Cell(3, 2).Value = "خدمات نصب و راه‌اندازی";
        ws.Cell(3, 3).Value = "";
        ws.Cell(3, 4).Value = sampleUnit?.Name ?? "نام واحد";
        ws.Cell(3, 5).Value = 9;
        ws.Cell(3, 6).Value = 4100000;
        ws.Cell(4, 1).Value = "دو سطر نمونه — پیش از بارگذاری حذف یا جایگزین شوند.";
        ws.Cell(4, 1).Style.Font.Italic = true;

        ws.Column(6).Style.NumberFormat.Format = "#,##0";
        ws.Columns(1, GoodsImportHeaders.Length).AdjustToContents();

        var help = wb.Worksheets.Add("راهنما");
        help.RightToLeft = true;
        var lines = new[]
        {
            "راهنمای ورود گروهی «کالا و خدمت» از اکسل",
            "",
            "• داده‌ها را از سطر ۲ کاربرگ «کالا و خدمت» وارد کنید (سطر ۱ عنوان ستون‌ها است).",
            "• «نام کالا/خدمت» اجباری است؛ «واحد کالا و خدمت» هم اجباری است و باید کد یا نام یکی از واحدهای فعال باشد.",
            "• ستون «خدمات‌دهنده» نام فارسی، نام لاتین یا شناسهٔ ملی خدمات‌دهندهٔ فعال است؛ خالی بماند، نخستین خدمات‌دهندهٔ فعال انتخاب می‌شود.",
            "• «شناسه کالا/خدمت» همان شناسهٔ رسمی کالا/خدمت در سامانهٔ مودیان است (اختیاری).",
            "• «درصد ارزش افزوده» بین ۰ تا ۱۰۰؛ خالی بماند صفر در نظر گرفته می‌شود.",
            "• «قیمت واحد (ریال)» به ریال و با جداکنندهٔ سه‌رقمی قابل ورود است؛ رقم فارسی هم پذیرفته می‌شود.",
            "• اگر «شناسه کالا/خدمت» (یا در نبود آن «نام») برای همان خدمات‌دهنده تکرار شود، همان رکورد به‌روزرسانی می‌شود.",
            "• کاربرگ «واحدهای موجود» و «خدمات‌دهنده‌ها» مقادیر مجاز را نشان می‌دهند؛ مقدار نامعتبر باعث رد همان سطر می‌شود."
        };
        for (var i = 0; i < lines.Length; i++)
        {
            help.Cell(i + 1, 1).Value = lines[i];
            if (i == 0) help.Cell(1, 1).Style.Font.Bold = true;
        }
        help.Column(1).AdjustToContents();

        var unitSheet = wb.Worksheets.Add("واحدهای موجود");
        unitSheet.RightToLeft = true;
        unitSheet.Cell(1, 1).Value = "کد واحد";
        unitSheet.Cell(1, 2).Value = "نام واحد";
        unitSheet.Row(1).Style.Font.Bold = true;
        unitSheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBF7");
        for (var i = 0; i < units.Count; i++)
        {
            unitSheet.Cell(i + 2, 1).Value = units[i].Code;
            unitSheet.Cell(i + 2, 2).Value = units[i].Name;
        }
        if (units.Count == 0) unitSheet.Cell(2, 1).Value = "واحدی ثبت نشده است؛ ابتدا در زبانهٔ «واحد» واحد اضافه کنید.";
        unitSheet.Columns(1, 2).AdjustToContents();

        var providerSheet = wb.Worksheets.Add("خدمات‌دهنده‌ها");
        providerSheet.RightToLeft = true;
        providerSheet.Cell(1, 1).Value = "نام خدمات‌دهنده";
        providerSheet.Cell(1, 2).Value = "نام لاتین";
        providerSheet.Cell(1, 3).Value = "شناسهٔ ملی";
        providerSheet.Row(1).Style.Font.Bold = true;
        providerSheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBF7");
        for (var i = 0; i < providers.Count; i++)
        {
            providerSheet.Cell(i + 2, 1).Value = providers[i].PersianName ?? "";
            providerSheet.Cell(i + 2, 2).Value = providers[i].EnglishName ?? "";
            providerSheet.Cell(i + 2, 3).Value = providers[i].NationalID ?? "";
        }
        providerSheet.Columns(1, 3).AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static string CellText(IXLCell cell)
        => cell.IsEmpty() ? "" : Fa.ToEn(cell.GetString()).Replace("\u200c", "").Trim();

    private static bool Match(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
           && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static byte ParsePercent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var raw = text.Replace("%", "").Replace("٪", "").Replace("،", "").Replace(",", "").Trim();
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            throw new InvalidOperationException($"درصد ارزش افزودهٔ «{text}» عدد معتبری نیست.");
        if (value < 0 || value > 100)
            throw new InvalidOperationException("درصد ارزش افزوده باید بین ۰ و ۱۰۰ باشد.");
        return (byte)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static decimal ReadDecimal(IXLCell cell)
    {
        if (cell.IsEmpty()) return 0;
        if (cell.TryGetValue<decimal>(out var value)) return value;
        var raw = Fa.ToEn(cell.GetString()).Replace(",", "").Replace("،", "").Replace(" ", "").Trim();
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"مقدار «{cell.GetString()}» عدد معتبری نیست.");
    }

    private async Task ValidateGoodsOrServiceAsync(MoadianGoodsOrServiceProfileDto dto)
    {
        if (dto.ServiceProviderId <= 0
            || !await _db.MoadianServiceProviderProfiles.AnyAsync(x => x.Id == dto.ServiceProviderId && !x.IsDeleted))
            throw new InvalidOperationException("خدمات‌دهندهٔ فعال را انتخاب کنید.");
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("نام کالا/خدمت الزامی است.");
        if (dto.Price < 0)
            throw new InvalidOperationException("قیمت نمی‌تواند منفی باشد.");
        if (dto.ValueAddedPercentage is < 0 or > 100)
            throw new InvalidOperationException("درصد ارزش افزوده باید بین صفر و صد باشد.");
        if (string.IsNullOrWhiteSpace(dto.UnitOfMeasurement)
            || !await _db.MoadianUnitsOfMeasurement.AnyAsync(x => x.Code == dto.UnitOfMeasurement.Trim() && !x.IsDeleted))
            throw new InvalidOperationException("واحد اندازه‌گیری فعال را انتخاب کنید.");
        AddLengthError(dto.Name, 50, "نام کالا/خدمت");
        AddLengthError(dto.UniqueIdentifier, 50, "شناسه یکتای کالا/خدمت");
        AddLengthError(dto.UnitOfMeasurement, 50, "کد واحد اندازه‌گیری");
    }

    private static void ValidateProvider(MoadianServiceProviderProfileDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.NationalId))
            throw new InvalidOperationException("شناسه ملی خدمات‌دهنده الزامی است.");
        if (string.IsNullOrWhiteSpace(dto.PersianName) && string.IsNullOrWhiteSpace(dto.EnglishName))
            throw new InvalidOperationException("نام فارسی یا نام انگلیسی خدمات‌دهنده را وارد کنید.");
        AddLengthError(dto.Type, 50, "نوع شخص");
        AddLengthError(dto.EconomicNumber, 50, "شماره اقتصادی");
        AddLengthError(dto.PersianName, 50, "نام فارسی");
        AddLengthError(dto.EnglishName, 50, "نام انگلیسی");
        AddLengthError(dto.NationalId, 50, "شناسه ملی خدمات‌دهنده");
        AddLengthError(dto.PostalCode, 50, "کدپستی");
    }

    private static void ValidateUnit(MoadianUnitOfMeasurementDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Code)) throw new InvalidOperationException("کد واحد الزامی است.");
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new InvalidOperationException("نام واحد الزامی است.");
        AddLengthError(dto.Code, 50, "کد واحد");
        AddLengthError(dto.Name, 50, "نام واحد");
    }

    private async Task<MoadianProviderConnectionProfileDto> LoadConnectionDtoAsync(int id)
        => await (from connection in _db.MoadianProviderConnections.AsNoTracking()
                  join provider in _db.MoadianServiceProviderProfiles.AsNoTracking()
                      on connection.ServiceProviderId equals provider.Id
                  where connection.Id == id
                  select new MoadianProviderConnectionProfileDto
                  {
                      Id = connection.Id,
                      ServiceProviderId = connection.ServiceProviderId,
                      ProviderName = provider.PersianName ?? provider.EnglishName ?? provider.NationalID,
                      WebServiceAddress = connection.WebServiceAddress,
                      TaxMemoryId = connection.TaxMemoryID,
                      HasPrivateKey = !string.IsNullOrWhiteSpace(connection.PrivateKeyPath),
                      IsDeleted = connection.IsDeleted,
                      LastTestAt = connection.LastConnectionTestAt,
                      LastTestSucceeded = connection.LastConnectionTestSucceeded,
                      LastTestMessage = connection.LastConnectionTestMessage
                  }).SingleAsync();

    private async Task<MoadianCustomerProfileDto> LoadCustomerDtoAsync(int id)
        => await (from customer in _db.MoadianCustomerProfiles.AsNoTracking()
                  join provider in _db.MoadianServiceProviderProfiles.AsNoTracking()
                      on customer.ServiceProviderId equals provider.Id
                  where customer.Id == id
                  select new MoadianCustomerProfileDto
                  {
                      Id = customer.Id,
                      ServiceProviderId = customer.ServiceProviderId,
                      ProviderName = provider.PersianName ?? provider.EnglishName ?? provider.NationalID,
                      Type = customer.Type,
                      Name = customer.Name,
                      NationalId = customer.NationalID,
                      EconomicNumber = customer.EconomicNumber,
                      Phone = customer.Phone,
                      PostalCode = customer.PostalCode,
                      Address = customer.Address,
                      IsDeleted = customer.IsDeleted
                  }).SingleAsync();

    private async Task<MoadianGoodsOrServiceProfileDto> LoadGoodsOrServiceDtoAsync(int id)
        => await (from item in _db.MoadianGoodsOrServices.AsNoTracking()
                  join provider in _db.MoadianServiceProviderProfiles.AsNoTracking()
                      on item.ServiceProviderId equals provider.Id
                  join unitRow in _db.MoadianUnitsOfMeasurement.AsNoTracking()
                      on item.UnitOfMeasurement equals unitRow.Code into units
                  from unit in units.DefaultIfEmpty()
                  where item.Id == id
                  select new MoadianGoodsOrServiceProfileDto
                  {
                      Id = item.Id,
                      ServiceProviderId = item.ServiceProviderId,
                      ProviderName = provider.PersianName ?? provider.EnglishName ?? provider.NationalID,
                      Name = item.Name,
                      UniqueIdentifier = item.UniqueIdentifier,
                      UnitOfMeasurement = item.UnitOfMeasurement,
                      UnitName = unit == null ? null : unit.Name,
                      ValueAddedPercentage = item.ValueAddedPercentage,
                      Price = item.Price,
                      IsDeleted = item.IsDeleted
                  }).SingleAsync();

    private static void ApplyProvider(MoadianServiceProviderProfile entity, MoadianServiceProviderProfileDto dto)
    {
        entity.Type = TrimOrNull(dto.Type);
        entity.EconomicNumber = TrimOrNull(dto.EconomicNumber);
        entity.PersianName = TrimOrNull(dto.PersianName);
        entity.EnglishName = TrimOrNull(dto.EnglishName);
        entity.NationalID = dto.NationalId.Trim();
        entity.PostalCode = TrimOrNull(dto.PostalCode);
    }

    private static void ApplyConnection(MoadianProviderConnectionProfile entity, MoadianProviderConnectionProfileDto dto)
    {
        entity.WebServiceAddress = dto.WebServiceAddress.Trim();
        entity.TaxMemoryID = dto.TaxMemoryId.Trim();
        // PrivateKeyPath is managed only by the protected file store; it is never accepted from this DTO.
    }

    private static void ApplyCustomer(MoadianCustomerProfile entity, MoadianCustomerProfileDto dto)
    {
        entity.Type = TrimOrNull(dto.Type);
        entity.Name = TrimOrNull(dto.Name);
        entity.NationalID = TrimOrNull(dto.NationalId);
        entity.EconomicNumber = TrimOrNull(dto.EconomicNumber);
        entity.Phone = TrimOrNull(dto.Phone);
        entity.PostalCode = TrimOrNull(dto.PostalCode);
        entity.Address = TrimOrNull(dto.Address);
    }

    private static void ApplyGoodsOrService(MoadianGoodsOrServiceProfile entity, MoadianGoodsOrServiceProfileDto dto)
    {
        entity.Name = TrimOrNull(dto.Name);
        entity.UniqueIdentifier = TrimOrNull(dto.UniqueIdentifier);
        entity.UnitOfMeasurement = dto.UnitOfMeasurement.Trim();
        entity.ValueAddedPercentage = checked((byte)dto.ValueAddedPercentage);
        entity.Price = dto.Price;
    }

    private static MoadianServiceProviderProfileDto ToProviderDto(MoadianServiceProviderProfile provider)
        => new()
        {
            Id = provider.Id,
            Type = provider.Type,
            EconomicNumber = provider.EconomicNumber,
            PersianName = provider.PersianName,
            EnglishName = provider.EnglishName,
            NationalId = provider.NationalID,
            PostalCode = provider.PostalCode,
            IsDeleted = provider.IsDeleted
        };

    private static MoadianUnitOfMeasurementDto ToUnitDto(MoadianUnitOfMeasurement unit)
        => new() { Id = unit.Id, Code = unit.Code, Name = unit.Name, IsDeleted = unit.IsDeleted };

    private static void AddLengthError(string? value, int maxLength, string label)
    {
        if (value is not null && value.Length > maxLength)
            throw new InvalidOperationException($"{label} بیش از {maxLength} نویسه است.");
    }

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
