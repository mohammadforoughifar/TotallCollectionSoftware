#nullable enable
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
using Inventory.Api.Data;
using Inventory.Client.Pages;
using Inventory.Shared.Dtos;

// Deliberately small fakes: no ASP.NET host, EF provider, Razor renderer or network.
public sealed class WoItem { public int Id { get; set; } public int OwnerUserId { get; set; } }
public sealed class AccessInfo { public int UserId { get; set; } }
public sealed class FakeRealtime { public event Action<string>? DataChanged; }
public sealed class FakeLocationChangedEventArgs : EventArgs { }
public sealed class FakeNavigation { public event EventHandler<FakeLocationChangedEventArgs>? LocationChanged; }

public sealed class FakeRead(string path, CancellationToken cancellation)
{
    private readonly TaskCompletionSource<List<WorkOrderSmsEventDto>> _response =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string Path { get; } = path;
    public CancellationToken Cancellation { get; } = cancellation;
    internal Task<List<WorkOrderSmsEventDto>> Response => _response.Task;
    // Intentionally ignore cancellation on response: this exercises the version guard
    // even if a request finishes after its view has closed or changed.
    public void Complete(params WorkOrderSmsEventDto[] rows) => _response.TrySetResult(rows.ToList());
    public void Fail() => _response.TrySetException(new InvalidOperationException("fake status read failed"));
}

public sealed class FakeHttp
{
    private readonly object _gate = new();
    private readonly List<FakeRead> _reads = new();
    private TaskCompletionSource<bool> _added = NewSignal();
    private static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Count { get { lock (_gate) return _reads.Count; } }
    public Task<T> GetFromJsonAsync<T>(string path, CancellationToken ct)
    {
        if (typeof(T) != typeof(List<WorkOrderSmsEventDto>)) throw new InvalidOperationException("Unexpected HTTP request");
        var read = new FakeRead(path, ct);
        TaskCompletionSource<bool> signal;
        lock (_gate) { _reads.Add(read); signal = _added; _added = NewSignal(); }
        signal.TrySetResult(true);
        return (Task<T>)(object)read.Response;
    }
    public async Task<FakeRead> At(int index)
    {
        while (true)
        {
            Task signal;
            lock (_gate)
            {
                if (_reads.Count > index) return _reads[index];
                signal = _added.Task;
            }
            await signal.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}

public sealed partial class StatusHarness : IDisposable
{
    private readonly FakeHttp Http = new();
    private readonly AccessInfo access = new() { UserId = 1 };
    private readonly FakeRealtime Rt = new();
    private readonly FakeNavigation Nav = new();
    private WoItem? detail;
    private bool gardeshOpen, chatMenuOpen;
    private List<WorkOrderSmsEventDto> smsEvents = new();
    private bool smsEventsLoading, smsEventsFailed;
    private long smsDetailSession, smsEventsVersion;
    private CancellationTokenSource? smsDetailReads;
    // Immediate controlled timer: a test explicitly completes each fake status read.
    private readonly WorkOrderSmsStatusPoller smsStatusPoller = new(TimeSpan.FromSeconds(5), 4, (_, _) => Task.CompletedTask);
    private Task lastWatch = Task.CompletedTask;

    public FakeHttp Requests => Http;
    public IReadOnlyList<WorkOrderSmsEventDto> Rows => smsEvents;
    public bool Loading => smsEventsLoading;
    public bool Failed => smsEventsFailed;
    public Task LastWatch => lastWatch;
    public Task Open(int id, int owner = 1) => BeginSmsStatusSession(new WoItem { Id = id, OwnerUserId = owner });
    public Task Refresh() => RefreshSmsEvents();
    public void Close() => CloseDetail();
    private Task InvokeAsync(Func<Task> callback) => callback();
    private void OnRtChanged(string scope) { }
    private void OnLocChanged(object? sender, FakeLocationChangedEventArgs e) { }
    private void StateHasChanged() { } // No renderer in this fixture.
}

public sealed class FakeOrder { public int Id { get; set; } public int? CompanyId { get; set; } public int OwnerUserId { get; set; } }
public sealed class FakeUser { public int Id { get; set; } public string Name { get; set; } = ""; }
public static class UserDisplay { public static string Name(FakeUser user) => user.Name; }
public sealed class FakeDb
{
    public List<FakeOrder> Orders { get; } = new();
    public List<FakeUser> People { get; } = new();
    public List<WorkOrderSmsNotification> Notifications { get; } = new();
    public IQueryable<FakeOrder> WorkOrders => Orders.AsQueryable();
    public IQueryable<FakeUser> Users => People.AsQueryable();
    public IQueryable<WorkOrderSmsNotification> WorkOrderSmsNotifications => Notifications.AsQueryable();
}
namespace Microsoft.EntityFrameworkCore
{
    public static class FakeQueryExtensions
    {
        public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> query) => query;
        public static Task<T?> SingleOrDefaultAsync<T>(this IQueryable<T> query) => Task.FromResult(query.SingleOrDefault());
        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> query) => Task.FromResult(query.ToList());
    }
}
public interface IActionResult { }
public sealed record FakeResult(string Kind, object? Value = null) : IActionResult;
public sealed partial class EndpointHarness(FakeDb db)
{
    private readonly FakeDb _db = db;
    public int? CompanyId { get; set; } = 1;
    public int MyUserId { get; set; } = 1;
    private Task<int?> ActiveCompanyIdAsync() => Task.FromResult(CompanyId);
    private IActionResult BadRequest(object value) => new FakeResult("bad-request", value);
    private IActionResult NotFound() => new FakeResult("not-found");
    private IActionResult Forbid() => new FakeResult("forbid");
    private IActionResult Ok(object value) => new FakeResult("ok", value);
}
