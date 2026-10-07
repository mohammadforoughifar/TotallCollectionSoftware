using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

/// <summary>
/// صورتحساب‌های مودیان به تفکیک خدمات‌دهنده — فهرست + عملیات ردیفی
/// (ارسال رسمی به سامانه و استعلام رسمی وضعیت). صفحات فقط با این اینترفیس کار می‌کنند.
/// </summary>
public interface IMoadianProviderInvoicesClientService
{
    Task<List<MoadianInvoice>> GetByProviderAsync(int providerId, string? search, MoadianInvoiceStatus? status = null);
    Task<MoadianSubmissionResultDto> SendAsync(int invoiceId);
    Task<MoadianSubmissionResultDto> InquiryAsync(int invoiceId);
}

public sealed class MoadianProviderInvoicesClientService : IMoadianProviderInvoicesClientService
{
    private readonly IApiClient _api;
    public MoadianProviderInvoicesClientService(IApiClient api) => _api = api;

    public Task<List<MoadianInvoice>> GetByProviderAsync(int providerId, string? search, MoadianInvoiceStatus? status = null)
    {
        var q = new List<string> { $"providerId={providerId}" };
        if (status is not null) q.Add($"status={(int)status}");
        if (!string.IsNullOrWhiteSpace(search)) q.Add($"search={Uri.EscapeDataString(search)}");
        return _api.GetAsync<List<MoadianInvoice>>($"api/moadian/provider-invoices?{string.Join("&", q)}");
    }

    // فراخوانی‌های بلند: ارسال/استعلام با سامانه مودیان ممکن است ده‌ها ثانیه طول بکشد.
    public Task<MoadianSubmissionResultDto> SendAsync(int invoiceId)
        => _api.PostLongAsync<MoadianSubmissionResultDto>($"api/moadian/provider-invoices/{invoiceId}/send", new { });

    public Task<MoadianSubmissionResultDto> InquiryAsync(int invoiceId)
        => _api.PostLongAsync<MoadianSubmissionResultDto>($"api/moadian/provider-invoices/{invoiceId}/inquiry", new { });
}
