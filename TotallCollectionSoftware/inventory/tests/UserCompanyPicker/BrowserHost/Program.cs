using System.Text.RegularExpressions;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.DataProtection;
using UserCompanyPickerTests;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var fixture = new TestFixture();
fixture.Register(builder.Services, staticRenderer: false);
var app = builder.Build();
var clientRoot = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "../../../src/Inventory.Client/wwwroot"));
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(clientRoot) });
app.UseAntiforgery();
// Run the actual application's keyboard contract as well as its styles and uiRect helper.
var index = File.ReadAllText(Path.Combine(clientRoot, "index.html"));
var keyboard = Regex.Matches(index, @"<script>([\s\S]*?)</script>")
    .Select(m => m.Groups[1].Value).First(s => s.Contains("document.addEventListener('keydown'"));
app.MapGet("/fixture-hotkeys.js", () => Results.Text(keyboard, "text/javascript"));
app.MapGet("/api/auth/users/photo/{id:int}", () => Results.Text("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 40 40'><circle cx='20' cy='20' r='20' fill='#dbe7ff'/></svg>", "image/svg+xml"));
app.MapGet("/fixture/assignments", () => fixture.Http.Assignments.Select(a => new { a.UserId, a.CompanyIds }));
app.MapGet("/fixture/info", () => new { fixture = "UserCompanyPicker", syntheticData = true });
app.MapPost("/fixture/reset", () =>
{
    var clean = new TestFixture();
    fixture.Http.AccessLoader = null;
    fixture.Http.Assignments.Clear();
    fixture.Http.Access.Clear();
    foreach (var pair in clean.Http.Access) fixture.Http.Access[pair.Key] = pair.Value;
    fixture.ApiState.Users.Clear(); fixture.ApiState.Users.AddRange(clean.ApiState.Users);
    fixture.ApiState.Saves.Clear();
    return Results.Ok();
});
app.MapPost("/fixture/access-error/{enabled:bool}", (bool enabled) =>
{
    fixture.Http.AccessLoader = enabled ? _ => Task.FromException<List<int>>(new HttpRequestException("Fixture-only failure")) : null;
    return Results.Ok();
});
app.MapRazorComponents<PickerBrowserHost.App>().AddInteractiveServerRenderMode();
app.Run();
