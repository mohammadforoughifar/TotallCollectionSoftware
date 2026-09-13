using Inventory.Shared.Dtos;
using Microsoft.JSInterop;

namespace Inventory.Client.Services;

/// <summary>قرارداد وضعیت احراز هویت سمت کلاینت.</summary>
public interface IAuthState
{
    bool IsLoggedIn { get; }
    bool IsAdmin { get; }
    bool IsOperator { get; }
    bool IsReferrer { get; }

    /// <summary>مدیر یا اپراتور — دسترسی به بخش‌های عملیاتی</summary>
    bool CanOperate { get; }
    string? Token { get; }
    int UserId { get; }
    string DisplayName { get; }
    string Role { get; }

    /// <summary>شناسه معرف مرتبط با کاربر جاری (اگر کاربر معرف باشد)</summary>
    int? ReferrerId { get; }

    /// <summary>بررسی دسترسی دقیق: Has("Orders.Read") یا Has("Dashboards.Financial")</summary>
    bool Has(string permission);

    /// <summary>آیا کاربر حداقل یک دسترسی از این ماژول دارد؟</summary>
    bool HasModule(string module);
    event Action? Changed;

    Task InitializeAsync();
    Task SignInAsync(LoginResponse login);
    Task SignOutAsync();
}

/// <summary>نگه‌داری توکن JWT و اطلاعات کاربر جاری (ذخیره در localStorage).</summary>
public class AuthState : IAuthState
{
    private readonly IJSRuntime _js;
    private LoginResponse? _session;

    public AuthState(IJSRuntime js) => _js = js;

    public bool IsLoggedIn => _session is not null;
    public bool IsAdmin => _session?.Role == "Admin";
    public bool IsOperator => _session?.Role == "Operator";
    public bool IsReferrer => _session?.Role == "Referrer";
    public bool CanOperate => IsAdmin || IsOperator;
    public string? Token => _session?.Token;
    public int UserId => _session?.UserId ?? 0;
    public string DisplayName => _session?.DisplayName ?? "";
    public string Role => _session?.Role ?? "";
    public int? ReferrerId => _session?.ReferrerId;

    // ================== بررسی دسترسی‌های RBAC ==================
    private HashSet<string>? _permCache;
    private HashSet<string> Perms =>
        _permCache ??= new HashSet<string>(_session?.Permissions ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

    public bool Has(string permission) => Perms.Contains(permission);

    public bool HasModule(string module) =>
        Perms.Any(p => p.StartsWith(module + ".", StringComparison.OrdinalIgnoreCase));

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        try
        {
            var json = await _js.InvokeAsync<string?>("localStorage.getItem", "authSession");
            if (!string.IsNullOrWhiteSpace(json))
                _session = System.Text.Json.JsonSerializer.Deserialize<LoginResponse>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            _permCache = null;
        }
        catch { _session = null; _permCache = null; }
        Changed?.Invoke();
    }

    public async Task SignInAsync(LoginResponse login)
    {
        _session = login;
        _permCache = null;
        await _js.InvokeVoidAsync("localStorage.setItem", "authSession",
            System.Text.Json.JsonSerializer.Serialize(login));
        Changed?.Invoke();
    }

    public async Task SignOutAsync()
    {
        _session = null;
        _permCache = null;
        await _js.InvokeVoidAsync("localStorage.removeItem", "authSession");
        Changed?.Invoke();
    }
}

/// <summary>قرارداد سرویس احراز هویت.</summary>
public interface IAuthApi
{
    Task<LoginResponse> LoginAsync(LoginRequest request);
    Task<List<UserDto>> GetUsersAsync();
    /// <summary>کاربران با صفحه‌بندی — خروجی استاندارد GetAll.</summary>
    Task<PagedResult<UserDto>> GetUsersPagedAsync(int page = 1, int pageSize = 20);
    Task<UserDto> SaveUserAsync(UserDto user);
    Task DeleteUserAsync(int id);
    Task<ReferrerDashboard> GetMyDashboardAsync();
    Task<List<ReferrerPayment>> GetMyPaymentsAsync();
    /// <summary>پرداخت‌های پنل معرف با صفحه‌بندی.</summary>
    Task<PagedResult<ReferrerPayment>> GetMyPaymentsPagedAsync(int page = 1, int pageSize = 20);
    Task<List<ReferrerProductItem>> GetMyProductsAsync(string? search = null);
    /// <summary>کالاهای پنل معرف با صفحه‌بندی.</summary>
    Task<PagedResult<ReferrerProductItem>> GetMyProductsPagedAsync(string? search = null, int page = 1, int pageSize = 20);
    Task ChangePasswordAsync(ChangePasswordRequest request);
}

/// <summary>پیاده‌سازی سرویس احراز هویت سمت کلاینت.</summary>
public class AuthApi : IAuthApi
{
    private readonly IApiClient _api;
    public AuthApi(IApiClient api) => _api = api;

    public Task<LoginResponse> LoginAsync(LoginRequest request)
        => _api.PostAsync<LoginResponse>("api/auth/login", request);

    /// <summary>همهٔ کاربران (برای کمبو/جداول بدون صفحه‌بندی سمت کلاینت).</summary>
    public Task<List<UserDto>> GetUsersAsync()
        => PagedFetch.AllAsync<UserDto>((page, size) =>
            _api.GetAsync<PagedResult<UserDto>>($"api/users?page={page}&pageSize={size}"));

    public Task<PagedResult<UserDto>> GetUsersPagedAsync(int page = 1, int pageSize = 20)
        => _api.GetAsync<PagedResult<UserDto>>($"api/users?page={page}&pageSize={pageSize}");

    public Task<UserDto> SaveUserAsync(UserDto user)
        => _api.PostAsync<UserDto>("api/users", user);

    public Task DeleteUserAsync(int id)
        => _api.DeleteAsync($"api/users/{id}");

    public Task<ReferrerDashboard> GetMyDashboardAsync()
        => _api.GetAsync<ReferrerDashboard>("api/my/dashboard");

    public Task<List<ReferrerPayment>> GetMyPaymentsAsync()
        => PagedFetch.AllAsync<ReferrerPayment>((page, size) =>
            _api.GetAsync<PagedResult<ReferrerPayment>>($"api/my/payments?page={page}&pageSize={size}"));

    public Task<PagedResult<ReferrerPayment>> GetMyPaymentsPagedAsync(int page = 1, int pageSize = 20)
        => _api.GetAsync<PagedResult<ReferrerPayment>>($"api/my/payments?page={page}&pageSize={pageSize}");

    public Task<List<ReferrerProductItem>> GetMyProductsAsync(string? search = null)
        => PagedFetch.AllAsync<ReferrerProductItem>((page, size) =>
            _api.GetAsync<PagedResult<ReferrerProductItem>>(
                $"api/my/products?search={Uri.EscapeDataString(search ?? "")}&page={page}&pageSize={size}"));

    public Task<PagedResult<ReferrerProductItem>> GetMyProductsPagedAsync(string? search = null, int page = 1, int pageSize = 20)
        => _api.GetAsync<PagedResult<ReferrerProductItem>>(
            $"api/my/products?search={Uri.EscapeDataString(search ?? "")}&page={page}&pageSize={pageSize}");

    public Task ChangePasswordAsync(ChangePasswordRequest request)
        => _api.PostAsync<object>("api/auth/change-password", request);
}
