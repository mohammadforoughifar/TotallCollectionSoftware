using System.Collections;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Inventory.Client.Pages.Settings;
using Inventory.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace RoleGroupSelectionTests;

public record Category(string Title, string[] Modules);

public sealed class Fixture : HttpMessageHandler
{
    public static List<Category> Categories { get; } = ((IEnumerable)typeof(Roles)
        .GetField("ModuleCategories", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!)
        .Cast<object>().Select(c => new Category((string)c.GetType().GetProperty("Title")!.GetValue(c)!,
            (string[])c.GetType().GetProperty("Modules")!.GetValue(c)!)).ToList();
    public List<Roles.PermissionDto> Permissions { get; } = new();
    public List<Roles.RoleDto> RoleList { get; } = new();
    public Dictionary<int, List<int>> Assignments { get; } = new();
    public List<(string Method, Roles.RoleDto Role)> Saves { get; } = new();
    public const int UnknownAssignedId = 987654;

    public Fixture()
    {
        foreach (var module in Categories.SelectMany(c => c.Modules).Distinct())
        {
            Add(module, "View");
            Add(module, module == "Moadian" ? "InvoiceCreate" : "Create");
        }
        Add("Moadian", "Configure"); // Legacy permission: never selected by visible group controls.
        Add("Moadian", "ProviderInvoices");
        Add("CustomAlpha", "View"); Add("CustomAlpha", "Create");
        Add("CustomBeta", "View"); Add("CustomBeta", "Create");
        Reset();
    }
    private void Add(string module, string action) => Permissions.Add(new() { Id = Permissions.Count + 1, Module = module, Action = action });
    public int Id(string module, string action) => Permissions.Single(p => p.Module == module && p.Action == action).Id;
    public void Reset()
    {
        RoleList.Clear(); Assignments.Clear(); Saves.Clear();
        RoleList.Add(new() { Id = 10, Name = "نقش آزمایشی", Description = "فقط دادهٔ ساختگی", IsActive = true });
        Assignments[10] = new() { Id("WorkOrders", "View"), Id("Moadian", "Configure"), UnknownAssignedId };
    }
    public void Register(IServiceCollection services, bool staticRenderer)
    {
        services.AddLogging();
        services.AddSingleton(new HttpClient(this) { BaseAddress = new Uri("https://fixture.invalid/") });
        services.AddSingleton<IToastService, ToastService>();
        if (staticRenderer)
        {
            services.AddSingleton<IJSRuntime, NoopJs>();
            services.AddSingleton<NavigationManager, TestNavigation>();
        }
    }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Get)
        {
            if (path == "/api/roles") return Json(RoleList);
            if (path == "/api/permissions") return Json(Permissions);
            if (path.StartsWith("/api/roles/") && path.EndsWith("/permissions"))
                return Json(Assignments.GetValueOrDefault(int.Parse(path.Split('/')[3]), new()));
        }
        if ((request.Method == HttpMethod.Post || request.Method == HttpMethod.Put) && path.StartsWith("/api/roles"))
        {
            var dto = (await request.Content!.ReadFromJsonAsync<Roles.RoleDto>(cancellationToken: ct))!;
            Saves.Add((request.Method.Method, dto));
            var id = request.Method == HttpMethod.Post ? RoleList.Max(r => r.Id) + 1 : int.Parse(path.Split('/').Last());
            Assignments[id] = dto.PermissionIds.ToList();
            RoleList.RemoveAll(r => r.Id == id);
            RoleList.Add(new() { Id = id, Name = dto.Name, Description = dto.Description, IsActive = dto.IsActive });
            return Json(new { id });
        }
        throw new InvalidOperationException("Unexpected request: " + request.Method + " " + path);
    }
    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
}

public sealed class NoopJs : IJSRuntime
{
    public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
    public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken ct, object?[]? args) => InvokeAsync<T>(identifier, args);
}
public sealed class TestNavigation : NavigationManager
{
    public TestNavigation() => Initialize("https://fixture.invalid/", "https://fixture.invalid/settings/roles");
    protected override void NavigateToCore(string uri, bool forceLoad) { }
}
