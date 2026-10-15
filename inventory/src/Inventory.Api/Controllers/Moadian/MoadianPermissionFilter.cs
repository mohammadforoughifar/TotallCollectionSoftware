using Inventory.Api.Services.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Inventory.Api.Controllers.Moadian;

/// <summary>
/// مجوز عملیاتی سامانهٔ مودیان.
/// همهٔ endpointهای کنترلر مودیان، علاوه بر احراز هویت سراسری API، باید دقیقاً یکی
/// از مجوزهای Moadian.* را داشته باشند. نگاشت عمداً fail-closed است: اکشن تازه‌ای
/// که به این فهرست افزوده نشود، تا زمان تعیین مجوز مناسب 403 می‌گیرد.
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

    // کلیدها نام متدهای C# هستند، نه نام route. این نگاشت در صورت اضافه‌شدن endpoint جدید
    // به‌صورت پیش‌فرض اجازه نمی‌دهد و توسعه‌دهنده را مجبور به انتخاب سطح دسترسی می‌کند.
    private static readonly IReadOnlyDictionary<string, string> RequiredActions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // مشاهدهٔ فهرست‌ها و جزئیات غیرحساس
            ["GetAllInvoice"] = "View",
            ["NextInvoiceNumber"] = "View",
            ["GetGoodOrService"] = "View",
            ["GetAllCostomer"] = "View",
            ["GetUnitOfMeasurementForComboAsync"] = "View",
            ["GetGoodOrServiceForComboAsync"] = "View",
            ["GetGoodOrServiceByIdAsync"] = "View",
            ["GetInvoiceByIdAsync"] = "View",
            ["GetUnitOfMeasurementByIdAsync"] = "View",
            ["GetAllUnitOfMeasurementAsync"] = "View",
            ["GetCpInfos"] = "View",
            ["GetFiscalYears"] = "View",

            // اتصال، اطلاعات شرکت، دوره مالی و عیب‌یابی؛ شامل کلید خصوصی/تنظیمات حساس است.
            ["GetConnect"] = "Configure",
            ["DeleteCpInfo"] = "Configure",
            ["Diagnose"] = "Diagnose",
            ["DiagnoseLists"] = "Diagnose",
            ["GetConnectForCpInfo"] = "Configure",
            ["ConnectionApiAsync"] = "Configure",
            ["SaveCpInfo"] = "Configure",
            ["SaveFiscalYear"] = "Configure",

            // کالا، واحد و مشتری (اطلاعات پایهٔ مودیان)
            ["InsertGoodOrServiceAsync"] = "MasterData",
            ["UpdateGoodOrServiceAsync"] = "MasterData",
            ["UpdateUnitOfMeasurementAsync"] = "MasterData",
            ["InsertUnitOfMeasurementAsync"] = "MasterData",
            ["DeleteUnitOfMeasurementByIdAsync"] = "MasterData",
            ["InsertCustomer"] = "MasterData",
            ["UpdateCustomer"] = "MasterData",

            // چرخهٔ صورتحساب
            ["AddInvoice"] = "Create",
            ["EslahiInvoiceAsync"] = "Update",
            ["EbtalInvoiceAsync"] = "Update",
            ["DeleteInvoiceByIdAsync"] = "Delete",
            ["SendInvoiceAsync"] = "Send",
            ["PreviewSendInvoiceAsync"] = "Send",
            ["UpdateStatusInvoice"] = "Send",
            ["EstelamInvoiceAsync"] = "Send",
            ["ErrorInvoiceAsync"] = "Send"
        };

    public MoadianPermissionFilter(IEffectivePermissions permissions) => _permissions = permissions;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor descriptor
            || !RequiredActions.TryGetValue(descriptor.ActionName, out var requiredAction))
        {
            context.Result = new ObjectResult(new
            {
                message = "برای این عملیات مودیان سطح دسترسی تعریف نشده است؛ عملیات تا زمان پیکربندی مجوز مسدود است."
            }) { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        // AuthorizeFilter سراسری پیش از این فیلتر، نشست معتبر را الزامی کرده است. این بررسی
        // برای استفادهٔ مستقل از کنترلر و دفاع در عمق نیز نگه داشته می‌شود.
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            context.Result = new ChallengeResult();
            return;
        }

        var permissions = await _permissions.GetAsync(context.HttpContext.User);
        var granted = permissions.Contains($"{Module}.{requiredAction}")
                      || await _permissions.IsLegacyAdminAsync(context.HttpContext.User);
        if (!granted)
        {
            context.Result = new ObjectResult(new
            {
                message = "شما مجوز لازم برای این عملیات سامانهٔ مودیان را ندارید."
            }) { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        await next();
    }
}
