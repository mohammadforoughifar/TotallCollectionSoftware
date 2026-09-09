using System.Text;
using System.Text.Json;
using Inventory.Api.Controllers;
using Inventory.Api.Data;
using Inventory.Api.Hubs;
using Inventory.Api.Infrastructure;
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

// RADIS-HR از همان Connection String و همان دیتابیس استفاده می‌کند، ولی DbContext و
// Migration History مستقلش را حفظ می‌کند تا موجودیت‌ها و منطق بک‌اند اصلی تغییر نکنند.
builder.Services.AddRadisHrModule(builder.Configuration, provider, connectionString);

// ثبت سرویس‌ها با اینترفیس (اصل وارونگی وابستگی — DIP)
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IWarehousingService, WarehousingService>(); // ماژول انبارداری
builder.Services.AddScoped<Inventory.Api.Services.Accounting.IAccountingService, Inventory.Api.Services.Accounting.AccountingService>(); // ماژول حسابداری
builder.Services.AddScoped<Inventory.Api.Services.Accounting.IAnalyticalDimensionService, Inventory.Api.Services.Accounting.AnalyticalDimensionService>(); // ابعاد تحلیلی (مرکز هزینه/شعبه)
builder.Services.AddScoped<Inventory.Api.Services.Accounting.IFixedAssetService, Inventory.Api.Services.Accounting.FixedAssetService>(); // دارایی ثابت و استهلاک
builder.Services.AddScoped<Inventory.Api.Services.Accounting.IBudgetService, Inventory.Api.Services.Accounting.BudgetService>(); // بودجه و کنترل بودجه
builder.Services.AddScoped<Inventory.Api.Services.Invoicing.IInvoicingService, Inventory.Api.Services.Invoicing.InvoicingService>(); // ماژول فاکتور
builder.Services.AddScoped<Inventory.Api.Services.Invoicing.IMoadianService, Inventory.Api.Services.Invoicing.MoadianService>(); // سامانه مودیان (فاکتور الکترونیکی)
builder.Services.AddScoped<Inventory.Api.Services.Invoicing.IFiscalPrinterService, Inventory.Api.Services.Invoicing.FiscalPrinterService>(); // چاپگر مالی
builder.Services.AddSingleton<Inventory.Api.Services.Invoicing.MoadianAutoSender>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Inventory.Api.Services.Invoicing.MoadianAutoSender>()); // ارسال خودکار صف مودیان
builder.Services.AddScoped<Inventory.Api.Services.Treasury.ITreasuryService, Inventory.Api.Services.Treasury.TreasuryService>(); // ماژول خزانه‌داری
builder.Services.AddScoped<Inventory.Api.Services.Stocktaking.IStocktakingService, Inventory.Api.Services.Stocktaking.StocktakingService>(); // ماژول انبارگردانی و بارکد
builder.Services.AddScoped<Inventory.Api.Services.Export.IExportService, Inventory.Api.Services.Export.ExportService>(); // خروجی PDF و Excel
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
// سازمان‌ها/سمت‌ها — مبنای جزء «واحد» در شماره نامه (Organization.NameUniq)
builder.Services.AddScoped<IOrganizationServices, OrganizationServices>();

// ---------- اتوماسیون اداری — نامه صادره (فاز دوم) — پوشه‌بندی تمیز ----------
builder.Services.AddScoped<Inventory.Api.Services.Office.Outgoing.IOutgoingPishnevisService, Inventory.Api.Services.Office.Outgoing.OutgoingPishnevisService>();
builder.Services.AddScoped<Inventory.Api.Services.Office.Outgoing.IOutgoingLetterService, Inventory.Api.Services.Office.Outgoing.OutgoingLetterService>();
builder.Services.AddScoped<Inventory.Api.Services.Office.Outgoing.IOutgoingLetterPrintService, Inventory.Api.Services.Office.Outgoing.OutgoingLetterPrintService>();
// ایمیل سازمانی (پست الکترونیک) — SMTP/IMAP با MailKit + حساب‌های دبیرخانه
builder.Services.AddScoped<Inventory.Api.Services.Office.Email.IEmailService, Inventory.Api.Services.Office.Email.EmailService>();

// ---------- آرشیو اسناد و مدارک (پوشه، دسترسی، ورژن، گردش تایید) ----------
builder.Services.AddScoped<Inventory.Api.Services.DocArchive.IDocAccessService, Inventory.Api.Services.DocArchive.DocAccessService>();
builder.Services.AddScoped<Inventory.Api.Services.DocArchive.IDocumentService, Inventory.Api.Services.DocArchive.DocumentService>();
// اعطای موقت «تایید مجدد رمز» برای فایل‌های مدارک محرمانه (در حافظه — ۱۵ دقیقه)
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<Inventory.Api.Services.DocArchive.IDocDownloadConfirmService, Inventory.Api.Services.DocArchive.DocDownloadConfirmService>();
builder.Services.AddScoped<Inventory.Api.Services.DocArchive.IDocFolderZipService, Inventory.Api.Services.DocArchive.DocFolderZipService>();
// سرویس OCR پایتون (اختیاری — بخش OcrService در appsettings). اگر BaseUrl خالی باشد موتور محلی استفاده می‌شود.
var ocrOptions = builder.Configuration.GetSection("OcrService")
    .Get<Inventory.Api.Services.DocArchive.OcrServiceOptions>()
    ?? new Inventory.Api.Services.DocArchive.OcrServiceOptions();
builder.Services.AddSingleton(ocrOptions);
builder.Services.AddSingleton<Inventory.Api.Services.DocArchive.IOcrServiceClient,
    Inventory.Api.Services.DocArchive.OcrServiceClient>();
builder.Services.AddSingleton<Inventory.Api.Services.DocArchive.IDocTextExtractorService, Inventory.Api.Services.DocArchive.DocTextExtractorService>();
builder.Services.AddSingleton<Inventory.Api.Services.DocArchive.IDocIndexService, Inventory.Api.Services.DocArchive.DocIndexService>();
// سرویس پس‌زمینه هشدار انقضای مدارک (روزانه)
builder.Services.AddSingleton<Inventory.Api.Services.DocArchive.DocExpiryWatcher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Inventory.Api.Services.DocArchive.DocExpiryWatcher>());

// ذخیره‌سازی فایل‌ها روی دیسک (uploads/ در روت API) + عکس کاربران
builder.Services.AddSingleton<FileStore>();
// نگهبان دسترسی پیوست‌ها (بر اساس ماژول صاحب پیوست)
builder.Services.AddScoped<IAttachmentGuard, AttachmentGuard>();
// واترمارک سمت سرور (حک روی بایت تصویر/PDF) برای مدارکِ دارای فلگ واترمارک
builder.Services.AddSingleton<Inventory.Api.Services.Watermark.IServerWatermarkService,
                               Inventory.Api.Services.Watermark.ServerWatermarkService>();
// گزارش‌های خروجی آرشیو اسناد (نیازمند فیلتر دسترسی کاربر)
builder.Services.AddScoped<Inventory.Api.Services.Export.IDocArchiveExportService,
                           Inventory.Api.Services.Export.DocArchiveExportService>();
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
                var path = ctx.Request.Path.Value ?? "";
                if (!string.IsNullOrEmpty(token) &&
                    (ctx.Request.Path.StartsWithSegments("/hubs/chat") ||
                     path.Contains("/download", StringComparison.OrdinalIgnoreCase) ||
                     path.Contains("/preview", StringComparison.OrdinalIgnoreCase) ||
                     path.Contains("/export-zip", StringComparison.OrdinalIgnoreCase) ||
                     path.Contains("/export", StringComparison.OrdinalIgnoreCase) ||
                     path.Contains("/template", StringComparison.OrdinalIgnoreCase)))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RadisHrAccess", policy =>
        policy.RequireAuthenticatedUser()
              .RequireAssertion(ctx => ctx.User.IsInRole("Admin")
                  || ctx.User.HasClaim("permission", "RadisHr.Access")));
});

// CORS برای کلاینت Blazor WASM (در محیط توسعه)
builder.Services.AddCors(options =>
    options.AddPolicy("wasm", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()
        .WithExposedHeaders("Content-Disposition", "Content-Length", "Accept-Ranges", "Content-Range")));

// کنترلرها + فیلتر سراسری خطا + JSON با نام‌گذاری camelCase
builder.Services.AddControllers(options =>
    {
        options.Filters.Add<ApiExceptionFilter>();
        // لاگ عملیات: ثبت خودکار هر POST/PUT/PATCH/DELETE در همه‌ی بخش‌ها
        options.Filters.Add<AuditLogFilter>();
        // میزبانی ماژول RADIS-HR: پیشوند مسیر radis-hr و سیاست دسترسی RadisHrAccess
        options.Conventions.Add(new RadisHrControllerConvention());
    })
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        // تنظیمات اصلی RADIS-HR برای پاسخ‌های دارای navigation property.
        o.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        o.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

// ================== Swagger — مستندات و تست تعاملی API (/swagger) ==================
// ================== SignalR — داشبورد بلادرنگ ==================
builder.Services.AddSignalR().AddJsonProtocol(o =>
{
    o.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});
builder.Services.AddSingleton<DashboardBroadcaster>();
builder.Services.AddScoped<Inventory.Api.Hubs.INotifyService, Inventory.Api.Hubs.NotifyService>();
builder.Services.AddScoped<Inventory.Api.Hubs.IChatRealtimeNotifier, Inventory.Api.Hubs.ChatRealtimeNotifier>();
builder.Services.AddScoped<Inventory.Api.Services.Chat.IChatService, Inventory.Api.Services.Chat.ChatService>();
builder.Services.AddScoped<Inventory.Api.Services.Chat.ChatAttachmentService>();
builder.Services.AddScoped<IPushService, PushService>();
builder.Services.AddScoped<IMessengerService, MessengerService>();
builder.Services.AddHttpClient("messenger", c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHttpClient("moadian", c => c.Timeout = TimeSpan.FromSeconds(30)); // سرویس مودیان (فاکتور الکترونیکی)
builder.Services.AddSingleton<HardwareMonitor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HardwareMonitor>());

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "API برنامه جامع — فروغ آریا + RADIS-HR V019",
        Version = "v1",
        Description = "تست تعاملی همه‌ی سرویس‌ها؛ APIهای منابع انسانی جامع زیر /radis-hr/api قرار دارند."
    });
    // DTOهایی مثل LoginRequest در هر دو سامانه وجود دارند؛ نام کامل از برخورد Schema جلوگیری می‌کند.
    o.CustomSchemaIds(type => (type.FullName ?? type.Name).Replace("+", "."));
});

var app = builder.Build();

// ================== سرویس OCR پایتون: شناسایی خودکار ==================
// اگر OcrService:BaseUrl خالی باشد، سرویس محلی پیش‌فرض (http://127.0.0.1:8765) جستجو می‌شود
// تا کاربر بدون دست‌زدن به تنظیمات هم بتواند وصل شود. برای خاموش‌کردن: OcrService:AutoDetect=false
if (!ocrOptions.IsConfigured && ocrOptions.AutoDetect)
{
    try
    {
        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        var probeResp = await probe.GetAsync("http://127.0.0.1:8765/health");
        if (probeResp.IsSuccessStatusCode)
        {
            var probeBody = await probeResp.Content.ReadAsStringAsync();
            if (probeBody.Contains("totall-ocr", StringComparison.OrdinalIgnoreCase))
            {
                ocrOptions.BaseUrl = "http://127.0.0.1:8765";
                app.Logger.LogInformation("[OcrService] سرویس OCR پایتون به‌صورت خودکار در http://127.0.0.1:8765 شناسایی شد.");
            }
        }
    }
    catch
    {
        // سرویس بالا نیست؛ پیام زیر در لاگ توضیح می‌دهد
    }
}
if (ocrOptions.IsConfigured)
    app.Logger.LogInformation("[OcrService] اتصال به سرویس OCR: {Base}", ocrOptions.BaseUrl);
else
    app.Logger.LogWarning("[OcrService] سرویس OCR پایتون تنظیم/شناسایی نشد — OCR فقط با موتور محلی (tesseract/poppler روی سرور) تلاش می‌شود.");

// ساخت دیتابیس و داده اولیه (نه در زمان ابزارهای EF)
if (!EF.IsDesignTime)
    await DbInitializer.InitializeAsync(app);

// اجرای Migration و Seed اصلی RADIS-HR روی همان دیتابیس برنامه.
await app.InitializeRadisHrAsync(EF.IsDesignTime);

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

// فایل‌های قدیمی چت هم فقط از endpoint دارای کنترل عضویت دانلود می‌شوند.
// این گارد مستقل از وجود index.html و از استقرار تک/دو سروره است.
app.Use(async (ctx, next) =>
{
    if (Inventory.Api.Services.Chat.ChatAttachmentService.IsLegacyPublicPath(ctx.Request.Path.Value))
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await next();
});


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
app.MapGet("/radis-hr", () => Results.Redirect("/bon-hr"));

// هاب بلادرنگ داشبورد
app.MapHub<DashboardHub>("/hubs/dashboard");
app.MapHub<Inventory.Api.Hubs.NotifyHub>("/hubs/notify");
app.MapHub<Inventory.Api.Hubs.ChatHub>("/hubs/chat");

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
        // پیوست نامه صادره (فایل های صادره) و پیوست ایمیل سازمانی (فایل های ایمیل)
        // هرگز به‌صورت استاتیک و بدون احراز هویت سرو نمی‌شوند —
        // دانلود فقط از مسیر API مجاز (OutgoingLetters / Email با RBAC) انجام می‌شود.
        if (ctx.Request.Path.StartsWithSegments("/فایل های صادره", StringComparison.OrdinalIgnoreCase) ||
            ctx.Request.Path.StartsWithSegments("/%D9%81%D8%A7%DB%8C%D9%84%20%D9%87%D8%A7%DB%8C%20%D8%B5%D8%A7%D8%AF%D8%B1%D9%87", StringComparison.OrdinalIgnoreCase) ||
            ctx.Request.Path.StartsWithSegments("/فایل های ایمیل", StringComparison.OrdinalIgnoreCase) ||
            ctx.Request.Path.StartsWithSegments("/%D9%81%D8%A7%DB%8C%D9%84%20%D9%87%D8%A7%DB%8C%20%D8%A7%DB%8C%D9%85%DB%8C%D9%84", StringComparison.OrdinalIgnoreCase))
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
