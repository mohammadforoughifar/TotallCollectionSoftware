using System.Reflection;
using Inventory.Api.Services.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Inventory.Api.Controllers.Moadian;

/// <summary>
/// مجوز عملیاتی سامانهٔ مودیان.
/// همهٔ endpointهای کنترلر مودیان، علاوه بر احراز هویت سراسری API، باید سطح دسترسی
/// Moadian.* مناسب را داشته باشند. نگاشت اکشن→سطح در RequiredActions زیر، منبع
/// واحد حقیقت است (کلیدها نام متدهای C# هستند، نه نام route).
///
/// رفتار وقتی اکشنی در نگاشت نیست (مثلاً endpoint تازه‌ای اضافه شده ولی سطحی
/// برای آن تعیین نشده — «drift»):
///   ۱. خودبررسی نگاشت در اولین درخواست، اکشن‌های بی‌سطح را در لاگ API گزارش
///      می‌کند؛ پس توسعه‌دهنده بلافاصله متوجه می‌شود، نه کاربر با دیدن 403.
///   ۲. مدیر قدیمی (Admin بدون نقش RBAC) و کاربرانی که همهٔ سطوح مودیان را دارند
///      («دسترسی کامل مودیان») عبور می‌کنند — دسترسی کامل واقعاً کامل است و drift
///      نباید نقشِ دسترسی‌کامل را قفل کند. عبور این‌ها هم یک‌بار در لاگ ثبت می‌شود.
///   ۳. برای سایر کاربران عملیات fail-closed باقی می‌ماند (403) و پیام، نام اکشن
///      را هم دارد تا مشخص باشد کدام سطح کم است.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class MoadianPermissionAttribute : TypeFilterAttribute
{
    public MoadianPermissionAttribute() : base(typeof(MoadianPermissionFilter)) { }
}

public sealed class MoadianPermissionFilter : IAsyncActionFilter
{
    private const string Module = "Moadian";
    private readonly IEffectivePermissions _permissions;
    private readonly ILogger<MoadianPermissionFilter> _logger;

    /// <summary>
    /// همهٔ سطوح مجوز ماژول مودیان (هم‌راستا با RbacSeeder). «دسترسی کامل مودیان»
    /// یعنی داشتن هر ۱۵ سطح جدید برای کاربر.
    /// </summary>
    private static readonly string[] AllLevels =
        { "View", "Company", "FiscalYear", "Connection", "Goods", "Units", "Customers", "InvoiceCreate", "InvoiceUpdate", "InvoiceDelete", "InvoiceSend", "InvoicePreview", "InvoiceStatus", "InvoiceInquiry", "Diagnose" };

    // کلیدها نام متدهای C# هستند، نه نام route. این نگاشت در صورت اضافه‌شدن endpoint جدید
    // به‌صورت پیش‌فرض اجازه نمی‌دهد و توسعه‌دهنده را مجبور به انتخاب سطح دسترسی می‌کند.
    private static readonly IReadOnlyDictionary<string, string> RequiredActions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // مشاهدهٔ صورتحساب
            ["GetAllInvoice"] = "View",
            ["GetInvoiceByIdAsync"] = "View",
            ["NextInvoiceNumber"] = "View",

            // شرکت
            ["GetCpInfos"] = "Company",
            ["SaveCpInfo"] = "Company",
            ["DeleteCpInfo"] = "Company",
            ["GetConnectForCpInfo"] = "Company",

            // دوره مالی
            ["GetFiscalYears"] = "FiscalYear",
            ["SaveFiscalYear"] = "FiscalYear",

            // اتصال و حافظه مالیاتی
            ["GetConnect"] = "Connection",
            ["ConnectionApiAsync"] = "Connection",

            // کالا/خدمت
            ["GetGoodOrService"] = "Goods",
            ["GetGoodOrServiceForComboAsync"] = "Goods",
            ["GetGoodOrServiceByIdAsync"] = "Goods",
            ["InsertGoodOrServiceAsync"] = "Goods",
            ["UpdateGoodOrServiceAsync"] = "Goods",

            // واحد اندازه‌گیری
            ["GetUnitOfMeasurementForComboAsync"] = "Units",
            ["GetUnitOfMeasurementByIdAsync"] = "Units",
            ["GetAllUnitOfMeasurementAsync"] = "Units",
            ["InsertUnitOfMeasurementAsync"] = "Units",
            ["UpdateUnitOfMeasurementAsync"] = "Units",
            ["DeleteUnitOfMeasurementByIdAsync"] = "Units",

            // مشتری
            ["GetAllCostomer"] = "Customers",
            ["InsertCustomer"] = "Customers",
            ["UpdateCustomer"] = "Customers",

            // چرخهٔ صورتحساب — ایجاد/اصلاح/ابطال/حذف
            ["AddInvoice"] = "InvoiceCreate",
            ["EslahiInvoiceAsync"] = "InvoiceUpdate",
            ["EbtalInvoiceAsync"] = "InvoiceUpdate",
            ["DeleteInvoiceByIdAsync"] = "InvoiceDelete",

            // ارسال و استعلام
            ["SendInvoiceAsync"] = "InvoiceSend",
            ["PreviewSendInvoiceAsync"] = "InvoicePreview",
            ["UpdateStatusInvoice"] = "InvoiceStatus",
            ["EstelamInvoiceAsync"] = "InvoiceInquiry",
            ["ErrorInvoiceAsync"] = "InvoiceInquiry",

            // عیب‌یابی
            ["Diagnose"] = "Diagnose",
            ["DiagnoseLists"] = "Diagnose",
        };

    // خودبررسی نگاشت فقط یک‌بار اجرا می‌شود و هشدار تکراری برای هر اکشن پیشگیری می‌شود.
    private static readonly SemaphoreSlim AuditGate = new(1, 1);
    private static bool _audited;
    private static readonly HashSet<string> _warnedActions = new(StringComparer.Ordinal);

    public MoadianPermissionFilter(IEffectivePermissions permissions, ILogger<MoadianPermissionFilter> logger)
    {
        _permissions = permissions;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        await AuditMapOnceAsync();

        if (context.ActionDescriptor is not ControllerActionDescriptor descriptor)
        {
            context.Result = Forbidden("کنترل دسترسی این عملیات مودیان قابل تشخیص نیست.");
            return;
        }

        // AuthorizeFilter سراسری پیش از این فیلتر، نشست معتبر را الزامی کرده است. این بررسی
        // برای استفادهٔ مستقل از کنترلر و دفاع در عمق نیز نگه داشته می‌شود.
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            context.Result = new ChallengeResult();
            return;
        }

        if (!RequiredActions.TryGetValue(descriptor.ActionName, out var requiredAction))
        {
            // اکشن در نگاشت سطح نیست (drift). دسترسی کامل مودیان / مدیر قدیمی عبور می‌کنند؛
            // بقیه با پیامی که نام اکشن را هم دارد مسدود می‌مانند.
            var userPerms = await _permissions.GetAsync(user);
            var hasFullMoadianAccess = AllLevels.All(level => userPerms.Contains($"{Module}.{level}"));
            var isLegacyAdmin = await _permissions.IsLegacyAdminAsync(user);

            if (hasFullMoadianAccess || isLegacyAdmin)
            {
                WarnDriftOnce(descriptor.ActionName, hasFullMoadianAccess ? "کاربر دارای دسترسی کامل مودیان" : "مدیر قدیمی");
                await next();
                return;
            }

            context.Result = Forbidden(
                $"برای این عملیات مودیان (اکشن: {descriptor.ActionName}) سطح دسترسی تعریف نشده است؛ عملیات تا زمان پیکربندی مجوز مسدود است.");
            return;
        }

        var permissions = await _permissions.GetAsync(user);
        var granted = permissions.Contains($"{Module}.{requiredAction}")
                      || await _permissions.IsLegacyAdminAsync(user);
        if (!granted)
        {
            context.Result = Forbidden("شما مجوز لازم برای این عملیات سامانهٔ مودیان را ندارید.");
            return;
        }

        await next();
    }

    /// <summary>
    /// خودبررسی نگاشت: در اولین درخواست، اگر اکشنی از کنترلر در RequiredActions نباشد
    /// هشدار در لاگ API می‌افتد — تا توسعه‌دهنده‌ای که endpoint تازه افزوده و فراموش کرده
    /// سطح تعیین کند، بلافاصله ببیند (و در لاگ هم دقیقاً نام اکشن‌ها باشد).
    /// </summary>
    private async Task AuditMapOnceAsync()
    {
        await AuditGate.WaitAsync();
        if (_audited)
        {
            AuditGate.Release();
            return;
        }
        _audited = true;
        AuditGate.Release();

        var actionNames = typeof(MoadianController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Where(m => m.ReturnType == typeof(Task)
                        || (m.ReturnType.IsGenericType && m.ReturnType.GetGenericTypeDefinition() == typeof(Task<>)))
            .Select(m => m.Name)
            .ToList();

        var missing = actionNames.Where(name => !RequiredActions.ContainsKey(name)).ToList();
        if (missing.Count > 0)
        {
            _logger.LogWarning(
                "نگاشت سطح دسترسی مودیان ناقص است: اکشن‌های {Actions} سطحی ندارند. این عملیات فقط برای مدیر قدیمی و کاربران دارای دسترسی کامل مودیان باز است و در اولین برخورد هشدار می‌افتد.",
                string.Join(", ", missing));
        }
        else
        {
            _logger.LogInformation(
                "نگاشت سطح دسترسی مودیان کامل است: هر {Count} اکشن کنترلر به یک سطح مجوز نگاشت شده است.",
                actionNames.Count);
        }
    }

    private void WarnDriftOnce(string actionName, string via)
    {
        lock (_warnedActions)
        {
            if (!_warnedActions.Add(actionName)) return;
        }
        _logger.LogWarning(
            "اکشن مودیان {Action} در نگاشت سطح دسترسی سطحی ندارد — عبور از مسیر {Via}. یک سطح مناسب را در MoadianPermissionFilter.RequiredActions برای آن تعیین کنید.",
            actionName, via);
    }

    private static ObjectResult Forbidden(string message) =>
        new(new { message }) { StatusCode = StatusCodes.Status403Forbidden };
}
