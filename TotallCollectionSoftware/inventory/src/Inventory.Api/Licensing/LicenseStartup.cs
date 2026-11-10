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

        services.AddScoped<IInstallationIdentity, InstallationIdentity>();
        services.AddScoped<LicenseEventLogger>();
        services.AddScoped<QuotaService>();
        services.AddScoped<LicenseSessionService>();

        services.AddSingleton<EntitlementCache>();
        services.AddSingleton<LicenseActivationThrottle>();
        services.AddScoped<ILicenseService, LicenseService>();

        services.AddHostedService<LicenseSessionCleanupWorker>();

        // گارد سراسری نوشتن — برای کنترلرهایی که [RequireModule] ندارند
        services.Configure<MvcOptions>(o => o.Filters.Add<LicenseWriteGuardFilter>());

        return services;
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

        try
        {
            // ۱) کلیدهای عمومی (بارگذاری همان لحظه انجام می‌شود؛ خطای اثر انگشت هم لاگ می‌شود)
            var keys = services.GetRequiredService<PublicKeyRingProvider>();
            if (keys.Ring.KeyIds.Count == 0)
                log.LogError("[License] هیچ کلید عمومی امضایی بارگذاری نشد؛ تا رفع این مشکل هیچ فایل لایسنسی پذیرفته نمی‌شود.");
            else
                log.LogInformation("[License] {Count} کلید عمومی امضا فعال است: {Keys}",
                    keys.Ring.KeyIds.Count, string.Join(", ", keys.Ring.KeyIds));

            // ۲) شناسهٔ نصب
            var identity = services.GetRequiredService<IInstallationIdentity>();
            var installationId = await identity.GetInstallationIdAsync();
            log.LogInformation("[License] شناسهٔ نصب: {InstallationId}", installationId);

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
