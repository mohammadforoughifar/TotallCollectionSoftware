using Inventory.Licensing.Abstractions;
using Inventory.Licensing.Entitlement;

namespace Inventory.Api.Licensing.Infrastructure;

/// <summary>
/// دروازهٔ لایسنس — وقتی لایسنس نامعتبر/موجود نیست، همهٔ APIها را می‌بندد مگر مسیرهای
/// ضروری.
///
/// <para>
/// <b>جای دقیق در خط لوله (مهم):</b>
/// <code>
/// UseAuthentication();
/// UseMiddleware&lt;LicenseGateMiddleware&gt;();      ← این‌جا
/// UseMiddleware&lt;ActiveCompanyMiddleware&gt;();
/// UseAuthorization();
/// </code>
/// بعد از احراز هویت (چون پیام/رفتار به کاربر وابسته است) و قبل از
/// <c>ActiveCompanyMiddleware</c> (تا وقتی لایسنس نامعتبر است، کوئری شرکت فعال هم زده نشود).
/// </para>
///
/// <para>
/// <b>اصل طراحی:</b> برنامه با لایسنس نامعتبر <b>Crash نمی‌کند</b>. بالا می‌آید، صفحهٔ
/// ورود و صفحهٔ بارگذاری لایسنس کار می‌کند، ولی بقیهٔ APIها ۴۰۳ با کد
/// <c>LICENSE_REQUIRED</c> برمی‌گردانند.
/// </para>
/// </summary>
public sealed class LicenseGateMiddleware
{
    /// <summary>کلید وضعیت در HttpContext.Items — برای کنترلرها و لاگ.</summary>
    public const string StatusItemKey = "LicenseStatus";

    /// <summary>مسیرهای آزاد (بدون لایسنس معتبر).</summary>
    private static readonly string[] Allowlist =
    {
        "/api/auth/login",          // مدیر باید بتواند وارد شود و لایسنس بگذارد
        "/api/license/",            // فعال‌سازی، درخواست فایل، وضعیت کلی، سلامتی
        "/api/health",              // Health Check آپدیت
        "/swagger",                 // مستندات (فقط برای عیب‌یابی)
        "/radis-hr/auth"            // ورود ماژول بن‌سازه
    };

    /// <summary>مسیرهایی که اصلاً بررسی نمی‌شوند (استاتیک/هاپ).</summary>
    private static readonly string[] IgnoredPrefixes =
    {
        "/_framework", "/_content", "/css", "/js", "/lib", "/fonts", "/images",
        "/hubs/", "/uploads/", "/favicon", "/index.html", "/ca.crt", "/https-setup"
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<LicenseGateMiddleware> _log;

    public LicenseGateMiddleware(RequestDelegate next, ILogger<LicenseGateMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext context, ILicenseService licenses)
    {
        var path = context.Request.Path.Value ?? "";

        // فایل‌های استاتیک و هاب‌ها: عبور
        if (!IsApiPath(path) || IsIgnored(path))
        {
            await _next(context);
            return;
        }

        LicenseOverallStatus status;
        try
        {
            status = licenses.GetOverallStatus();
        }
        catch (Exception ex)
        {
            // هرگز برنامه را نمی‌اندازیم؛ بدترین حالت = رفتار «لایسنس نامعتبر».
            _log.LogError(ex, "[License] خواندن وضعیت لایسنس در دروازه ناموفق بود.");
            status = LicenseOverallStatus.Invalid;
        }

        context.Items[StatusItemKey] = status;

        if (status == LicenseOverallStatus.Valid || IsAllowlisted(path))
        {
            await _next(context);
            return;
        }

        // ---------- لایسنس موجود نیست یا معتبر نیست ----------
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json; charset=utf-8";

        var message = status == LicenseOverallStatus.Missing
            ? "لایسنس این نصب هنوز فعال نشده است. برای شروع، فایل لایسنس را بارگذاری کنید یا با مدیر سیستم تماس بگیرید."
            : "لایسنس این نصب معتبر نیست. فایل لایسنس را از صفحهٔ فعال‌سازی بارگذاری کنید یا با پشتیبانی تماس بگیرید.";

        await context.Response.WriteAsJsonAsync(new
        {
            code = "LICENSE_REQUIRED",
            status = status.ToString(),
            message
        });
    }

    /// <summary>آیا این مسیر متعلق به API است؟ (صفحه‌های Blazor و فایل‌ها آزادند)</summary>
    private static bool IsApiPath(string path)
        => path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("/radis-hr", StringComparison.OrdinalIgnoreCase);

    private static bool IsIgnored(string path)
        => IgnoredPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>آیا مسیر در فهرست آزاد است؟</summary>
    public static bool IsAllowlisted(string path)
        => Allowlist.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
