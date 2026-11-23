using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.FileProviders;
using IncomingLetterAvatarTests;

if (!args.Contains("--serve"))
{
    await Tests.Run();
    return;
}

var builder = WebApplication.CreateBuilder(args.Where(a => a != "--serve").ToArray());
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var fixture = new Fixture();
fixture.Register(builder.Services, staticRenderer: false);
var app = builder.Build();
var sourceRoot = new DirectoryInfo(AppContext.BaseDirectory);
while (sourceRoot != null && !Directory.Exists(Path.Combine(sourceRoot.FullName, "src/Inventory.Client/wwwroot")))
    sourceRoot = sourceRoot.Parent;
var clientRoot = sourceRoot == null
    ? throw new DirectoryNotFoundException("Build and run this fixture inside the inventory source tree.")
    : Path.Combine(sourceRoot.FullName, "src/Inventory.Client/wwwroot");
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(clientRoot) });
app.UseAntiforgery();
var index = File.ReadAllText(Path.Combine(clientRoot, "index.html"));
var keyboard = Regex.Matches(index, @"<script>([\s\S]*?)</script>")
    .Select(m => m.Groups[1].Value).First(s => s.Contains("document.addEventListener('keydown'"));
app.MapGet("/fixture-hotkeys.js", () => Results.Text(keyboard, "text/javascript"));
app.MapGet("/fixture/info", () => new { fixture = "IncomingLetterAvatars", syntheticData = true });
app.MapPost("/fixture/reset", () => { fixture.Reset(); return Results.Ok(); });
app.MapGet("/fixture/calls", () => fixture.Calls.ToArray());
app.MapGet("/fixture/state", () => new { fixture.DetailReadCount, unread = fixture.Inbox.Count(r => !r.IsRead) });
app.MapPost("/fixture/tree-error/{enabled:bool}", (bool enabled) => { fixture.FailTree = enabled; return Results.Ok(); });
// Synthetic, distinct ID pictures. ID 99 deliberately tests the shared fallback.
app.MapGet("/api/auth/users/photo/{id:int}", (int id) => id == 99
    ? Results.NotFound()
    : Results.Text(Avatar(id), "image/svg+xml"));
app.MapGet("/uploads/users/default.jpg", () => Results.Text(Avatar(0), "image/svg+xml"));
app.MapRazorComponents<IncomingLetterAvatarTests.App>().AddInteractiveServerRenderMode();
app.Run();

static string Avatar(int id)
{
    var colors = new[] { "#64748b", "#2563eb", "#7c3aed", "#0d9488", "#d97706", "#db2777" };
    var color = colors[Math.Abs(id % colors.Length)];
    return $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 80 80'><rect width='80' height='80' rx='40' fill='{color}'/><circle cx='40' cy='27' r='13' fill='#fff'/><path d='M13 73a27 27 0 0 1 54 0' fill='#fff'/><text x='40' y='70' text-anchor='middle' font-family='sans-serif' font-size='18' fill='{color}'>{(id == 0 ? "?" : id.ToString())}</text></svg>";
}
