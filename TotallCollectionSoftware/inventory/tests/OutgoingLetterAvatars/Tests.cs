using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Inventory.Client.Pages.Office.Letters.Outgoing;
using Inventory.Client.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;

namespace OutgoingLetterAvatarTests;

public static class Tests
{
    public static async Task Run()
    {
        var checks = 0;
        void Check(bool ok, string text)
        {
            if (!ok) throw new InvalidOperationException("FAIL: " + text);
            Console.WriteLine("PASS: " + text); checks++;
        }
        bool HasPhoto(string html, int id, int count = 1) => Regex.Matches(html, $@"/api/auth/users/photo/{id}\?t=0").Count >= count;
        void Photo(string html, int id, string text, int count = 1) => Check(HasPhoto(html, id, count) && !html.Contains("/api/SystemUsers/photo/"), text);
        var fixture = new Fixture();
        var capture = new Capture();
        var services = new ServiceCollection();
        fixture.Register(services, staticRenderer: true);
        services.AddSingleton<IComponentActivator>(capture);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        Task<HtmlRootComponent> Render<T>(Dictionary<string, object?>? parameters = null) where T : IComponent =>
            renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters ?? new())));
        Task<string> Html(HtmlRootComponent root) => renderer.Dispatcher.InvokeAsync(() => WebUtility.HtmlDecode(root.ToHtmlString()));
        Task Change(ComponentBase component, Action? action = null) => renderer.Dispatcher.InvokeAsync(() => { action?.Invoke(); Probe.Refresh(component); });
        Task Act(ComponentBase component, string name, params object?[] args) => renderer.Dispatcher.InvokeAsync(async () =>
        {
            if (Probe.Call(component, name, args) is Task task) await task;
            Probe.Refresh(component);
        });
        static string List(string html) => html[html.IndexOf("<div class=\"list\">", StringComparison.Ordinal)..html.IndexOf("<main", StringComparison.Ordinal)];
        static string Modal(string html) => html[html.IndexOf("class=\"ltx outgoing-letter-gardesh\"", StringComparison.Ordinal)..];

        var root = await Render<OutgoingLetterCartable>(new() { ["Tab"] = "inbox" });
        var page = capture.Last<OutgoingLetterCartable>();
        var html = await Html(root);
        Check(!Probe.Get<bool>(page, "loading") && Probe.Get<List<OutgoingLetterListItemDto>>(page, "items").Count == 5,
            "actual outgoing cartable loads its real inbox lifecycle with synthetic data");
        var firstAvatar = Regex.Match(List(html), "<div class=\"mail [\\s\\S]*?<div class=\"mail-b\">").Value;
        Photo(firstAvatar, 21, "received row avatar uses the direct sender's login ID");
        Check(!HasPhoto(firstAvatar, 11), "original creator is not substituted for the referrer on an inbox row");
        Photo(List(html), 22, "users with identical display names retain their own photos");
        Photo(List(html), 11, "signer-only received row displays its named sender, even without an ErjaId");
        Check(List(html).Contains("user-avatar-default") && List(html).Contains("فرستنده نامشخص") && !List(html).Contains("/photo/0"),
            "unknown inbox identity uses a default avatar, never a guessed user");
        Check(fixture.Calls.All(c => c.Method.StartsWith("Get")), "loading avatars does not write to the letter workflow");
        foreach (var folder in new[] { "archive", "starred", "signing" })
        {
            await Act(page, "SetFolder", folder);
            html = List(await Html(root));
            Photo(html, folder == "signing" ? 11 : 21, $"{folder}: named user retains the correct login photo");
            if (folder == "starred") Check(!HasPhoto(html, 77) && html.Contains("به: گیرنده بیرونی"), "mixed starred list never puts a sender photo beside an external organization");
        }
        await Act(page, "SetFolder", "sent");
        html = List(await Html(root));
        Check(!html.Contains("/api/auth/users/photo/") && html.Contains("به: گیرنده بیرونی"), "sent organizations keep their existing non-user avatar");
        await Act(page, "SetFolder", "inbox");
        await Act(page, "OpenLetter", fixture.Inbox[0]);
        html = await Html(root);
        var header = html[html.IndexOf("class=\"paper-h\"", StringComparison.Ordinal)..html.IndexOf("class=\"paper-body", StringComparison.Ordinal)];
        Photo(header, 11, "reader header shows the original creator and their photo");
        foreach (var id in new[] { 21, 31, 32 }) Photo(html, id, $"reader internal recipient {id}: login-user photo");
        foreach (var id in new[] { 41, 42 }) Photo(html, id, $"reader signer {id}: photos appear in both summary and signature-status list", 2);
        Check(html.Contains("مدیر") && html.Contains("معاون") && html.Contains("در انتظار") && html.Contains("bi-check2-all"), "signer positions and signed/pending labels remain intact");

        var child = fixture.Tree[0].Children[0];
        fixture.Calls.Clear();
        await Act(page, "MenuGardesh", fixture.Inbox[0]);
        var modal = Probe.Get<OutgoingGardeshModal>(page, "gardesh");
        Check(Probe.Get<int>(modal, "senderUserId") == 11 && Probe.Get<DateTime>(modal, "letterDate") == Fixture.Created,
            "circulation root resolves original creator and creation date, not the inbox referrer/date");
        Check(fixture.Calls.Select(c => c.Method).Order().SequenceEqual(new[] { "GetDetailAsync", "GetGardeshAsync", "GetSignersAsync" }.Order()),
            "opening circulation only reads detail/tree/signers; no read, referral or signature write occurs");
        for (var view = 0; view < 3; view++)
        {
            await Change(modal, () => { Probe.Set(modal, "view", view); Probe.Set(modal, "selectedNode", child); });
            html = Modal(await Html(root));
            Photo(html, 11, $"circulation view {view}: original sender photo");
            Photo(html, 21, $"circulation view {view}: station recipient and direct-referrer photos", 3);
            Photo(html, 31, $"circulation view {view}: recipient and selected-station heading", 2);
            Check(HasPhoto(html, 41) && HasPhoto(html, 42), $"circulation view {view}: signed and pending signers both have photos");
            Check(html.Contains(child.Sender) && html.Contains("پاراف آزمایشی") && html.Contains("پاسخ آزمایشی"), $"circulation view {view}: full sender label, referral note and answer are preserved");
        }
        await Act(modal, "SelectNode", child);
        Check(Probe.Get<ErjaTreeNodeDto?>(modal, "selectedNode") is null, "station detail still toggles without altering the tree");
        await Act(page, "MenuGardeshCur");
        Check(Probe.Get<int>(modal, "senderUserId") == 11, "reader and list circulation entry points agree on author identity");

        var secretariatRoot = await Render<TestDabirkhane>();
        var secretariat = capture.Last<TestDabirkhane>();
        await Act(secretariat, "OpenGardesh", new DabirkhaneListItemDto { LetterId = 100, Title = "ورودی دبیرخانه", SadereNumber = "OUT-100", CreatorName = "اطلاعات سطر قدیمی", DateSabt = Fixture.Created.AddDays(9), DateSadere = Fixture.Created.AddDays(10) });
        var secretariatModal = Probe.Get<OutgoingGardeshModal>(secretariat, "gardesh");
        Check(Probe.Get<int>(secretariatModal, "senderUserId") == 11 && Probe.Get<DateTime>(secretariatModal, "letterDate") == Fixture.Created && Probe.Get<string>(secretariatModal, "letterNumber") == "OUT-100",
            "secretariat entry preserves its official number while resolving the correct original author/date");
        Photo(Modal(await Html(secretariatRoot)), 11, "secretariat circulation renders the same corrected component");
        var viewRoot = await Render<TestLetterView>();
        var standalone = capture.Last<TestLetterView>();
        await Change(standalone, () => Probe.Set(standalone, "letter", fixture.Letter));
        await Act(standalone, "OpenGardesh");
        Photo(Modal(await Html(viewRoot)), 11, "standalone viewer circulation entry uses the corrected author identity");

        fixture.TreeLoader = _ => Task.FromResult(new List<ErjaTreeNodeDto>());
        await renderer.Dispatcher.InvokeAsync(() => modal.Open(100, "Signer-only", "100"));
        html = Modal(await Html(root));
        Check(HasPhoto(html, 11) && HasPhoto(html, 41) && HasPhoto(html, 42), "signer-only circulation still shows creator and signer photos without referrals");
        fixture.TreeLoader = _ => Task.FromResult(fixture.Tree);
        fixture.SignersLoader = _ => Task.FromException<List<OutgoingSignerDto>>(new InvalidOperationException("Fixture signer failure"));
        await renderer.Dispatcher.InvokeAsync(() => modal.Open(100, "Signer failure", "100"));
        html = Modal(await Html(root));
        Check(HasPhoto(html, 31) && Probe.Get<string?>(modal, "loadError") is null, "a signer-list failure preserves existing referral-tree behavior");
        fixture.SignersLoader = _ => Task.FromResult(new List<OutgoingSignerDto>());
        fixture.TreeLoader = _ => Task.FromResult(new List<ErjaTreeNodeDto>());
        await renderer.Dispatcher.InvokeAsync(() => modal.Open(100, "Empty", "100"));
        Check(Modal(await Html(root)).Contains("گردشی ثبت نشده است"), "an empty circulation is not presented as a request error");
        fixture.TreeLoader = _ => Task.FromResult(fixture.Tree);
        fixture.DetailLoader = _ => Task.FromResult(new OutgoingLetterDetailDto { LetterId = 100, SenderUserId = 0, SenderName = "", DateSabt = Fixture.Created });
        await renderer.Dispatcher.InvokeAsync(() => modal.Open(100, "Unknown", "100"));
        html = Modal(await Html(root));
        Check(html.Contains("user-avatar-default") && html.Contains("کاربر نامشخص") && !html.Contains("/photo/0"), "missing creator identity uses the shared default photo and explicit unknown name");
        fixture.DetailLoader = _ => Task.FromException<OutgoingLetterDetailDto>(new InvalidOperationException("Denied"));
        await renderer.Dispatcher.InvokeAsync(() => modal.Open(100, "Failure", "100"));
        Check(Modal(await Html(root)).Contains("role=\"alert\"") && Probe.Get<int>(modal, "senderUserId") == 0 && Probe.Get<List<ErjaTreeNodeDto>>(modal, "roots").Count == 0,
            "metadata failure displays an error, never a stale or inferred creator");
        fixture.DetailLoader = _ => Task.FromResult<OutgoingLetterDetailDto>(null!);
        await renderer.Dispatcher.InvokeAsync(() => modal.Open(100, "Null", "100"));
        Check(Probe.Get<string?>(modal, "loadError") is not null, "null detail response cannot silently reuse a previous photo");

        var oldDetail = new TaskCompletionSource<OutgoingLetterDetailDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldTree = new TaskCompletionSource<List<ErjaTreeNodeDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldSigners = new TaskCompletionSource<List<OutgoingSignerDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DetailLoader = id => id == 201 ? oldDetail.Task : Task.FromResult(new OutgoingLetterDetailDto { LetterId = 202, SenderUserId = 72, SenderName = "نویسنده جدید", DateSabt = Fixture.Created });
        fixture.TreeLoader = id => id == 201 ? oldTree.Task : Task.FromResult(fixture.Tree);
        fixture.SignersLoader = id => id == 201 ? oldSigners.Task : Task.FromResult(new List<OutgoingSignerDto> { new() { UserId = 82, DisplayName = "امضاکننده جدید" } });
        var openingOld = renderer.Dispatcher.InvokeAsync(() => modal.Open(201, "Old", "201"));
        await renderer.Dispatcher.InvokeAsync(() => modal.Open(202, "New", "202"));
        oldDetail.SetResult(fixture.Letter); oldTree.SetResult(fixture.Tree); oldSigners.SetResult(fixture.Signers);
        await openingOld;
        Check(Probe.Get<int>(modal, "senderUserId") == 72 && Probe.Get<List<OutgoingSignerDto>>(modal, "signers").Single().UserId == 82,
            "late detail/tree/signer responses cannot replace a newly opened letter's identities");
        var pending = new TaskCompletionSource<OutgoingLetterDetailDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DetailLoader = _ => pending.Task;
        var closing = renderer.Dispatcher.InvokeAsync(() => modal.Open(203, "Closing", "203"));
        await Act(modal, "Close");
        pending.SetResult(fixture.Letter); await closing;
        Check(!Probe.Get<bool>(modal, "visible") && Probe.Get<int>(modal, "senderUserId") == 0, "closing invalidates in-flight identity responses");
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposing = renderer.Dispatcher.InvokeAsync(() => modal.Open(204, "Disposing", "204"));
        await renderer.Dispatcher.InvokeAsync(modal.Dispose);
        pending.SetResult(fixture.Letter); await disposing;
        Check(Probe.Get<int>(modal, "senderUserId") == 0, "disposed circulation ignores pending identity results");

        var avatar = await Render<UserAvatar>(new() { ["LoginUserId"] = 21, ["CacheBust"] = 123L });
        html = await Html(avatar);
        Check(html.Contains("https://api.test.invalid/api/auth/users/photo/21?t=123") && html.Contains("onerror=") && html.Contains("/uploads/users/default.jpg"),
            "shared avatar retains separate API base, cache key and missing-photo fallback");
        Console.WriteLine($"ALL {checks} OUTGOING-LETTER AVATAR CHECKS PASSED.");
    }

    private sealed class Capture : IComponentActivator
    {
        private readonly List<IComponent> components = new();
        public T Last<T>() where T : IComponent => components.OfType<T>().Last();
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            components.Add(component); return component;
        }
    }
}

public static class Probe
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
    private static MemberInfo Member(object o, string name)
    {
        for (var type = o.GetType(); type != null; type = type.BaseType)
            if (type.GetMember(name, Flags).FirstOrDefault() is { } member) return member;
        throw new InvalidOperationException("Missing member: " + name);
    }
    public static T Get<T>(object o, string name) => (T)((FieldInfo)Member(o, name)).GetValue(o)!;
    public static void Set(object o, string name, object? value) => ((FieldInfo)Member(o, name)).SetValue(o, value);
    public static object? Call(object o, string name, params object?[] args) => ((MethodInfo)Member(o, name)).Invoke(o, args);
    public static void Refresh(ComponentBase component) => typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null);
}
