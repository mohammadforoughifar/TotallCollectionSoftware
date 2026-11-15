using Inventory.Licensing.Abstractions;
using Inventory.Licensing.Entitlement;
using Inventory.Licensing.Modules;
using Microsoft.AspNetCore.SignalR;

namespace Inventory.Api.Licensing.Infrastructure;

/// <summary>
/// گارد لایسنس برای <b>هاب‌های SignalR</b> — همان قاعدهٔ HTTP، روی کانال بلادرنگ.
///
/// <para>
/// <b>چرا لازم شد؟</b> دروازهٔ <see cref="LicenseGateMiddleware"/> فقط مسیرهای
/// <c>/api/*</c> را می‌بیند و هاب‌ها روی <c>/hubs/*</c> هستند؛ یعنی تا پیش از این
/// هیچ‌یک از پنج هاب هیچ بررسی لایسنسی نداشتند — نه وضعیت کلی، نه ماژول.
/// </para>
///
/// <para>
/// <b>سه تصمیم، هر سه آگاهانه:</b>
/// </para>
/// <list type="number">
///   <item><b>اتصال (OnConnectedAsync) در حالت فقط‌خواندنی بسته نمی‌شود</b> برای هاب‌های
///   «خواندنی/حضوری» (داشبورد، اعلان، چت). دلیل: پوستهٔ برنامه برای نمایش پیام «لایسنس
///   فعال نیست» و بنر وضعیت به همین اتصال‌ها وابسته است؛ اگر اتصال رد شود کلاینت در
///   حلقهٔ اتصال مجدد می‌افتد و کاربر فکر می‌کند برنامه خراب است.</item>
///   <item><b>اتصال بسته می‌شود</b> برای هاب‌هایی که «قابلیت» هستند و ذاتشان کنش است
///   (ریموت دسکتاپ). دسترسی از راه دور به دستگاه‌های مشتری بدون لایسنس، همان چیزی است
///   که باید بسته بماند.</item>
///   <item><b>متد نوشتاری در حالت فقط‌خواندنی رد می‌شود</b>، هرچند اتصال برقرار باشد.
///   این هر دو خواستهٔ بازبینی را با هم برآورده می‌کند: پیام قابل‌فهم + نبودِ نوشتن.</item>
/// </list>
///
/// <para>
/// <b>ماژول غیرفعال هم بسته است:</b> اگر ماژول هاب در لایسنس نباشد (<c>Disabled</c>)،
/// اتصال با <see cref="HubException"/> رد می‌شود — نه با قطع ساکت، تا کلاینت بتواند
/// پیام فارسی را نشان دهد.
/// </para>
/// </summary>
public sealed class HubLicenseFilter : IHubFilter
{
    private readonly ILicenseService _licenses;
    private readonly ILogger<HubLicenseFilter> _log;

    public HubLicenseFilter(ILicenseService licenses, ILogger<HubLicenseFilter> log)
    {
        _licenses = licenses;
        _log = log;
    }

    /// <summary>
    /// نگاشت هاب → (کد ماژول، آیا اتصالش در حالت فقط‌خواندنی مجاز است، دلیل).
    ///
    /// <para>
    /// ⚠️ هاب تازه‌ای که این‌جا نباشد، <b>بسته</b> تلقی می‌شود (fail-closed) و در لاگ
    /// هشدار ثبت می‌شود — برخلاف نگاشت کنترلرها که fail-open است. تفاوت عمدی است:
    /// افتادن یک هاب در حالت باز، کانال بلادرنگِ بدون هیچ بررسی می‌سازد، در حالی که
    /// افتادن یک کنترلر در حالت بسته فقط یک خطای قابل‌مشاهده در تست است.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, HubLicensePolicy> Hubs = new(StringComparer.OrdinalIgnoreCase)
    {
        // داشبورد: ماژول همیشه‌فعال؛ اتصال لازم است تا بنر وضعیت و آمار نمایش داده شود.
        ["DashboardHub"] = new(ModuleCodes.Dashboard, ConnectInReadOnly: true,
            "داشبورد همیشه‌فعال است و همان کانالی است که وضعیت لایسنس را به کاربر نشان می‌دهد."),

        // اعلان: هیچ متد سمت کلاینت ندارد؛ فقط عضویت در گروه. کاملاً خواندنی.
        ["NotifyHub"] = new(ModuleCodes.Core, ConnectInReadOnly: true,
            "کانال اعلان فقط عضویت در گروه کاربری است؛ بدون آن هیچ پیام «لایسنس فعال نیست» دیده نمی‌شود."),

        // چت: متدهای هاب حضور (تایپ کردن) و عضویت در اتاق‌اند؛ ارسال پیام از HTTP می‌آید
        // و همان‌جا مسدود می‌شود. پس اتصال باز می‌ماند ولی «ارسال پیام» عملاً بسته است.
        ["ChatHub"] = new(ModuleCodes.Chat, ConnectInReadOnly: true,
            "متدهای هاب چت فقط حضور و عضویت اتاق‌اند؛ ارسال پیام از HTTP می‌آید و آن‌جا مسدود است."),

        // ریموت دسکتاپ: خودِ قابلیت، کنش است. بدون لایسنس و در حالت فقط‌خواندنی بسته.
        ["RemoteHub"] = new(ModuleCodes.ItAssets, ConnectInReadOnly: false,
            "دسترسی از راه دور به دستگاه مشتری، قابلیتِ کنشی است؛ بدون لایسنس معتبر باز نمی‌شود."),

        // ایجنت ریموت: فریم صفحه و نتیجهٔ رضایت. همین منطق.
        ["RemoteAgentHub"] = new(ModuleCodes.ItAssets, ConnectInReadOnly: false,
            "ارسال فریم صفحه و ثبت رضایت، کنش است و زیر همان قابلیت ریموت می‌افتد."),
    };

    /// <summary>
    /// متدهای هابی که در حالت فقط‌خواندنی <b>مجاز</b> می‌مانند — هر کدام با دلیل.
    /// هر متدی که این‌جا نباشد، در حالت فقط‌خواندنی رد می‌شود.
    /// </summary>
    private static readonly Dictionary<string, string> AllowedMethodsInReadOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        // ---- داشبورد ----
        ["RequestHardwareCheck"] =
            "درخواست یک دور پینگ دستگاه‌ها؛ دادهٔ کسب‌وکار نمی‌نویسد و فقط آمار سخت‌افزار را تازه می‌کند " +
            "(همان چیزی که GET های داشبورد نشان می‌دهند).",

        // ---- چت ----
        ["JoinConversation"] = "عضویت در گروه SignalR پس از بررسی عضویت واقعی در گفتگو؛ خواندن.",
        ["LeaveConversation"] = "خروج از گروه؛ هیچ داده‌ای نوشته نمی‌شود.",
        ["SendTyping"] = "نشانگر «در حال تایپ» — وضعیت گذرا، نه پیام و نه رکورد دیتابیس.",
        ["StopTyping"] = "خاموش کردن همان نشانگر.",
    };

    private sealed record HubLicensePolicy(string ModuleCode, bool ConnectInReadOnly, string Reason);

    /// <summary>
    /// نگاشت <b>نام کامل</b> هاب‌ها به کلید کوتاه همین جدول.
    ///
    /// <para>
    /// <b>چرا نام کامل؟</b> بازبینی چهارم (بند ۵) خواست تصمیم بر پایهٔ
    /// <c>GetType().Name</c> نباشد. نام کوتاه دو ضعف دارد: (۱) دو کلاس هم‌نام در
    /// دو namespace از هم تشخیص داده نمی‌شوند؛ (۲) کلاس‌های تولیدی کامپایلر
    /// (مثل انواع محلیِ تست) نام کوتاه دست‌کاری‌شده می‌گیرند و بی‌صدا از نگاشت
    /// بیرون می‌افتند.
    /// </para>
    ///
    /// <para>
    /// قاعدهٔ جدید: اول <c>Type.FullName</c>؛ اگر نبود، نام کوتاه (برای سازگاری با
    /// کلاس‌های مشتق/تست)؛ و اگر هیچ‌کدام نبود ⇒ <b>بسته</b> (fail-closed).
    /// نام‌های تولیدی کامپایلر (<c>&lt;…&gt;</c> یا <c>$</c>) همیشه رد می‌شوند.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> HubFullNames = new(StringComparer.Ordinal)
    {
        ["Inventory.Api.Hubs.DashboardHub"] = "DashboardHub",
        ["Inventory.Api.Hubs.NotifyHub"] = "NotifyHub",
        ["Inventory.Api.Hubs.ChatHub"] = "ChatHub",
        ["Inventory.Api.Hubs.Remote.RemoteHub"] = "RemoteHub",
        ["Inventory.Api.Hubs.Remote.RemoteAgentHub"] = "RemoteAgentHub",
    };

    /// <summary>
    /// کلید نگاشت برای یک نوع هاب — یا <c>null</c> اگر هاب شناخته‌شده نباشد (⇒ بسته).
    /// عمومی است تا تست بتواند بدون ساخت اتصال واقعی، تصمیم را بسنجد.
    /// </summary>
    public static string? ResolveHubKey(Type hubType)
    {
        ArgumentNullException.ThrowIfNull(hubType);

        var fullName = hubType.FullName;

        // ۱) نام کامل — دقیق‌ترین تشخیص
        if (!string.IsNullOrEmpty(fullName) && HubFullNames.TryGetValue(fullName, out var byFullName))
            return byFullName;

        var name = hubType.Name;

        // ۲) نام تولیدی کامپایلر: هرگز به نگاشت راه نمی‌یابد (وگرنه یک type محلی
        //    می‌تواند با فریب نام، از بررسی رد شود).
        if (name.Contains('<', StringComparison.Ordinal) || name.Contains('$', StringComparison.Ordinal))
            return null;

        // ۳) نام کوتاه — سازگاری با کلاس‌های مشتق و دوقلوهای تست
        return Hubs.ContainsKey(name) ? name : null;
    }

    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var hubType = context.Hub.GetType();
        var hubName = ResolveHubKey(hubType) ?? hubType.FullName ?? hubType.Name;
        var method = context.HubMethodName;

        if (AllowedMethodsInReadOnly.ContainsKey(method))
            return await next(context);

        // متد ناشناخته/نوشتاری: اگر لایسنس فقط‌خواندنی است، رد.
        if (await LicenseAccessState.IsReadOnlyAsync(_licenses, context.Context.ConnectionAborted))
        {
            _log.LogWarning("[License] متد هاب «{Hub}.{Method}» در حالت فقط‌خواندنی رد شد.", hubName, method);
            throw new HubException(
                "سامانه در حالت فقط‌خواندنی است (لایسنس فعال یا معتبر نیست)؛ این عملیات انجام نشد. " +
                "مشاهدهٔ اطلاعات آزاد است؛ برای فعال‌سازی، فایل لایسنس را از صفحهٔ «لایسنس» بارگذاری کنید.");
        }

        // ماژول غیرفعال: حتی با لایسنس معتبر هم این هاب در دسترس نیست.
        if (ResolveHubKey(hubType) is { } invokeKey && Hubs.TryGetValue(invokeKey, out var policy))
        {
            var state = _licenses.GetModuleState(policy.ModuleCode);
            if (state == ModuleState.Disabled)
            {
                _log.LogWarning("[License] متد هاب «{Hub}.{Method}» رد شد: ماژول {Module} در لایسنس نیست.",
                    hubName, method, policy.ModuleCode);
                throw new HubException(
                    $"ماژول «{ModuleCodes.TitleOf(policy.ModuleCode)}» در لایسنس این نصب فعال نیست؛ " +
                    "برای استفاده با پشتیبانی تماس بگیرید.");
            }
        }

        return await next(context);
    }

    /// <summary>
    /// بررسی وضعیت در لحظهٔ اتصال — پیش از هر متدی.
    ///
    /// <para>
    /// برای هاب‌های «کنشی» (ریموت)، در حالت فقط‌خواندنی اتصال رد می‌شود. برای بقیه،
    /// اتصال برقرار می‌ماند تا پوسته و بنر وضعیت کار کنند.
    /// </para>
    /// </summary>
    public async ValueTask OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, ValueTask> next)
    {
        var hubType = context.Hub.GetType();
        var hubKey = ResolveHubKey(hubType);
        var hubName = hubKey ?? hubType.FullName ?? hubType.Name;
        var ct = context.Context.ConnectionAborted;

        if (hubKey is null || !Hubs.TryGetValue(hubKey, out var policy))
        {
            // هاب نگاشت‌نشده: fail-closed + هشدار، تا در تست بعدی معلوم شود.
            _log.LogWarning("[License] هاب «{Hub}» در نگاشت لایسنس نیست؛ اتصال بسته شد. " +
                            "ردیف آن را به HubLicenseFilter اضافه کنید.", hubName);
            throw new HubException("این کانال در این نسخه مجاز نیست؛ با پشتیبانی تماس بگیرید.");
        }

        // ماژول غیرفعال ⇒ اتصال بسته (با پیام قابل‌فهم، نه قطع ساکت).
        var moduleState = _licenses.GetModuleState(policy.ModuleCode);
        if (moduleState == ModuleState.Disabled)
        {
            _log.LogWarning("[License] اتصال هاب «{Hub}» بسته شد: ماژول {Module} غیرفعال.",
                hubName, policy.ModuleCode);
            throw new HubException(
                $"ماژول «{ModuleCodes.TitleOf(policy.ModuleCode)}» در لایسنس این نصب فعال نیست.");
        }

        var readOnly = await LicenseAccessState.IsReadOnlyAsync(_licenses, ct);
        if (readOnly && !policy.ConnectInReadOnly)
        {
            _log.LogWarning("[License] اتصال هاب «{Hub}» در حالت فقط‌خواندنی بسته شد.", hubName);
            throw new HubException(
                "سامانه در حالت فقط‌خواندنی است؛ این کانال نیازمند لایسنس معتبر است. " +
                "برای فعال‌سازی، فایل لایسنس را از صفحهٔ «لایسنس» بارگذاری کنید.");
        }

        await next(context);
    }

    /// <summary>نگاشت هاب → ماژول، برای تست و گزارش (بدون دست‌زدن به نمونه‌های واقعی).</summary>
    public static IReadOnlyDictionary<string, (string ModuleCode, bool ConnectInReadOnly, string Reason)> Catalog
        => Hubs.ToDictionary(kv => kv.Key,
            kv => (kv.Value.ModuleCode, kv.Value.ConnectInReadOnly, kv.Value.Reason),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>نگاشت نام کامل ← کلید کوتاه (برای تست و گزارش).</summary>
    public static IReadOnlyDictionary<string, string> FullNameCatalog => HubFullNames;

    /// <summary>متدهایی که در حالت فقط‌خواندنی مجاز می‌مانند (برای تست و گزارش).</summary>
    public static IReadOnlyDictionary<string, string> ReadOnlySafeMethods => AllowedMethodsInReadOnly;
}
