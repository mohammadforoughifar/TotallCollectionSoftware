using System.Text.Json;
using Inventory.Shared.Dtos.Moadian;
using Microsoft.JSInterop;
using InvoiceVM = Inventory.Shared.Dtos.Moadian.VmInvoiceItems.InvoiceVM;

namespace Inventory.Client.Services;

/// <summary>
/// سرویس کلاینت سامانه مودیان (SDK 0.0.27) — پیوند مستقیم به MoadianController.
/// توجه: کنترلر مودیان فاقد [Route] است؛ با رویت پیش‌فرض ASP.NET همهٔ endpointها زیر پیشوند «Moadian/» قرار می‌گیرند.
/// </summary>
public interface IMoadianService
{
    // ---------- شرکت / سال مالی / مشتری ----------
    /// <summary>فقط خدمات‌دهنده‌های مودیان متعلق به شرکت فعال (CompanyId) را می‌خواند.</summary>
    Task<List<VMShowCpinfoByCompany>> GetCpInfosAsync(int? companyId = null);
    Task<ApiModel> SaveCpInfoAsync(VMShowCpinfoByCompany vm);
    Task<ApiModel> DeleteCpInfoAsync(int id);
    Task<ApiModel> DiagnoseAsync(); // گزارش اسکیمای مودیان (جدول/ستون/ردیف)
    Task<ApiModel> DiagnoseListsAsync(int cpinfoId); // تشخیص عمیق: چرا لیست‌ها با وجود ردیف خالی‌اند
    /// <summary>پیش‌نمایش شمارهٔ صورتحساب بعدی (حداکثر شمارهٔ شرکت + ۱).</summary>
    Task<ApiModel> NextInvoiceNumberAsync(int cpinfoId);
    Task<List<VMFiscalYear>> GetFiscalYearsAsync(int cpinfoId);
    Task<ApiModel> SaveFiscalYearAsync(VMFiscalYear vm);
    Task<List<VMCustomerList>> GetCustomersAsync(int cpinfoId);
    Task<ApiModel> InsertCustomerAsync(VMCustomerList vm);
    Task<ApiModel> UpdateCustomerAsync(VMCustomerList vm);

    // ---------- اتصال به سامانه ----------
    Task<ApiModel> IsConnectAsync(VMConnection vm);
    Task<VMConnection?> GetConnectionAsync(int cpinfoId);
    Task<ApiModel> SaveConnectionAsync(VMConnection vm);

    // ---------- صورتحساب ----------
    Task<ApiModel> GetAllInvoicesAsync(string operation, int cpinfoId, DateTime? firstDate, DateTime? endDate, string? search, int page, int pageSize);
    Task<ApiModel> AddInvoiceAsync(InvoiceVM vm);
    Task<ApiModel> SendInvoiceAsync(InvoiceVM vm);
    Task<ApiModel> PreviewSendInvoiceAsync(InvoiceVM vm);
    Task<ApiModel> EslahiInvoiceAsync(InvoiceVM vm);
    Task<ApiModel> EbtalInvoiceAsync(InvoiceVM vm);
    Task<ApiModel> EstelamInvoiceAsync(string referenceId, int invoiceId, string dateOrder);
    Task<ApiModel> ErrorInvoiceAsync(string referenceId);
    Task<ApiModel> UpdateStatusInvoiceAsync(int invoiceId);
    Task<InvoiceVM?> GetInvoiceAsync(int id);
    Task<ApiModel> DeleteInvoiceAsync(int id);

    // ---------- کالا / واحد سنجش ----------
    Task<ApiModel> GetGoodsAsync(int cpinfoId, string? search, int page, int pageSize);
    Task<ApiModel> InsertGoodAsync(VMGoodsOrService vm);
    Task<ApiModel> UpdateGoodAsync(VMGoodsOrService vm);
    Task<List<VMGoodsOrServicesList>> GetGoodsComboAsync(int cpinfoId);
    Task<ApiModel> GetUnitsAsync(int cpinfoId, string? search, int page, int pageSize);
    Task<ApiModel> InsertUnitAsync(VMUnitOfMeasurement vm);
    Task<ApiModel> UpdateUnitAsync(VMUnitOfMeasurement vm);
    Task<ApiModel> DeleteUnitAsync(int id);
    Task<List<VMUnitOfMeasurement>> GetUnitsComboAsync(int cpinfoId);
}

public class MoadianService : IMoadianService
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };
    private readonly IApiClient _api;
    private readonly IJSRuntime _js;
    public MoadianService(IApiClient api, IJSRuntime js) { _api = api; _js = js; }

    /// <summary>مبدل Data خامِ ApiModel به نوع مجرّد (JsonElement → T) — هرگز exception نمی‌دهد؛
    /// پاسخ‌های خطا با Data=false می‌آیند و در آن صورت default برمی‌گردد (پیام خطا از m.Message).</summary>
    public static T? As<T>(ApiModel? m)
    {
        if (m?.Data is null) return default;
        try
        {
            return m.Data switch
            {
                JsonElement el when el.ValueKind == JsonValueKind.Null => default,
                JsonElement el => el.Deserialize<T>(Opts),
                var v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v), Opts)
            };
        }
        catch { return default; }
    }

    /// <summary>برای فراخوانی‌هایی که باید داده برگردانند: خطای کسب‌وکار سرور را با پیام فارسی به‌صورت exception می‌رساند.</summary>
    private static T? Must<T>(ApiModel? m) where T : class
        => AsStrict<T>(m, "دریافت داده از سرور");

    /// <summary>مثل As ولی تشخیصی: هیچ شکستی را قورت نمی‌دهد — علت و پاسخ خام را در پیام خطا می‌آورد
    /// (تا لحظه‌ای که لیست «به‌سکوت خالی» شود، کادر قرمز علت دقیق را نشان بدهد).</summary>
    public static T? AsStrict<T>(ApiModel? m, string what)
    {
        if (m is null) throw new Exception(what + ": هیچ پاسخی از سرور دریافت نشد. (آیا API در دسترس است؟)");
        if (!m.Success) throw new Exception(what + ": " + (m.Message ?? "خطای نامشخص سرور") + " (کد " + m.Status + ")");
        var raw = m.Data switch
        {
            null => "null",
            JsonElement el => el.GetRawText(),
            var v => JsonSerializer.Serialize(v)
        };
        if (m.Data is null || (m.Data is JsonElement je && je.ValueKind == JsonValueKind.Null))
            throw new Exception(what + ": بدنهٔ دادهٔ پاسخ خالی است. (کد " + m.Status + " — " + (m.Message ?? "-") + ")");
        var value = As<T>(m);
        if (value is null)
            throw new Exception(what + ": ساختار پاسخ سرور قابل خواندن نیست. پاسخ خام: " + (raw.Length > 400 ? raw[..400] + "…" : raw));
        return value;
    }

    // ---------- شرکت / سال مالی / مشتری ----------
    public async Task<List<VMShowCpinfoByCompany>> GetCpInfosAsync(int? companyId = null)
    {
        // شناسهٔ شرکت را صریحاً به API می‌فرستیم؛ بنابراین فیلتر فرم اطلاعات مودیان
        // به وجود/ترتیب اعمال DefaultRequestHeaders وابسته نیست.
        var activeCompanyId = companyId;
        if (activeCompanyId is not > 0)
        {
            try
            {
                var saved = await _js.InvokeAsync<string?>("localStorage.getItem", "activeCompanyId");
                if (int.TryParse(saved, out var id) && id > 0) activeCompanyId = id;
            }
            catch { }
        }

        // اگر در نخستین رندر هنوز localStorage/CompanyContext آماده نباشد، درخواست را بدون
        // query می‌فرستیم تا API از هدر یا شرکت پیش‌فرضِ مجاز کاربر استفاده کند. API در هر دو
        // حالت فقط CompanyId واحد را برمی‌گرداند و هرگز لیست همهٔ خدمات‌دهندگان را نمی‌دهد.
        var path = activeCompanyId is > 0
            ? $"Moadian/GetCpInfos?companyId={activeCompanyId.Value}"
            : "Moadian/GetCpInfos";
        var list = Must<List<VMShowCpinfoByCompany>>(await _api.GetAsync<ApiModel>(path)) ?? new();

        // فیلتر منبع حقیقت، سمت سرور است: ApiClient روی همین درخواست X-Company-Id
        // می‌گذارد و API فقط رکوردهای همان CompanyId را برمی‌گرداند. اینجا دوباره فیلتر
        // نمی‌کنیم تا در صورت پاسخِ یک نسخهٔ قدیمی‌تر API که فیلد CompanyId را ارسال نکرده
        // بود، رکوردهای معتبر به‌اشتباه از رابط کاربری حذف نشوند.
        return list;
    }

    public async Task<ApiModel> SaveCpInfoAsync(VMShowCpinfoByCompany vm)
    {
        // در فرم ثبت شرکت مودیان، CompanyId باید همواره همراه payload باشد؛ تکیه‌کردن
        // صرف به DefaultRequestHeaders باعث می‌شد در شروع برنامه بعضی رکوردهای تازه زیر
        // شرکت پیش‌فرض ذخیره شوند و در شرکت انتخاب‌شده دیده نشوند.
        if (vm.CompanyId is not > 0)
        {
            try
            {
                var saved = await _js.InvokeAsync<string?>("localStorage.getItem", "activeCompanyId");
                if (int.TryParse(saved, out var activeCompanyId) && activeCompanyId > 0)
                    vm.CompanyId = activeCompanyId;
            }
            catch { }
        }

        return await _api.PostAsync<ApiModel>("Moadian/SaveCpInfo", vm);
    }

    public Task<ApiModel> DeleteCpInfoAsync(int id)
        => _api.DeleteAsync<ApiModel>($"Moadian/DeleteCpInfo/{id}");

    public Task<ApiModel> DiagnoseAsync()
        => _api.GetAsync<ApiModel>("Moadian/Diagnose");

    public Task<ApiModel> NextInvoiceNumberAsync(int cpinfoId)
        => _api.GetAsync<ApiModel>($"Moadian/NextInvoiceNumber?cpinfoId={cpinfoId}");

    public Task<ApiModel> DiagnoseListsAsync(int cpinfoId)
        => _api.GetAsync<ApiModel>($"Moadian/DiagnoseLists?cpinfoId={cpinfoId}");

    public async Task<List<VMFiscalYear>> GetFiscalYearsAsync(int cpinfoId)
        => Must<List<VMFiscalYear>>(await _api.GetAsync<ApiModel>($"Moadian/GetFiscalYears/{cpinfoId}")) ?? new();

    public Task<ApiModel> SaveFiscalYearAsync(VMFiscalYear vm)
        => _api.PostAsync<ApiModel>("Moadian/SaveFiscalYear", vm);

    public async Task<List<VMCustomerList>> GetCustomersAsync(int cpinfoId)
        => Must<List<VMCustomerList>>(await _api.GetAsync<ApiModel>($"Moadian/GetAllCostomer?cpinfoid={cpinfoId}")) ?? new();

    public Task<ApiModel> InsertCustomerAsync(VMCustomerList vm)
        => _api.PostAsync<ApiModel>("Moadian/InsertCustomerAsync", vm);

    public Task<ApiModel> UpdateCustomerAsync(VMCustomerList vm)
        => _api.PutAsync<ApiModel>("Moadian/UpdateCustomer", vm);

    // ---------- اتصال به سامانه ----------
    public Task<ApiModel> IsConnectAsync(VMConnection vm)
        => _api.PostAsync<ApiModel>("Moadian/GetIsConnectToMoadian", vm);

    public async Task<VMConnection?> GetConnectionAsync(int cpinfoId)
    {
        // «اتصال ثبت نشده» پاسخ موفق با Data خالی است، نه خطا — Must روی آن exception می‌داد
        // و فرم اتصال، اطلاعات شرکت قبلی را روی صفحه نگه می‌داشت. خطای واقعی سرور همچنان پرتاب می‌شود.
        var r = await _api.GetAsync<ApiModel>($"Moadian/GetConnectForCpInfo/{cpinfoId}");
        if (r is null) return null;
        if (!r.Success)
            throw new Exception("دریافت داده از سرور: " + (r.Message ?? "خطای نامشخص سرور") + " (کد " + r.Status + ")");
        return As<VMConnection>(r);
    }

    public Task<ApiModel> SaveConnectionAsync(VMConnection vm)
        => _api.PostAsync<ApiModel>("Moadian/ConnectionApiAsync", vm);

    // ---------- صورتحساب ----------
    public Task<ApiModel> GetAllInvoicesAsync(string operation, int cpinfoId, DateTime? firstDate, DateTime? endDate, string? search, int page, int pageSize)
    {
        var qs = $"operation={Uri.EscapeDataString(operation)}&CpinfoId={cpinfoId}"
             + $"&firstDate={firstDate:yyyy-MM-dd HH:mm:ss}&endDate={endDate:yyyy-MM-dd HH:mm:ss}"
             + $"&searchTerm={Uri.EscapeDataString(search ?? "")}&page={page}&pageSize={pageSize}";
        return _api.GetAsync<ApiModel>($"Moadian/GetAllInvoice?{qs}");
    }

    public Task<ApiModel> AddInvoiceAsync(InvoiceVM vm) => _api.PostAsync<ApiModel>("Moadian/AddInvoice", vm);
    public Task<ApiModel> SendInvoiceAsync(InvoiceVM vm) => _api.PostAsync<ApiModel>("Moadian/SendInvoiceAsync", vm);
    public Task<ApiModel> PreviewSendInvoiceAsync(InvoiceVM vm) => _api.PostAsync<ApiModel>("Moadian/PreviewSendInvoiceAsync", vm);
    public Task<ApiModel> EslahiInvoiceAsync(InvoiceVM vm) => _api.PostAsync<ApiModel>("Moadian/EslahiInvoiceAsync", vm);
    public Task<ApiModel> EbtalInvoiceAsync(InvoiceVM vm) => _api.PostAsync<ApiModel>("Moadian/EbtalInvoiceAsync", vm);

    public Task<ApiModel> EstelamInvoiceAsync(string referenceId, int invoiceId, string dateOrder)
        => _api.GetAsync<ApiModel>($"Moadian/EstelamInvoiceAsync?ReferenceId={Uri.EscapeDataString(referenceId)}&InvoiceId={invoiceId}&dateorder={Uri.EscapeDataString(dateOrder)}");

    public Task<ApiModel> ErrorInvoiceAsync(string referenceId)
        => _api.GetAsync<ApiModel>($"Moadian/ErrorInvoiceAsync?ReferenceId={Uri.EscapeDataString(referenceId)}");

    public Task<ApiModel> UpdateStatusInvoiceAsync(int invoiceId)
        => _api.GetAsync<ApiModel>($"Moadian/UpdateStatusInvoice/{invoiceId}");

    public async Task<InvoiceVM?> GetInvoiceAsync(int id)
        => Must<InvoiceVM>(await _api.GetAsync<ApiModel>($"Moadian/GetInvoiceByIdAsync/{id}"));

    public Task<ApiModel> DeleteInvoiceAsync(int id) => _api.DeleteAsync<ApiModel>($"Moadian/Invoice/{id}");

    // ---------- کالا / واحد سنجش ----------
    public Task<ApiModel> GetGoodsAsync(int cpinfoId, string? search, int page, int pageSize)
        => _api.GetAsync<ApiModel>($"Moadian/GetGoodOrService?cpinfoId={cpinfoId}&searchTerm={Uri.EscapeDataString(search ?? "")}&page={page}&pageSize={pageSize}");

    public Task<ApiModel> InsertGoodAsync(VMGoodsOrService vm) => _api.PostAsync<ApiModel>("Moadian/InsertGoodOrServiceAsync", vm);
    public Task<ApiModel> UpdateGoodAsync(VMGoodsOrService vm) => _api.PutAsync<ApiModel>("Moadian/UpdateGoodOrService", vm);

    public async Task<List<VMGoodsOrServicesList>> GetGoodsComboAsync(int cpinfoId)
        => Must<List<VMGoodsOrServicesList>>(await _api.GetAsync<ApiModel>($"Moadian/GetGoodOrServiceForComboAsync?cpinfoid={cpinfoId}")) ?? new();

    public Task<ApiModel> GetUnitsAsync(int cpinfoId, string? search, int page, int pageSize)
        => _api.GetAsync<ApiModel>($"Moadian/GetAllUnitOfMeasurementAsync?cpinfoid={cpinfoId}&searchTerm={Uri.EscapeDataString(search ?? "")}&page={page}&pageSize={pageSize}");

    public Task<ApiModel> InsertUnitAsync(VMUnitOfMeasurement vm) => _api.PostAsync<ApiModel>("Moadian/InsertUnitOfMeasurementAsync", vm);
    public Task<ApiModel> UpdateUnitAsync(VMUnitOfMeasurement vm) => _api.PutAsync<ApiModel>("Moadian/UpdateUnitOfMeasurementAsync", vm);
    public Task<ApiModel> DeleteUnitAsync(int id) => _api.DeleteAsync<ApiModel>($"Moadian/{id}");
    public async Task<List<VMUnitOfMeasurement>> GetUnitsComboAsync(int cpinfoId)
        => Must<List<VMUnitOfMeasurement>>(await _api.GetAsync<ApiModel>($"Moadian/GetUnitOfMeasurementForComboAsync?cpinfoid={cpinfoId}")) ?? new();
}
