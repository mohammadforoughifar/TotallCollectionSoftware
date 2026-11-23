using System.Collections.Concurrent;
using System.Reflection;
using Inventory.Client.Pages.Office.Letters.Outgoing;
using Inventory.Client.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace OutgoingLetterAvatarTests;

public sealed record ServiceCall(string Method, int? Id);

public sealed class Fixture
{
    public static readonly DateTime Created = new(2026, 11, 18, 10, 0, 0);
    public List<OutgoingLetterListItemDto> Inbox { get; } = new();
    public OutgoingLetterDetailDto Letter { get; private set; } = new();
    public List<ErjaTreeNodeDto> Tree { get; private set; } = new();
    public List<OutgoingSignerDto> Signers { get; private set; } = new();
    public ConcurrentQueue<ServiceCall> Calls { get; } = new();
    public Func<int, Task<OutgoingLetterDetailDto>> DetailLoader { get; set; } = default!;
    public Func<int, Task<List<ErjaTreeNodeDto>>> TreeLoader { get; set; } = default!;
    public Func<int, Task<List<OutgoingSignerDto>>> SignersLoader { get; set; } = default!;
    public IOutgoingLetterService Service { get; }
    public bool FailDetail { get; set; }

    public Fixture()
    {
        Service = DispatchProxy.Create<IOutgoingLetterService, OutgoingProxy>();
        ((OutgoingProxy)(object)Service).Fixture = this;
        Reset();
    }
    public void Reset()
    {
        Calls.Clear(); FailDetail = false; Inbox.Clear();
        Inbox.Add(new() { LetterId = 100, ErjaId = 200, SenderUserId = 21, Sender = "کاربر هم‌نام", Title = "نامه ارجاع‌شده برای بررسی", LetterNumber = "100", Date = Created.AddDays(2), IsNeshan = true });
        Inbox.Add(new() { LetterId = 101, ErjaId = 201, SenderUserId = 22, Sender = "کاربر هم‌نام", Title = "نامه دوم با فرستنده هم‌نام", LetterNumber = "101", Date = Created.AddDays(2) });
        Inbox.Add(new() { LetterId = 102, ErjaId = 202, SenderUserId = 99, Sender = "کاربر بدون عکس", Title = "آزمون عکس در دسترس نیست", LetterNumber = "102", Date = Created.AddDays(2) });
        Inbox.Add(new() { LetterId = 103, ErjaId = 203, SenderUserId = 0, Sender = "", Title = "آزمون شناسه نامشخص", LetterNumber = "103", Date = Created.AddDays(2) });
        Inbox.Add(new() { LetterId = 104, SenderUserId = 11, Sender = "ایجادکننده اصلی", Title = "نامه صرفاً امضایی", LetterNumber = "104", Date = Created, IsSigner = true, CanSign = true });
        Signers = new()
        {
            new() { Id = 1, SourceId = 100, UserId = 41, DisplayName = "امضاکننده اول", Order = 1, IsSigned = true, DateSigned = Created.AddDays(1), SignNote = "تأیید شد", SematId = 7, SematTitle = "مدیر" },
            new() { Id = 2, SourceId = 100, UserId = 42, DisplayName = "امضاکننده دوم", Order = 2, IsSigned = false, SematId = 8, SematTitle = "معاون" }
        };
        Letter = new()
        {
            LetterId = 100, LetterNumber = "100", Title = "نامه ارجاع‌شده برای بررسی", Text = "<p>این نامه و همه کاربران آن فقط برای آزمون ساخته شده‌اند.</p>",
            SenderUserId = 11, SenderName = "ایجادکننده اصلی", DateSabt = Created, ReceiverOrganization = "سازمان بیرونی آزمایشی", Status = 1,
            Signers = Signers,
            ReciversGirande = new() { new() { UserId = 21, FullName = "کاربر هم‌نام" } },
            ReciversErja = new() { new() { UserId = 31, FullName = "گیرنده ارجاع" } },
            ReciversHamesh = new() { new() { UserId = 32, FullName = "گیرنده رونوشت" } },
            MyErja = new() { ErjaId = 200, SourceId = 100, SenderUserId = 21, ReciverUserId = 31, Type = "ارجاع", MatnErja = "لطفاً بررسی شود", Date = Created.AddDays(2) }
        };
        Tree = new()
        {
            new() { ErjaId = 1, SenderUserId = 11, Sender = Letter.SenderName, ReciverUserId = 21, Reciver = "کاربر هم‌نام", Type = "گیرنده", Date = Created,
                Children = new()
                {
                    new() { ErjaId = 2, SenderUserId = 21, Sender = "کاربر هم‌نام با عنوان سازمانی طولانی برای بررسی چیدمان", ReciverUserId = 31, Reciver = "گیرنده ارجاع", Type = "ارجاع", Date = Created.AddDays(2), AmalgarTitle = "بررسی", MatnErja = "پاراف آزمایشی", Answer = "پاسخ آزمایشی", TypeTaeed = 1, IsRead = true, DateAnswer = Created.AddDays(2) },
                    new() { ErjaId = 3, SenderUserId = 21, Sender = "کاربر هم‌نام", ReciverUserId = 32, Reciver = "گیرنده رونوشت", Type = "هامش", Date = Created.AddDays(2) }
                }
            }
        };
        DetailLoader = _ => FailDetail ? Task.FromException<OutgoingLetterDetailDto>(new InvalidOperationException("Fixture-only metadata failure")) : Task.FromResult(Letter);
        TreeLoader = _ => Task.FromResult(Tree);
        SignersLoader = _ => Task.FromResult(Signers);
    }
    public void Register(IServiceCollection services, bool staticRenderer)
    {
        services.AddLogging();
        services.AddSingleton(Service);
        services.AddSingleton(DispatchProxy.Create<ILetterService, NoCallsProxy>());
        services.AddSingleton(DispatchProxy.Create<IIncomingLetterService, NoCallsProxy>());
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

public class OutgoingProxy : DispatchProxy
{
    public Fixture Fixture { get; set; } = default!;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        var name = method!.Name;
        Fixture.Calls.Enqueue(new(name, args?.FirstOrDefault() is int id ? id : null));
        switch (name)
        {
            case nameof(IOutgoingLetterService.GetDetailAsync): return Fixture.DetailLoader((int)args![0]!);
            case nameof(IOutgoingLetterService.GetGardeshAsync): return Fixture.TreeLoader((int)args![0]!);
            case nameof(IOutgoingLetterService.GetSignersAsync): return Fixture.SignersLoader((int)args![0]!);
            case nameof(IOutgoingLetterService.GetInboxAsync):
            case nameof(IOutgoingLetterService.GetArchiveAsync):
                return Task.FromResult(Page(Fixture.Inbox));
            case nameof(IOutgoingLetterService.GetSigningInboxAsync):
                return Task.FromResult(Page(Fixture.Inbox.Where(r => r.IsSigner)));
            case nameof(IOutgoingLetterService.GetSentAsync):
                return Task.FromResult(Page(new[] { new OutgoingLetterListItemDto { LetterId = 105, LetterNumber = "105", SenderUserId = 77, ReceiverOrganization = "گیرنده بیرونی", Title = "نامه ارسالی", Date = Fixture.Created, IsNeshan = true } }));
            case nameof(IOutgoingLetterService.GetStatsAsync):
                return Task.FromResult(new OutgoingLetterCartableStatsDto { InboxTotal = Fixture.Inbox.Count, InboxUnread = Fixture.Inbox.Count, SentTotal = 1 });
            case nameof(IOutgoingLetterService.GetAttachmentsAsync): return Task.FromResult(new List<LetterAttachmentDto>());
            case nameof(IOutgoingLetterService.MarkLetterReadAsync):
            case nameof(IOutgoingLetterService.MarkReadAsync): return Task.CompletedTask;
            case nameof(IOutgoingLetterService.GetUserSignatureDataUrlAsync): return Task.FromResult<string?>(null);
            default: throw new InvalidOperationException("Unexpected outgoing-service call: " + name);
        }
    }
    private static PagedResult<OutgoingLetterListItemDto> Page(IEnumerable<OutgoingLetterListItemDto> rows)
    {
        var list = rows.ToList();
        return new() { Items = list, TotalCount = list.Count, Page = 1, PageSize = 15 };
    }
}
public class ApiProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name == nameof(IApiClient.GetAsync))
        {
            var url = (string)args![0]!;
            if (url == "api/workorders/source-ids?module=OutgoingLetter") return Task.FromResult(new List<int>());
            if (url.StartsWith("api/workorders/for-source?module=OutgoingLetter&sourceId="))
            {
                // The page's work-order DTO is nested; return its actual requested list type.
                var resultType = method.GetGenericArguments()[0];
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(resultType)
                    .Invoke(null, new[] { Activator.CreateInstance(resultType) });
            }
        }
        throw new InvalidOperationException("Unexpected API call: " + method.Name);
    }
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
    public TestNavigation() => Initialize("https://fixture.invalid/", "https://fixture.invalid/outgoing-letters/inbox");
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
    public bool Has(string permission) => permission == "OutgoingLetters.Erja";
    public bool HasDirect(string permission) => Has(permission);
    public bool HasModule(string module) => false;
    public bool HasModuleDirect(string module) => false;
    public bool CanSee(string module) => false;
    public event Action? Changed { add { } remove { } }
    public Task InitializeAsync() => Task.CompletedTask;
    public Task SignInAsync(LoginResponse login) => Task.CompletedTask;
    public Task SignOutAsync() => Task.CompletedTask;
}

public sealed class TestDabirkhane : OutgoingDabirkhane
{
    protected override Task OnInitializedAsync() => Task.CompletedTask;
}
public sealed class TestLetterView : OutgoingLetterView
{
    protected override Task OnParametersSetAsync() => Task.CompletedTask;
}
