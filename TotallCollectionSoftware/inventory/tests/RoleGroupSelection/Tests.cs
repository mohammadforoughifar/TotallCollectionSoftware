using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Inventory.Client.Pages.Settings;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace RoleGroupSelectionTests;

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
        var fixture = new Fixture();
        var activator = new Capture();
        var services = new ServiceCollection();
        fixture.Register(services, staticRenderer: true);
        services.AddSingleton<IComponentActivator>(activator);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<Roles>());
        var page = activator.Pages.Last();
        Task<string> Html() => renderer.Dispatcher.InvokeAsync(() => WebUtility.HtmlDecode(root.ToHtmlString()));
        Task Act(string name, params object?[] args) => renderer.Dispatcher.InvokeAsync(async () =>
        {
            if (Probe.Call(page, name, args) is Task task) await task;
            Probe.Refresh(page);
        });
        Task Set(string name, object? value) => renderer.Dispatcher.InvokeAsync(() => { Probe.Set(page, name, value); Probe.Refresh(page); });
        HashSet<int> Selected() => Probe.Get<HashSet<int>>(page, "selectedPermissionIds");
        List<Roles.PermissionDto> For(Category category) => fixture.Permissions.Where(p => category.Modules.Contains(p.Module)).ToList();
        static bool Current(Roles.PermissionDto p) => p.Module != "Moadian" || p.Action is "View" or "InvoiceCreate";
        var knownModules = Fixture.Categories.SelectMany(c => c.Modules).ToHashSet();
        var others = fixture.Permissions.Where(p => !knownModules.Contains(p.Module)).ToList();
        Task<BulkControl> Bulk(IEnumerable<Roles.PermissionDto> permissions, string scope = "آزمایش") => renderer.Dispatcher.InvokeAsync(() =>
        {
            var fragment = (RenderFragment)Probe.Call(page, "BulkSelection", permissions, scope)!;
            using var builder = new RenderTreeBuilder();
            fragment(builder);
            var frames = builder.GetFrames();
            var attrs = new Dictionary<string, object?>();
            for (var i = 1; i < frames.Count && frames.Array[i].FrameType == RenderTreeFrameType.Attribute; i++)
                attrs[frames.Array[i].AttributeName] = frames.Array[i].AttributeValue;
            return new BulkControl(attrs);
        });
        Task Click(BulkControl control) => renderer.Dispatcher.InvokeAsync(async () =>
        {
            // Dispatch the callback emitted by the real Razor fragment, not a copy of its algorithm.
            switch (control.Attributes["onclick"])
            {
                case EventCallback callback: await callback.InvokeAsync(new MouseEventArgs()); break;
                case EventCallback<MouseEventArgs> callback: await callback.InvokeAsync(new()); break;
                case Action action: action(); break;
                default: throw new InvalidOperationException("Unexpected callback type");
            }
            Probe.Refresh(page);
        });

        Check(!Probe.Get<bool>(page, "loading"), "production Roles page loads roles and permissions through its real lifecycle");
        await Act("OpenCreateModal");
        var html = await Html();
        var categoryHeaders = Regex.Matches(html, "<details class=\"perm-tree-category\"[^>]*>\\s*<summary>(.*?)</summary>", RegexOptions.Singleline);
        Check(categoryHeaders.Count == Fixture.Categories.Count + 1 && categoryHeaders.All(m => m.Value.Contains("role=\"checkbox\"") && m.Value.Contains("انتخاب همه در گروه")),
            "every named category and Other has its own select-all control");
        var moduleHeaders = Regex.Matches(html, "<details class=\"perm-tree-module\"[^>]*>\\s*<summary>(.*?)</summary>", RegexOptions.Singleline);
        Check(moduleHeaders.Count == fixture.Permissions.Select(p => p.Module).Distinct().Count() && moduleHeaders.All(m => m.Value.Contains("role=\"checkbox\"")),
            "every module, including uncategorized modules, retains a select-all control");
        Check(Selected().Count == 0, "creating a role does not pre-grant permissions");

        foreach (var category in Fixture.Categories)
        {
            var perms = For(category);
            var expected = perms.Where(Current).Select(p => p.Id).ToHashSet();
            var outside = fixture.Permissions.First(p => !category.Modules.Contains(p.Module)).Id;
            var seed = new HashSet<int> { outside, Fixture.UnknownAssignedId, fixture.Id("Moadian", "Configure") };
            await Set("selectedPermissionIds", seed.ToHashSet());
            var before = await Bulk(perms);
            await Click(before);
            var selectedCorrectly = Selected().SetEquals(seed.Union(expected)) && (await Bulk(perms)).State == "true";
            await Click(await Bulk(perms));
            var clearedCorrectly = Selected().SetEquals(seed) && (await Bulk(perms)).State == "false";
            await Act("TogglePermission", expected.First(), true);
            var partial = await Bulk(perms);
            await Click(partial);
            Check(before.State == "false" && selectedCorrectly && clearedCorrectly && partial.State == "mixed" && Selected().SetEquals(seed.Union(expected)),
                $"category '{category.Title}': select/clear/mixed states affect only its own current permissions");
        }
        Check(fixture.Saves.Count == 0, "bulk selection is local until Save role is explicitly requested");

        var work = Fixture.Categories.Single(c => c.Modules.Contains("WorkOrders"));
        var workPermissions = For(work);
        var workModule = fixture.Permissions.Where(p => p.Module == "WorkOrders").ToList();
        var unrelated = fixture.Id("Email", "View");
        await Set("selectedPermissionIds", new HashSet<int> { unrelated });
        await Click(await Bulk(workModule));
        Check(Selected().SetEquals(workModule.Select(p => p.Id).Append(unrelated)), "module toggle does not select sibling modules");
        Check((await Bulk(workModule)).State == "true" && (await Bulk(workPermissions)).State == "mixed", "selecting one child module leaves its parent category partially selected");
        await Act("TogglePermission", fixture.Id("WorkOrders", "Create"), false);
        Check((await Bulk(workModule)).State == "mixed", "individual uncheck updates the module's mixed state");
        await Click(await Bulk(workModule));
        Check((await Bulk(workModule)).State == "true", "clicking a partially selected module selects the remaining permissions");

        await Set("permissionSearch", "Create");
        await Set("selectedPermissionIds", new HashSet<int> { unrelated });
        var matching = workPermissions.Where(p => p.Action == "Create").Select(p => p.Id).ToHashSet();
        var filteredControl = await Bulk(workPermissions);
        await Click(filteredControl);
        Check(Selected().SetEquals(matching.Append(unrelated)) && filteredControl.Label.StartsWith("انتخاب نتایج"),
            "filtered group selection grants only matching actions and uses an explicit results label");
        await Act("TogglePermission", fixture.Id("WorkOrders", "View"), true);
        await Click(await Bulk(workPermissions));
        Check(Selected().SetEquals(new[] { unrelated, fixture.Id("WorkOrders", "View") }), "filtered clear preserves hidden selected actions and unrelated categories");
        await Set("permissionSearch", "WorkOrders");
        await Click(await Bulk(workPermissions));
        Check(Selected().SetEquals(workModule.Select(p => p.Id).Append(unrelated)), "module-name search limits a parent select-all to that matching module");
        await Set("permissionSearch", "انتخاب-ناموجود");
        var noMatch = await Bulk(workPermissions);
        var beforeEmpty = Selected().ToHashSet();
        await Click(noMatch);
        Check(noMatch.Disabled && noMatch.State == "false" && Selected().SetEquals(beforeEmpty), "zero matches are disabled and never treated as all-selected");
        Check(!(await Html()).Contains("class=\"perm-tree-category\""), "search with no matches does not show an actionable empty group");

        await Set("permissionSearch", "");
        await Set("selectedPermissionIds", new HashSet<int> { unrelated });
        await Click(await Bulk(others));
        Check(Selected().SetEquals(others.Select(p => p.Id).Append(unrelated)), "Other selects all uncategorized modules without touching categorized permissions");
        await Click(await Bulk(others));
        Check(Selected().SetEquals(new[] { unrelated }), "Other clear affects only the uncategorized group");
        await Set("permissionSearch", "View");
        await Click(await Bulk(others));
        Check(Selected().SetEquals(others.Where(p => p.Action == "View").Select(p => p.Id).Append(unrelated)), "Other uses the same search-safe selection semantics");

        await Set("permissionSearch", "");
        var moadian = fixture.Permissions.Where(p => p.Module == "Moadian").ToList();
        var legacy = fixture.Id("Moadian", "Configure");
        await Set("selectedPermissionIds", new HashSet<int> { legacy, Fixture.UnknownAssignedId });
        var taxBefore = await Bulk(moadian);
        await Click(taxBefore);
        Check(taxBefore.State == "false" && Selected().SetEquals(moadian.Where(Current).Select(p => p.Id).Concat(new[] { legacy, Fixture.UnknownAssignedId })),
            "obsolete Moadian actions are not granted or counted; existing hidden assignments are preserved");
        await Click(await Bulk(moadian));
        Check(Selected().SetEquals(new[] { legacy, Fixture.UnknownAssignedId }), "clearing visible Moadian permissions does not silently drop historical IDs");
        var duplicate = new[] { workModule[0], workModule[0] };
        await Set("selectedPermissionIds", new HashSet<int>());
        await Click(await Bulk(duplicate));
        Check(Selected().SetEquals(new[] { workModule[0].Id }) && (await Bulk(duplicate)).State == "true", "duplicate catalog rows do not duplicate selected IDs or corrupt all-selected state");

        await Set("saving", true);
        var disabled = await Bulk(workPermissions);
        var beforeSaving = Selected().ToHashSet();
        await Click(disabled);
        await Act("TogglePermission", workModule[0].Id, false);
        Check(disabled.Disabled && Selected().SetEquals(beforeSaving), "group and individual handlers ignore changes while saving");
        await Set("saving", false);
        await Act("OpenEditModal", fixture.RoleList.Single());
        Check(Selected().SetEquals(fixture.Assignments[10]) && (await Bulk(workPermissions)).State == "mixed", "editing restores assigned IDs and the corresponding parent mixed state");
        await Click(await Bulk(workPermissions));
        var payload = Selected().ToHashSet();
        await Act("SaveRole");
        Check(fixture.Saves.Last().Method == "PUT" && fixture.Saves.Last().Role.Id == 10 && payload.SetEquals(fixture.Saves.Last().Role.PermissionIds),
            "edit save uses the unchanged role endpoint and selected permission IDs");
        Check(fixture.Saves.Last().Role.Name == "نقش آزمایشی" && fixture.Saves.Last().Role.IsActive, "bulk controls do not change role name or active state");
        await Act("OpenCreateModal");
        Check(Selected().Count == 0 && Probe.Get<string>(page, "permissionSearch") == "" && !Probe.Get<bool>(page, "expandPermissionTree"), "new role clears prior search, expansion and selections");
        var form = Probe.Get<object>(page, "form");
        form.GetType().GetProperty("Name")!.SetValue(form, "نقش جدید آزمایشی");
        await Click(await Bulk(others));
        await Act("SaveRole");
        Check(fixture.Saves.Last().Method == "POST" && fixture.Saves.Last().Role.PermissionIds.ToHashSet().SetEquals(others.Select(p => p.Id)),
            "new role save sends only the explicitly selected group's IDs");
        await Act("OpenEditModal", fixture.RoleList.Single(r => r.Id == 10));
        var persisted = Selected().ToHashSet();
        var savesBeforeCancel = fixture.Saves.Count;
        await Click(await Bulk(others));
        await Act("CloseModal");
        await Act("OpenEditModal", fixture.RoleList.Single(r => r.Id == 10));
        Check(fixture.Saves.Count == savesBeforeCancel && Selected().SetEquals(persisted), "cancel does not save bulk changes; reopening restores persisted permissions");
        await Act("ExpandAllPermissionTrees");
        await Act("CollapseAllPermissionTrees");
        Check(Selected().SetEquals(persisted), "expand/collapse controls remain independent from selection");
        Console.WriteLine($"ALL {checks} ROLE-GROUP SELECTION CHECKS PASSED.");
    }

    private record BulkControl(Dictionary<string, object?> Attributes)
    {
        public string State => (string)Attributes["aria-checked"]!;
        public string Label => (string)Attributes["aria-label"]!;
        public bool Disabled => Attributes.TryGetValue("disabled", out var value) && value is true;
    }
    private sealed class Capture : IComponentActivator
    {
        public List<Roles> Pages { get; } = new();
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            if (component is Roles role) Pages.Add(role);
            return component;
        }
    }
    private static class Probe
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        public static T Get<T>(object o, string name) => (T)o.GetType().GetField(name, Flags)!.GetValue(o)!;
        public static void Set(object o, string name, object? value) => o.GetType().GetField(name, Flags)!.SetValue(o, value);
        public static object? Call(object o, string name, params object?[] args) => o.GetType().GetMethod(name, Flags)!.Invoke(o, args);
        public static void Refresh(ComponentBase component) => typeof(ComponentBase).GetMethod("StateHasChanged", Flags)!.Invoke(component, null);
    }
}
