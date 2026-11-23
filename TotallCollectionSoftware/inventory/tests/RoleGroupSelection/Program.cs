using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.FileProviders;
using RoleGroupSelectionTests;

if (!args.Contains("--serve"))
{
    await Tests.Run();
    return;
}

// Interactive host for the exact production component, backed by in-memory fixtures only.
var builder = WebApplication.CreateBuilder(args.Where(a => a != "--serve").ToArray());
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var fixture = new Fixture();
fixture.Register(builder.Services, staticRenderer: false);
var app = builder.Build();
// Locate the real assets independently of the invocation's working directory.
var sourceRoot = new DirectoryInfo(AppContext.BaseDirectory);
while (sourceRoot != null && !Directory.Exists(Path.Combine(sourceRoot.FullName, "src/Inventory.Client/wwwroot")))
    sourceRoot = sourceRoot.Parent;
var clientRoot = sourceRoot == null
    ? throw new DirectoryNotFoundException("Run this fixture from a build inside the inventory source tree.")
    : Path.Combine(sourceRoot.FullName, "src/Inventory.Client/wwwroot");
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(clientRoot) });
app.UseAntiforgery();
// Exercise the real global keyboard handlers alongside the production styles.
var index = File.ReadAllText(Path.Combine(clientRoot, "index.html"));
var keyboard = Regex.Matches(index, @"<script>([\s\S]*?)</script>")
    .Select(m => m.Groups[1].Value).First(s => s.Contains("document.addEventListener('keydown'"));
app.MapGet("/fixture-hotkeys.js", () => Results.Text(keyboard, "text/javascript"));
app.MapGet("/fixture/info", () => new { fixture = "RoleGroupSelection", syntheticData = true });
app.MapPost("/fixture/reset", () => { fixture.Reset(); return Results.Ok(); });
app.MapGet("/fixture/catalog", () => new { categories = Fixture.Categories, permissions = fixture.Permissions });
app.MapGet("/fixture/saves", () => fixture.Saves.Select(s => new { s.Method, s.Role }));
app.MapRazorComponents<RoleGroupSelectionTests.App>().AddInteractiveServerRenderMode();
app.Run();
