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
    /// <remarks>
    /// ⚠️ <b>از بازبینی چهارم دیگر ست نمی‌شود:</b> مهلت «نصب موجود» حذف شد و جای آن
    /// «لایسنس پل» امضاشده آمد. ثابت عمداً نگه داشته شده تا کلاینت‌هایی که این هدر را
    /// می‌خوانند نشکنند (هرگز مقدار نمی‌گیرند)، و اگر روزی مفهوم «مهلت امضاشده» به
    /// پاسخ HTTP راه پیدا کرد، جای طبیعی‌اش همین است.
    /// </remarks>
    public const string GraceHeader = "X-License-Grace";

    /// <summary>
    /// هدر حالت «فقط‌خواندنی» — کلاینت بنر «سامانه در حالت مشاهده؛ ثبت غیرفعال است» نشان می‌دهد.
    ///
    /// <para>
    /// چرا هدر جدا از <see cref="GraceHeader"/>؟ چون معنای متفاوتی دارد: Grace یعنی
    /// «هنوز همه‌چیز کار می‌کند، تمدید کن»؛ ReadOnly یعنی «ثبت بسته است». اگر با هم
    /// ادغام شوند، کاربر نمی‌داند واقعاً می‌تواند ثبت کند یا نه.
    /// </para>
    /// </summary>
    public const string ReadOnlyHeader = "X-License-ReadOnly";

    /// <summary>کد خطای نوشتن در حالت فقط‌خواندنی (برای UI و پشتیبانی).</summary>
    public const string ReadOnlyCode = "LICENSE_READONLY";

    /// <summary>مسیرهای آزاد (بدون لایسنس معتبر).</summary>
    private static readonly string[] Allowlist =
    {
        // حساب کاربری — «دادهٔ مشتری» نیست؛ قفل کردنش یعنی مشتری حتی نتواند وارد شود
        // یا رمزش را عوض کند. (بازبینی سوم: Auth/logout/تغییر رمز/پروفایل شخصی)
        "/api/auth/",
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

        // ---------- مهلت «نصب موجود»: حذف شد ----------
        //
        // ❌ پیش از این، اگر نصب «موجود» تشخیص داده می‌شد و در بازهٔ مهلتِ ثبت‌شده در
        // دیتابیسِ خودش بود، این‌جا **همه‌چیز** (از جمله نوشتن) باز می‌شد.
        // تصمیم بازبینی چهارم: هیچ نصبی به خودش مهلت نمی‌دهد.
        //
        // جایگزین: «لایسنس پل» امضاشده — یک Subscription کوتاه با همهٔ ماژول‌ها که
        // ما صادر می‌کنیم (tools/license-sign.py --bridge). تفاوت اصلی: آن مهلت
        // **امضای ما** را دارد و در چارچوب همان سند لایسنس است، نه قاعده‌ای که خودِ
        // نصب روی ماشین مشتری برای خودش می‌سازد و می‌تواند پاکش کند.
        //
        // ⚠️ این را با ModuleState.Grace اشتباه نگیرید: آن، مهلت ۱۵ روزهٔ **پس از
        // انقضای** یک ماژول است و سر جای خود می‌ماند.

        // ---------- لایسنس موجود نیست یا معتبر نیست: **فقط‌خواندنی** ----------
        //
        // ⛔ این‌جا قبلاً همهٔ درخواست‌ها (حتی GET) با ۴۰۳ بسته می‌شد. تصمیم کارفرما:
        //    «دادهٔ مشتری هرگز قفل نمی‌شود». دو وضعیت Missing و Invalid هر دو ممکن است
        //    ناشی از چیزی خارج از کنترل مشتری باشند (جابه‌جایی VM ⇒ تغییر InstallationId،
        //    فایل خراب، کلید، بکاپ روی سخت‌افزار تازه). اگر خواندن هم بسته شود، مشتری برای
        //    دیدن اطلاعات خودش هم دسترسی ندارد.
        //
        // پس: خواندن آزاد، نوشتن بسته. و «نوشتن» فقط با فعل HTTP تعریف نمی‌شود؛
        // POSTهای فقط‌خواندنی (گزارش‌ها، جست‌وجوی آرشیو، خروجی‌ها) با صفت
        // [AllowInReadOnly] علامت‌گذاری شده‌اند و از همین‌جا عبور می‌کنند.
        var isReadOnlyStatus =
            status is LicenseOverallStatus.Missing or LicenseOverallStatus.Invalid;

        if (isReadOnlyStatus)
        {
            context.Response.Headers[ReadOnlyHeader] = "1";

            if (IsReadMethod(context.Request.Method) || IsMarkedReadOnlyAllowed(context))
            {
                await _next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json; charset=utf-8";

            var readOnlyMessage = status == LicenseOverallStatus.Missing
                ? "لایسنس این نصب هنوز فعال نشده است. مشاهدهٔ اطلاعات آزاد است، ولی ثبت و تغییر مسدود است. "
                  + "برای فعال‌سازی، فایل لایسنس را از صفحهٔ «لایسنس» بارگذاری کنید."
                : "لایسنس این نصب معتبر نیست. مشاهدهٔ اطلاعات آزاد است، ولی ثبت و تغییر مسدود است. "
                  + "فایل لایسنس را از صفحهٔ «لایسنس» بارگذاری کنید یا با پشتیبانی تماس بگیرید.";

            await context.Response.WriteAsJsonAsync(new
            {
                code = ReadOnlyCode,
                status = status.ToString(),
                readOnly = true,
                message = readOnlyMessage
            });
            return;
        }

        // ---------- (برای کامل‌بودن) هر وضعیت ناشناخته: فقط‌خواندنی ----------
        // اگر روزی وضعیت تازه‌ای اضافه شد و این‌جا مدیریت نشد، رفتار پیش‌فرض «قفل نکن» است.
        context.Response.Headers[ReadOnlyHeader] = "1";
        if (IsReadMethod(context.Request.Method) || IsMarkedReadOnlyAllowed(context))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(new
        {
            code = ReadOnlyCode,
            status = status.ToString(),
            readOnly = true,
            message = "دسترسی نوشتن در وضعیت فعلی لایسنس مجاز نیست؛ مشاهدهٔ اطلاعات آزاد است."
        });
    }

    /// <summary>آیا این فعل HTTP فقط می‌خواند؟</summary>
    public static bool IsReadMethod(string method)
        => HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

    /// <summary>
    /// آیا این درخواست، هرچند POST است، در واقع فقط می‌خواند و با صفت
    /// <see cref="AllowInReadOnlyAttribute"/> علامت‌گذاری شده است؟
    ///
    /// <para>
    /// چرا صفت و نه فهرست مسیر؟ چون فهرست مسیر با هر تغییر route کهنه می‌شود و کسی
    /// یادش نمی‌ماند به‌روزش کند. صفت روی خود اکشن است، در همان فایلی که تغییر می‌کند
    /// دیده می‌شود، و آزمون پوشش می‌تواند اطمینان دهد فقط اکشن‌های واقعاً فقط‌خواندنی
    /// آن را دارند.
    /// </para>
    /// </summary>
    private static bool IsMarkedReadOnlyAllowed(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is null) return false;

        if (endpoint.Metadata.GetMetadata<AllowInReadOnlyAttribute>() is not null) return true;

        // کنترلر ممکن است کلاس را علامت زده باشد (اکشن‌ها هم ارث می‌برند).
        return endpoint.Metadata.GetOrderedMetadata<AllowInReadOnlyAttribute>().Count > 0;
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
