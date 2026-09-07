using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using RadisHr.Api.Data;
using RadisHr.Api.Services;

namespace Inventory.Api.Infrastructure;

/// <summary>
/// HR controllers are compiled in Inventory.Api. Expose native /api/hr routes and
/// keep the old /radis-hr/api URLs as authenticated aliases for existing clients.
/// </summary>
public sealed class RadisHrControllerConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers.Where(c =>
                     c.ControllerType.Namespace == "RadisHr.Api.Controllers"))
        {
            foreach (var selector in controller.Selectors.ToArray())
            {
                var route = selector.AttributeRouteModel;
                if (route?.Template?.StartsWith("api/", StringComparison.Ordinal) != true) continue;

                var legacy = new SelectorModel(selector)
                {
                    AttributeRouteModel = new AttributeRouteModel(route) { Template = "radis-hr/" + route.Template }
                };
                selector.AttributeRouteModel = new AttributeRouteModel(route) { Template = "api/hr/" + route.Template[4..] };
                controller.Selectors.Add(legacy);
            }
            controller.Filters.Add(new AuthorizeFilter("RadisHrAccess"));
        }
    }
}

public static class RadisHrIntegration
{
    public static IServiceCollection AddRadisHrModule(
        this IServiceCollection services, string provider, string connectionString)
    {
        services.AddAuthorization(options =>
            options.AddPolicy("RadisHrAccess", policy => policy.RequireAuthenticatedUser()
                .RequireAssertion(context => context.User.IsInRole("Admin")
                    || context.User.HasClaim("permission", "RadisHr.Access"))));

        services.AddDbContext<RadisHr.Api.Data.AppDbContext>(options =>
        {
            if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
                options.UseSqlite(connectionString);
            else
                options.UseSqlServer(connectionString, sql =>
                {
                    sql.EnableRetryOnFailure(3);
                    sql.MigrationsHistoryTable("__EFMigrationsHistory_RadisHr");
                });
        });
        services.AddScoped<PayrollService>();
        services.AddScoped<AttendanceService>();
        return services;
    }

    public static async Task InitializeRadisHrAsync(this WebApplication app, bool isDesignTime)
    {
        if (isDesignTime) return;
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RadisHr.Api.Data.AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("RadisHr");
        try
        {
            await RadisHrDatabaseInitializer.InitializeAsync(db);
            await DbSeeder.SeedAsync(db, app.Configuration);
            logger.LogInformation("ماژول منابع انسانی داخل Inventory روی دیتابیس اصلی آماده شد.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "راه‌اندازی دیتابیس ماژول منابع انسانی ناموفق بود.");
            throw;
        }
    }
}
