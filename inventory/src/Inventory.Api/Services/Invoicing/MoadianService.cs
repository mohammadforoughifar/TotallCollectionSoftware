using Inventory.Api.Services;
using System.Data;
using System.Text.Json;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Db = Inventory.Api.Data;

namespace Inventory.Api.Services.Invoicing;

/// <summary>
/// سرویس پیش‌نویس‌های مودیان: تنظیمات امن، دوره‌های مالیاتی، اتصال به دو منبع فروش،
/// ویرایش/بازبینی و نگهداری رویدادها. ارسال مستقیم و خودکار عمداً fail-closed است؛
/// خروجی نمایشی JSON صرفاً snapshot محلی است و قالب رسمی محسوب نمی‌شود.
/// </summary>
public interface IMoadianService
{
    Task<MoadianSetting> GetSettingAsync();
    Task<MoadianSetting> SaveSettingAsync(MoadianSetting dto);

    Task<List<MoadianFiscalPeriod>> GetPeriodsAsync(Paging.Request? pagination = null);
    Task<MoadianFiscalPeriod> SavePeriodAsync(MoadianFiscalPeriod dto);
    Task DeletePeriodAsync(int id);

    Task<List<MoadianInvoice>> GetInvoicesAsync(int? periodId = null, MoadianInvoiceStatus? status = null, string? search = null, Paging.Request? pagination = null);
    Task<MoadianInvoice> GetInvoiceAsync(int id);
    Task<MoadianInvoice> CreateFromFacInvoiceAsync(int facInvoiceId, string? user);
    Task<MoadianInvoice?> CreateFromFacInvoiceIfEnabledAsync(int facInvoiceId, string? user);
    Task<MoadianInvoice> CreateFromOperationsTransactionAsync(int transactionId, string? user);
    Task<MoadianInvoice?> CreateFromOperationsTransactionIfEnabledAsync(int transactionId, string? user);
    Task<MoadianInvoice> CreateManualAsync(MoadianInvoiceRequest req, string? user);
    Task<MoadianInvoice> UpdateDraftAsync(int id, MoadianInvoiceRequest req, string? user);
    Task DeleteInvoiceAsync(int id);

    Task<MoadianInvoice> EnqueueAsync(int id, string? user);
    Task<MoadianInvoice> CancelAsync(int id, string? user);
    Task<MoadianInvoice> RetryAsync(int id, string? user);

    Task<int> SendPendingAsync();
    Task<MoadianPayloadResult> BuildPayloadAsync(int id);

    Task<List<MoadianLog>> GetLogsAsync(int? invoiceId = null, Paging.Request? pagination = null);
    Task<List<MoadianCpc>> GetCpcAsync(string? search = null, Paging.Request? pagination = null);
    Task<MoadianCpc> SaveCpcAsync(MoadianCpc dto);
    Task DeleteCpcAsync(int id);

    Task<MoadianDashboard> GetDashboardAsync();
}

public class MoadianService : IMoadianService
{
    private readonly Db.AppDbContext _db;
    private readonly ILogger<MoadianService> _log;
    private readonly IDataProtector _secrets;

    public MoadianService(Db.AppDbContext db, ILogger<MoadianService> log, IDataProtectionProvider protectionProvider,
        MoadianRuntimeSettings settings)
    {
        _db = db;
        _log = log;
        _secrets = protectionProvider.CreateProtector("Inventory.Moadian.Settings.v1");
        _settings = settings;
    }

    private readonly MoadianRuntimeSettings _settings;

    /// <summary>
    /// Next internal serial: max(existing max + 1, SerialStartNumber). The inno series is
    /// per-taxpayer in the system, so when the account has history from an older software the
    /// numbering must continue after it (duplicate inno => system error 0300101).
    /// </summary>
    internal static int NextSerialNumber(int currentMax, int serialStart)
        => Math.Max(currentMax + 1, Math.Max(0, serialStart));

    // =====================================================================
    // تنظیمات
    // =====================================================================
    public async Task<MoadianSetting> GetSettingAsync()
    {
        var s = await _db.MoadianSettings.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync()
                ?? new Db.MoadianSetting();
        var dto = ToDto(s);
        // رازها هرگز از API به مرورگر برگردانده نمی‌شوند؛ فقط وضعیت «تنظیم شده» نمایش داده می‌شود.
        dto.PrivateKeyPem = MaskSecret(s.PrivateKeyPem);
        dto.SigningCertificatePem = MaskSecret(s.SigningCertificatePem);
        dto.TaxCardToken = MaskSecret(s.TaxCardToken);
        return dto;
    }

    public async Task<MoadianSetting> SaveSettingAsync(MoadianSetting dto)
    {
        if (string.IsNullOrWhiteSpace(dto.TaxId))
            throw new InvalidOperationException("شماره ملی/شناسه مالیاتی الزامی است.");
        if (dto.TaxId.Trim().Length is not (10 or 11 or 14))
            throw new InvalidOperationException("شماره مالیاتی باید ۱۰ رقم (حقیقی)، ۱۱ رقم (حقوقی) یا ۱۴ رقم باشد.");

        var entity = await _db.MoadianSettings.OrderBy(x => x.Id).FirstOrDefaultAsync();
        if (entity is null)
        {
            entity = new Db.MoadianSetting();
            _db.MoadianSettings.Add(entity);
        }

        var fiscalMemoryId = NullIfEmpty(dto.FiscalMemoryId);
        if (fiscalMemoryId is { Length: > 20 })
            throw new InvalidOperationException("شناسه/کد حافظه مالیاتی بیش از ۲۰ نویسه است.");

        var submittedPrivateKey = dto.PrivateKeyPem;
        var submittedCertificate = dto.SigningCertificatePem;
        var submittedToken = dto.TaxCardToken;
        var preservePrivateKey = string.IsNullOrWhiteSpace(submittedPrivateKey) || submittedPrivateKey.StartsWith("*****", StringComparison.Ordinal);
        var preserveCertificate = string.IsNullOrWhiteSpace(submittedCertificate) || submittedCertificate.StartsWith("*****", StringComparison.Ordinal);
        var preserveToken = string.IsNullOrWhiteSpace(submittedToken) || submittedToken.StartsWith("*****", StringComparison.Ordinal);

        entity.TaxId = dto.TaxId.Trim();
        entity.FiscalMemoryId = fiscalMemoryId;
        entity.EconomicCode = string.IsNullOrWhiteSpace(dto.EconomicCode) ? null : dto.EconomicCode.Trim();
        entity.SellerName = (dto.SellerName ?? "").Trim();
        entity.SellerAddress = string.IsNullOrWhiteSpace(dto.SellerAddress) ? null : dto.SellerAddress.Trim();
        entity.SellerPostalCode = string.IsNullOrWhiteSpace(dto.SellerPostalCode) ? null : dto.SellerPostalCode.Trim();
        entity.SellerPhone = string.IsNullOrWhiteSpace(dto.SellerPhone) ? null : dto.SellerPhone.Trim();
        entity.TaxCardToken = preserveToken ? ProtectLegacySecretIfNeeded(entity.TaxCardToken) : ProtectSecret(submittedToken);
        entity.BaseUrl = string.IsNullOrWhiteSpace(dto.BaseUrl) ? null : dto.BaseUrl.Trim();
        // رازهای موجود به‌صورت خودکار از متن آشکار قدیمی به Data Protection منتقل می‌شوند.
        entity.PrivateKeyPem = preservePrivateKey ? ProtectLegacySecretIfNeeded(entity.PrivateKeyPem) : ProtectSecret(submittedPrivateKey);
        entity.SigningCertificatePem = preserveCertificate ? ProtectLegacySecretIfNeeded(entity.SigningCertificatePem) : ProtectSecret(submittedCertificate);
        entity.PublicKeyPem = dto.PublicKeyPem;
        entity.AutoSend = false; // سیاست صریح محصول: هیچ ارسال خودکاری مجاز نیست.
        entity.AutoDraftFromOperations = dto.AutoDraftFromOperations;
        entity.AutoDraftFromFacInvoices = dto.AutoDraftFromFacInvoices;
        entity.SendIntervalMinutes = Math.Max(1, dto.SendIntervalMinutes);
        entity.DefaultVatRate = Math.Clamp(dto.DefaultVatRate, 0, 100);
        entity.LoggingEnabled = dto.LoggingEnabled;
        entity.IsActive = dto.IsActive;
        entity.Notes = dto.Notes;
        entity.UpdatedAt = DateTime.Now;

        await _db.SaveChangesAsync();
        return await GetSettingAsync();
    }

    // =====================================================================
    // دوره‌های مالیاتی
    // =====================================================================
    public async Task<List<MoadianFiscalPeriod>> GetPeriodsAsync(Paging.Request? pagination = null)
    {
        var periods = await _db.MoadianFiscalPeriods.AsNoTracking()
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).ThenBy(x => x.Id).ToPageListAsync(pagination);

        // توجه: تجمیع decimal روی SQLite ترجمه نمی‌شود؛ ابتدا داده را می‌آوریم و تجمیع در حافظه انجام می‌شود
        var stats = (await _db.MoadianInvoices.AsNoTracking()
                .Where(i => i.Status != MoadianInvoiceStatus.Voided && periods.Select(p => p.Id).Contains(i.FiscalPeriodId))
                .Select(i => new { i.FiscalPeriodId, i.TotalNet })
                .ToListAsync())
            .GroupBy(i => i.FiscalPeriodId)
            .Select(g => new { Id = g.Key, Count = g.Count(), Net = g.Sum(i => i.TotalNet) })
            .ToList();

        return periods.Select(p =>
        {
            var st = stats.FirstOrDefault(s => s.Id == p.Id);
            return new MoadianFiscalPeriod
            {
                Id = p.Id, Year = p.Year, Month = p.Month, IsClosed = p.IsClosed, Notes = p.Notes,
                InvoiceCount = st?.Count ?? 0, NetTotal = st?.Net ?? 0
            };
        }).ToList();
    }

    public async Task<MoadianFiscalPeriod> SavePeriodAsync(MoadianFiscalPeriod dto)
    {
        if (dto.Year < 1300 || dto.Month is < 1 or > 12)
            throw new InvalidOperationException("سال/ماه دوره معتبر نیست.");
        var dup = await _db.MoadianFiscalPeriods.AnyAsync(p => p.Year == dto.Year && p.Month == dto.Month && p.Id != dto.Id);
        if (dup) throw new InvalidOperationException("این دوره قبلاً تعریف شده است.");

        Db.MoadianFiscalPeriod entity;
        if (dto.Id == 0)
        {
            entity = new Db.MoadianFiscalPeriod();
            _db.MoadianFiscalPeriods.Add(entity);
        }
        else
        {
            entity = await _db.MoadianFiscalPeriods.FindAsync(dto.Id)
                     ?? throw new InvalidOperationException("دوره یافت نشد.");
        }
        entity.Year = dto.Year;
        entity.Month = dto.Month;
        entity.IsClosed = dto.IsClosed;
        entity.Notes = dto.Notes;
        await _db.SaveChangesAsync();
        dto.Id = entity.Id;
        return dto;
    }

    public async Task DeletePeriodAsync(int id)
    {
        var entity = await _db.MoadianFiscalPeriods.FindAsync(id)
                     ?? throw new InvalidOperationException("دوره یافت نشد.");
        var hasInvoices = await _db.MoadianInvoices.AnyAsync(i => i.FiscalPeriodId == id);
        if (hasInvoices)
            throw new InvalidOperationException("این دوره دارای فاکتور است و قابل حذف نیست.");
        _db.MoadianFiscalPeriods.Remove(entity);
        await _db.SaveChangesAsync();
    }

    // =====================================================================
    // فاکتورها
    // =====================================================================
    public async Task<List<MoadianInvoice>> GetInvoicesAsync(int? periodId = null, MoadianInvoiceStatus? status = null, string? search = null, Paging.Request? pagination = null)
    {
        var q = _db.MoadianInvoices.AsNoTracking()
            .Include(i => i.FiscalPeriod).Include(i => i.Lines).AsQueryable();
        if (periodId is not null) q = q.Where(i => i.FiscalPeriodId == periodId);
        if (status is not null) q = q.Where(i => i.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(i => i.Number.ToString().Contains(search) || (i.BuyerName != null && i.BuyerName.Contains(search)));

        var rows = await q.OrderByDescending(i => i.Id).ToPageListAsync(pagination);
        var duplicateNumbers = (await _db.MoadianInvoices.AsNoTracking()
            .GroupBy(i => i.Number)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToListAsync()).ToHashSet();

        return rows.Select(i => ToDto(i, duplicateNumbers.Contains(i.Number))).ToList();
    }

    public async Task<MoadianInvoice> GetInvoiceAsync(int id)
    {
        var entity = await _db.MoadianInvoices.AsNoTracking()
            .Include(i => i.FiscalPeriod).Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == id)
            ?? throw new InvalidOperationException("فاکتور الکترونیکی یافت نشد.");
        var hasDuplicateNumber = await _db.MoadianInvoices.AsNoTracking()
            .AnyAsync(i => i.Number == entity.Number && i.Id != entity.Id);
        return ToDto(entity, hasDuplicateNumber);
    }

    public async Task<MoadianInvoice> CreateFromFacInvoiceAsync(int facInvoiceId, string? user)
    {
        var fac = await _db.FacInvoices.AsNoTracking()
            .Include(f => f.Party)
            .FirstOrDefaultAsync(f => f.Id == facInvoiceId)
            ?? throw new InvalidOperationException("فاکتور ERP یافت نشد.");
        if (fac.Status != InvoiceStatus.Confirmed)
            throw new InvalidOperationException("فقط فاکتور قطعی ERP قابل تبدیل به پیش‌نویس مودیان است.");
        if (fac.Kind is not (InvoiceKind.Sale or InvoiceKind.SaleReturn))
            throw new InvalidOperationException("از ERP فقط فاکتور فروش یا برگشت از فروش به مودیان تبدیل می‌شود.");
        var existingDraft = await _db.MoadianInvoices.AsNoTracking().Include(i => i.Lines).Include(i => i.FiscalPeriod)
            .FirstOrDefaultAsync(i => i.FacInvoiceId == facInvoiceId);
        if (existingDraft is not null) return await GetInvoiceAsync(existingDraft.Id);

        var setting = await _db.MoadianSettings.OrderBy(x => x.Id).FirstOrDefaultAsync()
                      ?? throw new InvalidOperationException("ابتدا تنظیمات مودیان را تکمیل کنید.");
        if (string.IsNullOrWhiteSpace(setting.TaxId))
            throw new InvalidOperationException("شناسه مالیاتی فروشنده در تنظیمات ثبت نشده است.");

        var period = await GetOrCreatePeriodAsync(fac.Date);
        if (period.IsClosed)
            throw new InvalidOperationException("دوره مالیاتی مربوط به تاریخ فاکتور بسته است.");


        var facLines = await _db.FacInvoiceLines.AsNoTracking()
            .Where(l => l.InvoiceId == fac.Id).OrderBy(l => l.RowNo).ToListAsync();
        if (facLines.Count == 0)
            throw new InvalidOperationException("فاکتور ERP قلمی ندارد.");
        var productIds = facLines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        var entity = new Db.MoadianInvoice
        {
            Kind = fac.Kind == InvoiceKind.SaleReturn ? MoadianInvoiceKind.SaleReturn : MoadianInvoiceKind.Sale,
            InvoiceType = MoadianTaxInvoiceType.Unselected,
            Date = fac.Date,
            FiscalPeriodId = period.Id,
            FacInvoiceId = fac.Id,
            FacInvoiceRef = $"FAC-{fac.Number}",
            Settlement = fac.Settlement.ToString(),
            TaxId = setting.TaxId,
            SellerName = setting.SellerName,
            EconomicCode = setting.EconomicCode,
            BuyerTaxId = fac.Party?.TaxId,
            BuyerName = fac.Party?.Name,
            BuyerAddress = fac.Party?.Address,
            BuyerPostalCode = fac.Party?.PostalCode,
            BuyerPhone = fac.Party?.Mobile ?? fac.Party?.Phone,
            Status = MoadianInvoiceStatus.Draft,
            CreatedBy = user,
            Description = fac.Description
        };

        var row = 1;
        foreach (var l in facLines)
        {
            var product = products.GetValueOrDefault(l.ProductId);
            var rowNo = row++;
            var gross = l.Quantity * l.UnitPrice;
            var taxable = l.Taxable;
            if (taxable == 0 && gross - l.Discount != 0)
                taxable = gross - l.Discount;
            var discount = Math.Clamp(gross - taxable, 0, gross);
            entity.Lines.Add(new Db.MoadianInvoiceLine
            {
                RowNo = rowNo,
                SstId = NullIfEmpty(l.TaxCode) ?? NullIfEmpty(product?.TaxCode) ?? "",
                UnitCode = NullIfEmpty(product?.TaxUnitCode),
                SstTitle = NullIfEmpty(l.Description) ?? NullIfEmpty(product?.Name) ?? $"قلم {rowNo}",
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                Discount = discount,
                VatRate = l.VatRate,
                VatAmount = l.VatAmount,
                Total = taxable + l.VatAmount
            });
        }

        if (fac.ShippingCost > 0)
        {
            entity.Lines.Add(new Db.MoadianInvoiceLine
            {
                RowNo = row,
                SstId = "",
                SstTitle = "هزینه حمل — شناسه مالیاتی را هنگام بازبینی تکمیل کنید",
                Quantity = 1,
                UnitPrice = fac.ShippingCost,
                Discount = 0,
                VatRate = 0,
                VatAmount = 0,
                Total = fac.ShippingCost
            });
        }

        RecalculateTotals(entity);
        await SaveNewInvoiceWithGlobalNumberAsync(entity);
        await LogAsync(entity.Id, MoadianLogAction.Created, $"پیش‌نویس مودیان {entity.Number} از فاکتور فروش ERP {fac.Id} ساخته شد.");
        return ToDto(entity, hasDuplicateNumberAcrossPeriods: false);
    }

    public async Task<MoadianInvoice?> CreateFromFacInvoiceIfEnabledAsync(int facInvoiceId, string? user)
    {
        var setting = await _db.MoadianSettings.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync();
        if (setting?.AutoDraftFromFacInvoices != true) return null;
        var existing = await _db.MoadianInvoices.AsNoTracking().Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.FacInvoiceId == facInvoiceId);
        if (existing is not null) return await GetInvoiceAsync(existing.Id);
        return await CreateFromFacInvoiceAsync(facInvoiceId, user);
    }

    public async Task<MoadianInvoice> CreateFromOperationsTransactionAsync(int transactionId, string? user)
    {
        var txn = await _db.Transactions.AsNoTracking().Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.Id == transactionId)
            ?? throw new InvalidOperationException("سند عملیات یافت نشد.");
        if (txn.Type != TransactionType.Sale)
            throw new InvalidOperationException("از بخش عملیات فقط سند فروش به مودیان تبدیل می‌شود.");
        var existingDraft = await _db.MoadianInvoices.AsNoTracking().Include(i => i.Lines).Include(i => i.FiscalPeriod)
            .FirstOrDefaultAsync(i => i.OperationsTransactionId == transactionId);
        if (existingDraft is not null) return await GetInvoiceAsync(existingDraft.Id);
        if (txn.Lines.Count == 0)
            throw new InvalidOperationException("سند فروش عملیات قلمی ندارد.");

        var setting = await _db.MoadianSettings.OrderBy(x => x.Id).FirstOrDefaultAsync()
                      ?? throw new InvalidOperationException("ابتدا تنظیمات مودیان را تکمیل کنید.");
        if (string.IsNullOrWhiteSpace(setting.TaxId))
            throw new InvalidOperationException("شناسه مالیاتی فروشنده در تنظیمات ثبت نشده است.");
        var period = await GetOrCreatePeriodAsync(txn.Date);
        if (period.IsClosed)
            throw new InvalidOperationException("دوره مالیاتی مربوط به تاریخ سند عملیات بسته است.");


        var productIds = txn.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var party = txn.PartyId is > 0 ? await _db.Parties.AsNoTracking().FirstOrDefaultAsync(p => p.Id == txn.PartyId) : null;

        var entity = new Db.MoadianInvoice
        {
            Kind = MoadianInvoiceKind.Sale,
            InvoiceType = MoadianTaxInvoiceType.Unselected,
            Date = txn.Date,
            FiscalPeriodId = period.Id,
            OperationsTransactionId = txn.Id,
            FacInvoiceRef = $"OPS-{txn.Number}",
            Settlement = txn.PaymentMethod.ToString(),
            TaxId = setting.TaxId,
            SellerName = setting.SellerName,
            EconomicCode = setting.EconomicCode,
            BuyerTaxId = party?.TaxId,
            BuyerName = party?.Name,
            BuyerAddress = party?.Address,
            BuyerPostalCode = party?.PostalCode,
            BuyerPhone = party?.Mobile ?? party?.Phone,
            Status = MoadianInvoiceStatus.Draft,
            CreatedBy = user,
            Description = txn.Description
        };

        var row = 1;
        foreach (var l in txn.Lines.OrderBy(l => l.Id))
        {
            var product = products.GetValueOrDefault(l.ProductId);
            var vatRate = product?.IsVatIncluded == true ? product.VatRate : 0m;
            var taxable = l.Quantity * l.Price;
            var vat = Math.Round(taxable * vatRate / 100m, 2, MidpointRounding.AwayFromZero);
            entity.Lines.Add(new Db.MoadianInvoiceLine
            {
                RowNo = row++,
                SstId = product?.TaxCode ?? "",
                UnitCode = product?.TaxUnitCode,
                SstTitle = product?.Name ?? l.Description ?? $"قلم {row}",
                Quantity = l.Quantity,
                UnitPrice = l.Price,
                Discount = 0,
                VatRate = vatRate,
                VatAmount = vat,
                Total = taxable + vat
            });
        }
        RecalculateTotals(entity);
        await SaveNewInvoiceWithGlobalNumberAsync(entity);
        await LogAsync(entity.Id, MoadianLogAction.Created, $"پیش‌نویس مودیان {entity.Number} از سند فروش عملیات {txn.Id} ساخته شد.");
        return ToDto(entity, hasDuplicateNumberAcrossPeriods: false);
    }

    public async Task<MoadianInvoice?> CreateFromOperationsTransactionIfEnabledAsync(int transactionId, string? user)
    {
        var setting = await _db.MoadianSettings.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync();
        if (setting?.AutoDraftFromOperations != true) return null;
        var existing = await _db.MoadianInvoices.AsNoTracking().Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.OperationsTransactionId == transactionId);
        if (existing is not null) return await GetInvoiceAsync(existing.Id);
        return await CreateFromOperationsTransactionAsync(transactionId, user);
    }


    public async Task<MoadianInvoice> CreateManualAsync(MoadianInvoiceRequest req, string? user)
    {
        var setting = await _db.MoadianSettings.OrderBy(x => x.Id).FirstOrDefaultAsync()
                      ?? throw new InvalidOperationException("ابتدا تنظیمات مودیان را تکمیل کنید.");
        EnsureSellerIdentity(setting);
        var period = await _db.MoadianFiscalPeriods.FindAsync(req.FiscalPeriodId)
                     ?? throw new InvalidOperationException("دوره مالیاتی انتخاب نشده است.");
        if (period.IsClosed)
            throw new InvalidOperationException("دوره مالیاتی انتخابی بسته است.");
        var issueDateError = MoadianInvoiceRules.ValidateIssueDate(req.Date);
        if (issueDateError is not null) throw new InvalidOperationException(issueDateError);
        EnsurePeriodMatchesDate(req.Date, period);
        ValidateInvoiceType(req.InvoiceType);
        ValidateInvoiceSelection(req);
        var lines = BuildDraftLines(req.Lines);
        if (lines.Count == 0)
            throw new InvalidOperationException("پیش‌نویس باید حداقل یک قلم با تعداد مثبت داشته باشد.");


        var entity = new Db.MoadianInvoice
        {
            Kind = req.Kind,
            InvoiceType = req.InvoiceType,
            InvoicePattern = req.InvoicePattern,
            InvoiceSubject = req.InvoiceSubject,
            ReferenceTaxId = NullIfEmpty(req.ReferenceTaxId),
            Date = req.Date,
            FiscalPeriodId = period.Id,
            Settlement = req.Settlement,
            TaxId = setting.TaxId,
            SellerName = setting.SellerName,
            EconomicCode = setting.EconomicCode,
            BuyerTaxId = NullIfEmpty(req.BuyerTaxId),
            BuyerName = NullIfEmpty(req.BuyerName),
            BuyerAddress = NullIfEmpty(req.BuyerAddress),
            BuyerPostalCode = NullIfEmpty(req.BuyerPostalCode),
            BuyerPhone = NullIfEmpty(req.BuyerPhone),
            Status = MoadianInvoiceStatus.Draft,
            CreatedBy = user,
            Description = req.Description,
            Lines = lines
        };
        RecalculateTotals(entity);
        await SaveNewInvoiceWithGlobalNumberAsync(entity);
        await LogAsync(entity.Id, MoadianLogAction.Created, $"پیش‌نویس مودیان {entity.Number} به‌صورت دستی ساخته شد.");
        return ToDto(entity, hasDuplicateNumberAcrossPeriods: false);
    }

    public async Task<MoadianInvoice> UpdateDraftAsync(int id, MoadianInvoiceRequest req, string? user)
    {
        var entity = await _db.MoadianInvoices.Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == id)
            ?? throw new InvalidOperationException("پیش‌نویس مودیان یافت نشد.");
        if (entity.Status != MoadianInvoiceStatus.Draft)
            throw new InvalidOperationException("فقط پیش‌نویسِ ارسال‌نشده قابل ویرایش است.");
        var setting = await _db.MoadianSettings.OrderBy(x => x.Id).FirstOrDefaultAsync()
                      ?? throw new InvalidOperationException("ابتدا تنظیمات مودیان را تکمیل کنید.");
        EnsureSellerIdentity(setting);
        var period = await _db.MoadianFiscalPeriods.FindAsync(req.FiscalPeriodId)
                     ?? throw new InvalidOperationException("دوره مالیاتی انتخاب نشده است.");
        if (period.IsClosed) throw new InvalidOperationException("دوره مالیاتی انتخابی بسته است.");
        var issueDateError = MoadianInvoiceRules.ValidateIssueDate(req.Date);
        if (issueDateError is not null) throw new InvalidOperationException(issueDateError);
        EnsurePeriodMatchesDate(req.Date, period);
        ValidateInvoiceType(req.InvoiceType);
        ValidateInvoiceSelection(req);
        var lines = BuildDraftLines(req.Lines);
        if (lines.Count == 0) throw new InvalidOperationException("پیش‌نویس باید حداقل یک قلم با تعداد مثبت داشته باشد.");

        // Number is now the internal serial candidate, not a per-period document number.
        // Moving an unsent draft between periods must therefore not change its Number.
        if (entity.FiscalPeriodId != period.Id)
        {
            var numberConflict = await _db.MoadianInvoices.AsNoTracking()
                .AnyAsync(i => i.FiscalPeriodId == period.Id && i.Number == entity.Number && i.Id != entity.Id);
            if (numberConflict)
                throw new InvalidOperationException("شمارهٔ تاریخی این پیش‌نویس با فاکتور دیگری در دورهٔ مقصد برخورد دارد؛ برای حفظ شماره‌های ثبت‌شده، دوره را تغییر ندهید تا سوابق به‌صورت دستی بازبینی شوند.");
        }
        entity.FiscalPeriodId = period.Id;
        entity.Kind = req.Kind;
        entity.InvoiceType = req.InvoiceType;
        entity.InvoicePattern = req.InvoicePattern;
        entity.InvoiceSubject = req.InvoiceSubject;
        entity.ReferenceTaxId = NullIfEmpty(req.ReferenceTaxId);
        entity.Date = req.Date;
        entity.Settlement = NullIfEmpty(req.Settlement);
        entity.BuyerTaxId = NullIfEmpty(req.BuyerTaxId);
        entity.BuyerName = NullIfEmpty(req.BuyerName);
        entity.BuyerAddress = NullIfEmpty(req.BuyerAddress);
        entity.BuyerPostalCode = NullIfEmpty(req.BuyerPostalCode);
        entity.BuyerPhone = NullIfEmpty(req.BuyerPhone);
        entity.Description = NullIfEmpty(req.Description);
        _db.MoadianInvoiceLines.RemoveRange(entity.Lines);
        entity.Lines = lines;
        RecalculateTotals(entity);
        await _db.SaveChangesAsync();
        await LogAsync(entity.Id, MoadianLogAction.Updated, $"پیش‌نویس مودیان {entity.Number} ویرایش شد.");
        return await GetInvoiceAsync(entity.Id);
    }

    public async Task DeleteInvoiceAsync(int id)
    {
        var entity = await _db.MoadianInvoices.Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == id)
            ?? throw new InvalidOperationException("فاکتور الکترونیکی یافت نشد.");
        if (entity.Status is MoadianInvoiceStatus.Sent or MoadianInvoiceStatus.Sending)
            throw new InvalidOperationException("فاکتور ارسال‌شده به سامانه قابل حذف نیست؛ آن را ابطال کنید.");
        _db.MoadianInvoiceLines.RemoveRange(entity.Lines);
        _db.MoadianInvoices.Remove(entity);
        await _db.SaveChangesAsync();
    }

    // =====================================================================
    // صف و چرخه ارسال
    // =====================================================================
    public Task<MoadianInvoice> EnqueueAsync(int id, string? user)
        => Task.FromException<MoadianInvoice>(new InvalidOperationException(
            "ارسال مودیان عمداً غیرفعال است؛ پیش‌نویس‌ها در صف ارسال قرار نمی‌گیرند."));

    public Task<MoadianInvoice> CancelAsync(int id, string? user)
        => Task.FromException<MoadianInvoice>(new InvalidOperationException(
            "ابطال سامانه‌ای تا راه‌اندازی و تأیید رسمی اتصال غیرفعال است. برای حذف پیش‌نویس از گزینهٔ حذف استفاده کنید."));

    public Task<MoadianInvoice> RetryAsync(int id, string? user)
        => Task.FromException<MoadianInvoice>(new InvalidOperationException(
            "ارسال مجدد مودیان عمداً غیرفعال است تا ارسال رسمی تأیید شود."));

    /// <summary>ارسال صف عمداً تا تأیید رسمی غیرفعال است.</summary>
    public Task<int> SendPendingAsync()
        => Task.FromException<int>(new InvalidOperationException(
            "ارسال مودیان عمداً غیرفعال است تا قالب رسمی جاری، امضا و دسترسی مجاز تأیید شوند."));

    /// <summary>نمایش خلاصهٔ محلی پیش‌نویس؛ این JSON قالب رسمی یا قابل ارسال نیست.</summary>
    public async Task<MoadianPayloadResult> BuildPayloadAsync(int id)
    {
        var inv = await _db.MoadianInvoices.AsNoTracking().Include(i => i.FiscalPeriod).Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == id)
            ?? throw new InvalidOperationException("فاکتور الکترونیکی یافت نشد.");
        var localSnapshot = new
        {
            previewOnly = true,
            warning = "Local review snapshot only; not an official Moadian payload.",
            invoice = new
            {
                inv.Id,
                inv.Number,
                kind = inv.Kind.ToString(),
                invoiceType = (int)inv.InvoiceType,
                invoiceTypeName = inv.InvoiceType.ToString(),
                invoicePattern = (int)inv.InvoicePattern,
                invoicePatternName = MoadianInvoiceRules.PatternTitle(inv.InvoicePattern),
                invoiceSubject = (int)inv.InvoiceSubject,
                invoiceSubjectName = MoadianInvoiceRules.SubjectTitle(inv.InvoiceSubject),
                inv.ReferenceTaxId,
                date = inv.Date.ToString("O"),
                fiscalPeriod = inv.FiscalPeriod is null ? null : $"{inv.FiscalPeriod.Year}/{inv.FiscalPeriod.Month:00}",
                status = inv.Status.ToString(),
                inv.Description
            },
            seller = new { inv.TaxId, inv.SellerName, inv.EconomicCode },
            buyer = new { inv.BuyerTaxId, inv.BuyerName, inv.BuyerAddress, inv.BuyerPostalCode, inv.BuyerPhone },
            lines = inv.Lines.OrderBy(l => l.RowNo).Select(l => new
            {
                l.RowNo,
                l.SstId,
                l.SstTitle,
                l.UnitCode,
                l.Quantity,
                l.UnitPrice,
                l.Discount,
                l.VatRate,
                l.VatAmount,
                l.Total
            }).ToList(),
            totals = new { inv.TotalGross, inv.TotalDiscount, inv.TotalTaxable, inv.TotalVat, inv.TotalNet }
        };
        return new MoadianPayloadResult
        {
            InvoiceId = inv.Id,
            Json = JsonSerializer.Serialize(localSnapshot, new JsonSerializerOptions { WriteIndented = true }),
            Signature = null,
            Uid = null,
            Warning = "این نمایش، خلاصهٔ محلی برای بازبینی است؛ قالب رسمی مودیان، امضا یا شناسهٔ ارسال نیست و نباید ارسال شود."
        };
    }

    // =====================================================================
    // لاگ / CPC / داشبورد
    // =====================================================================
    public async Task<List<MoadianLog>> GetLogsAsync(int? invoiceId = null, Paging.Request? pagination = null)
    {
        var q = _db.MoadianLogs.AsNoTracking().Include(l => l.Invoice).AsQueryable();
        if (invoiceId is not null) q = q.Where(l => l.InvoiceId == invoiceId);
        return await q.OrderByDescending(l => l.Id)
            .Select(l => new MoadianLog
            {
                Id = l.Id, InvoiceId = l.InvoiceId,
                InvoiceNumber = l.Invoice != null ? l.Invoice.Number : 0,
                Action = l.Action, Message = l.Message, Detail = l.Detail, CreatedAt = l.CreatedAt
            })
            .ToPageListAsync(pagination, defaultCap: 300);
    }

    public async Task<List<MoadianCpc>> GetCpcAsync(string? search = null, Paging.Request? pagination = null)
    {
        var q = _db.MoadianCpcList.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(c => c.Code.Contains(search) || c.Title.Contains(search));
        return await q.OrderBy(c => c.Code).ThenBy(x => x.Id)
            .Select(c => new MoadianCpc
            {
                Id = c.Id, Code = c.Code, Title = c.Title, EnTitle = c.EnTitle,
                IsActive = c.IsActive, Unit = c.Unit
            }).ToPageListAsync(pagination, defaultCap: 500);
    }

    public async Task<MoadianCpc> SaveCpcAsync(MoadianCpc dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.Title))
            throw new InvalidOperationException("کد و شرح شناسه کالا/خدمت الزامی است.");
        Db.MoadianCpc entity;
        if (dto.Id == 0)
        {
            entity = new Db.MoadianCpc();
            _db.MoadianCpcList.Add(entity);
        }
        else
        {
            entity = await _db.MoadianCpcList.FindAsync(dto.Id)
                     ?? throw new InvalidOperationException("شناسه یافت نشد.");
        }
        entity.Code = dto.Code.Trim();
        entity.Title = dto.Title.Trim();
        entity.EnTitle = dto.EnTitle;
        entity.IsActive = dto.IsActive;
        entity.Unit = dto.Unit;
        await _db.SaveChangesAsync();
        dto.Id = entity.Id;
        return dto;
    }

    public async Task DeleteCpcAsync(int id)
    {
        var entity = await _db.MoadianCpcList.FindAsync(id)
                     ?? throw new InvalidOperationException("شناسه یافت نشد.");
        _db.MoadianCpcList.Remove(entity);
        await _db.SaveChangesAsync();
    }

    public async Task<MoadianDashboard> GetDashboardAsync()
    {
        var setting = await _db.MoadianSettings.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync();
        var invoices = await _db.MoadianInvoices.AsNoTracking()
            .Where(i => i.Status != MoadianInvoiceStatus.Voided)
            .ToListAsync();

        var recentIssues = await _db.MoadianInvoices.AsNoTracking()
            .Where(i => i.Status == MoadianInvoiceStatus.Failed || i.Status == MoadianInvoiceStatus.Returned)
            .OrderByDescending(i => i.Id).Take(8).ToListAsync();

        return new MoadianDashboard
        {
            TotalCount = invoices.Count,
            DraftCount = invoices.Count(i => i.Status == MoadianInvoiceStatus.Draft),
            QueuedCount = invoices.Count(i => i.Status == MoadianInvoiceStatus.Queued),
            SentCount = invoices.Count(i => i.Status == MoadianInvoiceStatus.Sent),
            FailedCount = invoices.Count(i => i.Status == MoadianInvoiceStatus.Failed),
            ReturnedCount = invoices.Count(i => i.Status == MoadianInvoiceStatus.Returned),
            NetTotal = invoices.Sum(i => i.TotalNet),
            VatTotal = invoices.Sum(i => i.TotalVat),
            IsSimulation = setting is null || string.IsNullOrWhiteSpace(setting.BaseUrl),
            TaxId = setting?.TaxId,
            SellerName = setting?.SellerName,
            AutoSendMinutes = setting?.SendIntervalMinutes ?? 0,
            RecentIssues = recentIssues.Select(i => ToDto(i)).ToList()
        };
    }

    // =====================================================================
    private static void EnsureSellerIdentity(Db.MoadianSetting setting)
    {
        if (string.IsNullOrWhiteSpace(setting.TaxId))
            throw new InvalidOperationException("شناسه مالیاتی فروشنده در تنظیمات ثبت نشده است.");
    }

    private static void EnsurePeriodMatchesDate(DateTime date, Db.MoadianFiscalPeriod period)
    {
        var faDate = PersianDate.FromGregorian(date);
        if (faDate.Year != period.Year || faDate.Month != period.Month)
            throw new InvalidOperationException("تاریخ صورتحساب با دوره مالیاتی انتخاب‌شده هم‌خوانی ندارد.");
    }

    private static void ValidateInvoiceType(MoadianTaxInvoiceType type)
    {
        if (type is not (MoadianTaxInvoiceType.Type1 or MoadianTaxInvoiceType.Type2))
            throw new InvalidOperationException("نوع صورتحساب باید ۱ یا ۲ باشد.");
    }

    private static void ValidateInvoiceSelection(MoadianInvoiceRequest request)
    {
        var error = MoadianInvoiceRules.ValidateSelection(
            request.InvoiceType,
            request.InvoicePattern,
            request.InvoiceSubject,
            request.ReferenceTaxId);
        if (error is not null)
            throw new InvalidOperationException(error);
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<Db.MoadianInvoiceLine> BuildDraftLines(List<Inventory.Shared.Dtos.MoadianInvoiceLine>? requestLines)
    {
        if (requestLines is null) return new List<Db.MoadianInvoiceLine>();
        if (requestLines.Any(x => x.Quantity < 0))
            throw new InvalidOperationException("تعداد قلم نمی‌تواند منفی باشد.");
        var rows = new List<Db.MoadianInvoiceLine>();
        foreach (var line in requestLines.Where(x => x.Quantity > 0))
        {
            if (line.UnitPrice < 0 || line.Discount < 0 || line.VatRate is < 0 or > 100)
                throw new InvalidOperationException($"مقادیر قلم {line.RowNo} معتبر نیست.");
            if ((line.SstId?.Length ?? 0) > 40 || (line.UnitCode?.Length ?? 0) > 20 || (line.SstTitle?.Length ?? 0) > 400)
                throw new InvalidOperationException($"شناسه، واحد یا شرح قلم {line.RowNo} بیش از حد مجاز است.");
            var gross = line.Quantity * line.UnitPrice;
            if (line.Discount > gross)
                throw new InvalidOperationException($"تخفیف قلم {line.RowNo} از مبلغ ناخالص بیشتر است.");
            var taxable = gross - line.Discount;
            var vat = Math.Round(taxable * line.VatRate / 100m, 2, MidpointRounding.AwayFromZero);
            rows.Add(new Db.MoadianInvoiceLine
            {
                RowNo = rows.Count + 1,
                SstId = NullIfEmpty(line.SstId) ?? "",
                UnitCode = NullIfEmpty(line.UnitCode),
                SstTitle = NullIfEmpty(line.SstTitle) ?? "",
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Discount = line.Discount,
                VatRate = line.VatRate,
                VatAmount = vat,
                Total = taxable + vat
            });
        }
        return rows;
    }

    private static void RecalculateTotals(Db.MoadianInvoice invoice)
    {
        invoice.TotalGross = invoice.Lines.Sum(x => x.Quantity * x.UnitPrice);
        invoice.TotalDiscount = invoice.Lines.Sum(x => x.Discount);
        invoice.TotalTaxable = invoice.Lines.Sum(x => x.Taxable);
        invoice.TotalVat = invoice.Lines.Sum(x => x.VatAmount);
        invoice.TotalNet = invoice.TotalTaxable + invoice.TotalVat;
    }

    private string? ProtectSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.StartsWith("dp:v1:", StringComparison.Ordinal)) return trimmed;
        return "dp:v1:" + _secrets.Protect(trimmed);
    }

    private string? ProtectLegacySecretIfNeeded(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.StartsWith("dp:v1:", StringComparison.Ordinal) ? value : ProtectSecret(value);
    }

    private string? UnprotectSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!value.StartsWith("dp:v1:", StringComparison.Ordinal)) return value; // legacy value until next settings save
        try { return _secrets.Unprotect(value[6..]); }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unable to decrypt stored Moadian credential.");
            throw new InvalidOperationException("کلیدهای رمزگذاری تنظیمات مودیان در دسترس نیستند؛ از پشتیبان سامانه کمک بگیرید.");
        }
    }

    private static string? MaskSecret(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : "*****تنظیم‌شده*****";

    /// <summary>
    /// تخصیص Number سراسری به پیش‌نویس جدید، با حفظ سوابق تکراریِ دوره‌ای موجود.
    /// تراکنش Serializable همراه با ایندکس Number از تخصیص هم‌زمانِ یک عدد به دو دوره جلوگیری می‌کند.
    /// این فقط تخصیص سریال داخلی است؛ فرمت inno و تولید taxid تا تأیید مستند رسمی مسدود می‌مانند.
    /// </summary>
    private async Task SaveNewInvoiceWithGlobalNumberAsync(Db.MoadianInvoice invoice)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var maximum = await _db.MoadianInvoices
            .Select(i => (int?)i.Number)
            .MaxAsync() ?? 0;
        if (maximum < 0) maximum = 0;
        if (maximum == int.MaxValue)
            throw new InvalidOperationException("ظرفیت شماره داخلی صورتحساب تمام شده است.");

        invoice.Number = NextSerialNumber(maximum, _settings.SerialStartNumber);
        _db.MoadianInvoices.Add(invoice);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private async Task<Db.MoadianFiscalPeriod> GetOrCreatePeriodAsync(DateTime dt)
    {
        var fa = PersianDate.FromGregorian(dt);
        var period = await _db.MoadianFiscalPeriods
            .FirstOrDefaultAsync(p => p.Year == fa.Year && p.Month == fa.Month);
        if (period is null)
        {
            period = new Db.MoadianFiscalPeriod { Year = fa.Year, Month = fa.Month };
            _db.MoadianFiscalPeriods.Add(period);
            await _db.SaveChangesAsync();
        }
        return period;
    }

    private async Task LogAsync(int invoiceId, MoadianLogAction action, string message, string? detail = null)
    {
        _db.MoadianLogs.Add(new Db.MoadianLog
        {
            InvoiceId = invoiceId, Action = action, Message = message, Detail = detail
        });
        await _db.SaveChangesAsync();
    }

    private static MoadianSetting ToDto(Db.MoadianSetting s) => new()
    {
        Id = s.Id, TaxId = s.TaxId, FiscalMemoryId = s.FiscalMemoryId, EconomicCode = s.EconomicCode, SellerName = s.SellerName,
        SellerAddress = s.SellerAddress, SellerPostalCode = s.SellerPostalCode, SellerPhone = s.SellerPhone,
        TaxCardToken = s.TaxCardToken, BaseUrl = s.BaseUrl, PrivateKeyPem = s.PrivateKeyPem,
        SigningCertificatePem = s.SigningCertificatePem, PublicKeyPem = s.PublicKeyPem,
        AutoSend = false, AutoDraftFromOperations = s.AutoDraftFromOperations,
        AutoDraftFromFacInvoices = s.AutoDraftFromFacInvoices, SendIntervalMinutes = s.SendIntervalMinutes,
        DefaultVatRate = s.DefaultVatRate, LoggingEnabled = s.LoggingEnabled, IsActive = s.IsActive,
        Notes = s.Notes
    };

    private static MoadianInvoice ToDto(Db.MoadianInvoice i, bool? hasDuplicateNumberAcrossPeriods = null)
    {
        var (inqSuccess, inqErrors) = ParseInquiryJson(i.InquiryDataJson);
        return new()
        {
        Id = i.Id, Number = i.Number, HasDuplicateNumberAcrossPeriods = hasDuplicateNumberAcrossPeriods,
        Kind = i.Kind, InvoiceType = i.InvoiceType,
        InvoicePattern = i.InvoicePattern, InvoiceSubject = i.InvoiceSubject, ReferenceTaxId = i.ReferenceTaxId, Date = i.Date,
        FiscalPeriodId = i.FiscalPeriodId,
        FiscalPeriodTitle = i.FiscalPeriod != null ? $"{i.FiscalPeriod.Year}/{i.FiscalPeriod.Month:00}" : null,
        FiscalYearId = i.FiscalYearId, YearSerial = i.YearSerial, DocumentNumber = i.DocumentNumber,
        PayType = i.PayType, PayTypeTitle = MoadianPayTypes.Title(i.PayType),
        ServiceProviderId = i.ServiceProviderId, TaxId22 = i.Taxid,
        LastInquiryAt = i.LastInquiryAt, LastInquiryStatus = i.LastInquiryStatus,
        InquiryErrors = inqErrors, InquirySuccess = inqSuccess,
        LastSendPayload = i.LastSendPayload,
        FacInvoiceId = i.FacInvoiceId, OperationsTransactionId = i.OperationsTransactionId,
        FacInvoiceRef = i.FacInvoiceRef, Settlement = i.Settlement,
        TaxId = i.TaxId, SellerName = i.SellerName, EconomicCode = i.EconomicCode,
        BuyerTaxId = i.BuyerTaxId, BuyerName = i.BuyerName, BuyerAddress = i.BuyerAddress,
        BuyerPostalCode = i.BuyerPostalCode, BuyerPhone = i.BuyerPhone,
        TotalGross = i.TotalGross, TotalDiscount = i.TotalDiscount, TotalTaxable = i.TotalTaxable,
        TotalVat = i.TotalVat, TotalNet = i.TotalNet,
        Status = i.Status, ReferenceId = i.ReferenceId, TrackingId = i.TrackingId,
        ErrorCode = i.ErrorCode, ErrorMessage = i.ErrorMessage, Attempts = i.Attempts,
        QueuedAt = i.QueuedAt, SendAt = i.SendAt, ReturnedAt = i.ReturnedAt,
        CreatedBy = i.CreatedBy, CreatedAt = i.CreatedAt, Description = i.Description,
        Lines = i.Lines.OrderBy(l => l.RowNo).Select(l => new MoadianInvoiceLine
        {
            Id = l.Id, RowNo = l.RowNo, SstId = l.SstId, UnitCode = l.UnitCode, SstTitle = l.SstTitle,
            Quantity = l.Quantity, UnitPrice = l.UnitPrice, Discount = l.Discount,
            VatRate = l.VatRate, VatAmount = l.VatAmount, Total = l.Total
        }).ToList()
    };
    }

    /// <summary>پاسخ خام استعلام (JSON) را به فهرست خطاها/پیام‌های نمایش‌داده‌شدنی تبدیل می‌کند.</summary>
    private static (bool? Success, List<MoadianInquiryError> Errors) ParseInquiryJson(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
            return (null, new List<MoadianInquiryError>());
        var (success, _, _, errors) = MoadianSubmissionService.ParseInquiryData(dataJson);
        return (success, errors.Select(x => new MoadianInquiryError { Code = x.Code, Message = x.Msg }).ToList());
    }
}
