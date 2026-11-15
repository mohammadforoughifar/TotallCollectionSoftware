using Inventory.Licensing.Abstractions;
using Inventory.Licensing.Entitlement;
using Inventory.Licensing.Modules;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Inventory.Api.Licensing.Infrastructure;

/// <summary>
/// صفت «این عملیات به ماژول X نیاز دارد».
///
/// <para>
/// نمونه: <c>[RequireModule(ModuleCodes.Letters)]</c> روی کنترلر یا اکشن.
/// برخلاف <c>[ModulePermission]</c> که مجوز کاربر را می‌سنجد، این صفت <b>مالکیت لایسنس</b>
/// را می‌سنجد: آیا این نصب این ماژول را خریده است؟
/// </para>
///
/// <para>
/// رفتار: <c>Disabled</c> ⇒ ۴۰۳ با کد <c>LICENSE_MODULE_DISABLED</c>؛
/// <c>ReadOnly</c> + عملیات نوشتن ⇒ ۴۰۳ با کد <c>LICENSE_READONLY</c>.
/// خواندن در حالت فقط‌خواندنی آزاد است (دادهٔ مشتری هرگز قفل نمی‌شود).
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequireModuleAttribute : TypeFilterAttribute
{
    public RequireModuleAttribute(string module) : base(typeof(RequireModuleFilter))
    {
        Arguments = new object[] { module };
    }
}

/// <summary>فیلتر اجرایی <see cref="RequireModuleAttribute"/>.</summary>
public sealed class RequireModuleFilter : IAsyncActionFilter
{
    /// <summary>هدر هشدار — کلاینت با دیدن آن بنر «تمدید کنید» نشان می‌دهد.</summary>
    public const string StateHeader = "X-License-State";

    private readonly ILicenseService _licenses;
    private readonly string _module;
    private readonly ILogger<RequireModuleFilter> _log;

    public RequireModuleFilter(ILicenseService licenses, ILogger<RequireModuleFilter> log, string module)
    {
        _licenses = licenses;
        _log = log;
        _module = ModuleCodes.Normalize(module);
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var entitlement = await _licenses.GetEffectiveEntitlementAsync(context.HttpContext.RequestAborted);
        var state = entitlement.GetState(_module);

        context.HttpContext.Response.Headers[StateHeader] = state.ToString();

        if (state == ModuleState.Disabled)
        {
            context.Result = Denied(StatusCodes.Status403Forbidden, "LICENSE_MODULE_DISABLED",
                $"ماژول «{ModuleCodes.TitleOf(_module)}» در لایسنس این نصب فعال نیست. برای فعال‌سازی با پشتیبانی تماس بگیرید.");
            return;
        }

        var isWrite = !(HttpMethods.IsGet(context.HttpContext.Request.Method)
                        || HttpMethods.IsHead(context.HttpContext.Request.Method)
                        || HttpMethods.IsOptions(context.HttpContext.Request.Method));

        if (isWrite && state == ModuleState.ReadOnly)
        {
            _log.LogInformation("[License] عملیات نوشتن روی ماژول فقط‌خواندنی رد شد: {Module} {Path}", _module, context.HttpContext.Request.Path);
            context.Result = Denied(StatusCodes.Status403Forbidden, "LICENSE_READONLY",
                $"اعتبار ماژول «{ModuleCodes.TitleOf(_module)}» به پایان رسیده و در حالت فقط‌خواندنی است. " +
                "مشاهده و خروجی گرفتن آزاد است، ولی ثبت و ویرایش تا تمدید لایسنس امکان‌پذیر نیست.");
            return;
        }

        if (state is ModuleState.ExpiringSoon or ModuleState.Grace)
            context.HttpContext.Response.Headers["X-License-Warning"] = state == ModuleState.Grace ? "GRACE" : "EXPIRING";

        await next();
    }

    private static ObjectResult Denied(int status, string code, string message)
        => new(new { code, message }) { StatusCode = status };
}
