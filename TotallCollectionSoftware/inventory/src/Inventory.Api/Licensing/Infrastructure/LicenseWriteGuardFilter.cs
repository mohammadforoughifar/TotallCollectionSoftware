using Inventory.Licensing.Abstractions;
using Inventory.Licensing.Entitlement;
using Inventory.Licensing.Modules;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Inventory.Api.Licensing.Infrastructure;

/// <summary>
/// گارد سراسری نوشتن — «توری ایمنی» برای کنترلرهایی که صفت <see cref="RequireModuleAttribute"/>
/// ندارند.
///
/// <para>
/// برای هر درخواست نوشتن (POST/PUT/PATCH/DELETE) ماژول هدف را از
/// <see cref="LicenseModuleRegistry"/> پیدا می‌کند و دو چیز را می‌سنجد:
/// <list type="number">
/// <item>خودِ ماژول نباید <c>Disabled</c>/<c>ReadOnly</c> باشد.</item>
/// <item>پیش‌نیازهای ثابتِ همان ماژول (<see cref="ModuleDependencyMap"/>) نباید
/// <c>Disabled</c>/<c>ReadOnly</c> باشند — چون این عملیات روی دادهٔ آن‌ها می‌نویسد.</item>
/// </list>
/// </para>
///
/// <para>
/// پیام خطا صریحاً نام ماژول لازمی را می‌گوید تا کاربر بداند چه چیزی باید تمدید شود؛
/// «خطای نامشخص» نه.
/// </para>
/// </summary>
public sealed class LicenseWriteGuardFilter : IAsyncActionFilter
{
    private readonly ILicenseService _licenses;
    private readonly ILogger<LicenseWriteGuardFilter> _log;

    /// <summary>
    /// کنترلرهایی که یک‌بار «نگاشت‌نشده» لاگ شده‌اند (برای جلوگیری از پر شدن لاگ).
    /// ConcurrentDictionary چون فیلتر روی همهٔ درخواست‌های هم‌زمان اجرا می‌شود و
    /// HashSet ساده در نوشتن هم‌زمان ناسازگار است.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> LoggedUnmappedControllers =
        new(StringComparer.OrdinalIgnoreCase);

    public LicenseWriteGuardFilter(ILicenseService licenses, ILogger<LicenseWriteGuardFilter> log)
    {
        _licenses = licenses;
        _log = log;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var method = http.Request.Method;

        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
        {
            await next();
            return;
        }

        if (context.ActionDescriptor is not ControllerActionDescriptor descriptor)
        {
            await next();
            return;
        }

        // اگر اکشن خودش [RequireModule] دارد، همان بررسی کافی است.
        if (context.ActionDescriptor.EndpointMetadata.OfType<RequireModuleAttribute>().Any())
        {
            await next();
            return;
        }

        if (LicenseModuleRegistry.IsExempt(descriptor.ControllerName)) { await next(); return; }

        var module = LicenseModuleRegistry.Resolve(descriptor.ControllerName)
                     ?? LicenseModuleRegistry.ResolveFromPath(http.Request.Path.Value);

        if (module is null)
        {
            // کنترلر تازه که در رجیستری نیست: کار را متوقف نمی‌کنیم (تا انتشار تازه
            // مشتری را از کار نیندازد) ولی یک‌بار هشدار می‌دهیم تا ردیفش اضافه شود.
            if (LoggedUnmappedControllers.TryAdd(descriptor.ControllerName, 0))
                _log.LogWarning("[License] کنترلر «{Controller}» در LicenseModuleRegistry نیست؛ بررسی لایسنس اعمال نشد.",
                    descriptor.ControllerName);
            await next();
            return;
        }

        if (ModuleCodes.IsAlwaysOn(module)) { await next(); return; }

        var entitlement = await _licenses.GetEffectiveEntitlementAsync(http.RequestAborted);

        // ---------- ۱) وضعیت خود ماژول ----------
        var state = entitlement.GetState(module);
        if (state == ModuleState.Disabled)
        {
            context.Result = Denied("LICENSE_MODULE_DISABLED",
                $"ماژول «{ModuleCodes.TitleOf(module)}» در لایسنس این نصب فعال نیست. برای فعال‌سازی با پشتیبانی تماس بگیرید.");
            return;
        }
        if (state == ModuleState.ReadOnly)
        {
            context.Result = Denied("LICENSE_READONLY",
                $"اعتبار ماژول «{ModuleCodes.TitleOf(module)}» به پایان رسیده و در حالت فقط‌خواندنی است. " +
                "مشاهده و خروجی گرفتن آزاد است، ولی ثبت و ویرایش تا تمدید لایسنس امکان‌پذیر نیست.");
            return;
        }

        // ---------- ۲) پیش‌نیازها ----------
        foreach (var dependency in ModuleDependencyMap.RequiresOf(module))
        {
            if (dependency.Kind == DependencyKind.Hard) { continue; } // فعلاً یالی از این نوع تعریف نشده

            var requiredState = entitlement.GetState(dependency.Requires);
            if (requiredState is ModuleState.Disabled or ModuleState.ReadOnly)
            {
                var reason = string.IsNullOrWhiteSpace(dependency.Reason) ? "" : $" ({dependency.Reason})";
                context.Result = Denied("LICENSE_DEPENDENCY",
                    $"این عملیات روی دادهٔ ماژول «{ModuleCodes.TitleOf(dependency.Requires)}» می‌نویسد و لایسنس آن " +
                    $"در وضعیت «{StateTitle(requiredState)}» است{reason}. برای ثبت این عملیات، لایسنس ماژول " +
                    $"«{ModuleCodes.TitleOf(dependency.Requires)}» باید فعال یا تمدید شود. " +
                    "مشاهده و خروجی گرفتن داده‌های موجود آزاد است.");
                return;
            }
        }

        await next();
    }

    private static string StateTitle(ModuleState state) => state switch
    {
        ModuleState.Valid => "معتبر",
        ModuleState.ExpiringSoon => "در آستانهٔ انقضا",
        ModuleState.Grace => "مهلت تمدید",
        ModuleState.ReadOnly => "فقط خواندنی",
        _ => "غیرفعال"
    };

    private static ObjectResult Denied(string code, string message)
        => new(new { code, message }) { StatusCode = StatusCodes.Status403Forbidden };
}
