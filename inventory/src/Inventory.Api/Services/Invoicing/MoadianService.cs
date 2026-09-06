using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Db = Inventory.Api.Data;

namespace Inventory.Api.Services.Invoicing;

/// <summary>
/// سرویس سامانه مودیان (فاکتور الکترونیکی):
///   • تنظیمات اتصال (شماره مالیاتی، توکن، کلید امضا، آدرس سرویس)
///   • دوره‌های مالیاتی ماهانه
///   • ساخت فاکتور الکترونیکی از فاکتور فروش/خرید سیستم یا ثبت دستی
///   • ساخت Payload استاندارد مودیان + امضای RSA (در صورت وجود کلید)
///   • چرخه‌ی ارسال (صف ← ارسال ← پاسخ سامانه/خطا/برگشت) + شبیه‌سازی برای تست
///   • شناسه‌های کالا/خدمت (CPC) و لاگ کامل
/// </summary>
public interface IMoadianService
{
    Task<MoadianSetting> GetSettingAsync();
    Task<MoadianSetting> SaveSettingAsync(MoadianSetting dto);

    Task<List<MoadianFiscalPeriod>> GetPeriodsAsync();
    Task<MoadianFiscalPeriod> SavePeriodAsync(MoadianFiscalPeriod dto);
    Task DeletePeriodAsync(int id);

    Task<List<MoadianInvoice>> GetInvoicesAsync(int? periodId = null, MoadianInvoiceStatus? status = null, string? search = null);
    Task<MoadianInvoice> GetInvoiceAsync(int id);
    Task<MoadianInvoice> CreateFromFacInvoiceAsync(int facInvoiceId, string? user);
    Task<MoadianInvoice> CreateManualAsync(MoadianInvoiceRequest req, string? user);
    Task DeleteInvoiceAsync(int id);

    Task<MoadianInvoice> EnqueueAsync(int id, string? user);
    Task<MoadianInvoice> CancelAsync(int id, string? user);
    Task<MoadianInvoice> RetryAsync(int id, string? user);

    Task<int> SendPendingAsync();
    Task<MoadianPayloadResult> BuildPayloadAsync(int id);

    Task<List<MoadianLog>> GetLogsAsync(int? invoiceId = null);
    Task<List<MoadianCpc>> GetCpcAsync(string? search = null);
    Task<MoadianCpc> SaveCpcAsync(MoadianCpc dto);
    Task DeleteCpcAsync(int id);

    Task<MoadianDashboard> GetDashboardAsync();
}

public class MoadianService : IMoadianService
{
    private readonly Db.AppDbContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<MoadianService> _log;

    public MoadianService(Db.AppDbContext db, IHttpClientFactory httpFactory, ILogger<MoadianService> log)
    {
        _db = db; _httpFactory = httpFactory; _log = log;
    }

    // =====================================================================
    // تنظیمات
    // =====================================================================
    public async Task<MoadianSetting> GetSettingAsync()
    {
        var s = await _db.MoadianSettings.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync()
                ?? new Db.MoadianSetting();
        var dto = ToDto(s);
        // هرگز کلید خصوصی را در پاسخ عمومی برنگردان (فقط برای ویرایش UI)
        dto.PrivateKeyPem = string.IsNullOrWhiteSpace(s.PrivateKeyPem) ? null : "*****" + s.PrivateKeyPem.Length + "*****";
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

        // اگر کلید خصوصی از UI دست‌نخورده برگشت، مقدار قبلی حفظ شود
        if (dto.PrivateKeyPem is null || dto.PrivateKeyPem.StartsWith("*****"))
            dto.PrivateKeyPem = entity.PrivateKeyPem;

        entity.TaxId = dto.TaxId.Trim();
        entity.EconomicCode = dto.EconomicCode;
        entity.SellerName = dto.SellerName;
        entity.SellerAddress = dto.SellerAddress;
        entity.SellerPostalCode = dto.SellerPostalCode;
        entity.SellerPhone = dto.SellerPhone;
        entity.TaxCardToken = dto.TaxCardToken;
        entity.BaseUrl = string.IsNullOrWhiteSpace(dto.BaseUrl) ? null : dto.BaseUrl.Trim();
        entity.PrivateKeyPem = string.IsNullOrWhiteSpace(dto.PrivateKeyPem) ? null : dto.PrivateKeyPem.Trim();
        entity.PublicKeyPem = dto.PublicKeyPem;
        entity.AutoSend = dto.AutoSend;
        entity.SendIntervalMinutes = Math.Max(1, dto.SendIntervalMinutes);
        entity.DefaultVatRate = Math.Clamp(dto.DefaultVatRate, 0, 100);
        entity.LoggingEnabled = dto.LoggingEnabled;
        entity.IsActive = dto.IsActive;
        entity.Notes = dto.Notes;
        entity.UpdatedAt = DateTime.Now;

        await _db.SaveChangesAsync();
        return ToDto(entity);
    }

    // =====================================================================
    // دوره‌های مالیاتی
    // =====================================================================
    public async Task<List<MoadianFiscalPeriod>> GetPeriodsAsync()
    {
        var periods = await _db.MoadianFiscalPeriods.AsNoTracking()
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month).ToListAsync();

        // توجه: تجمیع decimal روی SQLite ترجمه نمی‌شود؛ ابتدا داده را می‌آوریم و تجمیع در حافظه انجام می‌شود
        var stats = (await _db.MoadianInvoices.AsNoTracking()
                .Where(i => i.Status != MoadianInvoiceStatus.Voided)
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
    public async Task<List<MoadianInvoice>> GetInvoicesAsync(int? periodId = null, MoadianInvoiceStatus? status = null, string? search = null)
    {
        var q = _db.MoadianInvoices.AsNoTracking()
            .Include(i => i.FiscalPeriod).Include(i => i.Lines).AsQueryable();
        if (periodId is not null) q = q.Where(i => i.FiscalPeriodId == periodId);
        if (status is not null) q = q.Where(i => i.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(i => i.Number.ToString().Contains(search) || (i.BuyerName != null && i.BuyerName.Contains(search)));

        return (await q.OrderByDescending(i => i.Id).ToListAsync()).Select(ToDto).ToList();
    }

    public async Task<MoadianInvoice> GetInvoiceAsync(int id)
    {
        var entity = await _db.MoadianInvoices.AsNoTracking()
            .Include(i => i.FiscalPeriod).Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == id)
            ?? throw new InvalidOperationException("فاکتور الکترونیکی یافت نشد.");
        return ToDto(entity);
    }

    public async Task<MoadianInvoice> CreateFromFacInvoiceAsync(int facInvoiceId, string? user)
    {
        var fac = await _db.FacInvoices.AsNoTracking()
            .Include(f => f.Party)
            .FirstOrDefaultAsync(f => f.Id == facInvoiceId)
            ?? throw new InvalidOperationException("فاکتور داخلی یافت نشد.");
        // اقلام جداگانه خوانده می‌شوند تا کالاهای حذف‌شده/بدون کالا حذف نشوند
        // (ThenInclude(Product) با join داخلی، سطرهای بدون کالای معتبر را حذف می‌کرد)
        var facLines = await _db.FacInvoiceLines.AsNoTracking()
            .Where(l => l.InvoiceId == fac.Id).OrderBy(l => l.RowNo).ToListAsync();
        if (fac.Status != InvoiceStatus.Confirmed)
            throw new InvalidOperationException("فقط فاکتور «قطعی» قابل تبدیل به فاکتور الکترونیکی است.");
        if (fac.Kind != InvoiceKind.Sale && fac.Kind != InvoiceKind.SaleReturn && fac.Kind != InvoiceKind.Purchase)
            throw new InvalidOperationException("این نوع فاکتور برای مودیان پشتیبانی نمی‌شود.");

        var already = await _db.MoadianInvoices.AnyAsync(i => i.FacInvoiceId == facInvoiceId);
        if (already)
            throw new InvalidOperationException("این فاکتور قبلاً به فاکتور الکترونیکی تبدیل شده است.");

        var setting = await _db.MoadianSettings.OrderBy(x => x.Id).FirstOrDefaultAsync()
                      ?? throw new InvalidOperationException("ابتدا تنظیمات مودیان را تکمیل کنید.");
        if (string.IsNullOrWhiteSpace(setting.TaxId))
            throw new InvalidOperationException("شماره مالیاتی فروشنده در تنظیمات ثبت نشده است.");

        var period = await GetOrCreatePeriodAsync(DateTime.Now);
        if (period.IsClosed)
            throw new InvalidOperationException("دوره مالیاتی جاری بسته است؛ ابتدا دوره را باز کنید.");

        var nextNo = await _db.MoadianInvoices.Where(i => i.FiscalPeriodId == period.Id)
            .Select(i => (int?)i.Number).MaxAsync() ?? 0;

        var kind = fac.Kind switch
        {
            InvoiceKind.Sale => MoadianInvoiceKind.Sale,
            InvoiceKind.SaleReturn => MoadianInvoiceKind.SaleReturn,
            InvoiceKind.Purchase => MoadianInvoiceKind.Purchase,
            _ => MoadianInvoiceKind.Sale
        };

        var entity = new Db.MoadianInvoice
        {
            Number = nextNo + 1,
            Kind = kind,
            Date = fac.Date,
            FiscalPeriodId = period.Id,
            FacInvoiceId = fac.Id,
            FacInvoiceRef = $"FAC-{fac.Number}",
            Settlement = fac.Settlement.ToString(),
            TaxId = setting.TaxId,
            SellerName = setting.SellerName,
            EconomicCode = setting.EconomicCode,
            BuyerTaxId = null, // بخش‌بندی تأمین‌کننده/مشتری در MoadianCpc از صفحه مودیان تکمیل می‌شود
            BuyerName = fac.Party?.Name,
            BuyerAddress = fac.Party?.Address,
            BuyerPhone = fac.Party?.Mobile ?? fac.Party?.Phone,
            Status = MoadianInvoiceStatus.Draft,
            CreatedBy = user,
            Description = fac.Description
        };

        var row = 1;
        foreach (var l in facLines)
        {
            var vatRate = l.VatRate > 0 ? l.VatRate : setting.DefaultVatRate;
            var taxable = l.Taxable > 0 ? l.Taxable : (l.Quantity * l.UnitPrice - l.Discount);
            var vat = l.VatAmount > 0 ? l.VatAmount : Math.Round(taxable * vatRate / 100m, 2);
            entity.Lines.Add(new Db.MoadianInvoiceLine
            {
                RowNo = row++,
                SstId = l.TaxCode ?? $"GENERAL-{row:000}",
                SstTitle = l.Description ?? $"قلم {row}",
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                Discount = l.Discount,
                VatRate = vatRate,
                VatAmount = vat,
                Total = taxable + vat
            });
        }

        entity.TotalGross = entity.Lines.Sum(x => x.Quantity * x.UnitPrice);
        entity.TotalDiscount = fac.TotalLineDiscount + fac.InvoiceDiscount;
        entity.TotalTaxable = entity.Lines.Sum(x => x.Taxable);
        entity.TotalVat = entity.Lines.Sum(x => x.VatAmount);
        entity.TotalNet = entity.Lines.Sum(x => x.Total);

        _db.MoadianInvoices.Add(entity);
        await _db.SaveChangesAsync();
        await LogAsync(entity.Id, MoadianLogAction.Created, $"فاکتور الکترونیکی {entity.Number} از فاکتور داخلی {fac.Id} ساخته شد.");

        return ToDto(entity);
    }

    public async Task<MoadianInvoice> CreateManualAsync(MoadianInvoiceRequest req, string? user)
    {
        if (req.Lines.Count == 0 || req.Lines.All(l => l.Quantity <= 0))
            throw new InvalidOperationException("فاکتور الکترونیکی حداقل یک قلم معتبر لازم دارد.");

        var setting = await _db.MoadianSettings.OrderBy(x => x.Id).FirstOrDefaultAsync()
                      ?? throw new InvalidOperationException("ابتدا تنظیمات مودیان را تکمیل کنید.");
        if (string.IsNullOrWhiteSpace(setting.TaxId))
            throw new InvalidOperationException("شماره مالیاتی فروشنده در تنظیمات ثبت نشده است.");

        var period = await _db.MoadianFiscalPeriods.FindAsync(req.FiscalPeriodId)
                     ?? throw new InvalidOperationException("دوره مالیاتی انتخاب نشده است.");
        if (period.IsClosed)
            throw new InvalidOperationException("دوره مالیاتی انتخابی بسته است.");
        if (req.Date.Year < 2020)
            req.Date = DateTime.Now;

        var nextNo = await _db.MoadianInvoices.Where(i => i.FiscalPeriodId == period.Id)
            .Select(i => (int?)i.Number).MaxAsync() ?? 0;

        var entity = new Db.MoadianInvoice
        {
            Number = nextNo + 1,
            Kind = req.Kind,
            Date = req.Date.Date,
            FiscalPeriodId = period.Id,
            Settlement = req.Settlement,
            TaxId = setting.TaxId,
            SellerName = setting.SellerName,
            EconomicCode = setting.EconomicCode,
            BuyerTaxId = req.BuyerTaxId,
            BuyerName = req.BuyerName,
            BuyerAddress = req.BuyerAddress,
            BuyerPostalCode = req.BuyerPostalCode,
            BuyerPhone = req.BuyerPhone,
            Status = MoadianInvoiceStatus.Draft,
            CreatedBy = user,
            Description = req.Description
        };

        var row = 1;
        foreach (var l in req.Lines)
        {
            var vatRate = l.VatRate > 0 ? l.VatRate : setting.DefaultVatRate;
            // مبالغ از سمت سرور محاسبه می‌شوند تا داده ارسالی به سامانه همیشه سازگار باشد
            var taxable = (l.Quantity * l.UnitPrice) - l.Discount;
            var vat = l.VatAmount > 0 ? l.VatAmount : Math.Round(taxable * vatRate / 100m, 2);
            entity.Lines.Add(new Db.MoadianInvoiceLine
            {
                RowNo = row++,
                SstId = string.IsNullOrWhiteSpace(l.SstId) ? $"GENERAL-{row:000}" : l.SstId.Trim(),
                SstTitle = l.SstTitle,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                Discount = l.Discount,
                VatRate = vatRate,
                VatAmount = vat,
                Total = taxable + vat
            });
        }

        entity.TotalGross = entity.Lines.Sum(x => x.Quantity * x.UnitPrice);
        entity.TotalDiscount = entity.Lines.Sum(x => x.Discount);
        entity.TotalTaxable = entity.Lines.Sum(x => x.Taxable);
        entity.TotalVat = entity.Lines.Sum(x => x.VatAmount);
        entity.TotalNet = entity.TotalTaxable + entity.TotalVat;

        _db.MoadianInvoices.Add(entity);
        await _db.SaveChangesAsync();
        await LogAsync(entity.Id, MoadianLogAction.Created, $"فاکتور الکترونیکی {entity.Number} به‌صورت دستی ثبت شد.");
        return ToDto(entity);
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
    public async Task<MoadianInvoice> EnqueueAsync(int id, string? user)
    {
        var entity = await _db.MoadianInvoices.FindAsync(id)
                     ?? throw new InvalidOperationException("فاکتور الکترونیکی یافت نشد.");
        if (entity.Status != MoadianInvoiceStatus.Draft && entity.Status != MoadianInvoiceStatus.Returned
            && entity.Status != MoadianInvoiceStatus.Failed)
            throw new InvalidOperationException("فقط فاکتور پیش‌نویس/برگشتی/ناموفق قابل ارسال به صف است.");

        entity.Status = MoadianInvoiceStatus.Queued;
        entity.QueuedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        await LogAsync(entity.Id, MoadianLogAction.Enqueued, "به صف ارسال سامانه اضافه شد.");
        return ToDto(entity);
    }

    public async Task<MoadianInvoice> CancelAsync(int id, string? user)
    {
        var entity = await _db.MoadianInvoices.FindAsync(id)
                     ?? throw new InvalidOperationException("فاکتور الکترونیکی یافت نشد.");
        if (entity.Status == MoadianInvoiceStatus.Voided)
            return ToDto(entity);
        if (entity.Status == MoadianInvoiceStatus.Sent)
            throw new InvalidOperationException("برای ابطال فاکتور ارسال‌شده، از سامانه مودیان «ابطال فاکتور» ثبت کنید.");
        if (entity.Status == MoadianInvoiceStatus.Sending)
            throw new InvalidOperationException("فاکتور در حال ارسال است؛ چند لحظه بعد دوباره تلاش کنید.");

        entity.Status = MoadianInvoiceStatus.Voided;
        await _db.SaveChangesAsync();
        await LogAsync(entity.Id, MoadianLogAction.Voided, "فاکتور ابطال شد.");
        return ToDto(entity);
    }

    public async Task<MoadianInvoice> RetryAsync(int id, string? user)
    {
        var entity = await _db.MoadianInvoices.FindAsync(id)
                     ?? throw new InvalidOperationException("فاکتور الکترونیکی یافت نشد.");
        if (entity.Status is not (MoadianInvoiceStatus.Failed or MoadianInvoiceStatus.Returned))
            throw new InvalidOperationException("فقط فاکتور ناموفق/برگشتی قابل ارسال مجدد است.");
        entity.Status = MoadianInvoiceStatus.Queued;
        entity.QueuedAt = DateTime.Now;
        entity.ErrorCode = null;
        entity.ErrorMessage = null;
        await _db.SaveChangesAsync();
        await LogAsync(entity.Id, MoadianLogAction.Enqueued, $"ارسال مجدد (تلاش {entity.Attempts + 1}).");
        return ToDto(entity);
    }

    /// <summary>ارسال همه‌ی فاکتورهای صف به سامانه (یا شبیه‌سازی). تعداد ارسال‌شده را برمی‌گرداند.</summary>
    public async Task<int> SendPendingAsync()
    {
        var setting = await _db.MoadianSettings.OrderBy(x => x.Id).FirstOrDefaultAsync();
        var queued = await _db.MoadianInvoices
            .Where(i => i.Status == MoadianInvoiceStatus.Queued)
            .OrderBy(i => i.Id).ToListAsync();

        int sent = 0;
        foreach (var invoice in queued)
        {
            invoice.Status = MoadianInvoiceStatus.Sending;
            invoice.Attempts++;
            await _db.SaveChangesAsync();

            try
            {
                var result = await BuildPayloadAsync(invoice.Id);

                // ===== شبیه‌سازی (بدون آدرس سرویس) =====
                if (setting is null || string.IsNullOrWhiteSpace(setting.BaseUrl))
                {
                    invoice.ReferenceId = SimulatedUid(invoice);
                    invoice.TrackingId = "SIM-" + Guid.NewGuid().ToString("N")[..12].ToUpper();
                    invoice.Status = MoadianInvoiceStatus.Sent;
                    invoice.SendAt = DateTime.Now;
                    invoice.ErrorCode = null;
                    invoice.ErrorMessage = null;
                    await _db.SaveChangesAsync();
                    await LogAsync(invoice.Id, MoadianLogAction.Sent,
                        $"ارسال آزمایشی (شبیه‌سازی) موفق — شماره پیگیری {invoice.TrackingId}");
                    sent++;
                    continue;
                }

                // ===== ارسال واقعی به سرویس مودیان =====
                if (string.IsNullOrWhiteSpace(setting.TaxCardToken))
                    throw new InvalidOperationException("شناسه کارتابل (توکن) در تنظیمات ثبت نشده است.");

                var client = _httpFactory.CreateClient("moadian");
                var req = new HttpRequestMessage(HttpMethod.Post, setting.BaseUrl)
                {
                    Content = new StringContent(result.Json, Encoding.UTF8, "application/json")
                };
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {setting.TaxCardToken}");
                if (!string.IsNullOrWhiteSpace(result.Signature))
                    req.Headers.TryAddWithoutValidation("X-Signature", result.Signature);

                var resp = await client.SendAsync(req);
                var body = await resp.Content.ReadAsStringAsync();

                // پاسخ سامانه: {"code": ..., "message": ..., "data": {...}}
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                var root = doc.RootElement;
                var code = 1;
                if (root.TryGetProperty("code", out var c))
                {
                    if (c.ValueKind == JsonValueKind.Number) code = c.GetInt32();
                    else if (c.ValueKind == JsonValueKind.String && int.TryParse(c.GetString(), out var ci)) code = ci;
                }
                else if (resp.IsSuccessStatusCode) code = 0;
                var msg = root.TryGetProperty("message", out var m) ? m.GetString() : resp.StatusCode.ToString();

                if (code == 0 && resp.IsSuccessStatusCode)
                {
                    invoice.ReferenceId = root.TryGetProperty("uid", out var uid) ? uid.GetString()
                        : (root.TryGetProperty("data", out var data) && data.TryGetProperty("uid", out var uid2) ? uid2.GetString() : null)
                          ?? invoice.ReferenceId;
                    invoice.TrackingId = root.TryGetProperty("referenceNumber", out var rn) ? rn.GetString()
                        : (root.TryGetProperty("data", out var data2) && data2.TryGetProperty("referenceNumber", out var rn2) ? rn2.GetString() : null)
                          ?? invoice.TrackingId;
                    invoice.Status = MoadianInvoiceStatus.Sent;
                    invoice.SendAt = DateTime.Now;
                    invoice.ErrorCode = null;
                    invoice.ErrorMessage = null;
                    await _db.SaveChangesAsync();
                    await LogAsync(invoice.Id, MoadianLogAction.Sent, $"ارسال موفق — پیگیری {invoice.TrackingId ?? "—"}", body);
                    sent++;
                }
                else
                {
                    invoice.Status = MoadianInvoiceStatus.Returned;
                    invoice.ReturnedAt = DateTime.Now;
                    invoice.ErrorCode = code.ToString();
                    invoice.ErrorMessage = msg;
                    await _db.SaveChangesAsync();
                    await LogAsync(invoice.Id, MoadianLogAction.Returned, $"برگشت از سامانه (کد {code}): {msg}", body);
                }
            }
            catch (Exception ex)
            {
                invoice.Status = MoadianInvoiceStatus.Failed;
                invoice.ErrorCode = "CLIENT";
                invoice.ErrorMessage = ex.Message;
                await _db.SaveChangesAsync();
                await LogAsync(invoice.Id, MoadianLogAction.Failed, $"خطا در ارسال: {ex.Message}");
                _log.LogError(ex, "Moadian send failed for invoice {Id}", invoice.Id);
            }
        }
        return sent;
    }

    /// <summary>ساخت Payload استاندارد مودیان برای یک فاکتور + امضا (در صورت وجود کلید).</summary>
    public async Task<MoadianPayloadResult> BuildPayloadAsync(int id)
    {
        var inv = await _db.MoadianInvoices.AsNoTracking().Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == id)
            ?? throw new InvalidOperationException("فاکتور الکترونیکی یافت نشد.");
        var setting = await _db.MoadianSettings.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync();

        var header = new Dictionary<string, object?>
        {
            ["taxid"] = inv.TaxId,
            ["indatim"] = new DateTimeOffset(inv.Date).ToUnixTimeMilliseconds(),
            ["inp"] = (int)inv.Kind, // الگوی مالیاتی ۱..۵ (فروش/فروش برگشتی/خرید/خرید برگشتی/پیش‌فاکتور)
            ["inno"] = inv.Number,
            ["inct"] = 1,   // نوع شناسه: ملی
            ["inty"] = inv.Kind == MoadianInvoiceKind.Purchase ? 3 : 1, // نوع فاکتور (کالا/خدمت)
            ["ins"] = 1,    // شماره نسخه
            ["tins"] = new[] { inv.TaxId },
            ["bpc"] = 1,
            ["bbc"] = 0,
            ["billid"] = Guid.NewGuid().ToString("N"),
            ["tpr"] = new Dictionary<string, object?>
            {
                ["tins"] = inv.TaxId,
                ["nam"] = inv.SellerName,
                ["adr"] = setting?.SellerAddress,
                ["pcn"] = setting?.SellerPostalCode,
                ["cpn"] = setting?.SellerPhone,
                ["tel"] = setting?.SellerPhone
            },
            ["setm"] = 1,
            ["flg"] = (int)(inv.Kind is MoadianInvoiceKind.SaleReturn or MoadianInvoiceKind.PurchaseReturn ? 1 : 0)
        };

        // خریدار — برای فروش الزامی است
        if (!string.IsNullOrWhiteSpace(inv.BuyerTaxId) || !string.IsNullOrWhiteSpace(inv.BuyerName))
        {
            header["cust"] = new Dictionary<string, object?>
            {
                ["tins"] = inv.BuyerTaxId,
                ["nam"] = inv.BuyerName,
                ["adr"] = inv.BuyerAddress,
                ["pcn"] = inv.BuyerPostalCode,
                ["cpn"] = inv.BuyerPhone,
                ["tel"] = inv.BuyerPhone,
                ["cno"] = 1
            };
        }

        var body = inv.Lines.OrderBy(l => l.RowNo).Select(l => (object)new Dictionary<string, object?>
        {
            ["sstid"] = l.SstId,
            ["sstt"] = l.SstTitle,
            ["am"] = l.Quantity,
            ["fee"] = l.Quantity * l.UnitPrice,
            ["di"] = l.Discount,
            ["vra"] = l.VatRate,
            ["vam"] = l.VatAmount,
            ["tsstam"] = l.Taxable + l.VatAmount
        }).ToList();

        var payload = new Dictionary<string, object?>
        {
            ["header"] = header,
            ["body"] = body,
            ["payments"] = new List<Dictionary<string, object?>>
            {
                new() { ["i"] = 1, ["t"] = inv.TotalNet }
            }
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        // امضای RSA (PKCS#1 v1.5 SHA-256) — در صورت موجود بودن کلید خصوصی
        string? signature = null;
        string? warning = null;
        var pem = setting?.PrivateKeyPem;
        if (!string.IsNullOrWhiteSpace(pem) && !pem.StartsWith("*****"))
        {
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(pem);
                signature = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(json), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            }
            catch (Exception ex)
            {
                warning = $"امضا ناموفق بود (کلید PEM معتبر نیست؟): {ex.Message}";
            }
        }
        else if (inv.Status != MoadianInvoiceStatus.Draft && setting is not null && !string.IsNullOrWhiteSpace(setting.BaseUrl))
        {
            warning = "کلید خصوصی امضا تنظیم نشده است — در حالت عملیاتی ارسال بدون امضا پذیرفته نمی‌شود.";
        }

        return new MoadianPayloadResult
        {
            InvoiceId = inv.Id,
            Json = json,
            Signature = signature,
            Uid = inv.ReferenceId,
            Warning = warning
        };
    }

    // =====================================================================
    // لاگ / CPC / داشبورد
    // =====================================================================
    public async Task<List<MoadianLog>> GetLogsAsync(int? invoiceId = null)
    {
        var q = _db.MoadianLogs.AsNoTracking().Include(l => l.Invoice).AsQueryable();
        if (invoiceId is not null) q = q.Where(l => l.InvoiceId == invoiceId);
        return await q.OrderByDescending(l => l.Id).Take(300)
            .Select(l => new MoadianLog
            {
                Id = l.Id, InvoiceId = l.InvoiceId,
                InvoiceNumber = l.Invoice != null ? l.Invoice.Number : 0,
                Action = l.Action, Message = l.Message, Detail = l.Detail, CreatedAt = l.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<List<MoadianCpc>> GetCpcAsync(string? search = null)
    {
        var q = _db.MoadianCpcList.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(c => c.Code.Contains(search) || c.Title.Contains(search));
        return await q.OrderBy(c => c.Code).Take(500)
            .Select(c => new MoadianCpc
            {
                Id = c.Id, Code = c.Code, Title = c.Title, EnTitle = c.EnTitle,
                IsActive = c.IsActive, Unit = c.Unit
            }).ToListAsync();
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
            RecentIssues = recentIssues.Select(ToDto).ToList()
        };
    }

    // =====================================================================
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

    private static string SimulatedUid(Db.MoadianInvoice inv)
    {
        // UID آزمایشی (در حالت عملیاتی از پاسخ سامانه خوانده می‌شود)
        var fa = PersianDate.FromGregorian(inv.Date);
        return $"SIM-{inv.Number:D6}-{fa.Year}{fa.Month:D2}";
    }

    private static MoadianSetting ToDto(Db.MoadianSetting s) => new()
    {
        Id = s.Id, TaxId = s.TaxId, EconomicCode = s.EconomicCode, SellerName = s.SellerName,
        SellerAddress = s.SellerAddress, SellerPostalCode = s.SellerPostalCode, SellerPhone = s.SellerPhone,
        TaxCardToken = s.TaxCardToken, BaseUrl = s.BaseUrl, PrivateKeyPem = s.PrivateKeyPem,
        PublicKeyPem = s.PublicKeyPem, AutoSend = s.AutoSend, SendIntervalMinutes = s.SendIntervalMinutes,
        DefaultVatRate = s.DefaultVatRate, LoggingEnabled = s.LoggingEnabled, IsActive = s.IsActive,
        Notes = s.Notes
    };

    private static MoadianInvoice ToDto(Db.MoadianInvoice i) => new()
    {
        Id = i.Id, Number = i.Number, Kind = i.Kind, Date = i.Date,
        FiscalPeriodId = i.FiscalPeriodId,
        FiscalPeriodTitle = i.FiscalPeriod != null ? $"{i.FiscalPeriod.Year}/{i.FiscalPeriod.Month:00}" : null,
        FacInvoiceId = i.FacInvoiceId, FacInvoiceRef = i.FacInvoiceRef, Settlement = i.Settlement,
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
            Id = l.Id, RowNo = l.RowNo, SstId = l.SstId, SstTitle = l.SstTitle,
            Quantity = l.Quantity, UnitPrice = l.UnitPrice, Discount = l.Discount,
            VatRate = l.VatRate, VatAmount = l.VatAmount, Total = l.Total
        }).ToList()
    };
}
