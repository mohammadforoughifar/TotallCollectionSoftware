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

    /// <summary>هدر هشدار مهلت — کلاینت بنر «لایسنس خود را فعال کنید» نشان می‌دهد.</summary>
    public const string GraceHeader = "X-License-Grace";

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
    /// <summary>
    /// مسیرهای API که با <c>/api</c> شروع نمی‌شوند ولی API واقعی‌اند.
    /// (هنگام بازبینی پیدا شد: <c>MoadianController</c> مسیر <c>/Moadian</c> دارد و
    /// دروازه آن را نمی‌دید — یعنی بدون لایسنس هم قابل صدا زدن بود.)
    /// </summary>
    private static readonly string[] AdditionalApiPrefixes =
    {
        "/Moadian"
    };

    /// <summary>
    /// مسیرهایی که <b>عمداً</b> بیرون از دروازهٔ لایسنس‌اند و اگر لایسنس نباشد هم باید کار کنند.
    ///
    /// <para>
    /// چرا؟ چون ربطی به دادهٔ کسب‌وکار ندارند و دقیقاً در همان لحظه‌ای لازم می‌شوند که
    /// لایسنسی روی نصب نیست:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>/ca.crt</c> — گواهی CA برای اینکه مرورگر HTTPS را قبول کند؛ پیش از هر ورودی لازم است.</item>
    ///   <item><c>/https-setup</c> — صفحهٔ نصب گواهی؛ همان مرحلهٔ قبل از ورود.</item>
    ///   <item><c>/eitaa-app</c> — صفحهٔ برنامک ایتا (Mini App)؛ کاربر آن را از داخل ایتا باز می‌کند و هیچ دادهٔ کسب‌وکاری برنمی‌گرداند.</item>
    /// </list>
    ///
    /// <para>
    /// ⚠️ این فهرست باید کوچک بماند و هر افزودن تازه نیاز به تأیید دارد. تست
    /// «پوشش مسیرها» هر مسیر تازه‌ای بیرون از دروازه را می‌شکند تا کسی بی‌صدا
    /// مسیر جدیدی را از گیت بیرون نگذارد (همان اشتباهی که با <c>/Moadian</c> رخ داد).
    /// </para>
    /// </summary>
    public static readonly string[] IntentionallyPublicPaths =
    {
        "/ca.crt",
        "/https-setup",
        "/eitaa-app"
    };

    /// <summary>آیا این مسیر جزء فهرست مستندِ «عمداً بیرون از دروازه» است؟</summary>
    public static bool IsIntentionallyPublic(string path)
        => IntentionallyPublicPaths.Any(p =>
            path.Equals(p, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase));

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

        // ---------- مهلت سازگاری «نصب موجود» ----------
        // نصب قدیمی که بعد از آپدیت هنوز لایسنس نگرفته بی‌درنگ بسته نمی‌شود؛ تا پایان
        // مهلتِ ثبت‌شده در دیتابیس کار می‌کند و روی پاسخ یک هدر هشدار می‌رود.
        // این تنها استثنای «باز بودن بدون لایسنس» است: یک‌بار، فقط برای نصب‌های موجود.
        try
        {
            if (await licenses.IsInExistingInstallGraceAsync(context.RequestAborted))
            {
                context.Response.Headers[GraceHeader] = "1";
                await _next(context);
                return;
            }
        }
        catch (Exception ex)
        {
            // اگر بررسی مهلت شکست خورد، رفتار سخت‌گیرانه (بستن) انتخاب می‌شود.
            _log.LogError(ex, "[License] بررسی مهلت سازگاری ناموفق بود؛ رفتار «لایسنس نامعتبر» اعمال می‌شود.");
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
    /// <summary>
    /// آیا این مسیر زیر پوشش دروازهٔ لایسنس است؟ (عمومی تا تست پوشش مسیرها بتواند
    /// همان تصمیم زمان اجرا را بسنجد، نه یک کپی دستی.)
    /// </summary>
    public static bool IsApiPath(string path)
        => path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("/radis-hr", StringComparison.OrdinalIgnoreCase)
           || AdditionalApiPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static bool IsIgnored(string path)
        => IgnoredPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>آیا مسیر در فهرست آزاد است؟</summary>
    public static bool IsAllowlisted(string path)
        => Allowlist.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
