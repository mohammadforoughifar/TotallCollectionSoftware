using Inventory.Api.Data;
using Inventory.Api.Services.Invoicing;
using Inventory.Shared;
using Xunit;

namespace MoadianPayload.Tests;

/// <summary>
/// Unit tests for BuildInvoiceDto - the INVOICE.V01 payload mapping.
///
/// Reference samples (verified against the production system):
///   Cash (setm=1): Cap=null, Insp=null, Indati2m=null, Tax17=null, Tvop=null,
///                  body Cop=null, body Vop=vam
///                  (sending cap/insp/cop with values caused warnings 14029/14030/1205601)
///   Credit (setm=2): Cap=null, Insp=base(tbill-tvam-todam), Tvop=tvam,
///                    body Cop=null, body Vop=vam
///   Buyer 11-digit (legal): Tob=2, Bid=id, Tinb=id (same national ID)
///   Buyer 10-digit (individual): Tob=1, Bid=id, Tinb=null
///   Buyer 14-digit (economic code): Tob=2, Bid=null, Tinb=id
/// </summary>
public class BuildInvoiceDtoTests
{
    private static MoadianInvoice CreateInvoice(string buyerTaxId, MoadianPayType? payType)
        => new()
        {
            Number = 123,
            InvoiceType = MoadianTaxInvoiceType.Type1,
            InvoicePattern = MoadianInvoicePattern.Sale,
            InvoiceSubject = MoadianInvoiceSubject.Original,
            BuyerTaxId = buyerTaxId,
            BuyerName = "Test Buyer",
            BuyerPostalCode = "1188678463",
            PayType = payType,
            Lines =
            {
                new MoadianInvoiceLine
                {
                    RowNo = 1,
                    SstId = "1111111111111",
                    UnitCode = "101",
                    SstTitle = "Test item",
                    Quantity = 2,
                    UnitPrice = 1000.5m,
                    Discount = 100.4m,
                    VatRate = 9,
                    VatAmount = 171.09m,
                    Total = 2072m
                }
            }
        };

    // Truncation expectations for the sample line above:
    //   Fee   = truncate(1000.5)          = 1000
    //   Prdis = truncate(2 * 1000.5)      = 2001
    //   Dis   = truncate(100.4)           = 100
    //   Adis  = Prdis - Dis               = 1901
    //   Vam   = truncate(171.09)          = 171
    //   Tsstam= Adis + Vam                = 2072
    //   Tbill = Adis + Vam (+ Todam 0)    = 2072
    //   base  = Tbill - Tvam - Todam      = 1901

    [Fact]
    public void Cash_Sends_Null_Cap_Insp_Cop_Indati2m_Tax17()
    {
        var dto = MoadianSubmissionService.BuildInvoiceDto(
            CreateInvoice("1234567898", MoadianPayType.Cash),
            "56899838610001", "A2ZRHM2073600000004C612", DateTime.Parse("2026-01-01T12:00:00"), out var inno);

        Assert.Equal("000000007B", inno);
        Assert.Equal(1, dto.Header.Setm);            // cash
        Assert.Null(dto.Header.Cap);
        Assert.Null(dto.Header.Insp);
        Assert.Null(dto.Header.Indati2m);
        Assert.Null(dto.Header.Tax17);
        Assert.Equal(2072m, dto.Header.Tbill);
        Assert.Equal(171m, dto.Header.Tvam);
        Assert.Null(dto.Header.Tvop);          // cash: tvop empty in the working samples (even with Tvam>0)

        var body = dto.Body.Single();
        Assert.Null(body.Cop);
        Assert.Equal(171m, body.Vop);
        Assert.Equal(2001m, body.Prdis);
        Assert.Equal(100m, body.Dis);
        Assert.Equal(1901m, body.Adis);
        Assert.Equal(2072m, body.Tsstam);
        Assert.Empty(dto.Payments);
    }

    [Fact]
    public void Credit_Sends_Null_Cap_And_Base_Amount_As_Insp()
    {
        var dto = MoadianSubmissionService.BuildInvoiceDto(
            CreateInvoice("14014038299", MoadianPayType.Credit),
            "56899838610001", "A2ZRHM2073600000004C612", DateTime.Parse("2026-01-01T12:00:00"), out _);

        Assert.Equal(2, dto.Header.Setm);            // credit (نسیه)
        Assert.Null(dto.Header.Cap);
        Assert.Equal(1901m, dto.Header.Insp);        // base amount (tax-exclusive)
        Assert.Equal(171m, dto.Header.Tvop);

        var body = dto.Body.Single();
        Assert.Null(body.Cop);
        Assert.Equal(171m, body.Vop);
        Assert.Empty(dto.Payments);
    }

    [Fact]
    public void Eleven_Digit_Buyer_Maps_To_Tob2_With_Bid_And_Tinb()
    {
        var dto = MoadianSubmissionService.BuildInvoiceDto(
            CreateInvoice("14014038299", MoadianPayType.Cash),
            "56899838610001", "A2ZRHM2073600000004C612", DateTime.Parse("2026-01-01T12:00:00"), out _);

        Assert.Equal(2, dto.Header.Tob);             // legal
        Assert.Equal("14014038299", dto.Header.Bid);
        Assert.Equal("14014038299", dto.Header.Tinb); // same 11-digit ID (system-verified sample)
        Assert.Null(dto.Header.Irtaxid);
    }

    [Fact]
    public void Ten_Digit_Buyer_Maps_To_Tob1_With_Bid_Only()
    {
        var dto = MoadianSubmissionService.BuildInvoiceDto(
            CreateInvoice("1234567898", MoadianPayType.Cash),
            "56899838610001", "A2ZRHM2073600000004C612", DateTime.Parse("2026-01-01T12:00:00"), out _);

        Assert.Equal(1, dto.Header.Tob);             // individual
        Assert.Equal("1234567898", dto.Header.Bid);
        Assert.Null(dto.Header.Tinb);
    }

    [Fact]
    public void Fourteen_Digit_Buyer_Maps_To_Tob2_With_Tinb_Only()
    {
        var dto = MoadianSubmissionService.BuildInvoiceDto(
            CreateInvoice("12345678901234", MoadianPayType.Cash),
            "56899838610001", "A2ZRHM2073600000004C612", DateTime.Parse("2026-01-01T12:00:00"), out _);

        Assert.Equal(2, dto.Header.Tob);
        Assert.Null(dto.Header.Bid);
        Assert.Equal("12345678901234", dto.Header.Tinb);
    }

    [Fact]
    public void Local_TaxIdGenerator_Reproduces_System_Verified_Sample_Taxid()
    {
        // The user's system-accepted sample from another software:
        //   Taxid  = A2ZRHM051000000004C612
        //   Inno   = 0000004C61  (serial 19553)
        //   Indatim = 1791621932351 (2026-10-10 08:45:32 UTC, day 20736 -> hex 05100)
        // memoryId: first 6 chars "A2ZRHM" (rest of TaxMemoryID is not used)
        var date = new DateTime(2026, 10, 10, 12, 15, 32); // any time on 2026-10-10
        var taxid = MoadianTaxIdGenerator.GenerateTaxId("A2ZRHM000000", 19553, date);
        Assert.Equal("A2ZRHM051000000004C612", taxid);
        Assert.Equal("0000004C61", MoadianTaxIdGenerator.ToInno(19553));
    }

    [Fact]
    public void Subject_And_Reference_Are_Passed_Through()
    {
        var inv = CreateInvoice("14014038299", MoadianPayType.Cash);
        inv.InvoiceSubject = MoadianInvoiceSubject.Void;
        inv.ReferenceTaxId = "A2ZRHM2073600000004C612";

        var dto = MoadianSubmissionService.BuildInvoiceDto(
            inv, "56899838610001", "A2ZRHM2073600000004C612", DateTime.Parse("2026-01-02T12:00:00"), out _);

        Assert.Equal(3, dto.Header.Ins);             // void
        Assert.Equal("A2ZRHM2073600000004C612", dto.Header.Irtaxid);
        Assert.Equal("A2ZRHM2073600000004C612", dto.Header.Taxid);
    }
}
