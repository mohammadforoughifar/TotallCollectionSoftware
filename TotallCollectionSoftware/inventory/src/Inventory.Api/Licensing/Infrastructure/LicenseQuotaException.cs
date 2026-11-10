using Inventory.Api.Data;
using Inventory.Licensing.Metrics;
using Inventory.Licensing.Modules;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Inventory.Api.Licensing.Infrastructure;

/// <summary>
/// استثنای «سقف سهمیه پر است» — از لایهٔ سرویس پرتاب می‌شود و در درخواست HTTP به
/// پاسخ ۴۰۳ با کد <c>LICENSE_QUOTA_EXCEEDED</c> تبدیل می‌شود.
///
/// <para>
/// چرا استثنا؟ چون نقطهٔ واقعی تصمیم (ثبت فاکتور/نامه) در لایهٔ سرویس است، نه در
/// کنترلر؛ و همان‌جا داخل تراکنش باید برگشت بخورد. اگر فقط در کنترلر بررسی کنیم،
/// هر مسیر تازه‌ای که سرویس را صدا بزند سقف را دور می‌زند.
/// </para>
/// </summary>
public sealed class LicenseQuotaException : Exception
{
    public LicenseQuotaException(string message, string metric) : base(message) => Metric = metric;

    /// <summary>کد متریکی که سقفش پر شده (مثل <c>invoice.create</c>).</summary>
    public string Metric { get; }
}

/// <summary>
/// تبدیل <see cref="LicenseQuotaException"/> به پاسخ ۴۰۳ + ثبت رخداد.
/// به‌صورت فیلتر سراسری ثبت می‌شود و قبل از <c>ApiExceptionFilter</c> عمومی عمل می‌کند
/// (چون آن فیلتر هر استثنا را به ۴۰۰ تبدیل می‌کند و ما ۴۰۳ با کد مشخص می‌خواهیم).
/// </summary>
public sealed class LicenseQuotaExceptionFilter : IExceptionFilter, IOrderedFilter
{
    private readonly ILogger<LicenseQuotaExceptionFilter> _log;

    public LicenseQuotaExceptionFilter(ILogger<LicenseQuotaExceptionFilter> log) => _log = log;

    /// <summary>
    /// قبل از <c>ApiExceptionFilter</c> عمومی اجرا می‌شود. بدون این ترتیب، آن فیلتر
    /// همهٔ استثناها را به ۴۰۰ تبدیل می‌کرد و سقفِ پر‌شده به‌جای ۴۰۳ با کد
    /// <c>LICENSE_QUOTA_EXCEEDED</c>، فقط یک ۴۰۰ متنی به کلاینت می‌رساند
    /// (در تست زندهٔ فاکتور همین اتفاق افتاد).
    /// </summary>
    public int Order => int.MinValue;

    public void OnException(ExceptionContext context)
    {
        var quota = Find(context.Exception);
        if (quota is null) return;

        _log.LogWarning("[License] سقف سهمیه رد شد: {Metric} — {Path}", quota.Metric, context.HttpContext.Request.Path);

        context.Result = new ObjectResult(new
        {
            code = "LICENSE_QUOTA_EXCEEDED",
            metric = quota.Metric,
            metricTitle = MetricCodes.TitleOf(quota.Metric),
            module = MetricCodes.ModuleOf(quota.Metric),
            moduleTitle = ModuleCodes.TitleOf(MetricCodes.ModuleOf(quota.Metric)),
            message = quota.Message
        })
        { StatusCode = StatusCodes.Status403Forbidden };

        context.ExceptionHandled = true;
    }

    /// <summary>
    /// پیدا کردن استثنای سهمیه در زنجیرهٔ InnerException — چون بعضی مسیرها استثنا را
    /// لایه‌بندی می‌کنند (مثلاً InvoicingService → کنترلر).
    /// </summary>
    private static LicenseQuotaException? Find(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is LicenseQuotaException quota) return quota;
        return null;
    }
}
