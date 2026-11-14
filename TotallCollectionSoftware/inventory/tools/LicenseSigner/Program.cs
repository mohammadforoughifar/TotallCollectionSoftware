using LicenseSigner;
using LicenseSigner.Data;
using LicenseSigner.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

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

builder.Services.AddSingleton(options);

var connection = options.ResolveConnectionString();
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
        cookie.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
        cookie.Cookie.SecurePolicy = CookieSecurePolicy.None; // فقط localhost
    });
builder.Services.AddAuthorization();

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(options.EffectiveWorkFolder(), "keys")));

var app = builder.Build();

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
