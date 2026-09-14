using Inventory.Client.Services;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

public class NavigationTests
{
    static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    [Fact] public void Every_original_destination_is_preserved_without_duplicates()
    {
        var original = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"original-routes.json")))!;
        var current = HrNavigation.Sections.SelectMany(s => s.Items).Select(i => i.Href).Append("hr-main").ToArray();
        Assert.Equal(current.Length, current.Distinct().Count());
        Assert.Empty(original.Except(current));
        Assert.Equal(72, original.Length);
    }
    [Fact] public void Every_catalog_destination_has_a_real_page()
    {
        var routes = Directory.EnumerateFiles(Path.Combine(Root,"inventory/src/Inventory.Client/Pages"),"*.razor",SearchOption.AllDirectories)
            .SelectMany(p => Regex.Matches(File.ReadAllText(p), "@page \"/([^\"]*)\"").Select(m => m.Groups[1].Value)).ToHashSet();
        Assert.All(HrNavigation.Sections.SelectMany(s => s.Items), i => Assert.Contains(i.Href,routes));
        Assert.Contains("hr-main/section/{SectionId}", routes);
        Assert.Contains("hr-main/employees/{Id:int}/profile", routes);
        Assert.Contains("hr-core/employees/{Id:int}", routes);
    }
    [Fact] public void No_permission_means_no_entry_and_admin_sees_all()
    {
        Assert.False(HrNavigation.CanEnter(_ => false, false));
        Assert.Equal(8,HrNavigation.Visible(_ => false,true).Length);
        Assert.All(HrNavigation.Visible(_ => false,true), g => Assert.NotEmpty(g.Items));
    }
    [Theory]
    [InlineData("FaAtt.Create", "fa-att/clock", "fa-att/daily")]
    [InlineData("FaAtt.Manage", "fa-att/leaves", "fa-att/my-leaves")]
    [InlineData("FaLms.Manage", "fa-lms/needs", "fa-lms/courses")]
    [InlineData("FaLms.Read", "fa-lms/courses", "fa-lms/needs")]
    [InlineData("FaCom.Manage", "fa-com/tickets", "fa-com/polls")]
    [InlineData("FaPay.Manage", "fa-pay", "hr-main/employees")]
    [InlineData("HrCore.Create", "hr-main/employees/new", "hr-main/employees")]
    [InlineData("HrCore.Read", "hr-main/employees", "hr-core/audit")]
    [InlineData("HrCore.Manage", "hr-core/audit", "hr-main/employees")]
    public void Exact_permissions_not_module_presence(string permission,string present,string absent)
    {
        var items = HrNavigation.Visible(p => p == permission,false).SelectMany(s => s.Items).Select(i => i.Href);
        Assert.Contains(present,items); Assert.DoesNotContain(absent,items);
    }
    [Theory]
    [InlineData("hr-main/employees/42/profile?tab=1", "hr-main/employees", "people")]
    [InlineData("hr-main/employees/new", "hr-main/employees/new", "people")]
    [InlineData("hr-core/employees/42", "hr-core/employees", "legacy")]
    [InlineData("fa-pay/reports", "fa-pay/reports", "reports")]
    [InlineData("fa-com/my-suggestions", "fa-com/my-suggestions", "my")]
    public void Longest_path_selects_one_leaf(string path,string href,string section)
    {
        Assert.Equal(href,HrNavigation.Find(path)?.Href);
        Assert.Equal(section,HrNavigation.SectionId(path));
    }
    [Fact] public void Routes_are_normalized_without_swallowing_other_modules()
    {
        Assert.Equal("hr-main/employees",HrNavigation.Path("/HR-MAIN/Employees/?q=a#x"));
        Assert.False(HrNavigation.IsWorkspace("bon-hr/employees"));
        Assert.False(HrNavigation.IsWorkspace("hr-main-other"));
        Assert.False(HrNavigation.IsWorkspace("work-orders"));
        Assert.Equal("setup",HrNavigation.SectionId("hr-main/section/setup"));
        Assert.Equal("پرونده پرسنل",HrNavigation.Title("hr-main/employees/42/profile"));
    }
}
