namespace Inventory.Api.Licensing.Infrastructure;

/// <summary>
/// قاعدهٔ «فعالیت واقعی» — بند ۳ صورت‌جلسه.
///
/// <para>
/// مسئله: کلاینت Blazor در تب باز، هر چند ثانیه شمارش اعلان و متریک زنده را می‌گیرد.
/// اگر هر درخواست «فعالیت» شمرده شود، یک تب فراموش‌شده تا ابد صندلی را اشغال می‌کند و
/// «کاربر هم‌زمان» بی‌معنا می‌شود. پس <b>فقط</b> پولینگ‌های پس‌زمینه معاف‌اند.
/// </para>
///
/// <para>
/// قاعدهٔ سخت: معافیّت فقط برای <c>GET</c>/<c>HEAD</c> و فقط برای مسیرهای زیر است.
/// هر متد غیر GET (حتی <c>POST /notifications/read</c>) فعالیت واقعی است؛ «خواندم» یک
/// عمل کاربر است نه پولینگ. و GET روی دادهٔ کسب‌وکار (مثل <c>/api/invoices</c>) هم
/// فعالیت است — استثنا «هر GET» نیست.
/// </para>
/// </summary>
public static class SeatActivityRules
{
    /// <summary>
    /// مسیرهای پولینگ که «فعالیت» نیستند. پیشوندی مقایسه می‌شوند (بدون حساسیت به حروف).
    /// </summary>
    private static readonly string[] PollingPrefixes =
    {
        "/api/notifications",
        "/api/chat/conversations",
        "/api/chat/unread",
        "/api/system/live-metrics",
        "/api/system/idle-state",
        "/api/dashboard/live",
        "/api/license",
        "/api/health",
        "/hubs"
    };

    /// <summary>مسیرهایی که دروازهٔ صندلی نباید آن‌ها را ببیند (ورود/خروج و وضعیت لایسنس).</summary>
    private static readonly string[] GateSkipPrefixes =
    {
        "/api/auth",
        "/api/license",
        "/api/health",
        "/swagger",
        "/hubs",
        "/_framework",
        "/_content"
    };

    /// <summary>آیا این درخواست از دروازهٔ صندلی مستثناست؟ (صندلی مصرف نمی‌کند)</summary>
    public static bool IsGateSkipped(string? path)
    {
        var p = Normalize(path);
        foreach (var prefix in GateSkipPrefixes)
            if (p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// آیا این درخواست «فعالیت واقعی» است؟ (صندلی را زنده نگه می‌دارد و LastSeen را می‌زند)
    /// </summary>
    public static bool IsActivity(string? method, string? path)
    {
        if (string.IsNullOrEmpty(method)) return false;

        var p = Normalize(path);

        // هاب‌ها اول: negotiate یک POST است، ولی اتصال بلادرنگ «فعالیت» نیست.
        if (p.StartsWith("/hubs", StringComparison.OrdinalIgnoreCase)) return false;

        // هر متدی جز GET/HEAD فعالیت است — بی‌قید‌و‌شرط.
        if (!string.Equals(method, HttpMethods.Get, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(method, HttpMethods.Head, StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var prefix in PollingPrefixes)
            if (p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        return true;
    }

    private static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "/";
        var p = path.Trim();
        var q = p.IndexOf('?');
        if (q >= 0) p = p[..q];
        if (!p.StartsWith('/')) p = "/" + p;
        return p.Length > 1 && p.EndsWith('/') ? p.TrimEnd('/') : p;
    }

    private static class HttpMethods
    {
        public const string Get = "GET";
        public const string Head = "HEAD";
    }
}
