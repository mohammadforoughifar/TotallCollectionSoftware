using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

/// <summary>
/// کلاینت بانک ایمیل مخاطبین دبیرخانه.
/// کاربر دبیرخانه یک‌بار آدرس را انتخاب می‌کند؛ در بانک می‌ماند و بار بعد
/// در فرم ارسال پیشنهاد می‌شود (اول آدرس‌های همان سازمان گیرنده).
/// </summary>
public interface IEmailBankClientService
{
    /// <summary>پیشنهاد آدرس‌ها — organization = سازمان گیرندهٔ نامه (اولویت اول)</summary>
    Task<EmailBankSuggestDto> SuggestAsync(string? term, string? organization, int take = 12);

    /// <summary>آدرس انتخاب/استفاده‌شده را در بانک ذخیره می‌کند (تکراری ⇒ Duplicate=true)</summary>
    Task<EmailBankSaveResultDto> RememberAsync(string email, string? organization, string? personName);

    /// <summary>آیا آدرس از قبل در بانک هست؟ (برای پیام تکراری)</summary>
    Task<EmailBankEntryDto?> FindAsync(string email);

    Task<PagedResult<EmailBankEntryDto>> ListAsync(string? search, bool? isActive, int page, int pageSize);
    Task<EmailBankSaveResultDto> SaveAsync(SaveEmailBankEntryDto dto);
    Task DeleteAsync(int id);
    Task MergeAsync(int keepId, int removeId);
}

public class EmailBankClientService : IEmailBankClientService
{
    private readonly IApiClient _api;

    public EmailBankClientService(IApiClient api) => _api = api;

    private static string Q(string? s) => Uri.EscapeDataString(s ?? "");

    public Task<EmailBankSuggestDto> SuggestAsync(string? term, string? organization, int take = 12)
        => _api.GetAsync<EmailBankSuggestDto>(
            $"api/email-bank/suggest?term={Q(term)}&organization={Q(organization)}&take={take}");

    public Task<EmailBankSaveResultDto> RememberAsync(string email, string? organization, string? personName)
        => _api.PostAsync<EmailBankSaveResultDto>("api/email-bank/remember",
            new { email, organization, personName });

    public async Task<EmailBankEntryDto?> FindAsync(string email)
    {
        var r = await _api.GetAsync<EmailBankFindResult>($"api/email-bank/find?email={Q(email)}");
        return r?.Found == true ? r.Entry : null;
    }

    public Task<PagedResult<EmailBankEntryDto>> ListAsync(string? search, bool? isActive, int page, int pageSize)
    {
        var active = isActive.HasValue ? isActive.Value.ToString().ToLowerInvariant() : "";
        return _api.GetAsync<PagedResult<EmailBankEntryDto>>(
            $"api/email-bank?search={Q(search)}&isActive={active}&page={page}&pageSize={pageSize}");
    }

    public Task<EmailBankSaveResultDto> SaveAsync(SaveEmailBankEntryDto dto)
        => _api.PostAsync<EmailBankSaveResultDto>("api/email-bank", dto);

    public Task DeleteAsync(int id) => _api.DeleteAsync($"api/email-bank/{id}");

    public Task MergeAsync(int keepId, int removeId)
        => _api.PostAsync<object>("api/email-bank/merge", new { keepId, removeId });

    private sealed class EmailBankFindResult
    {
        public bool Found { get; set; }
        public EmailBankEntryDto? Entry { get; set; }
    }
}
