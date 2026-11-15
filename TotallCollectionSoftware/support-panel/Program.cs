using LicenseSigner;
using LicenseSigner.Data;
using LicenseSigner.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

// بایندر پیکربندی فقط true/false می‌پذیرد؛ «1» را پیش از ساخت builder عادی می‌کنیم.
if (Environment.GetEnvironmentVariable("Signer__PreviewMode") == "1")
    Environment.SetEnvironmentVariable("Signer__PreviewMode", "true");

var builder = WebApplication.CreateBuilder(args);

var options = new SignerOptions();
builder.Configuration.GetSection("Signer").Bind(options);
// محیط بر پیکربندی اولویت دارد (رازها فقط از این راه می‌آیند).
options.ToolPath = Environment.GetEnvironmentVariable("Signer__ToolPath") ?? options.ToolPath;
options.PrivateKeyPath = Environment.GetEnvironmentVariable("Signer__PrivateKeyPath") ?? options.PrivateKeyPath;
options.PublicKeyPath = Environment.GetEnvironmentVariable("Signer__PublicKeyPath") ?? options.PublicKeyPath;
options.KeyId = Environment.GetEnvironmentVariable("Signer__KeyId") ?? options.KeyId;
options.WorkFolder = Environment.GetEnvironmentVariable("Signer__WorkFolder") ?? options.WorkFolder;
options.PresetsPath = Environment.GetEnvironmentVariable("Signer__PresetsPath") ?? options.PresetsPath;
options.DatabaseConnection = Environment.GetEnvironmentVariable("Signer__DatabaseConnection") ?? options.DatabaseConnection;
options.DatabaseConnection = Environment.GetEnvironmentVariable("ConnectionStrings__Panel") ?? options.DatabaseConnection;
// حالت پیش‌نمایش (دمو پشت پراکسی HTTPS): از پیکربندی یا متغیر محیطی.
options.PreviewMode = options.PreviewMode
    || string.Equals(Environment.GetEnvironmentVariable("Signer__PreviewMode"), "1", StringComparison.Ordinal)
    || string.Equals(Environment.GetEnvironmentVariable("Signer__PreviewMode"), "true", StringComparison.OrdinalIgnoreCase);

// مسیرهای نسبی (پوشهٔ کاری/ابزار/presets/کلیدها) نسبت به ریشهٔ برنامه باز می‌شوند، نه پوشهٔ جاری.
// این‌طور اجرا از Visual Studio/Rider (F5) و dotnet run هر دو یک نتیجه می‌دهند.
var contentRoot = builder.Environment.ContentRootPath;
if (!string.IsNullOrWhiteSpace(options.WorkFolder) && !Path.IsPathRooted(options.WorkFolder))
    options.WorkFolder = Path.Combine(contentRoot, options.WorkFolder);
if (!string.IsNullOrWhiteSpace(options.ToolPath) && !Path.IsPathRooted(options.ToolPath))
    options.ToolPath = Path.GetFullPath(Path.Combine(contentRoot, options.ToolPath));
if (!string.IsNullOrWhiteSpace(options.PresetsPath) && !Path.IsPathRooted(options.PresetsPath))
    options.PresetsPath = Path.GetFullPath(Path.Combine(contentRoot, options.PresetsPath));
if (!string.IsNullOrWhiteSpace(options.PrivateKeyPath) && !Path.IsPathRooted(options.PrivateKeyPath))
    options.PrivateKeyPath = Path.GetFullPath(Path.Combine(contentRoot, options.PrivateKeyPath));
if (!string.IsNullOrWhiteSpace(options.PublicKeyPath) && !Path.IsPathRooted(options.PublicKeyPath))
    options.PublicKeyPath = Path.GetFullPath(Path.Combine(contentRoot, options.PublicKeyPath));

// اگر مسیر ابزار امضا/presets تعیین نشده یا روی دیسک نیست، نسخهٔ کنار خود برنامه را
// امتحان می‌کنیم (<OutDir>/tools/…) که هدف BuildAndCopyLicenseTool می‌سازد؛
// این‌طور F5 در Visual Studio/Rider بدون تنظیم دستی مسیر کار می‌کند.
var bundledTool = Path.Combine(AppContext.BaseDirectory, "tools", "LicenseTool.dll");
if ((string.IsNullOrWhiteSpace(options.ToolPath) || !File.Exists(options.ToolPath)) && File.Exists(bundledTool))
    options.ToolPath = bundledTool;

var bundledPresets = Path.Combine(AppContext.BaseDirectory, "tools", "presets.json");
if ((string.IsNullOrWhiteSpace(options.PresetsPath) || !File.Exists(options.PresetsPath)) && File.Exists(bundledPresets))
    options.PresetsPath = bundledPresets;

builder.Services.AddSingleton(options);

if (options.PreviewMode)
{
    // پیش‌نمایش پشت دامنهٔ پراکسی اجرا می‌شود؛ فیلتر میزبان باید هر هاستی را بپذیرد.
    builder.Configuration["AllowedHosts"] = "*";
    // پشت پراکسی HTTPS: پروتکل/هاست واقعی از هدرهای X-Forwarded خوانده شود.
    builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                              | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
                              | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost;
        o.KnownNetworks.Clear();
        o.KnownProxies.Clear();
    });
    // توکن آنتی‌فورجری هم مثل کوکی نشست باید داخل iframe ارسال شود.
    builder.Services.AddAntiforgery(a =>
    {
        a.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.None;
        // همان‌چه در درخواست است: پشت پراکسی HTTPS ⇒ Secure، روی HTTP ساده ⇒ بدون Secure.
        a.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });
}

var connection = SignerOptions.NormalizeSqlitePath(
    options.ResolveConnectionString(builder.Configuration.GetConnectionString("Panel")), contentRoot);

// پوشهٔ فایل SQLite باید پیش از Migration وجود داشته باشد.
if (!connection.Contains("Server=", StringComparison.OrdinalIgnoreCase))
{
    var dataSource = connection.Split(';')
        .Select(p => p.Trim()).FirstOrDefault(p => p.StartsWith("Data Source", StringComparison.OrdinalIgnoreCase));
    if (dataSource is not null)
    {
        var path = dataSource[(dataSource.IndexOf('=') + 1)..].Trim();
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
    }
}
builder.Services.AddDbContext<SupportDbContext>(db =>
{
    if (connection.Contains("Server=", StringComparison.OrdinalIgnoreCase)) db.UseSqlServer(connection);
    else db.UseSqlite(connection);
});

builder.Services.AddScoped<AuditChainService>();
builder.Services.AddScoped<SupportAuthService>();
builder.Services.AddScoped<RequestService>();
builder.Services.AddScoped<DraftService>();
builder.Services.AddSingleton<LicenseToolRunner>();
builder.Services.AddSingleton<LicenseInspector>();
builder.Services.AddScoped<SigningRunner>();
builder.Services.AddScoped<SupportSettingsService>();
builder.Services.AddScoped<DeliveryService>();
builder.Services.AddScoped<BackupService>();
builder.Services.AddScoped<SeedService>();

builder.Services.AddRazorPages(pages => pages.Conventions.AuthorizeFolder("/"));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(cookie =>
    {
        cookie.LoginPath = "/Login";
        cookie.LogoutPath = "/Logout";
        cookie.AccessDeniedPath = "/Login";
        cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
        cookie.SlidingExpiration = true;
        cookie.Cookie.Name = "licsupport";
        cookie.Cookie.HttpOnly = true;
        // localhost: Lax + بدون Secure؛ پیش‌نمایش پشت HTTPS: None + Secure (برای iframe).
        cookie.Cookie.SameSite = options.PreviewMode
            ? Microsoft.AspNetCore.Http.SameSiteMode.None
            : Microsoft.AspNetCore.Http.SameSiteMode.Lax;
        cookie.Cookie.SecurePolicy = options.PreviewMode ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.None;
    });
builder.Services.AddAuthorization();

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(options.EffectiveWorkFolder(), "keys")));

var app = builder.Build();

if (options.PreviewMode)
{
    app.UseForwardedHeaders();
    app.Logger.LogWarning("حالت پیش‌نمایش فعال است (کوکی‌ها SameSite=None; Secure) — فقط برای دمو، نه استفادهٔ واقعی.");
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

// پایگاه‌داده + کاربر آغازین (گذرواژه فقط روی کنسول).
using (var scope = app.Services.CreateScope())
{
    var seed = scope.ServiceProvider.GetRequiredService<SeedService>();
    await seed.SeedAsync();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();

// میان‌افزار «تعویض اجباری گذرواژهٔ آغازین»: تا تعویض نشود، هیچ مسیر دیگری باز نیست.
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "/";
    var exempt = path.StartsWith("/ChangePassword", StringComparison.OrdinalIgnoreCase)
                 || path.StartsWith("/Logout", StringComparison.OrdinalIgnoreCase)
                 || path.StartsWith("/site.css", StringComparison.OrdinalIgnoreCase)
                 || path.StartsWith("/api/health", StringComparison.OrdinalIgnoreCase);

    if (!exempt && context.User.Identity?.IsAuthenticated == true)
    {
        var db = context.RequestServices.GetRequiredService<SupportDbContext>();
        var userId = context.User.UserId();
        var mustChange = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId).Select(u => u.MustChangePassword).FirstOrDefaultAsync();

        if (mustChange)
        {
            context.Response.Redirect("/ChangePassword?first=1");
            return;
        }
    }

    await next();
});

app.UseAuthorization();
app.MapRazorPages();
app.MapGet("/api/health", () => Results.Ok(new { ok = true, app = "LicenseSigner" }));

app.Run();
