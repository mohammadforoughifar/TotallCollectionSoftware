using Inventory.Client.Services;
using Preview;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddScoped<IApiClient, FixtureApi>();
builder.Services.AddScoped<IAuthState, FixtureAuth>();
builder.Services.AddScoped<IToastService, ToastService>();
builder.Services.AddHttpContextAccessor();
var app = builder.Build();
var client = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "../../../src/Inventory.Client"));
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(Path.Combine(client, "wwwroot")), RequestPath = "/assets" });
app.MapGet("/module.css", () => Results.File(Path.Combine(client, "obj/Debug/net8.0/scopedcss/bundle/Inventory.Client.styles.css"), "text/css"));
app.UseAntiforgery();
app.MapGet("/", () => Results.Redirect("/servers")).WithOrder(-1);
app.MapRazorComponents<App>().AddInteractiveServerRenderMode().AddAdditionalAssemblies(typeof(Inventory.Client.Pages.ItAssets.ServerDetails).Assembly);
app.Run();
