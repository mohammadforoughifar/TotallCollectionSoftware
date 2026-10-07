using System.Data;
using Db = Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Invoicing;

public interface IMoadianStandaloneInvoiceService
{
    /// <summary>ثبت صورتحساب جدید از فرم مستقل (بدون نیاز به ERP).</summary>
    Task<MoadianInvoice> CreateAsync(MoadianStandaloneInvoiceRequest request, string? user);
}

/// <summary>
/// ثبت صورتحساب مودیان مستقل از ERP: مشتری و کالا/خدمت از «اطلاعات پایه مودیان» تکمیل می‌شود،
/// مبالغ سمت سرور بازمحاسبه می‌شوند، شمارهٔ سند سالانه (سال/سریال) و سریال سراسری تخصیص می‌یابد و
/// سند در وضعیت «پیش‌نویس» ذخیره می‌شود. ارسال به سامانه همچنان غیرفعال است.
/// </summary>
public sealed class MoadianStandaloneInvoiceService : IMoadianStandaloneInvoiceService
{
    private readonly Db.AppDbContext _db;

    public MoadianStandaloneInvoiceService(Db.AppDbContext db) => _db = db;

    public async Task<MoadianInvoice> CreateAsync(MoadianStandaloneInvoiceRequest request, string? user)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ---------- فروشنده ----------
        var seller = await ResolveSellerAsync();

        // ---------- تاریخ و سال/دوره ----------
        if (request.Date == default) throw new InvalidOperationException("تاریخ فاکتور الزامی است.");
        var issueDateError = MoadianInvoiceRules.ValidateIssueDate(request.Date);
        if (issueDateError is not null) throw new InvalidOperationException(issueDateError);

        var fa = PersianDate.FromGregorian(request.Date);
        var yearEntity = request.FiscalYearId > 0
            ? await _db.MoadianFiscalYears.FirstOrDefaultAsync(y => y.Id == request.FiscalYearId)
              ?? throw new InvalidOperationException("سال مالی انتخاب‌شده یافت نشد.")
            : await _db.MoadianFiscalYears.FirstOrDefaultAsync(y => y.Year == fa.Year)
              ?? throw new InvalidOperationException(
                  $"برای سال {fa.Year} سال مالی تعریف نشده است؛ ابتدا در صفحهٔ «سال مالی مودیان» آن را تعریف کنید.");

        if (yearEntity.IsClosed)
            throw new InvalidOperationException($"سال مالی {yearEntity.Year} بسته است؛ ثبت صورتحساب جدید در آن مجاز نیست.");
        if (yearEntity.Year != fa.Year)
            throw new InvalidOperationException(
                $"تاریخ انتخابی در سال مالی {yearEntity.Year} نیست (سال تاریخ: {fa.Year}).");

        var period = await _db.MoadianFiscalPeriods
            .FirstOrDefaultAsync(p => p.Year == fa.Year && p.Month == fa.Month)
            ?? throw new InvalidOperationException(
                $"دورهٔ {fa.Year}/{fa.Month:00} تعریف نشده است؛ سال مالی را دوباره ذخیره کنید تا ۱۲ دوره ساخته شود.");
        if (period.IsClosed)
            throw new InvalidOperationException($"دورهٔ {period.Year}/{period.Month:00} بسته است؛ برای این تاریخ نمی‌توان صورتحساب ثبت کرد.");

        // ---------- خریدار (از اطلاعات پایه، در صورت انتخاب) ----------
        Db.MoadianCustomerProfile? customer = null;
        if (request.CustomerId is > 0)
        {
            customer = await _db.MoadianCustomerProfiles.AsNoTracking()
                .Include(c => c.ServiceProvider)
                .FirstOrDefaultAsync(c => c.Id == request.CustomerId!.Value && !c.IsDeleted)
                ?? throw new InvalidOperationException("مشتری انتخاب‌شده یافت نشد یا غیرفعال است.");
        }

        var buyerTaxId = FirstNonEmpty(request.BuyerTaxId, customer?.NationalID);
        var buyerName = FirstNonEmpty(request.BuyerName, customer?.Name);
        var buyerAddress = FirstNonEmpty(request.BuyerAddress, customer?.Address);
        var buyerPostalCode = FirstNonEmpty(request.BuyerPostalCode, customer?.PostalCode);
        var buyerPhone = FirstNonEmpty(request.BuyerPhone, customer?.Phone);
        if (string.IsNullOrWhiteSpace(buyerName))
            throw new InvalidOperationException("نام خریدار الزامی است (از فهرست مشتریان انتخاب کنید یا دستی وارد کنید).");

        // ---------- اقلام ----------
        var lines = await BuildLinesAsync(request.Lines);
        if (lines.Count == 0)
            throw new InvalidOperationException("صورتحساب باید حداقل یک قلم با تعداد مثبت داشته باشد.");

        // ---------- انتخاب‌های رسمی ----------
        ValidateInvoiceType(request.InvoiceType);
        var selectionError = MoadianInvoiceRules.ValidateSelection(
            request.InvoiceType, request.InvoicePattern, request.Subject, request.ReferenceTaxId);
        if (selectionError is not null) throw new InvalidOperationException(selectionError);

        var invoice = new Db.MoadianInvoice
        {
            Kind = request.Subject == MoadianInvoiceSubject.SaleReturn
                ? MoadianInvoiceKind.SaleReturn
                : MoadianInvoiceKind.Sale,
            InvoiceType = request.InvoiceType,
            InvoicePattern = request.InvoicePattern,
            InvoiceSubject = request.Subject,
            ReferenceTaxId = NullIfEmpty(request.ReferenceTaxId),
            Date = request.Date,
            FiscalPeriodId = period.Id,
            FiscalYearId = yearEntity.Id,
            Settlement = MoadianPayTypes.Title(request.PayType),
            PayType = request.PayType,
            // صورتحساب به خدمات‌دهندهٔ مشتری گره می‌خورد (مبنای لیست تفکیکی و
            // ارسال/استعلام رسمی). مشتری‌های قدیمی ServiceProviderId صفر دارند یا
            // خدمات‌دهندهٔشان حذف شده — در این صورت گره نمی‌خوریم (null)،
            // وگرنه محدودیت referential integrity خطا می‌دهد.
            ServiceProviderId = customer?.ServiceProvider is { IsDeleted: false } sp ? sp.Id : null,
            TaxId = seller.TaxId,
            SellerName = seller.SellerName,
            EconomicCode = seller.EconomicCode,
            BuyerTaxId = buyerTaxId,
            BuyerName = buyerName,
            BuyerAddress = buyerAddress,
            BuyerPostalCode = buyerPostalCode,
            BuyerPhone = buyerPhone,
            Status = MoadianInvoiceStatus.Draft,
            CreatedBy = user,
            Description = NullIfEmpty(request.Description),
            Lines = lines
        };

        RecalculateTotals(invoice);
        await SaveWithNumbersAsync(invoice, yearEntity.Id);

        _db.MoadianLogs.Add(new Db.MoadianLog
        {
            InvoiceId = invoice.Id,
            Action = MoadianLogAction.Created,
            Message = $"صورتحساب {invoice.DocumentNumber} (سریال {invoice.Number}) به‌صورت دستی و مستقل از ERP ثبت شد."
        });
        await _db.SaveChangesAsync();

        return ToDto(invoice);
    }

    /// <summary>تخصیص هم‌زمان و امن سریال سراسری و سریال/شمارهٔ سند سال مالی.</summary>
    private async Task SaveWithNumbersAsync(Db.MoadianInvoice invoice, int fiscalYearId)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var maximum = await _db.MoadianInvoices.Select(i => (int?)i.Number).MaxAsync() ?? 0;
        if (maximum == int.MaxValue)
            throw new InvalidOperationException("ظرفیت شمارهٔ داخلی صورتحساب تمام شده است.");
        invoice.Number = maximum + 1;

        var lastYearSerial = await _db.MoadianInvoices
            .Where(i => i.FiscalYearId == fiscalYearId)
            .Select(i => (int?)i.YearSerial)
            .MaxAsync() ?? 0;
        invoice.YearSerial = lastYearSerial + 1;

        var year = await _db.MoadianFiscalYears.AsNoTracking()
            .Where(y => y.Id == fiscalYearId).Select(y => y.Year).FirstAsync();
        invoice.DocumentNumber = $"{year}/{invoice.YearSerial:000000}";

        _db.MoadianInvoices.Add(invoice);
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private async Task<List<Db.MoadianInvoiceLine>> BuildLinesAsync(List<MoadianStandaloneLineRequest>? requestedLines)
    {
        var result = new List<Db.MoadianInvoiceLine>();
        if (requestedLines is null || requestedLines.Count == 0) return result;

        var missingGoodsIds = requestedLines
            .Where(l => l.Quantity > 0 && l.GoodsOrServiceId is > 0)
            .Select(l => l.GoodsOrServiceId!.Value)
            .Distinct()
            .ToList();
        var goodsLookup = missingGoodsIds.Count == 0
            ? new Dictionary<int, Db.MoadianGoodsOrServiceProfile>()
            : await _db.MoadianGoodsOrServices.AsNoTracking()
                .Where(g => missingGoodsIds.Contains(g.Id))
                .ToDictionaryAsync(g => g.Id);
        var unitNames = await _db.MoadianUnitsOfMeasurement.AsNoTracking()
            .ToDictionaryAsync(u => u.Code, u => u.Name);

        var row = 1;
        foreach (var line in requestedLines)
        {
            if (line.Quantity <= 0) continue;
            if (line.UnitPrice < 0 || line.Discount < 0)
                throw new InvalidOperationException($"قلم {row}: فی و تخفیف نمی‌توانند منفی باشند.");

            goodsLookup.TryGetValue(line.GoodsOrServiceId ?? 0, out var goods);
            if (line.GoodsOrServiceId is > 0 && goods is null)
                throw new InvalidOperationException($"قلم {row}: کالا/خدمت انتخاب‌شده یافت نشد.");

            var unitCode = FirstNonEmpty(line.UnitCode, goods?.UnitOfMeasurement);
            if (string.IsNullOrWhiteSpace(unitCode) && unitNames.Count == 0)
                throw new InvalidOperationException($"قلم {row}: واحد کالا/خدمت الزامی است.");
            unitCode = ResolveUnit(unitCode, unitNames) ?? "";

            var title = FirstNonEmpty(line.SstTitle, goods?.Name);
            if (string.IsNullOrWhiteSpace(title))
                throw new InvalidOperationException($"قلم {row}: نام کالا/خدمت الزامی است.");

            var unitPrice = line.UnitPrice > 0 ? line.UnitPrice : goods?.Price ?? 0;
            if (unitPrice < 0) unitPrice = 0;
            var vatRate = line.VatRate;
            if (vatRate == 0 && goods is not null)
                vatRate = goods.ValueAddedPercentage;

            var gross = Round(line.Quantity * unitPrice);
            var discount = Round(line.Discount);
            if (discount > gross)
                throw new InvalidOperationException($"قلم {row}: تخفیف از مبلغ قبل از تخفیف بیشتر است.");
            var taxable = gross - discount;
            var vat = Round(taxable * vatRate / 100m);

            result.Add(new Db.MoadianInvoiceLine
            {
                RowNo = row,
                SstId = FirstNonEmpty(line.SstId, goods?.UniqueIdentifier) ?? "",
                UnitCode = unitCode,
                SstTitle = title!,
                Quantity = line.Quantity,
                UnitPrice = unitPrice,
                Discount = discount,
                VatRate = vatRate,
                VatAmount = vat,
                Total = taxable + vat
            });
            row++;
        }

        return result;
    }

    private async Task<(string TaxId, string SellerName, string? EconomicCode)> ResolveSellerAsync()
    {
        var setting = await _db.MoadianSettings.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync();
        if (setting is not null && !string.IsNullOrWhiteSpace(setting.TaxId) && !string.IsNullOrWhiteSpace(setting.SellerName))
            return (setting.TaxId.Trim(), setting.SellerName.Trim(), TrimOrNull(setting.EconomicCode));

        // در نبود تنظیمات، هویت فروشنده از «اطلاعات پایه مودیان» (CPInfo) تکمیل می‌شود.
        var provider = await _db.MoadianServiceProviderProfiles.AsNoTracking()
            .Where(p => !p.IsDeleted)
            .OrderBy(p => p.Id)
            .FirstOrDefaultAsync();
        if (provider is not null && !string.IsNullOrWhiteSpace(provider.NationalID))
            return (provider.NationalID.Trim(),
                    FirstNonEmpty(provider.PersianName, provider.EnglishName) ?? provider.NationalID.Trim(),
                    TrimOrNull(provider.EconomicNumber));

        throw new InvalidOperationException(
            "هویت فروشنده مشخص نیست؛ ابتدا در «تنظیمات مودیان» (شناسه مالیاتی و نام فروشنده) یا در «اطلاعات پایه مودیان» یک خدمات‌دهندهٔ فعال تعریف کنید.");
    }

    private static string? ResolveUnit(string? unitCode, Dictionary<string, string> unitNames)
    {
        if (string.IsNullOrWhiteSpace(unitCode)) return null;
        var value = unitCode.Trim();
        if (unitNames.ContainsKey(value)) return value;
        // اگر نام واحد وارد شده باشد، کد متناظر آن بازگردانده می‌شود.
        var byName = unitNames.FirstOrDefault(kv => string.Equals(kv.Value, value, StringComparison.OrdinalIgnoreCase));
        return byName.Key ?? value;
    }

    private static void ValidateInvoiceType(MoadianTaxInvoiceType type)
    {
        if (type is not (MoadianTaxInvoiceType.Type1 or MoadianTaxInvoiceType.Type2))
            throw new InvalidOperationException("نوع صورتحساب باید نوع ۱ یا نوع ۲ باشد.");
    }

    private static void RecalculateTotals(Db.MoadianInvoice invoice)
    {
        invoice.TotalGross = Round(invoice.Lines.Sum(l => l.Quantity * l.UnitPrice));
        invoice.TotalDiscount = Round(invoice.Lines.Sum(l => l.Discount));
        invoice.TotalTaxable = Round(invoice.Lines.Sum(l => l.Taxable));
        invoice.TotalVat = Round(invoice.Lines.Sum(l => l.VatAmount));
        invoice.TotalNet = Round(invoice.TotalTaxable + invoice.TotalVat);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string? FirstNonEmpty(params string?[] values)
        => values.Select(v => v?.Trim()).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NullIfEmpty(string? value) => TrimOrNull(value);

    private static MoadianInvoice ToDto(Db.MoadianInvoice i) => new()
    {
        Id = i.Id,
        Number = i.Number,
        Kind = i.Kind,
        InvoiceType = i.InvoiceType,
        InvoicePattern = i.InvoicePattern,
        InvoiceSubject = i.InvoiceSubject,
        ReferenceTaxId = i.ReferenceTaxId,
        Date = i.Date,
        FiscalPeriodId = i.FiscalPeriodId,
        FiscalYearId = i.FiscalYearId,
        YearSerial = i.YearSerial,
        DocumentNumber = i.DocumentNumber,
        PayType = i.PayType,
        PayTypeTitle = MoadianPayTypes.Title(i.PayType),
        Settlement = i.Settlement,
        TaxId = i.TaxId,
        SellerName = i.SellerName,
        EconomicCode = i.EconomicCode,
        BuyerTaxId = i.BuyerTaxId,
        BuyerName = i.BuyerName,
        BuyerAddress = i.BuyerAddress,
        BuyerPostalCode = i.BuyerPostalCode,
        BuyerPhone = i.BuyerPhone,
        TotalGross = i.TotalGross,
        TotalDiscount = i.TotalDiscount,
        TotalTaxable = i.TotalTaxable,
        TotalVat = i.TotalVat,
        TotalNet = i.TotalNet,
        Status = i.Status,
        CreatedBy = i.CreatedBy,
        CreatedAt = i.CreatedAt,
        Description = i.Description,
        Lines = i.Lines.OrderBy(l => l.RowNo).Select(l => new MoadianInvoiceLine
        {
            Id = l.Id,
            RowNo = l.RowNo,
            SstId = l.SstId,
            UnitCode = l.UnitCode,
            SstTitle = l.SstTitle,
            Quantity = l.Quantity,
            UnitPrice = l.UnitPrice,
            Discount = l.Discount,
            VatRate = l.VatRate,
            VatAmount = l.VatAmount,
            Total = l.Total
        }).ToList()
    };
}
