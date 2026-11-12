using Inventory.Api.Licensing.Services;
using Inventory.Licensing.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Inventory.Api.Controllers;

/// <summary>
/// کلاس پایه کنترلرهای عملیاتی — نقش «مدیر» و «اپراتور».
/// (بخش‌های مدیریتی مثل کاربران/تنظیمات/داشبورد مدیریتی، جداگانه فقط Admin هستند.)
/// خطاهای منطق کسب‌وکار توسط فیلتر سراسری به پاسخ 400 با پیام فارسی تبدیل می‌شوند.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,Operator")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// سرویس لایسنس برای اعمال سهمیه در نقاط ثبت رکورد.
    ///
    /// <para>
    /// عمداً به‌صورت property و از <c>RequestServices</c> گرفته می‌شود، نه پارامتر سازنده:
    /// افزودن یک پارامتر به سازندهٔ ۱۲۵ کنترلر موجود، هم پرریسک است و هم هیچ سودی ندارد.
    /// این تنها جای «service locator» در پروژه است و فقط برای همین یک وابستگی.
    /// </para>
    /// </summary>
    protected ILicenseService Licenses => HttpContext.RequestServices.GetRequiredService<ILicenseService>();

    /// <summary>ماژول لایسنسِ همین کنترلر (از رجیستری) — برای تعیین ماژول در گارد سهمیه.</summary>
    protected string LicenseModule =>
        Inventory.Api.Licensing.Infrastructure.LicenseModuleRegistry.Resolve(
            ControllerContext.ActionDescriptor.ControllerName) ?? Inventory.Licensing.Modules.ModuleCodes.Core;
}

/// <summary>فیلتر سراسری خطا: هر Exception را به BadRequest با بدنه { message } تبدیل می‌کند.</summary>
public class ApiExceptionFilter : IExceptionFilter
{
    private readonly ILogger<ApiExceptionFilter> _logger;

    public ApiExceptionFilter(ILogger<ApiExceptionFilter> logger) => _logger = logger;

    public void OnException(ExceptionContext context)
    {
        // استثنای «سقف سهمیه» پاسخ اختصاصی خودش را دارد (۴۰۳ با کد LICENSE_QUOTA_EXCEEDED)
        // و نباید این‌جا به ۴۰۰ متنی تبدیل شود. MVC فیلترهای استثنا را به ترتیب معکوس
        // ثبت اجرا می‌کند؛ پس ترتیب‌دهی با Order کافی نبود و این‌جا صریحاً واگذار می‌شود.
        for (var current = context.Exception; current is not null; current = current.InnerException)
        {
            if (current is Inventory.Api.Licensing.Infrastructure.LicenseQuotaException)
            {
                _logger.LogInformation("سقف سهمیه لایسنس رد شد: {Path}", context.HttpContext.Request.Path);
                return;      // ExceptionHandled ست نمی‌شود تا فیلتر اختصاصی لایسنس پاسخ دهد.
            }
        }

        _logger.LogError(context.Exception, "خطا در پردازش درخواست {Path}", context.HttpContext.Request.Path);
        // زنجیره‌ی InnerException ها هم برگردانده می‌شود چون علت واقعی خطاهای دیتابیس (FK، طول ستون و…)
        // همیشه در InnerException است نه در پیام سطح اول.
        var sb = new System.Text.StringBuilder(context.Exception.Message);
        var inner = context.Exception.InnerException;
        while (inner is not null)
        {
            sb.Append(" | ").Append(inner.Message);
            inner = inner.InnerException;
        }
        context.Result = new BadRequestObjectResult(new { message = sb.ToString() });
        context.ExceptionHandled = true;
    }
}
