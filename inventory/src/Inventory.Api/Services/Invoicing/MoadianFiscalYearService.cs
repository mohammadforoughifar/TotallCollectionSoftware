using Db = Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Invoicing;

public interface IMoadianFiscalYearService
{
    Task<List<MoadianFiscalYearDto>> GetYearsAsync(int? providerId = null);
    Task<MoadianFiscalYearDetailDto> GetYearAsync(int id);
    Task<MoadianFiscalYearDetailDto> SaveYearAsync(MoadianFiscalYearRequest request);
    Task<MoadianFiscalYearDetailDto> SetYearClosedAsync(int id, bool closed);
    Task DeleteYearAsync(int id);
    Task<MoadianFiscalPeriodDto> SetPeriodClosedAsync(int periodId, bool closed);
    /// <summary>پیش‌نمایش شمارهٔ صورتحساب بعدی (سریال سراسری + شمارهٔ سند سالانه).</summary>
    Task<MoadianNextNumberDto> GetNextNumberAsync(int fiscalYearId, DateTime? date, int? periodId, int? providerId = null);
}

/// <summary>
/// «تعریف سال مالی» مودیان: سال شمسی + ساخت خودکار ۱۲ دورهٔ ماهانه، باز/بسته کردن سال و دوره‌ها،
/// و پیش‌نمایش شمارهٔ صورتحساب بعدی. شماره‌گذاری قطعی هنگام ذخیرهٔ فاکتور انجام می‌شود.
/// </summary>
public sealed class MoadianFiscalYearService : IMoadianFiscalYearService
{
    private const int MinYear = 1300;
    private const int MaxYear = 1500;

    private readonly Db.AppDbContext _db;

    public MoadianFiscalYearService(Db.AppDbContext db) => _db = db;

    public async Task<List<MoadianFiscalYearDto>> GetYearsAsync(int? providerId = null)
    {
        var years = await _db.MoadianFiscalYears.AsNoTracking()
            .Where(y => y.ServiceProviderId == (providerId ?? 0))
            .OrderByDescending(y => y.Year).ToListAsync();
        if (years.Count == 0) return new List<MoadianFiscalYearDto>();

        var periodStats = await _db.MoadianFiscalPeriods.AsNoTracking()
            .GroupBy(p => p.Year)
            .Select(g => new
            {
                Year = g.Key,
                Count = g.Count(),
                Closed = g.Count(p => p.IsClosed)
            })
            .ToListAsync();

        var invoiceStats = await _db.MoadianInvoices.AsNoTracking()
            .Where(i => i.FiscalYearId != null)
            .GroupBy(i => i.FiscalYearId!.Value)
            .Select(g => new
            {
                FiscalYearId = g.Key,
                Count = g.Count(),
                LastSerial = g.Max(i => i.YearSerial),
                LastDate = (DateTime?)g.Max(i => i.Date)
            })
            .ToListAsync();
        var moneyByYear = await LoadMoneyTotalsAsync(byFiscalYear: true);

        return years.Select(y =>
        {
            var periods = periodStats.FirstOrDefault(p => p.Year == y.Year);
            var invoices = invoiceStats.FirstOrDefault(i => i.FiscalYearId == y.Id);
            moneyByYear.TryGetValue(y.Id, out var money);
            return new MoadianFiscalYearDto
            {
                Id = y.Id,
                ServiceProviderId = y.ServiceProviderId,
                Year = y.Year,
                StartDate = y.StartDate,
                EndDate = y.EndDate,
                IsClosed = y.IsClosed,
                Notes = y.Notes,
                CreatedAt = y.CreatedAt,
                PeriodCount = periods?.Count ?? 0,
                ClosedPeriodCount = periods?.Closed ?? 0,
                InvoiceCount = invoices?.Count ?? 0,
                NetTotal = money.Net,
                VatTotal = money.Vat,
                LastYearSerial = invoices?.LastSerial ?? 0,
                LastInvoiceDate = invoices?.LastDate
            };
        }).ToList();
    }

    public async Task<MoadianFiscalYearDetailDto> GetYearAsync(int id)
    {
        var year = await _db.MoadianFiscalYears.AsNoTracking()
            .FirstOrDefaultAsync(y => y.Id == id)
            ?? throw new InvalidOperationException("سال مالی یافت نشد.");

        return new MoadianFiscalYearDetailDto
        {
            Year = (await GetYearsAsync()).FirstOrDefault(y => y.Id == year.Id) ?? new MoadianFiscalYearDto
            {
                Id = year.Id, Year = year.Year, StartDate = year.StartDate, EndDate = year.EndDate,
                IsClosed = year.IsClosed, Notes = year.Notes, CreatedAt = year.CreatedAt
            },
            Periods = await GetPeriodsAsync(year.Year, year.ServiceProviderId)
        };
    }

    public async Task<MoadianFiscalYearDetailDto> SaveYearAsync(MoadianFiscalYearRequest request)
    {
        if (request.Year is < MinYear or > MaxYear)
            throw new InvalidOperationException($"سال مالی باید بین {MinYear} و {MaxYear} باشد.");

        var start = PersianDate.ToGregorian(request.Year, 1, 1);
        var end = PersianDate.ToGregorian(request.Year, 12, PersianDate.DaysInMonth(request.Year, 12));

        var duplicate = await _db.MoadianFiscalYears.AnyAsync(y => y.Year == request.Year && y.ServiceProviderId == (request.ServiceProviderId) && y.Id != request.Id);
        if (duplicate)
            throw new InvalidOperationException($"سال مالی {request.Year} قبلاً تعریف شده است.");

        Db.MoadianFiscalYear entity;
        if (request.Id > 0)
        {
            entity = await _db.MoadianFiscalYears.FirstOrDefaultAsync(y => y.Id == request.Id)
                     ?? throw new InvalidOperationException("سال مالی یافت نشد.");
            var invoiceCount = await _db.MoadianInvoices.CountAsync(i => i.FiscalYearId == entity.Id);
            if (invoiceCount > 0 && entity.Year != request.Year)
                throw new InvalidOperationException("این سال مالی صورتحساب دارد؛ شمارهٔ سال قابل تغییر نیست.");
            if (invoiceCount > 0)
            {
                var periodsOutsideNewRange = await _db.MoadianFiscalPeriods.CountAsync(p =>
                    p.Year == entity.Year && (p.Month < 1 || p.Month > 12));
                if (periodsOutsideNewRange > 0)
                    throw new InvalidOperationException("دوره‌های این سال با بازهٔ جدید سازگار نیستند.");
            }
        }
        else
        {
            entity = new Db.MoadianFiscalYear { Year = request.Year, ServiceProviderId = request.ServiceProviderId, CreatedAt = DateTime.Now };
            _db.MoadianFiscalYears.Add(entity);
        }

        entity.Year = request.Year;
        entity.ServiceProviderId = request.ServiceProviderId;
        entity.StartDate = start;
        entity.EndDate = end;
        entity.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (request.IsClosed) entity.IsClosed = true;
        await _db.SaveChangesAsync();

        if (request.CreateMonthlyPeriods)
            await EnsureMonthlyPeriodsAsync(request.Year, providerId: entity.ServiceProviderId, closed: entity.IsClosed);

        if (entity.IsClosed)
            await SetPeriodsClosedAsync(request.Year, providerId: entity.ServiceProviderId, closed: true);

        return await GetYearAsync(entity.Id);
    }

    public async Task<MoadianFiscalYearDetailDto> SetYearClosedAsync(int id, bool closed)
    {
        var year = await _db.MoadianFiscalYears.FirstOrDefaultAsync(y => y.Id == id)
                   ?? throw new InvalidOperationException("سال مالی یافت نشد.");

        if (closed && !year.IsClosed)
        {
            var openDrafts = await _db.MoadianInvoices.CountAsync(i =>
                i.FiscalYearId == year.Id && i.Status == MoadianInvoiceStatus.Draft);
            if (openDrafts > 0)
                throw new InvalidOperationException($"این سال {openDrafts} پیش‌نویس ثبت‌شده دارد؛ ابتدا آن‌ها را تعیین تکلیف یا حذف کنید.");
        }

        year.IsClosed = closed;
        await _db.SaveChangesAsync();
        await SetPeriodsClosedAsync(year.Year, providerId: year.ServiceProviderId, closed);
        return await GetYearAsync(year.Id);
    }

    public async Task DeleteYearAsync(int id)
    {
        var year = await _db.MoadianFiscalYears.FirstOrDefaultAsync(y => y.Id == id)
                   ?? throw new InvalidOperationException("سال مالی یافت نشد.");

        if (await _db.MoadianInvoices.AnyAsync(i => i.FiscalYearId == year.Id))
            throw new InvalidOperationException("این سال مالی صورتحساب دارد و قابل حذف نیست.");

        var periods = await _db.MoadianFiscalPeriods.Where(p => p.Year == year.Year).ToListAsync();
        if (periods.Count > 0)
        {
            var periodIds = periods.Select(p => p.Id).ToList();
            if (await _db.MoadianInvoices.AnyAsync(i => periodIds.Contains(i.FiscalPeriodId)))
                throw new InvalidOperationException("دوره‌های این سال مالی صورتحساب دارند و قابل حذف نیستند.");
            _db.MoadianFiscalPeriods.RemoveRange(periods);
        }

        _db.MoadianFiscalYears.Remove(year);
        await _db.SaveChangesAsync();
    }

    public async Task<MoadianFiscalPeriodDto> SetPeriodClosedAsync(int periodId, bool closed)
    {
        var period = await _db.MoadianFiscalPeriods.FirstOrDefaultAsync(p => p.Id == periodId)
                     ?? throw new InvalidOperationException("دورهٔ مالیاتی یافت نشد.");

        if (closed)
        {
            var year = await _db.MoadianFiscalYears.AsNoTracking()
                .FirstOrDefaultAsync(y => y.Year == period.Year);
            if (year is not null && year.IsClosed)
                throw new InvalidOperationException("سال مالی بسته است؛ ابتدا سال را باز کنید.");
        }

        period.IsClosed = closed;
        await _db.SaveChangesAsync();
        return ToPeriodDto(period, 0, 0, 0);
    }

    public async Task<MoadianNextNumberDto> GetNextNumberAsync(int fiscalYearId, DateTime? date, int? periodId, int? providerId = null)
    {
        IQueryable<int?> numberQuery = _db.MoadianInvoices.AsNoTracking().Select(i => (int?)i.Number);
        if (providerId is > 0)
            numberQuery = _db.MoadianInvoices.AsNoTracking().Where(i => i.ServiceProviderId == providerId.Value).Select(i => (int?)i.Number);
        var result = new MoadianNextNumberDto
        {
            NextGlobalNumber = (await numberQuery.MaxAsync() ?? 0) + 1
        };

        var invoiceDate = date ?? DateTime.Now;
        var fa = PersianDate.FromGregorian(invoiceDate);

        Db.MoadianFiscalYear? year = null;
        if (fiscalYearId > 0)
        {
            year = await _db.MoadianFiscalYears.AsNoTracking().FirstOrDefaultAsync(y => y.Id == fiscalYearId);
            if (year is null)
            {
                result.CanCreate = false;
                result.Warning = "سال مالی انتخاب‌شده یافت نشد.";
                return result;
            }
            if (year.Year != fa.Year)
            {
                result.CanCreate = false;
                result.Warning = $"تاریخ انتخابی در سال مالی {year.Year} نیست (سال تاریخ: {fa.Year}). تاریخ را اصلاح کنید یا سال مالی درست را انتخاب کنید.";
            }
            if (year.IsClosed)
            {
                result.CanCreate = false;
                result.Warning = $"سال مالی {year.Year} بسته است؛ ثبت صورتحساب جدید در آن مجاز نیست.";
            }
        }
        else
        {
            year = await _db.MoadianFiscalYears.AsNoTracking()
                .FirstOrDefaultAsync(y => y.Year == fa.Year && y.ServiceProviderId == (providerId ?? 0));
            if (year is null)
            {
                result.CanCreate = false;
                result.Warning = $"برای سال {fa.Year} سال مالی تعریف نشده است؛ ابتدا در صفحهٔ «سال مالی مودیان» آن را تعریف کنید.";
            }
            else if (year.IsClosed)
            {
                result.CanCreate = false;
                result.Warning = $"سال مالی {year.Year} بسته است؛ ثبت صورتحساب جدید در آن مجاز نیست.";
            }
        }

        result.FiscalYearId = year?.Id;
        result.FiscalYear = year?.Year ?? fa.Year;

        var serial = 0;
        if (year is not null)
        {
            var serialQuery = _db.MoadianInvoices.AsNoTracking().Where(i => i.FiscalYearId == year.Id);
            if (providerId is not null) serialQuery = serialQuery.Where(i => i.ServiceProviderId == providerId.Value);
            serial = await serialQuery.Select(i => (int?)i.YearSerial).MaxAsync() ?? 0;
        }
        result.NextYearSerial = serial + 1;
        result.DocumentNumber = $"{result.FiscalYear}/{result.NextYearSerial:000000}";

        var periodQuery = _db.MoadianFiscalPeriods.AsNoTracking()
            .Where(p => p.Year == fa.Year && p.Month == fa.Month && p.ServiceProviderId == (providerId ?? 0));
        Db.MoadianFiscalPeriod? period;
        if (periodId is > 0)
        {
            period = await _db.MoadianFiscalPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == periodId);
            if (period is not null && (period.Year != fa.Year || period.Month != fa.Month))
            {
                // دورهٔ دستی انتخاب‌شده با تاریخ نمی‌خواند؛ دورهٔ درست را جایگزین می‌کنیم.
                period = await periodQuery.FirstOrDefaultAsync();
            }
        }
        else
        {
            period = await periodQuery.FirstOrDefaultAsync();
        }

        if (period is not null)
        {
            result.FiscalPeriodId = period.Id;
            result.FiscalPeriodTitle = $"{period.Year}/{period.Month:00}";
            if (period.IsClosed)
            {
                result.CanCreate = false;
                result.Warning = $"دورهٔ {period.Year}/{period.Month:00} بسته است؛ برای این تاریخ نمی‌توان صورتحساب ثبت کرد.";
            }
        }
        else if (result.CanCreate)
        {
            result.CanCreate = false;
            result.Warning = $"دورهٔ ماه {fa.Month:00} سال {fa.Year} تعریف نشده است؛ سال مالی را دوباره ذخیره کنید تا ۱۲ دوره ساخته شود.";
        }

        return result;
    }

    /// <summary>
    /// جمع مبالغ (خالص و مالیات) به تفکیک سال مالی یا دورهٔ مالیاتی.
    /// SQL Server جمع را در سمت پایگاه‌داده انجام می‌دهد؛ SQLite عملگر Sum روی decimal را پشتیبانی
    /// نمی‌کند، بنابراین در آن حالت جمع در حافظه انجام می‌شود.
    /// </summary>
    private async Task<Dictionary<int, (decimal Net, decimal Vat)>> LoadMoneyTotalsAsync(bool byFiscalYear)
    {
        List<(int Key, decimal Net, decimal Vat)> rows = NeedsClientSideMoneySum
            ? (byFiscalYear
                ? (await _db.MoadianInvoices.AsNoTracking()
                        .Where(i => i.FiscalYearId != null)
                        .Select(i => new { Key = i.FiscalYearId!.Value, i.TotalNet, i.TotalVat })
                        .ToListAsync())
                    .Select(x => (x.Key, x.TotalNet, x.TotalVat)).ToList()
                : (await _db.MoadianInvoices.AsNoTracking()
                        .Select(i => new { Key = i.FiscalPeriodId, i.TotalNet, i.TotalVat })
                        .ToListAsync())
                    .Select(x => (x.Key, x.TotalNet, x.TotalVat)).ToList())
            : (byFiscalYear
                ? (await _db.MoadianInvoices.AsNoTracking()
                        .Where(i => i.FiscalYearId != null)
                        .GroupBy(i => i.FiscalYearId!.Value)
                        .Select(g => new { Key = g.Key, Net = g.Sum(i => i.TotalNet), Vat = g.Sum(i => i.TotalVat) })
                        .ToListAsync())
                    .Select(x => (x.Key, x.Net, x.Vat)).ToList()
                : (await _db.MoadianInvoices.AsNoTracking()
                        .GroupBy(i => i.FiscalPeriodId)
                        .Select(g => new { Key = g.Key, Net = g.Sum(i => i.TotalNet), Vat = g.Sum(i => i.TotalVat) })
                        .ToListAsync())
                    .Select(x => (x.Key, x.Net, x.Vat)).ToList());

        return rows.GroupBy(r => r.Key)
            .ToDictionary(g => g.Key, g => (g.Sum(x => x.Net), g.Sum(x => x.Vat)));
    }

    /// <summary>SQLite عملگر Sum روی decimal را در سمت پایگاه‌داده پشتیبانی نمی‌کند.</summary>
    private bool NeedsClientSideMoneySum
        => _db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    private async Task EnsureMonthlyPeriodsAsync(int year, int providerId, bool closed)
    {
        var existing = await _db.MoadianFiscalPeriods.Where(p => p.Year == year && p.ServiceProviderId == providerId).Select(p => p.Month).ToListAsync();
        var missing = Enumerable.Range(1, 12).Except(existing).ToList();
        if (missing.Count == 0) return;

        foreach (var month in missing)
        {
            _db.MoadianFiscalPeriods.Add(new Db.MoadianFiscalPeriod
            {
                Year = year,
                ServiceProviderId = providerId,
                Month = month,
                IsClosed = closed,
                Notes = null
            });
        }
        await _db.SaveChangesAsync();
    }

    private async Task SetPeriodsClosedAsync(int year, int providerId, bool closed)
    {
        var periods = await _db.MoadianFiscalPeriods.Where(p => p.Year == year && p.ServiceProviderId == providerId).ToListAsync();
        if (periods.Count == 0) return;
        foreach (var period in periods) period.IsClosed = closed;
        await _db.SaveChangesAsync();
    }

    private async Task<List<MoadianFiscalPeriodDto>> GetPeriodsAsync(int year, int? providerId = null)
    {
        var periodQuery = _db.MoadianFiscalPeriods.AsNoTracking().Where(p => p.Year == year);
        if (providerId is not null) periodQuery = periodQuery.Where(p => p.ServiceProviderId == providerId.Value);
        var periods = await periodQuery.OrderBy(p => p.Month).ToListAsync();

        var stats = await _db.MoadianInvoices.AsNoTracking()
            .Where(i => i.FiscalPeriod != null && i.FiscalPeriod.Year == year)
            .GroupBy(i => i.FiscalPeriodId)
            .Select(g => new { PeriodId = g.Key, Count = g.Count() })
            .ToListAsync();
        var moneyByPeriod = await LoadMoneyTotalsAsync(byFiscalYear: false);

        return periods.Select(p =>
        {
            var s = stats.FirstOrDefault(x => x.PeriodId == p.Id);
            moneyByPeriod.TryGetValue(p.Id, out var money);
            return ToPeriodDto(p, s?.Count ?? 0, money.Net, money.Vat);
        }).ToList();
    }

    private static MoadianFiscalPeriodDto ToPeriodDto(
        Db.MoadianFiscalPeriod period, int invoiceCount, decimal netTotal, decimal vatTotal)
    {
        var start = PersianDate.ToGregorian(period.Year, period.Month, 1);
        var end = PersianDate.ToGregorian(period.Year, period.Month, PersianDate.DaysInMonth(period.Year, period.Month));
        return new MoadianFiscalPeriodDto
        {
            Id = period.Id,
            Year = period.Year,
            Month = period.Month,
            MonthName = PersianDate.MonthName(period.Month),
            IsClosed = period.IsClosed,
            StartDate = start,
            EndDate = end,
            Notes = period.Notes,
            InvoiceCount = invoiceCount,
            NetTotal = netTotal,
            VatTotal = vatTotal
        };
    }
}
