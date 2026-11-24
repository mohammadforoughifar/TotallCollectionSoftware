using EmailDeliveryUiTests;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.FileProviders;

if (!args.Contains("--serve"))
{
    await ClientTests.Run();
    await Tests.Run();
    return;
}
var builder = WebApplication.CreateBuilder(args.Where(a => a != "--serve").ToArray());
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var fixture = new Fixture(); fixture.Register(builder.Services, false);
var app = builder.Build();
var directory = new DirectoryInfo(AppContext.BaseDirectory);
while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "src/Inventory.Client/wwwroot"))) directory = directory.Parent;
var clientRoot = Path.Combine(directory!.FullName, "src/Inventory.Client/wwwroot");
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(clientRoot) });
app.UseAntiforgery();
app.MapPost("/fixture/reset", () => { fixture.Reset(); return Results.Ok(); });
app.MapPost("/fixture/mode/{mode}", (string mode) => { fixture.Mode = mode; return Results.Ok(); });
app.MapPost("/fixture/status-fails/{value:bool}", (bool value) => { fixture.StatusFails = value; return Results.Ok(); });
app.MapGet("/fixture/state", () => new { fixture.SendCalls, fixture.RegistrationCalls, fixture.StatusCalls, fixture.HistoryCalls, fixture.SyncCalls, fixture.ReadWrites, fixture.Keys, fixture.LastRegistration });
app.MapGet("/api/auth/users/photo/{id:int}", (int id) => Results.NotFound());
app.MapRazorComponents<EmailDeliveryUiTests.App>().AddInteractiveServerRenderMode();
app.Run();
