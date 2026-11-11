using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Infrastructure;

/// <summary>بیس کلی کنترلرها (معادل BaseApiController پروژهٔ مبدأ).</summary>
public class BaseApiController : ControllerBase
{
    /// <summary>
    /// سرویس لایسنس — همان توضیح <c>ApiControllerBase.Licenses</c>: property به‌جای
    /// پارامتر سازنده، تا ۱۲۵ سازندهٔ موجود دست نخورد.
    /// </summary>
    protected Inventory.Licensing.Abstractions.ILicenseService Licenses =>
        HttpContext.RequestServices.GetRequiredService<Inventory.Licensing.Abstractions.ILicenseService>();
}
