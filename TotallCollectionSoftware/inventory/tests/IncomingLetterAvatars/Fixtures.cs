using System.Collections.Concurrent;
using System.Reflection;
using Inventory.Client.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace IncomingLetterAvatarTests;

public sealed record ServiceCall(string Method, int? Id, int? Page = null, int? PageSize = null, bool? Starred = null);

public sealed class Fixture
{
    public static readonly DateTime Entered = new(2026, 11, 18, 10, 0, 0);
    public const string ExternalSender = "سازمان مبدأ بیرونی";
    public List<IncomingLetterListItemDto> Inbox { get; } = new();
    public IncomingLetterDetailDto Letter { get; private set; } = new();
    public List<ErjaTreeNodeDto> Tree { get; set; } = new();
    public ConcurrentQueue<ServiceCall> Calls { get; } = new();
    public int DetailReadCount { get; set; }
    public bool FailTree { get; set; }
    public Func<int, Task<List<ErjaTreeNodeDto>>> TreeLoader { get; set; } = default!;
    public IIncomingLetterService Service { get; }

    public Fixture()
    {
        Service = DispatchProxy.Create<IIncomingLetterService, IncomingProxy>();
        ((IncomingProxy)(object)Service).Fixture = this;
        Reset();
    }
    public void Reset()
    {
        Calls.Clear(); DetailReadCount = 0; FailTree = false; Inbox.Clear();
        void Row(int letter, int erja, int user, string name, string title, bool starred = false) => Inbox.Add(new()
        {
            LetterId = letter, ErjaId = erja, SenderUserId = user, Sender = name, Ferestande = ExternalSender,
            Title = title, LetterNumber = letter.ToString(), NumberSabt = letter, NumberLetterVarede = "EXT-" + letter,
            Date = Entered.AddDays(-1), DateErsal = Entered, IsNeshan = starred, ErjaType = "ارجاع"
        });
        Row(100, 200, 21, "ارجاع‌دهنده هم‌نام", "نامه ارجاع‌شده برای بررسی", true);
        Row(101, 201, 22, "ارجاع‌دهنده هم‌نام", "نامه دوم با ارجاع‌دهنده هم‌نام");
        Row(102, 202, 99, "کاربر بدون عکس", "آزمون تصویر ناموجود");
        Row(103, 203, 0, "", "آزمون شناسه نامشخص");
        Row(100, 205, 61, "ارجاع‌دهنده دوم همان نامه", "ارجاع دوم همان نامه");
        for (var i = 0; i < 16; i++) Row(200 + i, 300 + i, 80 + i, "کاربر آزمایشی " + (i + 1), "نامه صفحه‌بندی " + (i + 1));
        var first = new ErjaDto { ErjaId = 1, SourceId = 100, SenderUserId = 11, SenderName = "ثبت‌کننده اولیه", ReciverUserId = 21, ReciverName = "ارجاع‌دهنده هم‌نام", Type = "گیرنده", Date = Entered, IsRead = true };
        var current = new ErjaDto { ErjaId = 200, SourceId = 100, ParentErjaId = 1, SenderUserId = 21, SenderName = "ارجاع‌دهنده هم‌نام با عنوان سازمانی طولانی برای آزمون چیدمان", ReciverUserId = 31, ReciverName = "گیرنده ارجاع", Type = "ارجاع", Date = Entered.AddDays(2), AmalgarTitle = "بررسی", MatnErja = "پاراف آزمایشی", MohlatPasokh = Entered.AddDays(4) };
        var cc = new ErjaDto { ErjaId = 3, SourceId = 100, ParentErjaId = 1, SenderUserId = 21, SenderName = "ارجاع‌دهنده هم‌نام", ReciverUserId = 32, ReciverName = "گیرنده رونوشت", Type = "هامش", Date = Entered.AddDays(2), Answer = "پاسخ آزمایشی", DateAnswer = Entered.AddDays(2), IsRead = true, TypeTaeed = 1 };
        Letter = new()
        {
            LetterId = 100, SourceId = 100, LetterNumber = "100", NumberSabt = 100, Title = "نامه ارجاع‌شده برای بررسی",
            Description = "<p>این نامه و کاربران آن فقط برای آزمون ساخته شده‌اند.</p>",
            Ferestande = ExternalSender, NumberLetterVarede = "EXT-100", CreatorUserId = 11, CreatorName = "ثبت‌کننده اولیه",
            Date = Entered.AddDays(-1), DateErsal = Entered, CurrentErja = current, ActiveErjaId = 200,
            Erjas = new() { first, current, cc }
        };
        static ErjaTreeNodeDto Node(ErjaDto e) => new()
        {
            ErjaId = e.ErjaId, SenderUserId = e.SenderUserId, Sender = e.SenderName, ReciverUserId = e.ReciverUserId, Reciver = e.ReciverName,
            Type = e.Type, Date = e.Date, AmalgarTitle = e.AmalgarTitle, MatnErja = e.MatnErja, MohlatPasokh = e.MohlatPasokh,
            IsRead = e.IsRead, DateRead = e.DateRead, Answer = e.Answer, DateAnswer = e.DateAnswer, TypeTaeed = e.TypeTaeed
        };
        var root = Node(first); root.Children = new() { Node(current), Node(cc) }; Tree = new() { root };
        TreeLoader = _ => FailTree ? Task.FromException<List<ErjaTreeNodeDto>>(new InvalidOperationException("Fixture-only circulation failure")) : Task.FromResult(Tree);
    }
    public IncomingLetterDetailDto ReadDetail(int id)
    {
        // Model the important production contract: GetDetailAsync marks received referrals read.
        DetailReadCount++;
        foreach (var row in Inbox.Where(r => r.LetterId == id)) row.IsRead = true;
        if (Letter.CurrentErja is { } current) current.IsRead = true;
        if (Tree.Count > 0 && Tree[0].Children.Count > 0) Tree[0].Children[0].IsRead = true;
        return Letter;
    }
    public void Register(IServiceCollection services, bool staticRenderer)
    {
        services.AddLogging();
        services.AddSingleton(Service);
        services.AddSingleton(DispatchProxy.Create<ILetterService, ArchiveProxy>());
        services.AddSingleton(DispatchProxy.Create<IOutgoingLetterService, NoCallsProxy>());
        services.AddSingleton(DispatchProxy.Create<IEmailClientService, NoCallsProxy>());
        services.AddSingleton(DispatchProxy.Create<IApiClient, ApiProxy>());
        services.AddSingleton<IAuthState, TestAuth>();
        services.AddSingleton<IToastService, ToastService>();
        services.AddSingleton<LayoutState>();
        services.AddSingleton<RealtimeService>();
        services.AddSingleton<LetterPrintWorkflow>();
        services.AddSingleton<AiAssistantService>();
        services.AddSingleton(new ApiOptions { BaseUrl = staticRenderer ? "https://api.test.invalid/" : "" });
        services.AddSingleton(new HttpClient(new NoHttp()) { BaseAddress = new Uri("https://fixture.invalid/") });
        if (staticRenderer)
        {
            services.AddSingleton<IJSRuntime, SilentJs>();
            services.AddSingleton<NavigationManager, TestNavigation>();
        }
    }
}

public class IncomingProxy : DispatchProxy
{
    public Fixture Fixture { get; set; } = default!;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        var name = method!.Name;
        if (name == nameof(IIncomingLetterService.GetInboxAsync))
        {
            var search = (string?)args![0]; var unread = (bool?)args[1];
            var page = (int)args[2]!; var size = (int)args[3]!; var starred = (bool?)args[4];
            Fixture.Calls.Enqueue(new(name, null, page, size, starred));
            IEnumerable<IncomingLetterListItemDto> rows = Fixture.Inbox;
            if (!string.IsNullOrWhiteSpace(search)) rows = rows.Where(r => (r.Title + r.Ferestande + r.Sender).Contains(search, StringComparison.OrdinalIgnoreCase));
            if (unread == true) rows = rows.Where(r => !r.IsRead);
            if (starred == true) rows = rows.Where(r => r.IsNeshan);
            return Task.FromResult(Page(rows, page, size));
        }
        Fixture.Calls.Enqueue(new(name, args?.FirstOrDefault() is int id ? id : null));
        return name switch
        {
            nameof(IIncomingLetterService.GetDetailAsync) => Task.FromResult(Fixture.ReadDetail((int)args![0]!)),
            nameof(IIncomingLetterService.GetGardeshAsync) => Fixture.TreeLoader((int)args![0]!),
            nameof(IIncomingLetterService.GetSentAsync) => Task.FromResult(Page(new[] { new IncomingLetterListItemDto { LetterId = 100, NumberSabt = 100, SenderUserId = 77, Sender = "ثبت‌کننده آزمایشی", Ferestande = IncomingLetterAvatarTests.Fixture.ExternalSender, Title = "نامه ثبت‌شده", DateErsal = IncomingLetterAvatarTests.Fixture.Entered } }, (int)args![1]!, (int)args[2]!)),
            nameof(IIncomingLetterService.GetArchiveAsync) => Task.FromResult(Page(Fixture.Inbox, (int)args![1]!, (int)args[2]!)),
            nameof(IIncomingLetterService.GetStatsAsync) => Task.FromResult(new IncomingLetterCartableStatsDto { TotalInbox = Fixture.Inbox.Count, UnreadCount = Fixture.Inbox.Count(r => !r.IsRead), TotalStarred = Fixture.Inbox.Count(r => r.IsNeshan), TotalSent = 1 }),
            nameof(IIncomingLetterService.GetAttachmentsAsync) => Task.FromResult(new List<LetterAttachmentDto>()),
            _ => throw new InvalidOperationException("Unexpected incoming-service call: " + name)
        };
    }
    private static PagedResult<IncomingLetterListItemDto> Page(IEnumerable<IncomingLetterListItemDto> rows, int page, int size)
    {
        var list = rows.ToList();
        return new() { Items = list.Skip((page - 1) * size).Take(size).ToList(), TotalCount = list.Count, Page = page, PageSize = size };
    }
}
public class ApiProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name == nameof(IApiClient.GetAsync))
        {
            var url = (string)args![0]!;
            if (url == "api/workorders/source-ids?module=IncomingLetter") return Task.FromResult(new List<int>());
            if (url.StartsWith("api/workorders/for-source?module=IncomingLetter&sourceId="))
            {
                var type = method.GetGenericArguments()[0];
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(type).Invoke(null, new[] { Activator.CreateInstance(type) });
            }
        }
        throw new InvalidOperationException("Unexpected API call: " + method.Name);
    }
}
public class ArchiveProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name == nameof(ILetterService.GetBayeganiTreeAsync)
        ? Task.FromResult(new List<BayeganiNodeDto>()) : throw new InvalidOperationException("Unexpected internal-letter call: " + method.Name);
}
public class NoCallsProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new InvalidOperationException("Unexpected service call: " + method?.Name);
}
public sealed class NoHttp : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new InvalidOperationException("Unexpected real HTTP request");
}
public sealed class SilentJs : IJSRuntime
{
    public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
    public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken ct, object?[]? args) => ValueTask.FromResult(default(T)!);
}
public sealed class TestNavigation : NavigationManager
{
    public TestNavigation() => Initialize("https://fixture.invalid/", "https://fixture.invalid/incoming-letters");
    protected override void NavigateToCore(string uri, bool forceLoad) { }
}
public sealed class TestAuth : IAuthState
{
    public bool IsLoggedIn => true;
    public bool IsAdmin => false;
    public bool IsOperator => true;
    public bool IsReferrer => false;
    public bool CanOperate => true;
    public string? Token => null;
    public int UserId => 31;
    public string DisplayName => "گیرنده ارجاع";
    public string Role => "Operator";
    public int? ReferrerId => null;
    public bool Has(string permission) => permission is "IncomingLetters.Erja" or "IncomingLetters.Read";
    public bool HasDirect(string permission) => Has(permission);
    public bool HasModule(string module) => false;
    public bool HasModuleDirect(string module) => false;
    public bool CanSee(string module) => false;
    public event Action? Changed { add { } remove { } }
    public Task InitializeAsync() => Task.CompletedTask;
    public Task SignInAsync(LoginResponse login) => Task.CompletedTask;
    public Task SignOutAsync() => Task.CompletedTask;
}
