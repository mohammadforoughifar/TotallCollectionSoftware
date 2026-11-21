using System.Collections;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Inventory.Client.Pages;
using Inventory.Client.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UserCompanyPickerTests;

var checks = 0;
void Check(bool ok, string description)
{
    if (!ok) throw new InvalidOperationException("FAIL: " + description);
    Console.WriteLine("PASS: " + description); checks++;
}
var fixture = new TestFixture();
var activator = new CaptureComponents();
var services = new ServiceCollection();
fixture.Register(services);
services.AddSingleton<IComponentActivator>(activator);
await using var provider = services.BuildServiceProvider();
await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<Users>());
var page = activator.Last<Users>();
Check(!Probe.Get<bool>(page, "loading"), "real Users page loads fixture companies, roles and users");
Task<string> Html(HtmlRootComponent? other = null) => renderer.Dispatcher.InvokeAsync(() => WebUtility.HtmlDecode((other ?? root).ToHtmlString()));
Task Act(ComponentBase component, string method, params object?[] args) => renderer.Dispatcher.InvokeAsync(async () =>
{
    if (Probe.Call(component, method, args) is Task task) await task;
    Probe.Refresh(component);
});
Task Set(ComponentBase component, string field, object? value) => renderer.Dispatcher.InvokeAsync(() =>
{
    Probe.Set(component, field, value); Probe.Refresh(component);
});
HashSet<int> Selected() => Probe.Get<HashSet<int>>(page, "selectedCompanyIds");
CheckComboBox Combo() => activator.Last<CheckComboBox>();
List<LookupItem> Matches() => (List<LookupItem>)Probe.Property(Combo(), "FilteredItems")!;
int[] OrganizationIds() => ((IEnumerable)Probe.Property(page, "AvailableOrganizations")!).Cast<object>()
    .Select(o => (int)o.GetType().GetProperty("OrganizationId")!.GetValue(o)!).Order().ToArray();
Task BeginEdit(int id) => renderer.Dispatcher.InvokeAsync(() => (Task)Probe.Call(page, "OpenEdit", fixture.ApiState.Users.First(u => u.Id == id))!);
void FillNewUser()
{
    var form = Probe.Get<UserDto>(page, "form");
    form.Username = "new-user"; form.Password = "test-only";
    form.FirstName = "کاربر"; form.LastName = "آزمایشی"; form.OrganizationId = 701;
    Probe.Set(page, "selectedRoleIds", new HashSet<int> { 10 });
}

await Act(page, "OpenNew");
var html = await Html();
Check(html.Contains("id=\"user-company-picker\"") && html.Contains("aria-haspopup=\"dialog\"") && html.Contains("aria-expanded=\"false\""),
    "new-user form contains a labelled, collapsed check combo");
Check(Selected().Count == 0, "new user starts without silently granting company access");
await Act(Combo(), "ToggleMenu");
html = await Html();
var optionLabels = Regex.Matches(html, "<label class=\"check-combo-option.*?</label>", RegexOptions.Singleline);
Check(optionLabels.Count == fixture.Http.Companies.Count && optionLabels.All(m => m.Value.Contains("type=\"checkbox\"")),
    "every company has a real checkbox; the list is not capped at fifty");
Check(html.Contains("position:fixed") && html.Contains("جستجوی نام یا شناسهٔ کمپانی"), "popup uses the existing floating-menu placement and a search field");
await Act(Combo(), "SetChecked", 1001, true);
Check(Selected().SetEquals(new[] { 1001 }) && Probe.Get<bool>(Combo(), "open"), "selecting a company keeps the popup open for multi-selection");
await Set(Combo(), "query", "پارس");
Check(Matches().Select(i => i.Id).SequenceEqual(new[] { 1002 }), "name search filters the checkbox list");
await Act(Combo(), "SetChecked", 1002, true);
Check(Selected().SetEquals(new[] { 1001, 1002 }) && Probe.Get<string>(Combo(), "query") == "پارس", "selecting a search result preserves previous choices and the current query");
Check((await Html()).Contains("حذف شرکت کیان"), "a selected company outside the filter remains visible as a removable chip");
await Set(Combo(), "query", "  كيان  ");
Check(Matches().Select(i => i.Id).SequenceEqual(new[] { 1001 }), "Arabic kaf/yeh and surrounding spaces match Persian company names");
await Set(Combo(), "query", "۱۰۰۲");
Check(Matches().Select(i => i.Id).SequenceEqual(new[] { 1002 }), "Persian-digit ID search works");
await Set(Combo(), "query", "١٠٠٣");
Check(Matches().Select(i => i.Id).SequenceEqual(new[] { 1003 }), "Arabic-digit ID search works");
await Set(Combo(), "query", "omega");
Check(Matches().Select(i => i.Id).SequenceEqual(new[] { 1003 }), "Latin company names are searched case-insensitively");
await Set(Combo(), "query", "no-matching-company");
Check((await Html()).Contains("موردی با این جستجو پیدا نشد") && Selected().Count == 2, "empty search results never erase company access selections");
await Act(Combo(), "SetChecked", 1001, false);
Check(Selected().SetEquals(new[] { 1002 }), "chip removal can remove a selected company outside the search results");
await Act(Combo(), "SetChecked", 1002, true);
Check(Selected().Count == 1, "checking an already-selected ID does not create duplicates");
await Set(Combo(), "query", "هم‌نام");
await Act(Combo(), "SetChecked", 1004, true);
await Act(Combo(), "SetChecked", 1005, true);
Check(Matches().Count == 2 && Selected().IsSupersetOf(new[] { 1004, 1005 }), "companies with identical names remain independently selectable by ID");
await Act(Combo(), "SetChecked", 1004, false);
Check(!Selected().Contains(1004) && Selected().Contains(1005), "unchecking one same-name company leaves the other selected");
Probe.Set(Combo(), "trigger", new ElementReference("test-trigger", new WebElementReferenceContext(fixture.Js)));
await Act(Combo(), "OnKeyDown", new KeyboardEventArgs { Key = "Escape" });
Check(!Probe.Get<bool>(Combo(), "open") && Probe.Get<bool>(page, "showModal"), "Escape closes the picker without closing the user form");
Check(fixture.Js.Calls.Contains("Blazor._internal.domWrapper.focus"), "keyboard close returns focus to the trigger");
await Act(Combo(), "ToggleMenu");
Check(Probe.Get<string>(Combo(), "query") == "" && Selected().SetEquals(new[] { 1002, 1005 }), "reopening clears only the search, not the selections");
await Act(Combo(), "CloseMenu");
Check(!Probe.Get<bool>(Combo(), "open") && Selected().Count == 2, "outside-click close retains all choices");
Check(fixture.Http.Assignments.Count == 0, "checkbox changes do not persist until the user saves the form");

// Exercise the real save path and the existing organization filtering.
await Act(page, "OpenNew");
await Act(Combo(), "ToggleMenu");
await Act(Combo(), "SetChecked", 1001, true);
await Set(Combo(), "query", "پارس");
await Act(Combo(), "SetChecked", 1002, true);
Check(OrganizationIds().SequenceEqual(new[] { 701, 702, 704 }), "organization filtering follows every selected company, not only the current search");
await renderer.Dispatcher.InvokeAsync(FillNewUser);
await Act(page, "Save");
Check(fixture.Http.Assignments.Last().UserId == 77 && fixture.Http.Assignments.Last().CompanyIds.SequenceEqual(new[] { 1001, 1002 }),
    "new user save submits all selected IDs through the original company-access endpoint");
Check(fixture.ApiState.Saves.Last().RoleIds!.SequenceEqual(new[] { 10 }) && fixture.ApiState.Saves.Last().OrganizationId == 701,
    "company picker does not rewrite the selected roles or organizational unit");
await BeginEdit(11);
Check(Selected().SetEquals(new[] { 1001, 1002 }), "editing loads the user's previously assigned companies");
await Act(Combo(), "ToggleMenu");
html = await Html();
Check(Regex.Matches(html, "<label class=\"check-combo-option selected\".*?</label>", RegexOptions.Singleline)
    .Count(m => m.Value.Contains(" checked")) == 2, "saved company assignments render as checked options");
await Act(Combo(), "SetChecked", 1002, false);
await Set(Combo(), "query", "omega");
await Act(Combo(), "SetChecked", 1003, true);
await Act(page, "Save");
Check(fixture.Http.Assignments.Last().UserId == 11 && fixture.Http.Assignments.Last().CompanyIds.SequenceEqual(new[] { 1001, 1003 }),
    "edit save adds and removes companies while preserving hidden selected IDs");

// Slow/failing requests must not turn another user's memberships into saved access.
var first = new TaskCompletionSource<List<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
var second = new TaskCompletionSource<List<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
fixture.Http.AccessLoader = id => id == 11 ? first.Task : second.Task;
var openFirst = BeginEdit(11);
html = await Html();
Check(Probe.Get<bool>(page, "companyAccessLoading") && Selected().Count == 0 && Combo().Disabled,
    "editing clears stale chips and disables the picker until access is loaded");
Check(Regex.IsMatch(html, "data-hotkey=\"f1\"[^>]*disabled"), "save is disabled while company access is being loaded");
var savedBefore = fixture.ApiState.Saves.Count;
await Act(page, "Save");
Check(fixture.ApiState.Saves.Count == savedBefore, "save handler also rejects saving while memberships are loading");
var openSecond = BeginEdit(12);
second.SetResult(new() { 1003 }); await openSecond;
first.SetResult(new() { 1001 }); await openFirst;
Check(Probe.Get<UserDto>(page, "form").Id == 12 && Selected().SetEquals(new[] { 1003 }), "late response for another user cannot overwrite current company choices");
var abandoned = new TaskCompletionSource<List<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
fixture.Http.AccessLoader = _ => abandoned.Task;
var oldEdit = BeginEdit(11);
await Act(page, "OpenNew");
await Act(Combo(), "SetChecked", 1002, true);
abandoned.SetResult(new() { 1001 }); await oldEdit;
Check(Probe.Get<UserDto>(page, "form").Id == 0 && Selected().SetEquals(new[] { 1002 }), "opening a new-user form invalidates a previous edit's pending access load");
var closed = new TaskCompletionSource<List<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
fixture.Http.AccessLoader = _ => closed.Task;
var closeEdit = BeginEdit(11);
await Act(page, "CloseModal"); closed.SetResult(new() { 1001 }); await closeEdit;
Check(!Probe.Get<bool>(page, "showModal") && Selected().Count == 0, "closing the form invalidates pending company responses");
fixture.Http.AccessLoader = _ => Task.FromException<List<int>>(new HttpRequestException("Fixture: unavailable"));
await BeginEdit(11);
html = await Html();
Check(html.Contains("کمپانی‌های کاربر دریافت نشدند") && html.Contains("تلاش دوباره") && Combo().Disabled,
    "load failure shows a retry action rather than silently treating access as empty");
await Act(page, "Save");
Check(fixture.ApiState.Saves.Count == savedBefore, "a failed access read cannot accidentally revoke all company permissions on save");
fixture.Http.AccessLoader = null;
await Act(page, "LoadCompanyAccess", 11);
Check(!Combo().Disabled && Selected().SetEquals(fixture.Http.Access[11]), "retry restores prior selections and re-enables the picker");
fixture.Http.AccessLoader = _ => Task.FromResult<List<int>>(null!);
await BeginEdit(11);
Check(Combo().Disabled && Probe.Get<string?>(page, "companyAccessError") is not null, "unexpected null memberships are treated as a read error, not permission removal");
fixture.Http.AccessLoader = null;
await Act(page, "LoadCompanyAccess", 11);
await Act(Combo(), "SetChecked", 1001, false);
await Act(Combo(), "SetChecked", 1003, false);
await Act(page, "Save");
Check(fixture.Http.Assignments.Last().CompanyIds.Count == 0, "explicitly unchecking every company retains the existing empty-list save behavior");

await Act(page, "OpenNew");
await renderer.Dispatcher.InvokeAsync(FillNewUser);
await Act(page, "OnCompaniesChanged", new List<int> { 1001, 1003 });
var saveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
fixture.ApiState.SaveGate = saveGate.Task;
var pendingSave = renderer.Dispatcher.InvokeAsync(() => (Task)Probe.Call(page, "Save")!);
await Act(page, "OpenNew"); // A different form must not change the in-flight assignment.
saveGate.SetResult(); await pendingSave;
fixture.ApiState.SaveGate = null;
Check(fixture.Http.Assignments.Last().CompanyIds.SequenceEqual(new[] { 1001, 1003 }), "company-access payload is frozen before an asynchronous user save");

var oldCombo = Combo();
await Act(page, "OpenNew");
Check(!ReferenceEquals(oldCombo, Combo()) && Selected().Count == 0 && !Probe.Get<bool>(Combo(), "open"), "each new form receives a fresh closed picker with no previous choices");
fixture.Auth.IsAdmin = false;
await Act(page, "OpenNew");
Check(!(await Html()).Contains("id=\"user-company-picker\""), "company assignment stays admin-only");
await Act(page, "OnCompaniesChanged", new List<int> { 1001 });
Check(Selected().Count == 0, "non-admin callbacks cannot grant company access");
var assignmentsBefore = fixture.Http.Assignments.Count;
await renderer.Dispatcher.InvokeAsync(FillNewUser);
await Act(page, "Save");
Check(fixture.Http.Assignments.Count == assignmentsBefore, "operator user saves never call the company-assignment endpoint");

// Standalone contract: immutable callback payload, disabled state and missing lookups.
var original = new List<int> { 999, 1001 };
List<int>? emitted = null;
var standaloneRoot = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<CheckComboBox>(ParameterView.FromDictionary(new Dictionary<string, object?>
{
    ["Items"] = fixture.Http.Companies,
    ["Values"] = original,
    ["ValuesChanged"] = EventCallback.Factory.Create<List<int>>(new object(), ids => emitted = ids)
})));
var standalone = Combo();
await Act(standalone, "SetChecked", 1002, true);
Check(original.SequenceEqual(new[] { 999, 1001 }) && emitted!.SequenceEqual(new[] { 999, 1001, 1002 }),
    "picker emits a new ordered list without mutating caller values or dropping unavailable IDs");
Check((await Html(standaloneRoot)).Contains("شناسهٔ ۹۹۹"), "missing company lookup remains visible by ID instead of silently losing its assignment");
await renderer.Dispatcher.InvokeAsync(() => standalone.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Disabled"] = true })));
emitted = null;
await Act(standalone, "SetChecked", 1001, false);
await Act(standalone, "ToggleMenu");
Check(emitted is null && !Probe.Get<bool>(standalone, "open"), "disabled picker neither opens nor changes selection");
var emptyRoot = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<CheckComboBox>());
await Act(Combo(), "ToggleMenu");
Check((await Html(emptyRoot)).Contains("موردی برای انتخاب وجود ندارد"), "empty company catalog has an explicit empty state");

Console.WriteLine($"ALL {checks} USER-COMPANY PICKER CHECKS PASSED.");

public sealed class CaptureComponents : IComponentActivator
{
    private readonly List<IComponent> components = new();
    public IComponent CreateInstance(Type componentType)
    {
        var instance = (IComponent)Activator.CreateInstance(componentType)!;
        components.Add(instance); return instance;
    }
    public T Last<T>() where T : IComponent => components.OfType<T>().Last();
}

public static class Probe
{
    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private static MemberInfo Member(object obj, string name)
    {
        for (var type = obj.GetType(); type != null; type = type.BaseType)
            if (type.GetMember(name, Flags).FirstOrDefault() is { } member) return member;
        throw new InvalidOperationException("Missing member: " + name);
    }
    public static T Get<T>(object obj, string name) => (T)((FieldInfo)Member(obj, name)).GetValue(obj)!;
    public static void Set(object obj, string name, object? value) => ((FieldInfo)Member(obj, name)).SetValue(obj, value);
    public static object? Property(object obj, string name) => ((PropertyInfo)Member(obj, name)).GetValue(obj);
    public static object? Call(object obj, string name, params object?[] args) => ((MethodInfo)Member(obj, name)).Invoke(obj, args);
    public static void Refresh(ComponentBase component) => Call(component, "StateHasChanged");
}
