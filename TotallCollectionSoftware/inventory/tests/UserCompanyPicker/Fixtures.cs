using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Inventory.Client.Extensions;
using Inventory.Client.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace UserCompanyPickerTests;

public sealed class TestFixture
{
    public TestAuth Auth { get; } = new();
    public TestHttp Http { get; } = new();
    public TestJs Js { get; } = new();
    public IAuthApi Api { get; } = DispatchProxy.Create<IAuthApi, AuthProxy>();
    public AuthProxy ApiState => (AuthProxy)(object)Api;

    public void Register(IServiceCollection services, bool staticRenderer = true)
    {
        services.AddLogging();
        services.AddSingleton<IAuthState>(Auth);
        services.AddSingleton(Api);
        services.AddSingleton(DispatchProxy.Create<IReferrerService, ReferrerProxy>());
        services.AddSingleton<IToastService, ToastService>();
        services.AddSingleton<LayoutState>();
        services.AddSingleton(new ApiOptions { BaseUrl = "" });
        services.AddSingleton(new HttpClient(Http) { BaseAddress = new Uri("https://fixture.invalid/") });
        if (staticRenderer)
        {
            services.AddSingleton<IJSRuntime>(Js);
            services.AddSingleton<NavigationManager, TestNavigation>();
        }
    }
}

public sealed class TestHttp : HttpMessageHandler
{
    public List<LookupItem> Companies { get; } = new()
    {
        new() { Id = 1001, Name = "شرکت کیان" },
        new() { Id = 1002, Name = "پارس تجارت" },
        new() { Id = 1003, Name = "OMEGA Group" },
        new() { Id = 1004, Name = "کمپانی هم‌نام" },
        new() { Id = 1005, Name = "کمپانی هم‌نام" }
    };
    public Dictionary<int, List<int>> Access { get; } = new() { [11] = new() { 1001, 1002 }, [12] = new() { 1003 } };
    public Func<int, Task<List<int>>>? AccessLoader;
    public List<(int UserId, List<int> CompanyIds)> Assignments { get; } = new();

    public TestHttp()
    {
        Companies.AddRange(Enumerable.Range(2000, 60).Select(i => new LookupItem { Id = i, Name = "کمپانی آزمایشی " + i }));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/api/SystemCompanies") return Json(Companies);
        if (path == "/api/SystemOrganizations") return Json(new object[]
        {
            new { OrganizationId = 701, CompanyId = (int?)1001, NameUnit = "واحد کیان", NameUniq = "K", IsActive = true },
            new { OrganizationId = 702, CompanyId = (int?)1002, NameUnit = "واحد پارس", NameUniq = "P", IsActive = true },
            new { OrganizationId = 703, CompanyId = (int?)1003, NameUnit = "واحد امگا", NameUniq = "O", IsActive = true },
            new { OrganizationId = 704, CompanyId = (int?)null, NameUnit = "واحد مشترک", NameUniq = "S", IsActive = true },
            new { OrganizationId = 705, CompanyId = (int?)1001, NameUnit = "واحد غیرفعال", NameUniq = "D", IsActive = false }
        });
        if (path == "/api/roles") return Json(new[] { new { Id = 10, Name = "Operator", IsActive = true }, new { Id = 20, Name = "Admin", IsActive = true } });
        if (path.StartsWith("/api/workorders/allowed/")) return Json(Array.Empty<int>());
        if (path.StartsWith("/api/company-context/users/"))
        {
            var userId = int.Parse(path.Split('/').Last());
            if (request.Method == HttpMethod.Put)
            {
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var ids = payload.RootElement.GetProperty("companyIds").EnumerateArray().Select(x => x.GetInt32()).ToList();
                Assignments.Add((userId, ids));
                Access[userId] = ids;
                return Json(new { count = ids.Count });
            }
            var access = AccessLoader is null ? Access.GetValueOrDefault(userId, new()) : await AccessLoader(userId);
            return Json(access);
        }
        throw new InvalidOperationException("Unexpected request: " + request.Method + " " + path);
    }

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
}

public class AuthProxy : DispatchProxy
{
    public List<UserDto> Users { get; } = new()
    {
        new() { Id = 11, Username = "first", FirstName = "کاربر", LastName = "اول", Role = "Operator", RoleIds = new() { 10 }, IsActive = true, CreatedAt = new DateTime(2026, 11, 21), OrganizationId = 701 },
        new() { Id = 12, Username = "second", FirstName = "کاربر", LastName = "دوم", Role = "Operator", RoleIds = new() { 10 }, IsActive = true, CreatedAt = new DateTime(2026, 11, 21), OrganizationId = 703 }
    };
    public List<UserDto> Saves { get; } = new();
    public Task? SaveGate;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        switch (method!.Name)
        {
            case nameof(IAuthApi.GetUsersAsync): return Task.FromResult(Users.ToList());
            case nameof(IAuthApi.SaveUserAsync):
                var copy = JsonSerializer.Deserialize<UserDto>(JsonSerializer.Serialize((UserDto)args![0]!))!;
                if (copy.Id == 0) copy.Id = 77 + Saves.Count;
                return CompleteSave(copy);
            default: throw new InvalidOperationException("Unexpected auth service call: " + method.Name);
        }
    }
    private async Task<UserDto> CompleteSave(UserDto copy)
    {
        if (SaveGate is { } gate) await gate;
        Saves.Add(copy);
        Users.RemoveAll(u => u.Id == copy.Id); Users.Add(copy);
        return copy;
    }
}

public class ReferrerProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == nameof(IReferrerService.GetAllAsync)
        ? Task.FromResult(new List<Referrer>())
        : throw new InvalidOperationException("Unexpected referrer call");
}

public sealed class TestJs : IJSRuntime
{
    public List<string> Calls { get; } = new();
    public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, default, args);
    public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken ct, object?[]? args)
    {
        Calls.Add(identifier);
        return ValueTask.FromResult(identifier == "uiRect"
            ? (T)(object)new UiRect(20, 100, 420, 142, 400, 42, 900, 800)
            : default(T)!);
    }
}

public sealed class TestNavigation : NavigationManager
{
    public TestNavigation() => Initialize("https://fixture.invalid/", "https://fixture.invalid/users");
    protected override void NavigateToCore(string uri, bool forceLoad) { }
}

public sealed class TestAuth : IAuthState
{
    public bool IsAdmin { get; set; } = true;
    public bool IsLoggedIn => true;
    public bool IsOperator => !IsAdmin;
    public bool IsReferrer => false;
    public bool CanOperate => true;
    public string? Token => null;
    public int UserId => 1;
    public string DisplayName => "مدیر آزمایشی";
    public string Role => IsAdmin ? "Admin" : "Operator";
    public int? ReferrerId => null;
    public bool Has(string permission) => IsAdmin;
    public bool HasDirect(string permission) => Has(permission);
    public bool HasModule(string module) => IsAdmin;
    public bool HasModuleDirect(string module) => IsAdmin;
    public bool CanSee(string module) => IsAdmin;
    public event Action? Changed { add { } remove { } }
    public Task InitializeAsync() => Task.CompletedTask;
    public Task SignInAsync(LoginResponse login) => Task.CompletedTask;
    public Task SignOutAsync() => Task.CompletedTask;
}
