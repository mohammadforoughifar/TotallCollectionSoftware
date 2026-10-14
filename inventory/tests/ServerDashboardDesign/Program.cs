using Inventory.Client.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var output = args.FirstOrDefault() ?? "rendered";
Directory.CreateDirectory(output);
var services = new ServiceCollection().AddLogging().BuildServiceProvider();
await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
var date = new DateTime(2026, 10, 14, 12, 12, 0);
var data = new Dictionary<string, ServerDashboardDto>
{
    ["empty"] = new() { GeneratedAt = date },
    ["example"] = new() { GeneratedAt = date, Total = 24, Healthy = 17, Warning = 2, Critical = 1,
        Disconnected = 1, Stale = 2, Unknown = 1, Maintenance = 2, OpenAlertCount = 5, MonitoringEnabledCount = 21 }
};
foreach (var (name, summary) in data)
{
    var html = await renderer.Dispatcher.InvokeAsync(async () =>
    {
        var result = await renderer.RenderComponentAsync<ServerDashboard>(ParameterView.FromDictionary(new Dictionary<string, object?>
        { ["Summary"] = summary, ["CanCreate"] = true, ["AutoRefresh"] = true }));
        return result.ToHtmlString();
    });
    if (System.Text.RegularExpressions.Regex.Matches(html, "class=\"infra-metric ").Count != 8) throw new Exception("Expected 8 metric cards");
    if (!html.Contains("aria-pressed=\"true\"")) throw new Exception("Missing selected filter");
    if (!html.Contains("infra-auto-refresh")) throw new Exception("Missing integrated refresh control");
    if (html.Contains("infra-empty-hint") != (name == "empty")) throw new Exception("Wrong empty state");
    await File.WriteAllTextAsync(Path.Combine(output, name + ".html"), html);
}
Console.WriteLine("PASS: actual Razor component compiled and rendered; 8 cards, selected filter, refresh toggle and correct empty-state visibility in both fixtures (8 assertions).");
