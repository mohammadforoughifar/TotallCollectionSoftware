using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Inventory.Client.Pages.Office.Letters;
using Inventory.Client.Pages.Office.Letters.Incoming;
using Inventory.Client.Pages.Office.Letters.Outgoing;
using Inventory.Client.Services;
using Inventory.Client.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + message);
    checks++;
    Console.WriteLine("PASS: " + message);
}
void Photo(string html, int id, string message, int count = 1) => Check(
    Regex.Matches(html, $@"/api/auth/users/photo/{id}\?t=0").Count >= count &&
    !html.Contains("/api/SystemUsers/photo/"), message);
var letters = DispatchProxy.Create<ILetterService, LetterProxy>();
var fixture = (LetterProxy)(object)letters;
var services = new ServiceCollection();
services.AddLogging();
services.AddSingleton(letters);
services.AddSingleton(DispatchProxy.Create<IOutgoingLetterService, NoCallsProxy>());
services.AddSingleton(DispatchProxy.Create<IIncomingLetterService, NoCallsProxy>());
services.AddSingleton(DispatchProxy.Create<IEmailClientService, NoCallsProxy>());
services.AddSingleton(DispatchProxy.Create<IApiClient, NoCallsProxy>());
services.AddSingleton<IAuthState, TestAuth>();
services.AddSingleton<IToastService, ToastService>();
services.AddSingleton<IJSRuntime, SilentJs>();
services.AddSingleton<NavigationManager, TestNavigation>();
services.AddSingleton<RealtimeService>();
services.AddSingleton<LayoutState>();
services.AddSingleton<LetterPrintWorkflow>();
services.AddSingleton<AiAssistantService>();
services.AddSingleton(new ApiOptions { BaseUrl = "https://api.test.invalid/" });
services.AddSingleton(new HttpClient(new NoHttp()) { BaseAddress = new Uri("https://api.test.invalid/") });
await using var provider = services.BuildServiceProvider();
await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
Task<HtmlRootComponent> Render<T>(Dictionary<string, object?>? parameters = null) where T : IComponent =>
    renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters ?? new())));
Task<string> Html(HtmlRootComponent root) => renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
Task Change(ComponentBase component, Action? action = null) => renderer.Dispatcher.InvokeAsync(() =>
{
    action?.Invoke();
    typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null);
});

var recipients = new List<LetterReciverDto>
{
    new() { UserId = 21, FullName = "Same name", SematTitle = "Finance" },
    new() { UserId = 22, FullName = "Same name", SematTitle = "Operations" }
};
TestPicker? picker = null;
var picked = new HashSet<int> { 21 };
var pickerRoot = await Render<TestPicker>(new()
{
    ["Users"] = recipients, ["SelectedUsers"] = picked,
    ["Setup"] = (Action<TestPicker>)(p => { picker = p; Probe.Set(p, "open", true); })
});
var html = await Html(pickerRoot);
Photo(html, 21, "normal recipient: login-user photo in option and selected chip", 2);
Photo(html, 22, "identical display names still use each user's own ID");
await Change(picker!, () => Probe.Set(picker!, "query", "Operations"));
html = await Html(pickerRoot);
Photo(html, 21, "filtering does not remove selected recipient's photo");
Photo(html, 22, "position search and the correct avatar coexist");
await renderer.Dispatcher.InvokeAsync(() => (Task)Probe.Call(picker!, "ToggleUser", 22)!);
Check(picked.SetEquals(new[] { 21, 22 }), "selection still stores user IDs, not photo URLs or display names");
var missingRoot = await Render<TestPicker>(new() { ["SelectedUsers"] = new HashSet<int> { 999 } });
html = await Html(missingRoot);
Check(html.Contains("user-avatar-default") && !html.Contains("/photo/999"), "missing lookup uses a default instead of guessing an option's user");
var groupRoot = await Render<TestPicker>(new()
{
    ["Groups"] = new List<LetterGroupDto> { new() { GroupId = 21, NameGroup = "Group", MemberCount = 2 } },
    ["SelectedGroups"] = new HashSet<int> { 21 },
    ["Setup"] = (Action<TestPicker>)(p => { Probe.Set(p, "mode", "group"); Probe.Set(p, "open", true); })
});
html = await Html(groupRoot);
Check(html.Contains("bi-people") && !html.Contains("/api/auth/users/photo/21"), "group IDs remain group icons, never user photos");

// The production signer picker uses negative selection IDs for each user/position pair.
var compose = new ComposeOutgoingModal();
Probe.Call(compose, "BuildSignerOptions", new List<LetterReciverDto>
{
    new() { UserId = 81, FullName = "Signer", PositionOptions = new() { new() { SematId = 7, Title = "Manager" }, new() { SematId = 8, Title = "Director" } } },
    new() { UserId = 82, FullName = "Signer two" }
});
var options = Probe.Get<List<LetterReciverDto>>(compose, "signerUsers");
Check(options.Count == 3 && options.All(o => o.UserId < 0) && options.Select(o => o.AvatarLoginUserId).SequenceEqual(new int?[] { 81, 81, 82 }),
    "real signer-option builder retains negative keys but supplies real photo IDs");
var signerRoot = await Render<TestPicker>(new()
{
    ["Users"] = options, ["SelectedUsers"] = new HashSet<int> { options[1].UserId },
    ["Setup"] = (Action<TestPicker>)(p => Probe.Set(p, "open", true))
});
html = await Html(signerRoot);
Photo(html, 81, "multi-position signer shows one person's photo for both options and the selected chip", 3);
Photo(html, 82, "second signer shows their own photo");
Check(!html.Contains("/photo/-"), "temporary signer keys are never used as photo IDs");
Probe.Set(compose, "signerSelOptionIds", new HashSet<int> { options[0].UserId, options[1].UserId });
Probe.Set(compose, "lastSignerSelOptionIds", new HashSet<int> { options[0].UserId });
Probe.Call(compose, "OnSignerChanged");
var signerSelection = (List<OutgoingSignerSelectionDto>)Probe.Property(compose, "SelectedSignerSelections")!;
Check(signerSelection.Count == 1 && signerSelection[0].UserId == 81 && signerSelection[0].SematId == 8,
    "choosing another position still keeps one signature with the real user and selected position");

// Real referral components all reuse the corrected picker. Their business code is unchanged.
Action<object> prepareSheet = p =>
{
    Probe.Set(p, "visible", true); Probe.Set(p, "users", recipients);
    Probe.Set(p, "selUsers", new HashSet<int> { 21 });
};
TestErja? erja = null;
var erjaRoot = await Render<TestErja>(new() { ["Setup"] = (Action<TestErja>)(p => { erja = p; prepareSheet(p); }) });
Photo(await Html(erjaRoot), 21, "internal referral sheet uses the real login photo");
var outgoingRoot = await Render<TestOutgoingErja>(new() { ["Setup"] = (Action<TestOutgoingErja>)(p => prepareSheet(p)) });
Photo(await Html(outgoingRoot), 21, "outgoing referral sheet receives the shared correction");
var incomingRoot = await Render<TestIncomingErja>(new() { ["Setup"] = (Action<TestIncomingErja>)(p => prepareSheet(p)) });
Photo(await Html(incomingRoot), 21, "incoming referral sheet receives the shared correction");
await renderer.Dispatcher.InvokeAsync(async () =>
{
    Probe.Set(erja!, "letterId", 100); Probe.Set(erja!, "parentErjaId", (int?)10);
    Probe.Set(erja!, "amalgarId", (int?)3); Probe.Set(erja!, "selGroups", new HashSet<int> { 9 });
    await (Task)Probe.Call(erja!, "Submit")!;
});
Check(fixture.LastErja is { LetterId: 100, ParentErjaId: 10, AmalgarId: 3 } sent &&
      sent.ReciversGirandegan.SequenceEqual(new[] { 21 }) && sent.GroupsGirandegan.SequenceEqual(new[] { 9 }) &&
      sent.ReciversHamesh.Count == 0, "referral submission keeps recipient/group/parent/action IDs intact");
await renderer.Dispatcher.InvokeAsync(async () =>
{
    Probe.Set(erja!, "isErja", false);
    await (Task)Probe.Call(erja!, "Submit")!;
});
Check(fixture.LastErja is { AmalgarId: 1 } cc && cc.ReciversHamesh.SequenceEqual(new[] { 21 }) &&
      cc.GroupsHamesh.SequenceEqual(new[] { 9 }) && cc.ReciversGirandegan.Count == 0, "CC submission keeps its original recipient and group semantics");

var now = new DateTime(2026, 11, 21, 10, 0, 0);
var row = new InnerLetterListItemDto { LetterId = 100, ErjaId = 200, SenderUserId = 21, Sender = "Direct referrer", Title = "Letter", LetterNumber = "100", Date = now, IsNeshan = true };
var detail = new InnerLetterDetailDto
{
    LetterId = 100, SenderUserId = 11, SenderName = "Original creator", Title = "Authoritative title", LetterNumber = "100", DateSabt = now.AddDays(-3),
    ReciversGirande = new() { new() { UserId = 21, FullName = "Direct referrer" } },
    ReciversErja = new() { new() { UserId = 31, FullName = "Action recipient" } },
    ReciversHamesh = new() { new() { UserId = 32, FullName = "CC recipient" } }
};
TestCartable? cartable = null;
var cartableRoot = await Render<TestCartable>(new()
{
    ["Setup"] = (Action<TestCartable>)(p => { cartable = p; Probe.Set(p, "loading", false); Probe.Set(p, "items", new List<InnerLetterListItemDto> { row }); Probe.Set(p, "selected", detail); })
});
html = await Html(cartableRoot);
var rowMarkup = html[html.IndexOf("class=\"mail ", StringComparison.Ordinal)..html.IndexOf("<main", StringComparison.Ordinal)];
Photo(rowMarkup, 21, "inbox photo belongs to the direct sender named on that row");
Check(!rowMarkup.Contains("/photo/11?"), "original author is not substituted for the direct inbox sender");
var paper = html[html.IndexOf("class=\"paper-h\"", StringComparison.Ordinal)..html.IndexOf("class=\"paper-body", StringComparison.Ordinal)];
Photo(paper, 11, "letter reader header retains the original author identity");
Photo(html, 31, "reader action recipient uses login photo");
Photo(html, 32, "reader CC recipient uses login photo");
await Change(cartable!, () => Probe.Set(cartable!, "folder", "starred"));
Photo(await Html(cartableRoot), 21, "starred received letters retain direct sender avatars");
await Change(cartable!, () =>
{
    Probe.Set(cartable!, "folder", "inbox"); Probe.Set(cartable!, "selected", null);
    Probe.Set(cartable!, "items", new List<InnerLetterListItemDto> { new() { LetterId = 101, ErjaId = 201, Date = now } });
});
html = await Html(cartableRoot);
Check(html.Contains("user-avatar-default") && WebUtility.HtmlDecode(html).Contains("فرستنده نامشخص"), "unknown inbox sender uses default photo and is not mistaken for a sent letter");
await Change(cartable!, () =>
{
    Probe.Set(cartable!, "folder", "sent");
    Probe.Set(cartable!, "items", new List<InnerLetterListItemDto> { new() { LetterId = 101, SenderUserId = 999, Reciver = "Recipient", Date = now } });
});
Check(!(await Html(cartableRoot)).Contains("/photo/999?"), "sent list does not place the current sender's photo beside a recipient name");

var child = new ErjaTreeNodeDto { ErjaId = 2, SenderUserId = 21, Sender = "Long referrer " + new string('X', 70), ReciverUserId = 31, Reciver = "Action recipient", Type = "ارجاع", Date = now, AmalgarTitle = "Review" };
var tree = new List<ErjaTreeNodeDto>
{
    new() { ErjaId = 1, SenderUserId = 11, Sender = detail.SenderName, ReciverUserId = 21, Reciver = "Direct referrer", Type = "گیرنده", Date = now.AddDays(-3), Children = new() { child } }
};
fixture.Detail = _ => Task.FromResult(detail);
fixture.Tree = _ => Task.FromResult(tree);
// Use the real cartable menu method: list metadata must never become the circulation root.
fixture.Calls.Clear();
await renderer.Dispatcher.InvokeAsync(() => (Task)Probe.Call(cartable!, "MenuGardesh", row)!);
var modal = Probe.Get<GardeshModal>(cartable!, "gardesh");
Check(Probe.Get<int>(modal, "senderUserId") == 11 && Probe.Get<DateTime>(modal, "letterDate") == detail.DateSabt,
    "opening from inbox uses authoritative original sender and creation date");
Check(fixture.Calls.SequenceEqual(new[] { "GetDetailAsync", "GetGardeshAsync" }), "opening circulation only reads metadata/tree, never marks read or sends a referral");
for (var view = 0; view < 3; view++)
{
    await Change(modal, () => { Probe.Set(modal, "view", view); Probe.Set(modal, "selectedNode", child); });
    html = await Html(cartableRoot);
    Photo(html, 11, $"circulation view {view}: original sender photo");
    Photo(html, 21, $"circulation view {view}: recipient and direct-sender photos", 3);
    Photo(html, 31, $"circulation view {view}: recipient and selected station heading", 2);
    Check(html.Contains(child.Sender), $"circulation view {view}: long sender name is not truncated out of its photo label");
}
Probe.Set(cartable!, "selected", detail);
await renderer.Dispatcher.InvokeAsync(() => (Task)Probe.Call(cartable!, "MenuGardeshCur")!);
Check(Probe.Get<int>(modal, "senderUserId") == 11, "reader and list entry points agree on circulation root");

// Reopening/closing while requests are in flight must not show a previous person's photo.
var firstDetail = new TaskCompletionSource<InnerLetterDetailDto>(TaskCreationOptions.RunContinuationsAsynchronously);
var firstTree = new TaskCompletionSource<List<ErjaTreeNodeDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
var secondDetail = new TaskCompletionSource<InnerLetterDetailDto>(TaskCreationOptions.RunContinuationsAsynchronously);
var secondTree = new TaskCompletionSource<List<ErjaTreeNodeDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
fixture.Detail = id => id == 101 ? firstDetail.Task : secondDetail.Task;
fixture.Tree = id => id == 101 ? firstTree.Task : secondTree.Task;
var openFirst = renderer.Dispatcher.InvokeAsync(() => modal.Open(101, "Old", "101"));
var openSecond = renderer.Dispatcher.InvokeAsync(() => modal.Open(102, "New", "102"));
secondDetail.SetResult(new() { LetterId = 102, SenderUserId = 72, SenderName = "New author", DateSabt = now });
secondTree.SetResult(tree);
await openSecond;
firstDetail.SetResult(detail); firstTree.SetResult(tree);
await openFirst;
Check(Probe.Get<int>(modal, "senderUserId") == 72, "late response from previously opened letter cannot replace current identity");
var closingDetail = new TaskCompletionSource<InnerLetterDetailDto>(TaskCreationOptions.RunContinuationsAsynchronously);
fixture.Detail = _ => closingDetail.Task; fixture.Tree = _ => Task.FromResult(tree);
var closing = renderer.Dispatcher.InvokeAsync(() => modal.Open(103, "Closing", "103"));
await Change(modal, () => Probe.Call(modal, "Close"));
closingDetail.SetResult(detail); await closing;
Check(!Probe.Get<bool>(modal, "visible") && Probe.Get<int>(modal, "senderUserId") == 0, "closing invalidates pending identity response");
fixture.Detail = _ => Task.FromException<InnerLetterDetailDto>(new InvalidOperationException("Denied"));
await renderer.Dispatcher.InvokeAsync(() => modal.Open(104, "Unavailable", "104"));
html = await Html(cartableRoot);
Check(Probe.Get<int>(modal, "senderUserId") == 0 && Probe.Get<string?>(modal, "loadError") is not null && html.Contains("role=\"alert\""),
    "metadata failure shows an error instead of a stale or guessed original sender");

var avatarRoot = await Render<UserAvatar>(new() { ["LoginUserId"] = 21, ["CacheBust"] = 123L });
html = await Html(avatarRoot);
Check(html.Contains("https://api.test.invalid/api/auth/users/photo/21?t=123") && html.Contains("onerror=") && html.Contains("/uploads/users/default.jpg"),
    "photo URL preserves API base, cache key and missing-photo fallback");
var defaultRoot = await Render<UserAvatar>(new() { ["LoginUserId"] = 0 });
html = await Html(defaultRoot);
Check(html.Contains("user-avatar-default") && !html.Contains("/photo/0"), "missing identity uses the default photo");

Console.WriteLine($"ALL {checks} INNER-LETTER AVATAR CHECKS PASSED.");

public static class Probe
{
    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
    private static MemberInfo Member(object obj, string name)
    {
        for (var t = obj.GetType(); t != null; t = t.BaseType)
            if (t.GetMember(name, Flags).FirstOrDefault() is { } member) return member;
        throw new InvalidOperationException("Missing member " + name);
    }
    public static void Set(object obj, string name, object? value) => ((FieldInfo)Member(obj, name)).SetValue(obj, value);
    public static T Get<T>(object obj, string name) => (T)((FieldInfo)Member(obj, name)).GetValue(obj)!;
    public static object? Property(object obj, string name) => ((PropertyInfo)Member(obj, name)).GetValue(obj);
    public static object? Call(object obj, string name, params object?[] args) => ((MethodInfo)Member(obj, name)).Invoke(obj, args);
}
public sealed class TestPicker : LetterPickCombo
{
    [Parameter] public Action<TestPicker>? Setup { get; set; }
    protected override void OnInitialized() => Setup?.Invoke(this);
}
public sealed class TestErja : ErjaSheet
{
    [Parameter] public Action<TestErja>? Setup { get; set; }
    protected override void OnInitialized() => Setup?.Invoke(this);
}
public sealed class TestOutgoingErja : OutgoingErjaSheet
{
    [Parameter] public Action<TestOutgoingErja>? Setup { get; set; }
    protected override void OnInitialized() => Setup?.Invoke(this);
}
public sealed class TestIncomingErja : IncomingErjaSheet
{
    [Parameter] public Action<TestIncomingErja>? Setup { get; set; }
    protected override void OnInitialized() => Setup?.Invoke(this);
}
public sealed class TestCartable : LetterCartable
{
    [Parameter] public Action<TestCartable>? Setup { get; set; }
    protected override Task OnInitializedAsync() { Setup?.Invoke(this); return Task.CompletedTask; }
    protected override Task OnParametersSetAsync() => Task.CompletedTask;
}
public class LetterProxy : DispatchProxy
{
    public Func<int, Task<InnerLetterDetailDto>> Detail = _ => throw new InvalidOperationException("Unexpected detail load");
    public Func<int, Task<List<ErjaTreeNodeDto>>> Tree = _ => throw new InvalidOperationException("Unexpected tree load");
    public List<string> Calls { get; } = new();
    public AddErjaDto? LastErja;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        Calls.Add(method!.Name);
        switch (method.Name)
        {
            case nameof(ILetterService.GetDetailAsync): return Detail((int)args![0]!);
            case nameof(ILetterService.GetGardeshAsync): return Tree((int)args![0]!);
            case nameof(ILetterService.AddErjaAsync): LastErja = (AddErjaDto)args![0]!; return Task.CompletedTask;
            default: throw new InvalidOperationException("Unexpected letter-service call: " + method.Name);
        }
    }
}
public class NoCallsProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? method, object?[]? args) => throw new InvalidOperationException("Unexpected service call: " + method?.Name);
}
public sealed class NoHttp : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new InvalidOperationException("Unexpected network request");
}
public sealed class SilentJs : IJSRuntime
{
    public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
    public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken ct, object?[]? args) => ValueTask.FromResult(default(T)!);
}
public sealed class TestNavigation : NavigationManager
{
    public TestNavigation() => Initialize("https://app.test.invalid/", "https://app.test.invalid/letters");
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
    public string DisplayName => "Action recipient";
    public string Role => "Operator";
    public int? ReferrerId => null;
    public bool Has(string permission) => permission == "InnerLetters.Erja";
    public bool HasDirect(string permission) => Has(permission);
    public bool HasModule(string module) => false;
    public bool HasModuleDirect(string module) => false;
    public bool CanSee(string module) => false;
    public event Action? Changed { add { } remove { } }
    public Task InitializeAsync() => Task.CompletedTask;
    public Task SignInAsync(LoginResponse login) => Task.CompletedTask;
    public Task SignOutAsync() => Task.CompletedTask;
}
