using System.Diagnostics;
using System.Net;
using System.Security.Authentication;
using System.Text.Json;
using Db = Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaxCollectData.Library.Abstraction;
using TaxCollectData.Library.Dto;
using TaxCollectData.Library.Dto.Config;
using TaxCollectData.Library.Dto.Content;
using TaxCollectData.Library.Dto.Properties;
using TaxCollectData.Library.Dto.Transfer;
using TaxCollectData.Library.Enums;
using TaxCollectData.Library.Extensions;

namespace Inventory.Api.Services.Invoicing;

public interface IMoadianSubmissionService
{
    /// <summary>ارسال رسمی صورتحساب به سامانه مودیان (INVOICE.V01) و به‌روزرسانی وضعیت/لاگ.</summary>
    Task<MoadianSubmissionResultDto> SendAsync(int invoiceId, string? user);

    /// <summary>استعلام رسمی وضعیت صورتحساب از سامانه مودیان با شماره مرجع و به‌روزرسانی وضعیت/لاگ.</summary>
    Task<MoadianSubmissionResultDto> InquiryAsync(int invoiceId, string? user);
}

/// <summary>
/// ارسال و استعلام رسمی صورتحساب‌های مودیان از طریق TaxCollectData.Library (0.0.34).
///
/// هر عملیات کلاینت SDK مستقل می‌سازد (همان الگوی تست اتصال):
///   GetServerInformation → RequestToken → SetToken → فراخوانی کاری
/// و از فرستندهٔ امن (با اعتبارسنجی TLS و قفل origin) به‌جای فرستندهٔ پیش‌فرض SDK استفاده می‌کند.
/// شناسهٔ احراز هویت (clientId) به ترتیب امتحان می‌شود: شناسه ملی، شماره اقتصادی،
/// شناسهٔ حافظهٔ مالیاتی — تا جایی که RequestToken موفق شود.
/// هیچ رازی (کلید/توکن/مسیر) در پیام یا لاگ ظاهر نمی‌شود.
/// </summary>
public sealed class MoadianSubmissionService : IMoadianSubmissionService
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(100);
    private static readonly TimeSpan OverallTimeout = TimeSpan.FromSeconds(300);

    private readonly Db.AppDbContext _db;
    private readonly MoadianRuntimeSettings _settings;
    private readonly ILogger<MoadianSubmissionService> _logger;

    public MoadianSubmissionService(
        Db.AppDbContext db,
        MoadianRuntimeSettings settings,
        ILogger<MoadianSubmissionService> logger)
    {
        _db = db;
        _settings = settings;
        _logger = logger;
    }

    // =====================================================================
    // ارسال
    // =====================================================================
    public async Task<MoadianSubmissionResultDto> SendAsync(int invoiceId, string? user)
    {
        var (invoice, provider, connection, baseUrl, keyId, apiVersion, clientType) = await ResolveSubmissionContextAsync(invoiceId);

        if (invoice.Status is MoadianInvoiceStatus.Sent or MoadianInvoiceStatus.AwaitingBuyerConfirmation)
            throw new InvalidOperationException("این صورتحساب قبلاً ارسال شده است؛ برای وضعیت فعلی «استعلام از سامانه» را استفاده کنید.");
        if (invoice.Status == MoadianInvoiceStatus.BuyerConfirmed)
            throw new InvalidOperationException("این صورتحساب توسط خریدار تأیید شده است؛ ارسال مجدد مجاز نیست.");
        if (invoice.Status == MoadianInvoiceStatus.BuyerRejected)
            throw new InvalidOperationException("این صورتحساب توسط خریدار رد شده است؛ برای اصلاح، موضوع اصلاحی/برگشتی جدید ثبت کنید.");
        if (invoice.Status == MoadianInvoiceStatus.Voided)
            throw new InvalidOperationException("صورتحساب ابطال‌شده قابل ارسال نیست.");

        if (invoice.InvoiceType is not (MoadianTaxInvoiceType.Type1 or MoadianTaxInvoiceType.Type2))
            throw new InvalidOperationException("نوع صورتحساب (inty) انتخاب نشده است؛ پیش از ارسال، نوع ۱ یا ۲ را تعیین کنید.");
        if (invoice.InvoicePattern == MoadianInvoicePattern.Unselected)
            throw new InvalidOperationException("الگوی صورتحساب (inp) انتخاب نشده است؛ پیش از ارسال، الگو را تعیین کنید.");
        if (invoice.InvoiceSubject == MoadianInvoiceSubject.Unselected)
            throw new InvalidOperationException("موضوع صورتحساب (ins) انتخاب نشده است؛ پیش از ارسال، موضوع را تعیین کنید.");
        if (MoadianInvoiceRules.RequiresReference(invoice.InvoiceSubject) && string.IsNullOrWhiteSpace(invoice.ReferenceTaxId))
            throw new InvalidOperationException("برای این موضوع، شناسهٔ مالیاتی ۲۲ نویسه‌ای صورتحساب مرجع (Irtaxid) الزامی است.");
        if (invoice.Lines.Count == 0)
            throw new InvalidOperationException("صورتحساب بدون قلم قابل ارسال نیست.");
        if (string.IsNullOrWhiteSpace(invoice.BuyerName))
            throw new InvalidOperationException("نام خریدار ثبت نشده است.");

        var tins = FirstNonEmpty(invoice.EconomicCode, provider.EconomicNumber, provider.NationalID);
        if (string.IsNullOrWhiteSpace(tins))
            throw new InvalidOperationException("شماره اقتصادی فروشنده (Tins) مشخص نیست؛ در پروفایل خدمات‌دهنده ثبت کنید.");

        using var overallCts = new CancellationTokenSource(OverallTimeout);
        var token = overallCts.Token;

        var identities = BuildIdentityCandidates(provider.NationalID, provider.EconomicNumber, connection.TaxMemoryID);
        if (identities.Count == 0)
            throw new InvalidOperationException("برای احراز هویت، «شناسه ملی»، «شماره اقتصادی» یا «شناسه حافظهٔ مالیاتی» خدمات‌دهنده باید ثبت شده باشد.");

        try
        {
            // کلاینت اولین شناسه ساخته می‌شود تا تولیدکنندهٔ رسمی taxid از DI SDK در دسترس باشد.
            var firstClient = CreateTaxApis(baseUrl, connection.PrivateKeyPath, keyId, clientType, apiVersion, identities[0].Value);
            var (taxid, usedFallback) = ResolveTaxId(invoice, connection.TaxMemoryID, invoice.Number, invoice.Date, firstClient.Provider);
            var invoiceDto = BuildInvoiceDto(invoice, tins, taxid, out _);

            var sent = false;
            Exception? lastAuthError = null;
            for (var i = 0; i < identities.Count; i++)
            {
                var authed = false;
                var client = i == 0
                    ? firstClient
                    : CreateTaxApis(baseUrl, connection.PrivateKeyPath, keyId, clientType, apiVersion, identities[i].Value);
                try
                {
                    var serverInformation = await client.TaxApis.GetServerInformationAsync().WaitAsync(token);
                    if (serverInformation is null)
                        throw new InvalidOperationException("سامانه پاسخ قابل تفسیری برای GET_SERVER_INFORMATION برنگرداند.");
                    var tokenModel = await client.TaxApis.RequestTokenAsync().WaitAsync(token);
                    if (tokenModel is null)
                        throw new InvalidOperationException("سامانه توکن دسترسی برنگرداند.");
                    client.TaxApis.SetToken(tokenModel);
                    authed = true;

                    sent = await DoSendAsync(client.TaxApis, invoice, invoiceDto, taxid, usedFallback, identities[i].FieldLabel, token);
                    break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // خطای فاز احراز هویت — شناسهٔ بعدی را امتحان کن.
                    if (authed)
                        throw; // خطای فاز ارسال/ذخیره — بازآزمایش با شناسهٔ بعدی خطر ارسال تکراری دارد
                    lastAuthError = ex;
                    _logger.LogWarning("Moadian send authentication failed for {ExceptionType}.", ex.GetType().Name);
                }
                finally
                {
                    DisposeClient(client);
                }
            }

            if (!sent)
                throw new InvalidOperationException(
                    $"ارتباط با سامانه برقرار شد، اما احراز هویت این خدمات‌دهنده ناموفق بود: {(lastAuthError is null ? "خطای نامشخص" : Sanitize(lastAuthError.Message))}");

            return await FinishResultAsync(invoice);
        }
        catch (OperationCanceledException) when (overallCts.IsCancellationRequested)
        {
            throw new InvalidOperationException("مهلت عملیات با سامانه مودیان به پایان رسید؛ دوباره تلاش کنید.");
        }
    }

    /// <summary>فراخوانی SendInvoicesAsync + نگاشت پاسخ + به‌روزرسانی وضعیت و لاگ.</summary>
    private async Task<bool> DoSendAsync(
        ITaxApis sdk,
        Db.MoadianInvoice invoice,
        InvoiceDto invoiceDto,
        string taxid,
        bool usedFallback,
        string identity,
        CancellationToken token)
    {
        HttpResponse<AsyncResponseModel?>? response = null;
        Exception? sendError = null;
        try
        {
            response = await sdk.SendInvoicesAsync(new List<InvoiceDto> { invoiceDto }, null).WaitAsync(token);
        }
        catch (OperationCanceledException ex)
        {
            // timeout کلاینت (درخواست ممکن است رسیده باشد) یا مهلت کلی — هر دو «نتیجه نامعلوم».
            sendError = ex;
        }
        catch (Exception ex)
        {
            sendError = ex;
        }

        var unknownOutcome = sendError is TimeoutException or OperationCanceledException || response is null;
        if (unknownOutcome)
        {
            // در این حالت ممکن است سامانه بسته را پذیرفته باشد؛ وضعیت را «ناموفق» ثبت نمی‌کنیم
            // (جلوگیری از ثبت تکراری در کارپوشه) و کاربر را به استعلام هدایت می‌کنیم.
            invoice.Status = MoadianInvoiceStatus.Sending;
            invoice.Taxid = taxid;
            if (string.IsNullOrWhiteSpace(invoice.ReferenceId))
                invoice.ReferenceId = MoadianTaxIdGenerator.ToInno(invoice.Number);
            invoice.SendAt = DateTime.Now;
            invoice.Attempts++;
            invoice.ErrorCode = null;
            invoice.ErrorMessage = "درخواست ارسال شد اما پاسخ قطعی از سامانه دریافت نشد؛ برای نتیجهٔ قطعی «استعلام از سامانه» را انجام دهید.";
            AddLog(invoice, MoadianLogAction.Sent,
                "درخواست ارسال به سامانه مودیان رفت اما پاسخ قطعی دریافت نشد.",
                BuildSendDetail(0, null, invoice.ReferenceId, taxid, usedFallback, identity));
            await _db.SaveChangesAsync(token);
            return true;
        }

        if (sendError is not null)
        {
            invoice.Status = MoadianInvoiceStatus.Failed;
            invoice.Taxid = taxid;
            invoice.SendAt = DateTime.Now;
            invoice.ReturnedAt = DateTime.Now;
            invoice.Attempts++;
            invoice.ErrorCode = "NETWORK";
            invoice.ErrorMessage = Truncate(DescribeFailure(sendError), 500);
            AddLog(invoice, MoadianLogAction.Failed,
                "ارسال به سامانه مودیان از نظر شبکه ناموفق بود.",
                BuildSendDetail(0, null, null, taxid, usedFallback, identity));
            await _db.SaveChangesAsync(token);
            return true;
        }

        var status = response!.Status;
        var body = response.Body;

        // ---------- پاسخ خالی/تفسیرنشده از سمت فرستنده (408) ----------
        if (body is null)
        {
            if (status is 408 or 496)
            {
                invoice.Status = MoadianInvoiceStatus.Sending;
                invoice.Taxid = taxid;
                if (string.IsNullOrWhiteSpace(invoice.ReferenceId))
                    invoice.ReferenceId = MoadianTaxIdGenerator.ToInno(invoice.Number);
                invoice.SendAt = DateTime.Now;
                invoice.Attempts++;
                invoice.ErrorMessage = "پاسخ سامانه قابل تفسیر نبود؛ برای نتیجهٔ قطعی «استعلام از سامانه» را انجام دهید.";
                AddLog(invoice, MoadianLogAction.Sent,
                    "درخواست ارسال رفت اما پاسخ سامانه قابل تفسیر نبود.",
                    BuildSendDetail(status, null, invoice.ReferenceId, taxid, usedFallback, identity));
            }
            else
            {
                invoice.Status = MoadianInvoiceStatus.Failed;
                invoice.Taxid = taxid;
                invoice.SendAt = DateTime.Now;
                invoice.ReturnedAt = DateTime.Now;
                invoice.Attempts++;
                invoice.ErrorCode = $"HTTP{status}";
                invoice.ErrorMessage = $"سامانه با کد وضعیت HTTP {status} پاسخ داد و جزئیات قابل تفسیری نداد.";
                AddLog(invoice, MoadianLogAction.Failed,
                    "ارسال به سامانه ناموفق بود (پاسخ خالی).",
                    BuildSendDetail(status, null, null, taxid, usedFallback, identity));
            }
            await _db.SaveChangesAsync(token);
            return true;
        }

        // ---------- پاسخ با جزئیات بسته (per-packet) ----------
        if (body.Result is { Count: > 0 })
        {
            var packet = body.Result.First();
            if (string.IsNullOrEmpty(packet.ErrorCode))
            {
                invoice.Status = MoadianInvoiceStatus.AwaitingBuyerConfirmation;
                invoice.Taxid = taxid;
                invoice.ReferenceId = FirstNonEmpty(packet.ReferenceNumber, MoadianTaxIdGenerator.ToInno(invoice.Number));
                invoice.TrackingId = packet.Uid;
                invoice.SendAt = DateTime.Now;
                invoice.ReturnedAt = null;
                invoice.Attempts++;
                invoice.ErrorCode = null;
                invoice.ErrorMessage = null;
                AddLog(invoice, MoadianLogAction.Sent,
                    "صورتحساب به سامانه مودیان ارسال شد؛ در انتظار استعلام/تأیید خریدار.",
                    BuildSendDetail(status, packet.Uid, invoice.ReferenceId, taxid, usedFallback, identity));
                await _db.SaveChangesAsync(token);
                return true;
            }

            invoice.Status = MoadianInvoiceStatus.Failed;
            invoice.Taxid = taxid;
            if (!string.IsNullOrEmpty(packet.ReferenceNumber))
                invoice.ReferenceId = packet.ReferenceNumber;
            invoice.SendAt = DateTime.Now;
            invoice.ReturnedAt = DateTime.Now;
            invoice.Attempts++;
            invoice.ErrorCode = packet.ErrorCode;
            invoice.ErrorMessage = Truncate(Sanitize(packet.ErrorDetail), 500);
            AddLog(invoice, MoadianLogAction.Failed,
                $"سامانه این صورتحساب را با خطا پذیرفت (کد {packet.ErrorCode}).",
                BuildSendDetail(status, packet.Uid, packet.ReferenceNumber, taxid, usedFallback, identity));
            await _db.SaveChangesAsync(token);
            return true;
        }

        // ---------- خطای سطح بسته‌بندی ----------
        if (body.Errors is { Count: > 0 })
        {
            var error = body.Errors[0];
            invoice.Status = MoadianInvoiceStatus.Failed;
            invoice.Taxid = taxid;
            invoice.SendAt = DateTime.Now;
            invoice.ReturnedAt = DateTime.Now;
            invoice.Attempts++;
            invoice.ErrorCode = error.ErrorCode;
            invoice.ErrorMessage = Truncate(Sanitize(error.Detail), 500);
            AddLog(invoice, MoadianLogAction.Failed,
                $"ارسال به سامانه ناموفق بود (کد {error.ErrorCode}).",
                BuildSendDetail(status, null, null, taxid, usedFallback, identity));
            await _db.SaveChangesAsync(token);
            return true;
        }

        // ---------- پذیرش بدون جزئیات بسته (HTTP 2xx) ----------
        if (status is >= 200 and < 300)
        {
            invoice.Status = MoadianInvoiceStatus.AwaitingBuyerConfirmation;
            invoice.Taxid = taxid;
            if (string.IsNullOrWhiteSpace(invoice.ReferenceId))
                invoice.ReferenceId = MoadianTaxIdGenerator.ToInno(invoice.Number);
            invoice.SendAt = DateTime.Now;
            invoice.ReturnedAt = null;
            invoice.Attempts++;
            invoice.ErrorCode = null;
            invoice.ErrorMessage = null;
            AddLog(invoice, MoadianLogAction.Sent,
                "صورتحساب به سامانه مودیان ارسال شد (پذیرش بدون جزئیات بسته).",
                BuildSendDetail(status, null, invoice.ReferenceId, taxid, usedFallback, identity));
            await _db.SaveChangesAsync(token);
            return true;
        }

        invoice.Status = MoadianInvoiceStatus.Failed;
        invoice.Taxid = taxid;
        invoice.SendAt = DateTime.Now;
        invoice.ReturnedAt = DateTime.Now;
        invoice.Attempts++;
        invoice.ErrorCode = $"HTTP{status}";
        invoice.ErrorMessage = $"سامانه با کد وضعیت HTTP {status} پاسخ داد.";
        AddLog(invoice, MoadianLogAction.Failed,
            "ارسال به سامانه ناموفق بود.",
            BuildSendDetail(status, null, null, taxid, usedFallback, identity));
        await _db.SaveChangesAsync(token);
        return true;
    }

    /// <summary>ساخت DTO رسمی (INVOICE.V01) از موجودیت داخلی — نگاشت مستقیم فیلدهای سامانه.</summary>
    internal static InvoiceDto BuildInvoiceDto(Db.MoadianInvoice inv, string tins, string taxid, out string inno)
    {
        inno = MoadianTaxIdGenerator.ToInno(inv.Number);
        var issueMs = new DateTimeOffset(inv.Date).ToUnixTimeMilliseconds();
        var (tob, bid, tinb) = ResolveBuyer(inv);
        var payType = inv.PayType ?? MoadianPayType.Cash;

        var header = new InvoiceHeaderDto
        {
            Taxid = taxid,
            Indatim = issueMs,
            Indati2m = issueMs,
            Inty = (int)inv.InvoiceType,
            Inno = inno,
            Irtaxid = string.IsNullOrWhiteSpace(inv.ReferenceTaxId) ? null : inv.ReferenceTaxId,
            Inp = (int)inv.InvoicePattern,
            Ins = (int)inv.InvoiceSubject,
            Tins = tins,
            Tob = tob,
            Bid = bid,
            Tinb = tinb,
            Bpc = NullIfEmpty(inv.BuyerPostalCode),
            Tprdis = inv.TotalGross,
            Tdis = inv.TotalDiscount,
            Tadis = inv.TotalTaxable,
            Tvam = inv.TotalVat,
            Todam = 0m,
            Tbill = inv.TotalNet,
            Setm = (int)payType,
            Cap = payType == MoadianPayType.Cash ? inv.TotalNet : 0m,
            Insp = payType == MoadianPayType.Cash ? 0m : inv.TotalNet,
            Tvop = 0m,
            Tax17 = 0m
        };

        var body = inv.Lines
            .OrderBy(l => l.RowNo)
            .Select(l => new InvoiceBodyDto
            {
                Sstid = l.SstId,
                Sstt = l.SstTitle,
                Mu = NullIfEmpty(l.UnitCode),
                Am = l.Quantity,
                Fee = l.UnitPrice,
                Prdis = l.Quantity * l.UnitPrice,
                Dis = l.Discount,
                Adis = l.Taxable,
                Vra = l.VatRate,
                Vam = l.VatAmount,
                Tsstam = l.Total
            })
            .ToList();

        var payments = new List<PaymentDto>
        {
            new()
            {
                Pmt = (int)payType,
                Pv = (long)inv.TotalNet,
                Pdt = issueMs
            }
        };

        return new InvoiceDto
        {
            Header = header,
            Body = body,
            Payments = payments,
            Extension = new List<InvoiceExtension>()
        };
    }

    /// <summary>نوع خریدار: ۱۰ رقم = شماره اقتصادی (حقوقی=0)؛ ۱۱ رقم = شناسه ملی (حقیقی=1).</summary>
    internal static (int? Tob, string? Bid, string? Tinb) ResolveBuyer(Db.MoadianInvoice inv)
    {
        var id = inv.BuyerTaxId?.Trim();
        if (string.IsNullOrWhiteSpace(id)) return (null, null, null);
        if (id.Length == 10 && id.All(char.IsDigit)) return (0, null, id);
        return (1, id, null);
    }

    private static (string Taxid, bool Fallback) ResolveTaxId(
        Db.MoadianInvoice invoice, string taxMemoryId, long serial, DateTime date, IServiceProvider services)
    {
        if (invoice.Taxid is { Length: 22 })
            return (invoice.Taxid, false);

        ITaxIdGenerator? generator = null;
        try
        {
            generator = services.GetService<ITaxIdGenerator>();
        }
        catch (Exception)
        {
            generator = null;
        }

        if (generator is not null)
        {
            try
            {
                return (generator.GenerateTaxId(taxMemoryId, serial, date), false);
            }
            catch (Exception)
            {
                // تولیدکنندهٔ رسمی SDK در دسترس نبود — به نسخهٔ محلی (همان الگوریتم) روی می‌آوریم.
            }
        }

        return (MoadianTaxIdGenerator.GenerateTaxId(taxMemoryId, serial, date), true);
    }

    // =====================================================================
    // استعلام
    // =====================================================================
    public async Task<MoadianSubmissionResultDto> InquiryAsync(int invoiceId, string? user)
    {
        var (invoice, provider, connection, baseUrl, keyId, apiVersion, clientType) = await ResolveSubmissionContextAsync(invoiceId);

        if (string.IsNullOrWhiteSpace(invoice.ReferenceId))
            throw new InvalidOperationException("این صورتحساب هنوز به سامانه ارسال نشده است (شماره مرجع ندارد)؛ ابتدا «ارسال به سامانه مودیان» را انجام دهید.");

        using var overallCts = new CancellationTokenSource(OverallTimeout);
        var token = overallCts.Token;
        var referenceId = invoice.ReferenceId!;
        var identities = BuildIdentityCandidates(provider.NationalID, provider.EconomicNumber, connection.TaxMemoryID);
        if (identities.Count == 0)
            throw new InvalidOperationException("برای احراز هویت، «شناسه ملی»، «شماره اقتصادی» یا «شناسه حافظهٔ مالیاتی» خدمات‌دهنده باید ثبت شده باشد.");

        try
        {
            Exception? lastAuthError = null;
            foreach (var (_, identity) in identities)
            {
                var client = CreateTaxApis(baseUrl, connection.PrivateKeyPath, keyId, clientType, apiVersion, identity);
                try
                {
                    var serverInformation = await client.TaxApis.GetServerInformationAsync().WaitAsync(token);
                    if (serverInformation is null)
                        throw new InvalidOperationException("سامانه پاسخ قابل تفسیری برای GET_SERVER_INFORMATION برنگرداند.");
                    var tokenModel = await client.TaxApis.RequestTokenAsync().WaitAsync(token);
                    if (tokenModel is null)
                        throw new InvalidOperationException("سامانه توکن دسترسی برنگرداند.");
                    client.TaxApis.SetToken(tokenModel);

                    List<InquiryResultModel>? results = null;
                    Exception? inquiryError = null;
                    try
                    {
                        results = await client.TaxApis.InquiryByReferenceIdAsync(new List<string> { referenceId }).WaitAsync(token);
                    }
                    catch (OperationCanceledException ex)
                    {
                        inquiryError = ex;
                    }
                    catch (Exception ex)
                    {
                        inquiryError = ex;
                    }

                    if (inquiryError is not null)
                    {
                        AddLog(invoice, MoadianLogAction.Inquiry,
                            "استعلام وضعیت از سامانه با خطا مواجه شد.",
                            Sanitize(inquiryError.Message));
                        await _db.SaveChangesAsync(token);
                        return new MoadianSubmissionResultDto
                        {
                            Success = false,
                            Message = DescribeFailure(inquiryError),
                            Invoice = ToDto(invoice)
                        };
                    }

                    var result = results is null || results.Count == 0
                        ? null
                        : results.FirstOrDefault(r => string.Equals(r?.ReferenceNumber, referenceId, StringComparison.OrdinalIgnoreCase)) ?? results[0];

                    if (result is null)
                    {
                        AddLog(invoice, MoadianLogAction.Inquiry,
                            "سامانه برای این شماره مرجع هنوز نتیجه‌ای ثبت نکرده است.",
                            null);
                        await _db.SaveChangesAsync(token);
                        return new MoadianSubmissionResultDto
                        {
                            Success = true,
                            Message = "سامانه برای این شماره مرجع هنوز نتیجه‌ای ثبت نکرده است؛ کمی بعد دوباره استعلام بگیرید.",
                            Invoice = ToDto(invoice)
                        };
                    }

                    var (mapped, statusFa) = MapSystemStatus(result.Status);
                    var (_, errCode, errMsg) = ParseInquiryData(result.Data);

                    invoice.LastInquiryAt = DateTime.Now;
                    invoice.LastInquiryStatus = result.Status;
                    if (mapped is not null && invoice.Status != MoadianInvoiceStatus.Voided)
                        invoice.Status = mapped.Value;
                    if (!string.IsNullOrWhiteSpace(errCode))
                    {
                        invoice.ErrorCode = errCode;
                        invoice.ErrorMessage = Truncate(FirstNonEmpty(errMsg, "خطای سامانه ثبت شده است."), 500);
                        invoice.ReturnedAt = DateTime.Now;
                    }
                    AddLog(invoice, MoadianLogAction.Inquiry,
                        $"وضعیت سامانه برای این صورتحساب: {statusFa} ({result.Status}).",
                        Truncate(DataToJson(result.Data), 2000));
                    await _db.SaveChangesAsync(token);

                    return new MoadianSubmissionResultDto
                    {
                        Success = true,
                        Message = $"وضعیت سامانه: {statusFa} ({result.Status}).",
                        Invoice = ToDto(invoice)
                    };
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // خطای احراز هویت — شناسهٔ بعدی را امتحان کن.
                    lastAuthError = ex;
                    _logger.LogWarning("Moadian inquiry authentication failed for {ExceptionType}.", ex.GetType().Name);
                }
                finally
                {
                    DisposeClient(client);
                }
            }

            throw new InvalidOperationException(
                $"ارتباط با سامانه برقرار شد، اما احراز هویت این خدمات‌دهنده ناموفق بود: {(lastAuthError is null ? "خطای نامشخص" : Sanitize(lastAuthError.Message))}");
        }
        catch (OperationCanceledException) when (overallCts.IsCancellationRequested)
        {
            throw new InvalidOperationException("مهلت عملیات با سامانه مودیان به پایان رسید؛ دوباره تلاش کنید.");
        }
    }

    // =====================================================================
    // زیرساخت مشترک
    // =====================================================================

    private async Task<(Db.MoadianInvoice Invoice, Db.MoadianServiceProviderProfile Provider, Db.MoadianProviderConnectionProfile Connection,
        string BaseUrl, string KeyId, string ApiVersion, ClientType ClientType)> ResolveSubmissionContextAsync(int invoiceId)
    {
        var invoice = await _db.MoadianInvoices
            .Include(i => i.Lines)
            .FirstOrDefaultAsync(i => i.Id == invoiceId)
            ?? throw new InvalidOperationException("صورتحساب یافت نشد.");

        if (invoice.ServiceProviderId is null)
            throw new InvalidOperationException("این صورتحساب به خدمات‌دهندهٔ مودیان گره نخورده است؛ ارسال/استعلام رسمی فقط برای صورتحساب‌های دارای خدمات‌دهنده ممکن است.");

        var provider = await _db.MoadianServiceProviderProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == invoice.ServiceProviderId.Value && !p.IsDeleted)
            ?? throw new InvalidOperationException("خدمات‌دهندهٔ این صورتحساب یافت نشد یا غیرفعال است.");

        var connection = await _db.MoadianProviderConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ServiceProviderId == provider.Id && !c.IsDeleted)
            ?? throw new InvalidOperationException("اتصال این خدمات‌دهنده به سامانه مودیان ثبت نشده است؛ از «اطلاعات پایه مودیان» اتصال را ایجاد کنید.");

        if (string.IsNullOrWhiteSpace(connection.TaxMemoryID))
            throw new InvalidOperationException("شناسهٔ حافظه مالیاتی اتصال ثبت نشده است؛ اتصال را کامل کنید.");
        if (string.IsNullOrWhiteSpace(connection.PrivateKeyPath) || !File.Exists(connection.PrivateKeyPath))
            throw new InvalidOperationException("فایل کلید خصوصی این اتصال در سرور موجود نیست؛ ابتدا فایل .key را بارگذاری کنید.");

        var clientType = ResolveClientType(_settings.ClientType);
        if (!TryBuildBaseUrl(connection.WebServiceAddress, _settings.BaseUrl, clientType, out var baseUrl, out var urlError))
            throw new InvalidOperationException(urlError);

        var apiVersion = (_settings.ApiVersion ?? "").Trim();
        if (!IsSupportedApiVersion(apiVersion))
            throw new InvalidOperationException("مقدار Moadian:ApiVersion باید خالی (سبک v2) یا v1 باشد؛ SDK نسخهٔ 0.0.34 برای سایر مقادیر سرویس انتقال را ثبت نمی‌کند.");

        var keyId = (_settings.SignatureKeyId ?? "").Trim();
        return (invoice, provider, connection, baseUrl, keyId, apiVersion, clientType);
    }

    /// <summary>نتیجهٔ نهایی پس از به‌روزرسانی موفق (پیام از روی وضعیتِ تازه می‌آید).</summary>
    private static Task<MoadianSubmissionResultDto> FinishResultAsync(Db.MoadianInvoice invoice)
    {
        var message = invoice.Status switch
        {
            MoadianInvoiceStatus.AwaitingBuyerConfirmation => "صورتحساب به سامانه مودیان ارسال شد؛ در انتظار تأیید خریدار است.",
            MoadianInvoiceStatus.Sending => "درخواست ارسال ثبت شد اما پاسخ قطعی دریافت نشد؛ «استعلام از سامانه» را انجام دهید.",
            MoadianInvoiceStatus.Failed => $"ارسال ناموفق بود: {invoice.ErrorMessage}",
            _ => "عملیات انجام شد."
        };
        var result = new MoadianSubmissionResultDto
        {
            Success = invoice.Status is MoadianInvoiceStatus.AwaitingBuyerConfirmation
                or MoadianInvoiceStatus.Sent
                or MoadianInvoiceStatus.Sending
                or MoadianInvoiceStatus.BuyerConfirmed,
            Message = message,
            Invoice = ToDto(invoice)
        };
        return Task.FromResult(result);
    }

    private void AddLog(Db.MoadianInvoice invoice, MoadianLogAction action, string message, string? detail)
        => _db.MoadianLogs.Add(new Db.MoadianLog
        {
            InvoiceId = invoice.Id,
            Action = action,
            Message = Truncate(message, 300),
            Detail = string.IsNullOrWhiteSpace(detail) ? null : detail
        });

    private static string BuildSendDetail(
        int httpStatus, string? uid, string? referenceNumber, string taxid, bool usedFallback, string identity)
    {
        var payload = new Dictionary<string, string?>
        {
            ["httpStatus"] = httpStatus > 0 ? httpStatus.ToString() : null,
            ["uid"] = uid,
            ["referenceNumber"] = referenceNumber,
            ["taxid"] = taxid,
            ["taxidSource"] = usedFallback ? "local-generator" : "sdk",
            ["identityField"] = identity
        };
        return JsonSerializer.Serialize(payload);
    }

    /// <summary>نگاشت وضعیت رسمی سامانه (InvoiceStatus) به وضعیت داخلی + عنوان فارسی.</summary>
    internal static (MoadianInvoiceStatus? Status, string Fa) MapSystemStatus(string? raw)
    {
        var value = raw?.Trim().ToUpperInvariant() ?? "";
        return value switch
        {
            "PENDING" => (MoadianInvoiceStatus.AwaitingBuyerConfirmation, "در انتظار تأیید خریدار"),
            "CONFIRM" => (MoadianInvoiceStatus.BuyerConfirmed, "تأییدشده توسط خریدار"),
            "SYSTEM_CONFIRM" => (MoadianInvoiceStatus.BuyerConfirmed, "تأیید سیستمی (تأیید خودکار مالیاتی)"),
            "REJECT" => (MoadianInvoiceStatus.BuyerRejected, "ردشده توسط خریدار"),
            _ => (null, value.Length == 0 ? "نامشخص" : $"نامشخص ({raw!.Trim()})")
        };
    }

    /// <summary>خواندن success/error از دادهٔ استعلام (Data) به‌صورت مقاوم در برابر شکل serializer.</summary>
    internal static (bool? Success, string? Code, string? Msg) ParseInquiryData(object? data)
    {
        if (data is null) return (null, null, null);

        JsonElement? element = data switch
        {
            JsonElement je when je.ValueKind is JsonValueKind.Object => je,
            string s when s.TrimStart().StartsWith("{", StringComparison.Ordinal) => TryParseJson(s),
            _ => SerializeToElement(data)
        };
        if (element is not { } e) return (null, null, null);

        bool? success = null;
        if (e.TryGetProperty("success", out var sv) && sv.ValueKind is JsonValueKind.True or JsonValueKind.False)
            success = sv.GetBoolean();
        string? code = null;
        string? msg = null;
        if (e.TryGetProperty("error", out var ev) && ev.ValueKind == JsonValueKind.Array && ev.GetArrayLength() > 0)
        {
            var first = ev[0];
            code = first.ValueKind == JsonValueKind.Object && first.TryGetProperty("code", out var cv) ? cv.GetString() : null;
            msg = first.ValueKind == JsonValueKind.Object && first.TryGetProperty("msg", out var mv) ? mv.GetString() : null;
        }
        return (success, code, msg);
    }

    private static JsonElement? TryParseJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement? SerializeToElement(object value)
    {
        try
        {
            return JsonSerializer.SerializeToElement(value);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? DataToJson(object? data)
    {
        if (data is null) return null;
        if (data is JsonElement je) return je.GetRawText();
        if (data is string s) return s;
        try
        {
            return JsonSerializer.Serialize(data);
        }
        catch (Exception)
        {
            return data.ToString();
        }
    }

    // ---------- ساخت کلاینت SDK (همان الگوی تست اتصال) ----------

    private static (ITaxApis TaxApis, MoadianSecureHttpRequestSender Sender, ServiceProvider Provider) CreateTaxApis(
        string baseUrl,
        string privateKeyPath,
        string keyId,
        ClientType clientType,
        string apiVersion,
        string clientId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTaxApi(
            baseUrl,
            clientId,
            new NormalProperties(clientType, apiVersion),
            new Pkcs8SignatoryConfig(privateKeyPath, keyId),
            contentSignatoryConfig: null,
            encryptionConfig: new EncryptionConfig("", ""));

        // فرستندهٔ پیش‌فرض SDK اعتبارسنجی گواهی TLS را غیرفعال می‌کند؛ جایگزین امن زیر آن را کنار می‌گذارد.
        var sender = new MoadianSecureHttpRequestSender(baseUrl, RequestTimeout);
        services.RemoveAll<IHttpRequestSender>();
        services.AddSingleton<IHttpRequestSender>(sender);

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<ITaxApis>(), sender, provider);
    }

    private static void DisposeClient((ITaxApis TaxApis, MoadianSecureHttpRequestSender Sender, ServiceProvider Provider) client)
    {
        client.Sender.Dispose();
        client.Provider.Dispose();
    }

    private static ClientType ResolveClientType(string? configured)
        => string.Equals(configured?.Trim(), "TSP", StringComparison.OrdinalIgnoreCase) ? ClientType.TSP : ClientType.SELF_TSP;

    private static bool IsSupportedApiVersion(string apiVersion)
        => apiVersion.Length == 0 || string.Equals(apiVersion, "v1", StringComparison.OrdinalIgnoreCase);

    private static List<(string FieldLabel, string Value)> BuildIdentityCandidates(
        string? nationalId, string? economicNumber, string? taxMemoryId)
    {
        var candidates = new List<(string, string)>();
        void Add(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var trimmed = value.Trim();
            if (candidates.Any(x => string.Equals(x.Item2, trimmed, StringComparison.Ordinal))) return;
            candidates.Add((label, trimmed));
        }
        Add("شناسه ملی", nationalId);
        Add("شماره اقتصادی", economicNumber);
        Add("شناسه حافظهٔ مالیاتی", taxMemoryId);
        return candidates;
    }

    /// <summary>
    /// ساخت آدرس پایهٔ سامانه از WebServiceAddress (و در نبود آن Moadian:BaseUrl) —
    /// مطابق منطق تست اتصال؛ اگر آدرس با tsp/self-tsp تمام شود آن بخش حذف می‌شود (SDK خودش اضافه می‌کند).
    /// </summary>
    private static bool TryBuildBaseUrl(string? webServiceAddress, string? fallbackBaseUrl, ClientType clientType, out string baseUrl, out string error)
    {
        baseUrl = "";
        error = "";

        var candidate = (string.IsNullOrWhiteSpace(webServiceAddress) ? fallbackBaseUrl : webServiceAddress)?.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            error = "آدرس وب‌سرویس سامانه در این اتصال تنظیم نشده است.";
            return false;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || uri.Host.Length == 0)
        {
            error = "آدرس وب‌سرویس سامانه معتبر نیست.";
            return false;
        }

        var isLoopback = uri.IsLoopback;
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && isLoopback))
        {
            error = "آدرس سامانه باید HTTPS باشد (HTTP فقط از loopback مجاز است).";
            return false;
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        var clientTypeSegment = clientType == ClientType.TSP ? "tsp" : "self-tsp";
        var lastSegment = path.Length == 0 ? "" : path[(path.LastIndexOf('/') + 1)..];
        if (lastSegment.Equals(clientTypeSegment, StringComparison.OrdinalIgnoreCase))
            path = path[..^lastSegment.Length].TrimEnd('/');

        var builder = new UriBuilder(uri) { Path = path + "/", Query = "", Fragment = "" };
        baseUrl = builder.Uri.AbsoluteUri;
        return true;
    }

    private static string DescribeFailure(Exception exception)
    {
        if (exception is TimeoutException || exception is OperationCanceledException)
            return "مهلت پاسخ سامانه به پایان رسید (Timeout).";
        if (exception is HttpRequestException http)
        {
            for (Exception? current = http; current is not null; current = current.InnerException)
                if (current is AuthenticationException)
                    return "برقراری اتصال امن (TLS) با سامانه ناموفق بود؛ گواهی سرور یا خطای شبکه را بررسی کنید.";
            return "دسترسی به سامانه برقرار نشد (DNS/شبکه/فایروال یا HTTPS را بررسی کنید).";
        }
        if (string.IsNullOrWhiteSpace(exception.Message))
            return "خطای نامشخص در تماس با سامانه.";
        return Sanitize(exception.Message);
    }

    private static string Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "خطای نامشخص در تماس با سامانه.";
        return string.Join(' ', message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Truncate(string? value, int max)
        => value is null ? "" : value.Length <= max ? value : value[..max];

    private static string? FirstNonEmpty(params string?[] values)
        => values.Select(v => v?.Trim()).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string? NullIfEmpty(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
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
        FiscalYearId = i.FiscalYearId,
        YearSerial = i.YearSerial,
        DocumentNumber = i.DocumentNumber,
        PayType = i.PayType,
        PayTypeTitle = MoadianPayTypes.Title(i.PayType),
        ServiceProviderId = i.ServiceProviderId,
        Taxid = i.Taxid,
        LastInquiryAt = i.LastInquiryAt,
        LastInquiryStatus = i.LastInquiryStatus,
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
