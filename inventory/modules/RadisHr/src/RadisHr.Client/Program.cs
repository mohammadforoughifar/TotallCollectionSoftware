using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Options;
using RadisHr.Client;
using RadisHr.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// ───── نشانی سرویس REST
// حالت عادی (توصیه‌شده): برنامه از پروژهٔ RadisHr.Api اجرا می‌شود و کلاینت روی همان
// مبدأ میزبانی می‌گردد، پس نشانی پایه همان BaseAddress است.
// اگر کلاینت را جداگانه اجرا کنید، کافی است ApiBaseUrl را در wwwroot/appsettings.json
// به نشانی سرویس (مثلاً http://localhost:5080) تنظیم کنید.
var apiBaseUrl = builder.Configuration["ApiBaseUrl"];
if (string.IsNullOrWhiteSpace(apiBaseUrl))
    apiBaseUrl = builder.HostEnvironment.BaseAddress;
if (!apiBaseUrl.EndsWith('/')) apiBaseUrl += "/";

builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(apiBaseUrl)
    //BaseAddress = new Uri("http://192.168.1.130:8090/")
    //BaseAddress = new Uri("http://94.183.59.57:8090/");
});

builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<AuthState>();
builder.Services.AddScoped<ApiClient>();

var host = builder.Build();

// بازیابی نشست ذخیره‌شده پیش از نخستین رندر
await host.Services.GetRequiredService<AuthState>().InitializeAsync();

await host.RunAsync();
