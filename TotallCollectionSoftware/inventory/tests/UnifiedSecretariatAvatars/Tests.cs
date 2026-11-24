using System.Net;
using System.Text.RegularExpressions;
using Inventory.Client.Pages.Office.Letters.Incoming;
using Inventory.Client.Pages.Office.Letters.Outgoing;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Probe = OutgoingLetterAvatarTests.Probe;

namespace UnifiedSecretariatAvatarTests;

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
        bool HasPhoto(string html, int id) => html.Contains($"/api/auth/users/photo/{id}?t=0");
        void Photo(string html, int id, string text) => Check(HasPhoto(html, id) && !html.Contains("/api/SystemUsers/photo/"), text);
        var fixture = new Fixture(); var capture = new Capture(); var services = new ServiceCollection();
        fixture.Register(services, staticRenderer: true); services.AddSingleton<IComponentActivator>(capture);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        Task<HtmlRootComponent> Render<T>(Dictionary<string, object?>? parameters = null) where T : IComponent => renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters ?? new())));
        Task<string> Html(HtmlRootComponent root) => renderer.Dispatcher.InvokeAsync(() => WebUtility.HtmlDecode(root.ToHtmlString()));
        Task Change(ComponentBase component, Action? action = null) => renderer.Dispatcher.InvokeAsync(() => { action?.Invoke(); Probe.Refresh(component); });
        Task Act(ComponentBase component, string name, params object?[] args) => renderer.Dispatcher.InvokeAsync(async () => { if (Probe.Call(component, name, args) is Task task) await task; Probe.Refresh(component); });
        static List<string> OutRows(string html) => Regex.Matches(html, @"<div class=""scard secretariat-outgoing-letter""[\s\S]*?<button[^>]*title=""نمایش نامه""").Select(m => m.Value).ToList();
        static List<string> InRows(string html) => Regex.Matches(html, @"<div class=""iin-row"">[\s\S]*?<div class=""iin-row-actions"">").Select(m => m.Value).ToList();
        static string Role(string html, string cls) => Regex.Match(html, "<span class=\"" + cls + @""">[\s\S]*?</span>").Value;
        static string Modal(string html, string kind) => html[html.IndexOf("class=\"ltx " + kind + "-letter-gardesh\"", StringComparison.Ordinal)..];
        bool NoWrites() => fixture.IncomingDetailReads == 0 && fixture.OutgoingReadWrites == 0 && fixture.Calls.All(c => c.Method.StartsWith("Get"));

        var root = await Render<OutgoingDabirkhane>();
        var page = capture.Last<OutgoingDabirkhane>();
        var html = await Html(root); var rows = OutRows(html);
        Check(rows.Count == 15 && !Probe.Get<bool>(page, "loading") && Probe.Get<int>(page, "dabirTotal") == 21, "actual unified secretariat runs its real lookups and paged pending-list lifecycle");
        Photo(Role(rows[0], "secretariat-creator"), 11, "outgoing pending row uses its stored creator's login photo");
        Photo(Role(rows[1], "secretariat-creator"), 12, "same-name outgoing creators retain distinct photos");
        Check(rows[0].Contains("ایجادکننده:") && rows[0].Contains("letter-external-party") && !rows[0].Contains("secretariat-registrar"), "creator is labelled separately from external destination and nonexistent registration actor");
        Photo(rows[2], 99, "outgoing missing photo uses the unchanged shared fallback contract");
        Check(rows[3].Contains("user-avatar-default") && rows[3].Contains("کاربر نامشخص") && rows[4].Contains("user-avatar-default"), "null/negative creator IDs fall back safely");
        Check(!html.Contains("/photo/900") && !html.Contains("/photo/-7") && NoWrites(), "listing user photos neither substitutes the signed-in user nor reads/marks letters");
        await Act(page, "GoDabirPage", 2); rows = OutRows(await Html(root));
        Photo(rows[0], 90, "page two retains its own creator identity instead of recycling earlier rows");
        Check(rows.Count == 6 && fixture.Calls.Any(c => c.Page == 2 && c.Size == 15), "secretariat paging remains server-driven with the existing page size");
        await Act(page, "SetTab", "registered"); rows = OutRows(await Html(root));
        Check(rows.Count == 5, "registered tab preserves its service scope and counts");
        Photo(Role(rows[0], "secretariat-creator"), 11, "registered row still shows the letter creator");
        Photo(Role(rows[0], "secretariat-registrar"), 51, "registered row separately shows the clerk who registered it");
        Photo(Role(rows[1], "secretariat-registrar"), 52, "same-name registration clerks have separate ID-based photos");
        Check(Role(rows[2], "secretariat-registrar").Contains("user-avatar-default") && !HasPhoto(Role(rows[2], "secretariat-registrar"), 13), "legacy registrar without an ID is not guessed from an identical creator name");
        Photo(Role(rows[3], "secretariat-registrar"), 99, "known registrar without an available photo keeps the shared image fallback");
        Check(rows[0].Contains("تحویل‌گیرنده بیرونی") && rows[0].Contains("روش ارسال:") && rows[4].Contains("بایگانی شده"), "delivery details and registration/archive status remain intact");
        Check(NoWrites(), "switching pending/registered tabs for avatars causes no detail fetch or workflow write");

        await Change(page, () => Probe.Set(page, "fCreatorId", 12));
        await Act(page, "ReloadFromFilterAsync"); rows = OutRows(await Html(root));
        Check(rows.Count == 1 && HasPhoto(rows[0], 12) && HasPhoto(rows[0], 52) && fixture.Calls.Any(c => c.Creator == 12), "advanced creator filter preserves correct creator/registrar pair and server filtering");
        await Act(page, "ClearFilters");
        var registeredRow = fixture.Outgoing.Single(r => r.LetterId == 1200);
        fixture.Calls.Clear();
        await Act(page, "OpenGardesh", registeredRow); html = Modal(await Html(root), "outgoing");
        Photo(html, 11, "outgoing circulation root remains the original creator, not the registration clerk");
        Check(!HasPhoto(html, 51) && HasPhoto(html, 61) && HasPhoto(html, 31) && HasPhoto(html, 41) && HasPhoto(html, 42), "circulation displays only actual referral/signature participants, never inventing a clerk referral");
        Check(fixture.Calls.Select(c => c.Method).Order().SequenceEqual(new[] { "GetDetailAsync", "GetGardeshAsync", "GetSignersAsync" }.Order()) && fixture.OutgoingReadWrites == 0,
            "outgoing circulation is still read-only and does not call the viewer's mark-read method");
        await Act(Probe.Get<OutgoingGardeshModal>(page, "gardesh"), "Close");
        await Act(page, "View", 1200);
        Check(fixture.Navigation.LastUri == "outgoing-letters/view/1200", "display action retains the selected letter ID, not either user ID");
        var viewRoot = await Render<OutgoingLetterView>(new() { ["LetterId"] = 1200 });
        html = await Html(viewRoot);
        Photo(Role(html, "secretariat-registrar"), 51, "actual outgoing viewer displays registering user's authoritative ID from detail");
        Check(HasPhoto(html, 11) && fixture.OutgoingReadWrites == 1, "viewer keeps its creator photo and normal read-marking behavior");
        var viewer = capture.Last<OutgoingLetterView>();
        var oldDetail = fixture.OutgoingDetail(1202);
        await Change(viewer, () => Probe.Set(viewer, "letter", oldDetail));
        Check(Role(await Html(viewRoot), "secretariat-registrar").Contains("user-avatar-default"), "old detail payload without registrar ID uses a default, not a name-matched creator");

        await Act(page, "SetTab", "archived"); html = await Html(root);
        var tree = capture.Last<DabirkhaneArchiveTree>();
        Check(Regex.Matches(html, @"class=""tnode secretariat-archive-letter""").Count == 4, "real archive tree retains all letter leaves and hierarchy");
        Photo(html, 11, "archive first leaf displays the letter creator, not the archive owner");
        Photo(html, 12, "archive same-name creator retains its independent picture");
        Check(html.Contains("user-avatar-default") && !HasPhoto(html, 777) && !HasPhoto(html, 8001) && !HasPhoto(html, 900), "folders/legacy nodes never borrow folder, archive, signed-in or guessed user IDs");
        await Act(tree, "OpenLetter", fixture.ArchiveTree[0].Children[1]);
        Check(fixture.Navigation.LastUri == "outgoing-letters/view/1201", "archive display callback still uses letter identity");
        int? moved = null, unarchived = null;
        await Render<DabirkhaneArchiveTree>(new()
        {
            ["Tree"] = fixture.ArchiveTree,
            ["OnMove"] = EventCallback.Factory.Create<int>(new object(), (int id) => moved = id),
            ["OnUnarchive"] = EventCallback.Factory.Create<int>(new object(), (int id) => unarchived = id)
        });
        var callbackTree = capture.Last<DabirkhaneArchiveTree>();
        await Act(callbackTree, "Move", fixture.ArchiveTree[0].Children[0]);
        await Act(callbackTree, "Unarchive", fixture.ArchiveTree[0].Children[0]);
        Check(moved == 8001 && unarchived == 1200, "archive move/unarchive callbacks retain node/letter IDs rather than photo IDs");

        fixture.Calls.Clear();
        await Change(page, () => Probe.Set(page, "dabirType", "varede"));
        var panel = capture.Last<IntegratedIncomingPanel>();
        html = await Html(root); var inRows = InRows(html);
        Check(inRows.Count == 12 && Probe.Get<int>(panel, "registeredTotal") == 16, "incoming panel mounts within the actual unified page and loads server-paged rows");
        Photo(inRows[0], 21, "incoming registered row shows its returned registrar photo");
        Photo(inRows[1], 22, "same-name incoming users keep separate login identities");
        Check(inRows[0].Contains("ثبت‌کننده:") && inRows[0].Contains("فرستندهٔ مبدأ:") && !inRows[0].Contains("ارجاع‌دهنده:"), "incoming registrar is visibly separate from external origin");
        Check(inRows[3].Contains("کاربر نامشخص") && inRows[3].Contains("user-avatar-default"), "unknown incoming registrar uses the shared default without guessing from current user");
        Check(fixture.IncomingDetailReads == 0 && fixture.Calls.All(c => c.Method == "GetSecretariatAsync"), "incoming list images add no per-row detail requests or unread side effects");
        await Act(panel, "GoPage", 2); inRows = InRows(await Html(root));
        Check(inRows.Count == 4 && HasPhoto(inRows[0], 118), "incoming page change displays the new page's registrar photos");
        await Act(panel, "ToggleSelected", 2108);
        Check(Probe.Get<HashSet<int>>(panel, "selectedIds").SetEquals(new[] { 2108 }), "incoming selection remains keyed to letter IDs");
        await Change(page, () => Probe.Set(page, "showGuide", true));
        await Change(page, () => Probe.Set(page, "showGuide", false));
        Check(ReferenceEquals(panel, Probe.Get<IntegratedIncomingPanel>(page, "incomingPanel")) && Probe.Get<HashSet<int>>(panel, "selectedIds").Contains(2108), "help visibility preserves the mounted panel, page and selection state");
        await Act(page, "OpenIncomingView", "archived"); inRows = InRows(await Html(root));
        Photo(inRows[0], 25, "direct incoming archive shows the actual letter registrar supplied by the corrected read projection");
        Photo(inRows[1], 61, "shared incoming archive shows its returned registrar, not a private referral");
        Check(inRows[0].Contains("ثبت‌کننده:") && inRows[1].Contains("ثبت‌کننده:"), "shared incoming archive labels each row as the registrar; private referral rows are not imported");
        Check(inRows[2].Contains("user-avatar-default") && inRows[2].Contains("کاربر نامشخص") && !Regex.Match(inRows[2], @"<span class=""iin-person-name"">[\s\S]*?</span>").Value.Contains(Fixture.Outside),
            "legacy direct archive external text is not mislabelled as an internal user");
        Check(inRows[3].Contains("user-avatar-default") && !inRows[3].Contains("/photo/-8"), "invalid archived referral identity uses a default photo without an invalid request");
        var before = fixture.IncomingDetailReads;
        fixture.Calls.Clear(); await Act(panel, "OpenFlow", fixture.Archived[1]);
        html = Modal(await Html(root), "incoming");
        Photo(html, 61, "incoming circulation launched inside the unified page retains the direct-referrer photo");
        Check(HasPhoto(html, 21) && HasPhoto(html, 31) && fixture.IncomingDetailReads == before && fixture.Calls.Single().Method == "GetGardeshAsync",
            "unified incoming circulation only loads the tree, never the mutating detail endpoint");
        await Act(Probe.Get<IncomingGardeshModal>(panel, "flowModal"), "Close");
        await Act(panel, "Show", fixture.Archived[1]);
        html = await Html(root);
        Check(html.Contains("incoming-letter-view") && HasPhoto(html, 21) && HasPhoto(html, 61) && HasPhoto(html, 31) && fixture.IncomingDetailReads == before + 1,
            "unified incoming full view shows registrar/history photos while retaining its ordinary detail-read behavior");
        fixture.Calls.Clear();
        var fullView = Probe.Get<IncomingLetterViewModal>(panel, "viewModal");
        var loadedLetter = Probe.Get<IncomingLetterDetailDto>(fullView, "letter");
        await renderer.Dispatcher.InvokeAsync(() => fullView.OnGardesh.InvokeAsync(loadedLetter));
        Check(fixture.Calls.Single().Method == "GetGardeshAsync" && fixture.IncomingDetailReads == before + 1, "full-view circulation reuses loaded metadata without reading the incoming letter again");
        await Act(Probe.Get<IncomingGardeshModal>(panel, "flowModal"), "Close");
        await Act(fullView, "Close");
        await Act(page, "OpenIncomingView", "registered");
        await Change(panel, () => { Probe.Set(panel, "search", "2001"); });
        await Act(panel, "ResetAndReloadAsync"); inRows = InRows(await Html(root));
        Check(inRows.Count == 1 && HasPhoto(inRows[0], 22) && fixture.Calls.Any(c => c.Module == "incoming" && c.Search == "2001"), "incoming search preserves server-side filtering and the matching user's photo");
        fixture.Auth.CanRegister = false; await Change(panel);
        html = await Html(root);
        Check(!Regex.IsMatch(html, @"<button class=""iin-btn incoming-create""") && !html.Contains("title=\"ویرایش نامه (حتی پس از رویت گیرندگان)\""), "existing incoming register/edit permission gates are unchanged by photo rendering");
        Check(!html.Contains("/api/SystemUsers/photo/") && !html.Contains("/photo/0?") && !html.Contains("/photo/900?"), "all unified user photos retain login-ID namespace without fallback identity substitution");
        // Exercise actual shared-archive UI handlers; a call to any personal archive
        // service method now fails the fixture instead of being silently accepted.
        fixture.Auth.CanRegister = true;
        await Change(panel, () => Probe.Set(panel, "search", ""));
        await Act(panel, "ResetAndReloadAsync");
        await Act(panel, "ToggleSelected", 2000);
        fixture.Calls.Clear(); fixture.ArchiveCommands.Clear();
        var archiveTask = Act(panel, "ArchiveSelected");
        var picker = Probe.Get<DabirkhaneArchivePicker>(panel, "folderPicker");
        Check(Probe.Get<bool>(picker, "visible") && picker.Incoming, "incoming shared picker opens in incoming scope");
        Check(fixture.Calls.Any(c => c.Method == "GetSecretariatTreeAsync") && !fixture.Calls.Any(c => c.Method.Contains("DabirkhaneBayegani")), "incoming picker never loads outgoing or personal folders");
        await Change(picker, () => Probe.Set(picker, "selectedId", (int?)0));
        await Act(picker, "Confirm"); await archiveTask;
        var archivedCommand = (ArchiveSecretariatLettersDto)fixture.ArchiveCommands.Single(c => c.Method == "ArchiveSecretariatLettersAsync").Payload!;
        Check(archivedCommand.FolderId == 0 && archivedCommand.LetterIds.SequenceEqual(new[] { 2000 }), "incoming group archive accepts root and sends letter IDs to the shared endpoint");
        Check(!Probe.Get<bool>(panel, "archiveBusy") && Probe.Get<HashSet<int>>(panel, "selectedIds").Count == 0, "shared group archive releases busy state and clears selection");
        await Act(page, "OpenIncomingView", "archived");
        html = await Html(root);
        Check(html.Contains("خروج از بایگانی") && html.Contains("پوشه‌های مشترک"), "shared incoming archive has explicit unarchive and folder-management controls");
        var justArchived = fixture.Archived.Single(l => l.LetterId == 2000);
        await Act(panel, "Archive", justArchived);
        Check(fixture.ArchiveCommands.Any(c => c.Method == "UnarchiveSecretariatLetterAsync" && (int)c.Payload! == 2000), "incoming unarchive is explicit and letter-scoped, never a personal toggle");
        var moveTask = Act(panel, "MoveArchivedLetter", 8200);
        await Change(picker, () => Probe.Set(picker, "selectedId", (int?)0));
        await Act(picker, "Confirm"); await moveTask;
        var moveCommand = (MoveArchivedLetterDto)fixture.ArchiveCommands.Single(c => c.Method == "MoveSecretariatLetterAsync").Payload!;
        Check(moveCommand.BayeganiId == 8200 && moveCommand.NewParentId == 0, "incoming shared letter move keeps archive node identity and supports root");
        await Act(panel, "ToggleFolders");
        var sharedTree = capture.Last<DabirkhaneArchiveTree>();
        Check(sharedTree.Incoming && (await Html(root)).Contains("پوشه مشترک وارده"), "folder manager renders the incoming shared tree");
        await Change(sharedTree, () => Probe.Set(sharedTree, "newFolderName", "پوشه آزمون مشترک"));
        await Act(sharedTree, "CreateRoot");
        Check(fixture.ArchiveCommands.Any(c => c.Method == "AddSecretariatMainCategoryAsync"), "shared incoming folder creation uses the correct API");
        await Change(sharedTree, () => Probe.Set(sharedTree, "renameValue", "نام جدید مشترک"));
        await Act(sharedTree, "SaveRename", 9100);
        Check(fixture.IncomingFolders.Single(f => f.BayeganiId == 9100).Title == "نام جدید مشترک", "shared incoming folder rename is not owner-bound in the UI");
        fixture.Calls.Clear();
        await Act(panel, "OnDataChanged", "incoming-letters");
        Check(fixture.Calls.Any(c => c.Method == "GetSecretariatAsync"), "incoming shared archive subscribes to the existing live refresh channel");
        Check(!fixture.ArchiveCommands.Any(c => c.Method is "ToggleLetterBayeganiAsync" or "ArchiveLettersAsync" or "UnarchiveByErjaAsync"), "unified incoming archive never calls personal cartable mutation routes");
        Console.WriteLine($"ALL {checks} UNIFIED-SECRETARIAT RAZOR CHECKS PASSED.");
    }
    private sealed class Capture : IComponentActivator
    {
        private readonly List<IComponent> components = new();
        public T Last<T>() where T : IComponent => components.OfType<T>().Last();
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!; components.Add(component); return component;
        }
    }
}
