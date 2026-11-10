using Inventory.Api.Licensing.Controllers;
using Inventory.Api.Licensing.Infrastructure;
using Inventory.Api.Licensing.Providers;
using Inventory.Api.Licensing.Services;
using Inventory.Licensing.Abstractions;
using Inventory.Licensing.Entitlement;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Licensing;

/// <summary>
/// ثبت سرویس‌ها و راه‌اندازی لایسنس.
///
/// <para>
/// ترتیب راه‌اندازی (طبق خواستهٔ سند): <b>اول مایگریشن، بعد بررسی لایسنس</b>.
/// شکست بررسی لایسنس <b>مانع بالا آمدن برنامه نمی‌شود</b>؛ فقط ماژول‌ها قفل می‌شوند.
/// </para>
/// </summary>
public static class LicenseStartup
{
    /// <summary>ثبت سرویس‌ها و گاردها.</summary>
    public static IServiceCollection AddLicensing(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new LicensingOptions();
        configuration.GetSection(LicensingOptions.SectionName).Bind(options);
        services.AddSingleton(options);

        services.AddSingleton<LicensePaths>();
        services.AddSingleton<PublicKeyRingProvider>();

        // Provider: در بستهٔ OnPrem فقط فایل امضاشده وجود دارد.
#if SAAS
        if (LicenseDeployment.FromConfiguration(configuration) == Inventory.Licensing.Model.DeploymentKind.Saas)
            services.AddScoped<ILicenseProvider, DatabaseLicenseProvider>();
        else
            services.AddScoped<ILicenseProvider, SignedFileLicenseProvider>();
#else
        services.AddScoped<ILicenseProvider, SignedFileLicenseProvider>();
#endif

        // هم قرارداد و هم کلاس مشخص ثبت می‌شود: راه‌اندازی به EnsureWithGraceAsync نیاز دارد
        // و آن جزو قرارداد ILicenseProvider نیست. (نبودش در اجرای واقعی، خطای «No service for type» داد.)
        services.AddScoped<InstallationIdentity>();
        services.AddScoped<IInstallationIdentity>(sp => sp.GetRequiredService<InstallationIdentity>());
        services.AddScoped<LicenseEventLogger>();
        services.AddSingleton<QuotaIntegrity>();      // کلید HMAC سهمیه، یک‌بار خوانده می‌شود
        services.AddScoped<QuotaService>();
        services.AddScoped<QuotaSendCoordinator>();   // رزرو ← تماس ← تسویه (مودیان)
        services.AddScoped<LicenseSessionService>();

        services.AddSingleton<EntitlementCache>();
        services.AddSingleton<LicenseActivationThrottle>();
        services.AddScoped<ILicenseService, LicenseService>();

        services.AddHostedService<LicenseSessionCleanupWorker>();

        // گارد سراسری نوشتن — برای کنترلرهایی که [RequireModule] ندارند
        services.Configure<MvcOptions>(o => o.Filters.Add<LicenseWriteGuardFilter>());

        // تبدیل «سقف سهمیه پر است» به ۴۰۳ با کد مشخص.
        // AddLicensing قبل از AddControllers صدا زده می‌شود، پس فیلتر ما زودتر از
        // ApiExceptionFilter (که هر استثنا را به ۴۰۰ تبدیل می‌کند) اجرا می‌شود.
        // ترتیب صریح لازم است: overload بدون order یک TypeFilterAttribute می‌سازد که
        // Order خودش صفر است و IOrderedFilter درونی نادیده گرفته می‌شود؛ در نتیجه
        // ApiExceptionFilter عمومی زودتر اجرا می‌شد و پاسخ ۴۰۰ می‌داد نه ۴۰۳.
        services.Configure<MvcOptions>(o => o.Filters.Add<LicenseQuotaExceptionFilter>(int.MinValue));

        return services;
    }

    /// <summary>
    /// مقایسهٔ کنترلرهای واقعی اسمبلی با رجیستری ماژول‌ها و ثبت نتیجه در لاگ.
    ///
    /// <para>
    /// چرا؟ رفتار زمان اجرا برای کنترلر نگاشت‌نشده «fail-open با هشدار» است تا سرویس
    /// مشتری با یک کنترلر تازه نخوابد. ولی اگر کسی هشدار را نبیند، پوشش لایسنس بی‌سروصدا
    /// سوراخ می‌شود. این بررسی، کل تصویر را یک‌بار در بوت می‌دهد (و تست
    /// <c>tests/Licensing</c> همان را سخت‌گیرانه و شکست‌خور چک می‌کند).
    /// </para>
    /// </summary>
    private static void LogRegistryCoverage(ILogger log)
    {
        try
        {
            var allTypes = typeof(LicenseStartup).Assembly.GetTypes()
                .Where(t => t.IsClass && typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(t))
                .ToList();

            // کلاس پایه (BaseApiController) کنترلر مسیرپذیر نیست: از آن ارث‌بری می‌شود ولی
            // خودش هیچ مسیری ندارد. پس هر کلاسی که کلاس دیگری از آن مشتق شده، حذف می‌شود.
            var controllers = allTypes
                .Where(t => t.IsPublic && !t.IsAbstract)
                .Where(t => !allTypes.Any(other => other != t && t.IsAssignableFrom(other)))
                .Select(t => t.Name.EndsWith("Controller", StringComparison.Ordinal) ? t.Name[..^"Controller".Length] : t.Name)
                .ToList();

            var unmapped = controllers
                .Where(name => !LicenseModuleRegistry.IsExempt(name) && LicenseModuleRegistry.Resolve(name) is null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();

            if (unmapped.Count == 0)
                log.LogInformation("[License] پوشش رجیستری: همهٔ {Count} کنترلر به ماژول نگاشت شده‌اند.", controllers.Count);
            else
                log.LogWarning("[License] پوشش رجیستری: {Count} کنترلر نگاشت نشده‌اند (رفتار: عبور با هشدار): {Names}",
                    unmapped.Count, string.Join(", ", unmapped));
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "[License] خودبازبینی پوشش رجیستری انجام نشد.");
        }
    }

    /// <summary>
    /// آماده‌سازی اولیه: خواندن کلیدهای عمومی، ساخت شناسهٔ نصب و پر کردن کش.
    /// <para>
    /// این متد استثنا پرتاب نمی‌کند؛ در بدترین حالت برنامه با وضعیت «لایسنس نامعتبر»
    /// بالا می‌آید و صفحهٔ فعال‌سازی کار می‌کند.
    /// </para>
    /// </summary>
    public static async Task InitializeLicensingAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var log = services.GetRequiredService<ILoggerFactory>().CreateLogger("License");

        // ۰) خودبازبینی پوشش رجیستری — قبل از هر چیز دیگری و بیرون از try، چون این
        //    بررسی هیچ ربطی به لایسنس این نصب ندارد و باید همیشه در لاگ باشد.
        LogRegistryCoverage(log);

        try
        {
            // ۱) کلیدهای عمومی (بارگذاری همان لحظه انجام می‌شود؛ خطای اثر انگشت هم لاگ می‌شود)
            var keys = services.GetRequiredService<PublicKeyRingProvider>();
            if (keys.Ring.KeyIds.Count == 0)
                log.LogError("[License] هیچ کلید عمومی امضایی بارگذاری نشد؛ تا رفع این مشکل هیچ فایل لایسنسی پذیرفته نمی‌شود.");
            else
                log.LogInformation("[License] {Count} کلید عمومی امضا فعال است: {Keys}",
                    keys.Ring.KeyIds.Count, string.Join(", ", keys.Ring.KeyIds));

            // ۲) شناسهٔ نصب + تشخیص «نصب موجود» و ثبت مهلت سازگاری (فقط یک‌بار در عمر نصب)
            var identity = services.GetRequiredService<InstallationIdentity>();
            var installation = await identity.EnsureWithGraceAsync();
            log.LogInformation("[License] شناسهٔ نصب: {InstallationId} | نصب موجود: {Legacy} | مهلت: {Grace}",
                installation.InstallationId, installation.IsLegacyInstall, installation.GraceUntil?.ToString("yyyy-MM-dd") ?? "-");

            // ۳) کش وضعیت
            var cache = services.GetRequiredService<EntitlementCache>();
            var entitlement = await cache.GetAsync();

            log.LogInformation("[License] وضعیت: {Status} — مشتری: {Customer} — ماژول‌های فعال: {Count}",
                entitlement.Status, entitlement.CustomerName ?? "-",
                entitlement.Modules.Count(m => m.Value.IsReadable));

            foreach (var issue in entitlement.Issues.Where(i => i.Severity == LicenseIssueSeverity.Error))
                log.LogWarning("[License] {Code}: {Message}", issue.Code, issue.Message);
        }
        catch (Exception ex)
        {
            // «شکست بررسی لایسنس مانع بالا آمدن برنامه نشود»
            log.LogError(ex, "[License] آماده‌سازی لایسنس با خطا مواجه شد؛ برنامه با وضعیت «لایسنس نامعتبر» ادامه می‌دهد.");
            try
            {
                services.GetRequiredService<EntitlementCache>().Invalidate();
            }
            catch { /* بی‌صدا */ }
        }
    }
}
