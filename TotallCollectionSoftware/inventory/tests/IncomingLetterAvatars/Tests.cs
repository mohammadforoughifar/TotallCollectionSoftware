using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Inventory.Client.Pages.Office.Letters.Incoming;
using Inventory.Client.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;

namespace IncomingLetterAvatarTests;

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
        static string List(string html) => html[html.IndexOf("<div class=\"list\">", StringComparison.Ordinal)..html.IndexOf("</section>", html.IndexOf("<div class=\"list\">", StringComparison.Ordinal), StringComparison.Ordinal)];
        static string Modal(string html) => html[html.IndexOf("class=\"ltx incoming-letter-gardesh\"", StringComparison.Ordinal)..];
        static List<string> Rows(string html) => Regex.Matches(List(html), @"<div class=""mail [\s\S]*?(?=<div class=""mail |<div class=""lt-pager)").Select(m => m.Value).ToList();
        bool OnlyTreeCall() => fixture.Calls.Count == 1 && fixture.Calls.Single().Method == "GetGardeshAsync";

        var root = await Render<IncomingLetterCartable>(new() { ["Tab"] = "inbox" });
        var page = capture.Last<IncomingLetterCartable>();
        var html = await Html(root);
        var rows = Rows(html);
        Check(!Probe.Get<bool>(page, "loading") && rows.Count == 15 && Probe.Get<int>(page, "totalItems") == 21,
            "real incoming cartable lifecycle loads the server-paged synthetic inbox");
        Photo(rows[0], 21, "received row uses its direct referrer's login ID");
        Check(!HasPhoto(rows[0], 11) && !HasPhoto(rows[0], 31) && rows[0].Contains("ارجاع‌دهنده: ارجاع‌دهنده هم‌نام"),
            "row photo/name is the referrer, not the original registrar or signed-in recipient");
        Check(rows[0].Contains("فرستندهٔ مبدأ: " + Fixture.ExternalSender) && !Regex.Match(rows[0], @"<div class=""mail-pv letter-origin""[\s\S]*?</div>").Value.Contains("<img"),
            "external sender remains separately labelled, never assigned an internal-user photo");
        Photo(rows[1], 22, "identically named referrers retain distinct ID-based photos");
        Photo(rows[2], 99, "known user without a photo retains the shared onerror/default contract");
        Check(rows[3].Contains("user-avatar-default") && rows[3].Contains("کاربر نامشخص") && !rows[3].Contains("/photo/0"),
            "missing referrer identity falls back without guessing from external sender or display name");
        Photo(rows[4], 61, "a second referral of the same letter uses that row's own sender ID");
        Check(fixture.DetailReadCount == 0 && fixture.Inbox.All(r => !r.IsRead), "inbox avatar rendering does not fetch detail or mark anything read");

        await Act(page, "GoPageAsync", 2);
        html = List(await Html(root));
        Photo(html, 90, "page two displays its own referrers, not recycled first-page photos");
        Check(!HasPhoto(html, 21) && Probe.Get<List<IncomingLetterListItemDto>>(page, "letters").Count == 6 && fixture.Calls.Any(c => c.Page == 2 && c.PageSize == 15),
            "avatar changes preserve server paging and total counts");
        await Change(page, () => Probe.Set(page, "pageSize", 10));
        await Act(page, "OnPageSizeChangedAsync");
        Check(Probe.Get<int>(page, "currentPage") == 1 && Probe.Get<List<IncomingLetterListItemDto>>(page, "letters").Count == 10,
            "changing page size still resets paging and keeps each row identity");
        await Act(page, "ToggleSelectErja", 200);
        Check(Probe.Get<List<int>>(page, "selectedErjaIds").SequenceEqual(new[] { 200 }), "row selection continues to use referral IDs, not photo/login IDs");
        await Act(page, "SetFilter", "starred");
        Photo(List(await Html(root)), 21, "starred filter keeps direct-referrer photo semantics");
        Check(Probe.Get<List<IncomingLetterListItemDto>>(page, "letters").Count == 1 && fixture.Calls.Any(c => c.Starred == true),
            "star filtering is still performed by the service");
        await Act(page, "SetFolder", "sent");
        html = List(await Html(root));
        Photo(html, 77, "registered folder uses its actual registrar login ID");
        Check(html.Contains("ثبت‌کننده: ثبت‌کننده آزمایشی") && html.Contains(Fixture.ExternalSender) && !html.Contains("ارجاع‌دهنده:"),
            "registered-folder labels do not falsely claim a direct incoming referral");
        await Act(page, "SetFolder", "starred");
        Photo(List(await Html(root)), 21, "starred folder reuses the received-row identity correctly");
        await Act(page, "SetFolder", "inbox");
        await Change(page, () => Probe.Set(page, "pageSize", 15));
        await Act(page, "OnPageSizeChangedAsync");

        fixture.Calls.Clear();
        await Act(page, "OpenGardesh", fixture.Inbox[0]);
        var modal = Probe.Get<IncomingGardeshModal>(page, "gardeshModal");
        Check(OnlyTreeCall() && fixture.DetailReadCount == 0 && fixture.Inbox.All(r => !r.IsRead),
            "list circulation reads only the authorized tree endpoint, not the mutating detail endpoint");
        Check(Probe.Get<int>(page, "openMenuLetterId") == 0, "opening circulation closes the row menu without selecting/reading the letter");
        Check((int)Probe.Call(modal, "CountAll")! == 3, "all nested referral stations remain in the circulation");
        for (var view = 0; view < 3; view++)
        {
            await Change(modal, () => Probe.Set(modal, "view", view));
            html = Modal(await Html(root));
            var origin = Regex.Match(html, @"<(?:div|span) class=""[^""]*letter-origin""[\s\S]*?</div>").Value;
            Check(origin.Contains(Fixture.ExternalSender) && !origin.Contains("<img"), $"circulation view {view}: external origin has no guessed registrar/referrer photo");
            Photo(html, 11, $"circulation view {view}: original registrar's photo appears on their actual referral");
            Photo(html, 21, $"circulation view {view}: direct referrer and prior recipient use the same correct photo", 3);
            Photo(html, 31, $"circulation view {view}: received referral has its recipient's photo");
            Photo(html, 32, $"circulation view {view}: nested CC recipient has their own photo");
            Check(html.Contains(fixture.Tree[0].Children[0].Sender), $"circulation view {view}: full referrer name is preserved with its photo");
            if (view == 0) Check(html.Contains("پاراف آزمایشی") && html.Contains("پاسخ آزمایشی") && html.Contains("رویت نشده"), "list preserves referral note, answer and unread status");
            if (view == 1) Check(html.Contains("پاسخ آزمایشی") && html.Contains("مهلت:") && html.Contains("در انتظار رویت"), "tree preserves answer, deadline, approval/read status and hierarchy");
            if (view == 2) Check(html.Contains("fnode wt") && html.Contains("fnode ok") && html.Contains("مهلت:"), "flowchart preserves pending/completed stations and deadlines");
        }
        Check(OnlyTreeCall() && fixture.DetailReadCount == 0, "switching all three views does not fetch details or write referral/read state");
        await Act(page, "OpenGardesh", fixture.Inbox[4]);
        html = Modal(await Html(root));
        Check(!HasPhoto(html, 61) && HasPhoto(html, 11), "clicked row's referrer is not promoted into the original registrar slot");
        await Act(modal, "Close");

        await Act(page, "SelectLetter", fixture.Inbox[0]);
        html = await Html(root);
        var header = html[html.IndexOf("class=\"paper-h\"", StringComparison.Ordinal)..html.IndexOf("class=\"paper-body", StringComparison.Ordinal)];
        Photo(header, 11, "reader pairs original registrar's name and photo, separate from external origin");
        Check(header.Contains("ثبت‌کننده:") && header.Contains("ثبت‌کننده اولیه") && header.Contains(Fixture.ExternalSender), "reader keeps both the external sender and authoritative internal registrar labelled");
        foreach (var id in new[] { 21, 31, 32 }) Photo(html, id, $"reader participants: recipient {id} uses a login photo");
        Check(fixture.DetailReadCount == 1 && fixture.Inbox[0].IsRead && fixture.Inbox[4].IsRead, "opening the actual reader retains the original mark-read behavior for all received referrals");
        fixture.Calls.Clear();
        await Act(page, "OpenGardeshFromReader", 100);
        Check(OnlyTreeCall() && fixture.DetailReadCount == 1, "reader circulation reuses visible metadata without another mark-read detail request");
        var count = fixture.Calls.Count;
        await Act(page, "OpenGardeshFromReader", 999);
        Check(fixture.Calls.Count == count, "reader entry refuses mismatched/stale letter IDs");
        await Act(modal, "Close");

        var detailModal = Probe.Get<IncomingLetterViewModal>(page, "viewModal");
        await renderer.Dispatcher.InvokeAsync(() => detailModal.Open(100));
        html = await Html(root);
        html = html[html.IndexOf("class=\"ltx incoming-letter-view\"", StringComparison.Ordinal)..];
        Check(HasPhoto(html, 11, 2) && HasPhoto(html, 21, 3) && HasPhoto(html, 31) && HasPhoto(html, 32),
            "full-detail modal shows registrar plus every referral sender/recipient with correct photos");
        Check(html.Contains("پاراف آزمایشی") && html.Contains("پاسخ آزمایشی") && html.Contains("ثبت پاسخ") && html.Contains("ارجاع نامه"),
            "history photos preserve referral/answer text and existing action buttons");
        fixture.Calls.Clear();
        var beforeRead = fixture.DetailReadCount;
        await renderer.Dispatcher.InvokeAsync(() => detailModal.OnGardesh.InvokeAsync(fixture.Letter));
        Check(OnlyTreeCall() && fixture.DetailReadCount == beforeRead, "full-view circulation callback passes loaded metadata without refetching detail");
        var archiveModal = Probe.Get<IncomingLetterViewModal>(page, "archiveViewModal");
        fixture.Calls.Clear();
        await renderer.Dispatcher.InvokeAsync(() => archiveModal.OnGardesh.InvokeAsync(fixture.Letter));
        Check(OnlyTreeCall() && fixture.DetailReadCount == beforeRead, "archive-view circulation uses the same read-safe callback contract");

        var panelRoot = await Render<IntegratedIncomingPanel>();
        var panel = capture.Last<IntegratedIncomingPanel>();
        Check(!Probe.Get<bool>(panel, "loading") && Probe.Get<List<IncomingLetterListItemDto>>(panel, "items").Count == 1, "real integrated secretariat lifecycle still loads its registered list");
        fixture.Calls.Clear();
        await Act(panel, "OpenFlow", fixture.Inbox[4]);
        Photo(Modal(await Html(panelRoot)), 11, "secretariat list opens the same corrected circulation component");
        Check(OnlyTreeCall() && fixture.DetailReadCount == beforeRead, "secretariat circulation never treats the row referrer as registrar or reads detail");
        var panelView = Probe.Get<IncomingLetterViewModal>(panel, "viewModal");
        await renderer.Dispatcher.InvokeAsync(() => panelView.Open(100));
        beforeRead = fixture.DetailReadCount;
        fixture.Calls.Clear();
        await renderer.Dispatcher.InvokeAsync(() => panelView.OnGardesh.InvokeAsync(fixture.Letter));
        Check(OnlyTreeCall() && fixture.DetailReadCount == beforeRead, "secretariat full-detail callback reuses its loaded letter and changes no read state");

        fixture.TreeLoader = _ => Task.FromResult(new List<ErjaTreeNodeDto>());
        await renderer.Dispatcher.InvokeAsync(() => modal.Open(100, "Empty", Fixture.ExternalSender, 100, Fixture.Entered));
        Check(Modal(await Html(root)).Contains("هنوز ارجاعی") && Probe.Get<string?>(modal, "loadError") is null, "empty circulation remains a normal no-referrals state, not an error or invented user");
        fixture.TreeLoader = _ => Task.FromResult(new List<ErjaTreeNodeDto> { new() { ErjaId = 7, SenderUserId = 0, ReciverUserId = -5, Sender = "", Reciver = "" } });
        for (var view = 0; view < 3; view++)
        {
            await renderer.Dispatcher.InvokeAsync(() => modal.Open(100, "Unknown", Fixture.ExternalSender, 100, Fixture.Entered));
            await Change(modal, () => Probe.Set(modal, "view", view));
            html = Modal(await Html(root));
            Check(Regex.Matches(html, "user-avatar-default").Count == 2 && html.Contains("کاربر نامشخص") && !html.Contains("/api/auth/users/photo/"),
                $"circulation view {view}: missing/nonpositive participant IDs use default photos without name matching");
        }
        fixture.TreeLoader = _ => Task.FromException<List<ErjaTreeNodeDto>>(new InvalidOperationException("Denied"));
        await renderer.Dispatcher.InvokeAsync(() => modal.Open(100, "Failure", Fixture.ExternalSender, 100, Fixture.Entered));
        html = Modal(await Html(root));
        Check(html.Contains("role=\"alert\"") && !html.Contains("<img") && Probe.Get<List<ErjaTreeNodeDto>>(modal, "roots").Count == 0,
            "tree failure displays an explicit error, never stale participant photos");
        fixture.TreeLoader = _ => Task.FromResult<List<ErjaTreeNodeDto>>(null!);
        await renderer.Dispatcher.InvokeAsync(() => modal.Open(100, "Null", Fixture.ExternalSender, 100, Fixture.Entered));
        Check(Probe.Get<string?>(modal, "loadError") is not null, "null tree response cannot reuse previous identities");
        fixture.TreeLoader = _ => Task.FromResult(fixture.Tree);
        await renderer.Dispatcher.InvokeAsync(() => modal.Open(100, "Recovered", Fixture.ExternalSender, 100, Fixture.Entered));
        Check(Probe.Get<string?>(modal, "loadError") is null && HasPhoto(Modal(await Html(root)), 31), "reopening after a failed tree load recovers correct photos");

        var pending = new TaskCompletionSource<List<ErjaTreeNodeDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newTree = new List<ErjaTreeNodeDto> { new() { ErjaId = 20, SenderUserId = 72, Sender = "فرستنده جدید", ReciverUserId = 82, Reciver = "گیرنده جدید", Date = Fixture.Entered } };
        fixture.TreeLoader = id => id == 201 ? pending.Task : Task.FromResult(newTree);
        var openingOld = renderer.Dispatcher.InvokeAsync(() => modal.Open(201, "Old", "مبدأ قدیمی", 201, Fixture.Entered));
        await renderer.Dispatcher.InvokeAsync(() => modal.Open(202, "New", "مبدأ جدید", 202, Fixture.Entered));
        pending.SetResult(fixture.Tree); await openingOld;
        html = Modal(await Html(root));
        Check(HasPhoto(html, 72) && HasPhoto(html, 82) && !HasPhoto(html, 11) && html.Contains("مبدأ جدید") && !html.Contains("مبدأ قدیمی"),
            "late tree responses cannot overwrite a newly opened letter's origin or participant identities");
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        openingOld = renderer.Dispatcher.InvokeAsync(() => modal.Open(201, "Old failure", "Old", 201, Fixture.Entered));
        await renderer.Dispatcher.InvokeAsync(() => modal.Open(202, "New", "New", 202, Fixture.Entered));
        pending.SetException(new InvalidOperationException("Stale failure")); await openingOld;
        Check(Probe.Get<string?>(modal, "loadError") is null && HasPhoto(Modal(await Html(root)), 72), "stale failures do not replace newer photos with an error");
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.TreeLoader = _ => pending.Task;
        var closing = renderer.Dispatcher.InvokeAsync(() => modal.Open(203, "Closing", "Closing", 203, Fixture.Entered));
        await Act(modal, "Close");
        pending.SetResult(fixture.Tree); await closing;
        Check(!Probe.Get<bool>(modal, "visible") && Probe.Get<List<ErjaTreeNodeDto>>(modal, "roots").Count == 0, "closing circulation invalidates pending identity responses");
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposing = renderer.Dispatcher.InvokeAsync(() => modal.Open(204, "Disposing", "Disposing", 204, Fixture.Entered));
        await renderer.Dispatcher.InvokeAsync(modal.Dispose);
        pending.SetResult(fixture.Tree); await disposing;
        Check(Probe.Get<List<ErjaTreeNodeDto>>(modal, "roots").Count == 0, "disposed circulation ignores pending participant responses");
        Check(fixture.Calls.All(c => c.Method == "GetGardeshAsync") && fixture.DetailReadCount == beforeRead,
            "edge cases and race handling never call the mutating detail API or workflow write methods");

        var avatar = await Render<UserAvatar>(new() { ["LoginUserId"] = 21, ["CacheBust"] = 123L });
        html = await Html(avatar);
        Check(html.Contains("https://api.test.invalid/api/auth/users/photo/21?t=123") && html.Contains("onerror=") && html.Contains("/uploads/users/default.jpg"),
            "shared avatar preserves separate API base, cache busting and missing-photo fallback");
        Console.WriteLine($"ALL {checks} INCOMING-LETTER AVATAR CHECKS PASSED.");
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
