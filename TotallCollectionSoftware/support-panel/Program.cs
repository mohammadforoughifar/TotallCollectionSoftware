using SupportPanel;
using SupportPanel.Data;
using SupportPanel.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

// بایندر پیکربندی فقط true/false می‌پذیرد؛ «1» را پیش از ساخت builder عادی می‌کنیم.
if (Environment.GetEnvironmentVariable("Panel__PreviewMode") == "1")
    Environment.SetEnvironmentVariable("Panel__PreviewMode", "true");

var builder = WebApplication.CreateBuilder(args);

var options = new PanelOptions();
builder.Configuration.GetSection("Panel").Bind(options);
// محیط بر پیکربندی اولویت دارد.
options.DatabaseConnection = Environment.GetEnvironmentVariable("Panel__DatabaseConnection") ?? options.DatabaseConnection;
options.WorkFolder = Environment.GetEnvironmentVariable("Panel__WorkFolder") ?? options.WorkFolder;
options.DatabaseConnection = Environment.GetEnvironmentVariable("ConnectionStrings__Panel") ?? options.DatabaseConnection;
options.PreviewMode = options.PreviewMode
    || string.Equals(Environment.GetEnvironmentVariable("Panel__PreviewMode"), "1", StringComparison.Ordinal)
    || string.Equals(Environment.GetEnvironmentVariable("Panel__PreviewMode"), "true", StringComparison.OrdinalIgnoreCase);

// مسیر نسبی پوشهٔ کاری نسبت به ریشهٔ برنامه باز می‌شود تا F5 و dotnet run یک نتیجه بدهند.
var contentRoot = builder.Environment.ContentRootPath;
if (!string.IsNullOrWhiteSpace(options.WorkFolder) && !Path.IsPathRooted(options.WorkFolder))
    options.WorkFolder = Path.Combine(contentRoot, options.WorkFolder);

builder.Services.AddSingleton(options);

if (options.PreviewMode)
{
    // پیش‌نمایش پشت دامنهٔ پراکسی اجرا می‌شود؛ فیلتر میزبان باید هر هاستی را بپذیرد.
    builder.Configuration["AllowedHosts"] = "*";
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
        a.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });
}

var connection = PanelOptions.NormalizeSqlitePath(
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
builder.Services.AddScoped<SupportSettingsService>();
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
        cookie.Cookie.Name = "supportpanel";
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
app.MapGet("/api/health", () => Results.Ok(new { ok = true, app = "SupportPanel" }));

app.Run();
