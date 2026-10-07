using Db = Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Paging = Inventory.Api.Services.Paging;

namespace Inventory.Api.Services.Invoicing;

public interface IMoadianProviderInvoiceService
{
    /// <summary>فهرست صورتحساب‌های یک خدمات‌دهنده با فیلتر دوره/وضعیت و جستجوی زنده (شماره/خریدار/taxid).</summary>
    Task<List<MoadianInvoice>> GetByProviderAsync(int providerId, int? periodId, string? search, MoadianInvoiceStatus? status, Paging.Request? pagination = null);
}

/// <summary>
/// فهرست صورتحساب‌های مودیان «به تفکیک خدمات‌دهنده» — دادهٔ صفحهٔ
/// «صورتحساب‌های خدمات‌دهنده» با ستون‌های رسمی (موضوع/شماره/تاریخ/مشتری/مجموع/
/// نوع پرداخت/الگو/وضعیت) و عملیات ردیفی (جزئیات/چاپ/ارسال/استعلام).
/// </summary>
public sealed class MoadianProviderInvoiceService : IMoadianProviderInvoiceService
{
    private readonly Db.AppDbContext _db;

    public MoadianProviderInvoiceService(Db.AppDbContext db) => _db = db;

    public async Task<List<MoadianInvoice>> GetByProviderAsync(
        int providerId, int? periodId, string? search, MoadianInvoiceStatus? status, Paging.Request? pagination = null)
    {
        var term = search?.Trim();
        var query = _db.MoadianInvoices.AsNoTracking()
            .Include(i => i.FiscalPeriod)
            .Include(i => i.Lines)
            .Where(i => i.ServiceProviderId == providerId);

        if (periodId is > 0)
            query = query.Where(i => i.FiscalPeriodId == periodId);
        if (status is not null)
            query = query.Where(i => i.Status == status);
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(i =>
                (i.DocumentNumber != null && i.DocumentNumber.Contains(term))
                || (i.BuyerName != null && i.BuyerName.Contains(term))
                || (i.Taxid != null && i.Taxid.Contains(term))
                || i.Number.ToString().Contains(term));

        var rows = await query
            .OrderByDescending(i => i.Date)
            .ThenByDescending(i => i.Id)
            .ToPageListAsync(pagination);

        return rows.Select(ToDto).ToList();
    }

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
        FiscalPeriodTitle = i.FiscalPeriod != null ? $"{i.FiscalPeriod.Year}/{i.FiscalPeriod.Month:00}" : null,
        FiscalYearId = i.FiscalYearId,
        YearSerial = i.YearSerial,
        DocumentNumber = i.DocumentNumber,
        PayType = i.PayType,
        PayTypeTitle = MoadianPayTypes.Title(i.PayType),
        ServiceProviderId = i.ServiceProviderId,
        TaxId22 = i.Taxid,
        LastInquiryAt = i.LastInquiryAt,
        LastInquiryStatus = i.LastInquiryStatus,
        FacInvoiceId = i.FacInvoiceId,
        OperationsTransactionId = i.OperationsTransactionId,
        FacInvoiceRef = i.FacInvoiceRef,
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
        ReferenceId = i.ReferenceId,
        TrackingId = i.TrackingId,
        ErrorCode = i.ErrorCode,
        ErrorMessage = i.ErrorMessage,
        Attempts = i.Attempts,
        QueuedAt = i.QueuedAt,
        SendAt = i.SendAt,
        ReturnedAt = i.ReturnedAt,
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
