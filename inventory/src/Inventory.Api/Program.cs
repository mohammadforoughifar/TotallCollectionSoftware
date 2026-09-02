using System.Text;
using System.Text.Json;
using Inventory.Api.Controllers;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ================== تنظیمات دیتابیس ==================
// پیش‌فرض: SQL Server — برای توسعه/تست می‌توان Provider را روی Sqlite گذاشت.
var provider = builder.Configuration["Database:Provider"] ?? "SqlServer";
var connectionString = builder.Configuration.GetConnectionString("Default");

if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException(
        "رشته اتصال دیتابیس پیدا نشد. لطفاً مقدار ConnectionStrings:Default را در appsettings.json تنظیم کنید.");

// لاگ راه‌اندازی (رمز عبور پوشیده می‌شود) برای اشکال‌زدایی اتصال
Console.WriteLine($"[DB] Provider = {provider}");
Console.WriteLine($"[DB] ConnectionString = {Mask(connectionString)}");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        options.UseSqlite(connectionString);
    else
        options.UseSqlServer(connectionString);
});

// ثبت سرویس‌ها با اینترفیس (اصل وارونگی وابستگی — DIP)
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<AttendanceRecalcService>();
builder.Services.AddScoped<AttendanceSecurityService>();
builder.Services.AddScoped<IRepairService, RepairService>();
builder.Services.AddScoped<IExpenseService, ExpenseService>();

// ---------- اتوماسیون اداری — نامه داخلی (کارتابل، ارجاع، پیش‌نویس، گروه‌های گیرندگان) ----------
builder.Services.AddScoped<ILetterGroupService, LetterGroupService>();
builder.Services.AddScoped<IInnerLetterService, InnerLetterService>();
builder.Services.AddScoped<IErjaService, ErjaService>();
builder.Services.AddScoped<IPishnevisService, PishnevisService>();
// ساختار شماره اندیکاتور (LetterStrature) و بایگانی درختی نامه‌ها
builder.Services.AddScoped<ILetterStratureService, LetterStratureService>();
builder.Services.AddScoped<IArchiveService, ArchiveService>();

// ---------- اتوماسیون اداری — نامه صادره (فاز دوم) — پوشه‌بندی تمیز ----------
builder.Services.AddScoped<Inventory.Api.Services.Office.Outgoing.IOutgoingPishnevisService, Inventory.Api.Services.Office.Outgoing.OutgoingPishnevisService>();
builder.Services.AddScoped<Inventory.Api.Services.Office.Outgoing.IOutgoingLetterService, Inventory.Api.Services.Office.Outgoing.OutgoingLetterService>();
builder.Services.AddScoped<Inventory.Api.Services.Office.Outgoing.IOutgoingLetterPrintService, Inventory.Api.Services.Office.Outgoing.OutgoingLetterPrintService>();

// ذخیره‌سازی فایل‌ها روی دیسک (uploads/ در روت API) + عکس کاربران
builder.Services.AddSingleton<FileStore>();
builder.Services.AddSingleton<UserPhotoService>();

// ================== پیوست‌های پروژه — رمزنگاری AES روی دیسک ==================
builder.Services.AddSingleton<IProjectFileProtection, ProjectFileProtection>();

// ================== احراز هویت JWT ==================
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = AuthService.JwtIssuer,
            ValidAudience = AuthService.JwtIssuer,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthService.JwtKey))
        };
        // دانلود پیوست با لینک مستقیم (تگ <a>) هدر Authorization ندارد؛
        // برای مسیرهای دانلود، توکن از query string خوانده می‌شود (الگوی استاندارد SignalR).
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) &&
                    ctx.Request.Path.Value?.Contains("/download", StringComparison.OrdinalIgnoreCase) == true)
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// CORS برای کلاینت Blazor WASM (در محیط توسعه)
builder.Services.AddCors(options =>
    options.AddPolicy("wasm", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

// کنترلرها + فیلتر سراسری خطا + JSON با نام‌گذاری camelCase
builder.Services.AddControllers(options =>
    {
        options.Filters.Add<ApiExceptionFilter>();
        // لاگ عملیات: ثبت خودکار هر POST/PUT/PATCH/DELETE در همه‌ی بخش‌ها
        options.Filters.Add<AuditLogFilter>();
    })
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    });

// ================== Swagger — مستندات و تست تعاملی API (/swagger) ==================
// ================== SignalR — داشبورد بلادرنگ ==================
builder.Services.AddSignalR().AddJsonProtocol(o =>
{
    o.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});
builder.Services.AddSingleton<DashboardBroadcaster>();
builder.Services.AddScoped<Inventory.Api.Hubs.INotifyService, Inventory.Api.Hubs.NotifyService>();
builder.Services.AddScoped<IPushService, PushService>();
builder.Services.AddScoped<IMessengerService, MessengerService>();
builder.Services.AddHttpClient("messenger", c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton<HardwareMonitor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HardwareMonitor>());

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "API برنامه انبار — فروغ آریا",
        Version = "v1",
        Description = "تست تعاملی همه‌ی سرویس‌ها — از جمله اسکن شبکه (/api/NetworkScan) و اسکن دوربین (/api/CctvScan)"
    });
});

var app = builder.Build();

// ساخت دیتابیس و داده اولیه (نه در زمان ابزارهای EF)
if (!EF.IsDesignTime)
    await DbInitializer.InitializeAsync(app);

// سازماندهی پیوست‌ها در پوشه‌های اختصاصی هر پروژه (کد پروژه ← تصاویر/مستندات) — فقط فایل‌های قدیمیِ بدون‌پوشه
if (!EF.IsDesignTime)
{
    using var orgScope = app.Services.CreateScope();
    var orgDb = orgScope.ServiceProvider.GetRequiredService<AppDbContext>();
    await app.Services.GetRequiredService<IProjectFileProtection>().OrganizeProjectFoldersAsync(orgDb);
}

// در همه‌ی محیط‌ها فعال است (حتی Production) تا روی هر سیستمی قابل تست باشد
app.UseSwagger();
app.UseSwaggerUI(o =>
{
    o.SwaggerEndpoint("/swagger/v1/swagger.json", "API انبار v1");
    o.DocumentTitle = "API برنامه انبار";
});

app.UseCors("wasm");

app.UseAuthentication();
app.UseAuthorization();

// جلوگیری از کش‌شدن نسخه‌ی قدیمی کلاینت: همه‌ی پاسخ‌های HTML بدون کش (به‌علاوه‌ی StaticFiles)
app.Use(async (ctx, next) =>
{
    await next();
    if (!ctx.Response.HasStarted
        && ctx.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true
        && !ctx.Response.Headers.CacheControl.Any())
    {
        ctx.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
    }
});

app.MapControllers();

// هاب بلادرنگ داشبورد
app.MapHub<DashboardHub>("/hubs/dashboard");
app.MapHub<Inventory.Api.Hubs.NotifyHub>("/hubs/notify");

// پوشه‌ی فایل‌های آپلودی داخل wwwroot (عکس‌های کاربران، پیوست‌ها) — با UseStaticFiles معمول سرو می‌شود
Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "wwwroot", "uploads", "users"));

// سرو فایل‌های استاتیک کلاینت (استقرار تک‌سروره — در صورت وجود پوشه wwwroot)
var clientRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
if (Directory.Exists(clientRoot) && File.Exists(Path.Combine(clientRoot, "index.html")))
{
    // امنیت پیوست‌ها: فایل‌های رمزنگاری‌شده زیر wwwroot/SecureFiles هرگز به‌صورت استاتیک و بدون احراز هویت
    // سرو نشوند — دسترسی به آن‌ها فقط از مسیر API (ProjectAttachController با RBAC) مجاز است.
    app.Use(async (ctx, next) =>
    {
        if (ctx.Request.Path.StartsWithSegments("/SecureFiles", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        await next();
    });
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        // جلوگیری از کش‌شدن نسخه‌ی قدیمی کلاینت (service worker مرورگر)
        OnPrepareResponse = ctx =>
        {
            var path = ctx.Context.Request.Path.Value ?? "";
            if (path.StartsWith("/_framework/", StringComparison.OrdinalIgnoreCase))
                ctx.Context.Response.Headers.CacheControl = "public, max-age=86400";
            else
                ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        }
    });
    app.MapFallbackToFile("index.html");
}

var port = Environment.GetEnvironmentVariable("PORT") ?? "5100";
app.Run($"http://0.0.0.0:{port}");

// پوشاندن رمز عبور در لاگ رشته اتصال
static string Mask(string cs)
{
    foreach (var key in new[] { "Password", "Pwd", "User Id" })
    {
        var idx = cs.IndexOf(key + "=", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var valStart = idx + key.Length + 1;
            var end = cs.IndexOf(';', valStart);
            if (end < 0) end = cs.Length;
            return cs[..valStart] + "***" + cs[end..];
        }
    }
    return cs;
}
