namespace Inventory.Shared.Dtos;

/// <summary>
/// پارامترهای صفحه‌بندی سراسری بک‌اند (Query String).
/// روش اصلی: <c>skip</c>/<c>take</c> — تعداد رکوردهای ردشده و تعداد رکوردهای دریافتی.
/// برای سازگاری با کلاینت فعلی، <c>page</c>/<c>pageSize</c> هم پذیرفته می‌شود و به skip/take تبدیل می‌گردد.
/// اگر skip یا take ارسال شده باشد بر page/pageSize اولویت دارد.
/// </summary>
public class PagingRequest
{
    /// <summary>اندازه پیش‌فرض صفحه وقتی هیچ پارامتری ارسال نشده باشد.</summary>
    public const int DefaultTake = 20;

    /// <summary>سقف تعداد رکورد در هر درخواست.</summary>
    public const int MaxTake = 500;

    /// <summary>Query String برای دریافت بزرگ‌ترین صفحه مجاز (skip=0&amp;take=MaxTake) — برای لیست‌هایی که کلاینت خودش صفحه‌بندی می‌کند.</summary>
    public static readonly string AllQuery = $"skip=0&take={MaxTake}";

    /// <summary>تعداد رکوردهایی که از ابتدای لیست رد می‌شوند.</summary>
    public int? Skip { get; set; }

    /// <summary>تعداد رکوردهایی که برگردانده می‌شوند.</summary>
    public int? Take { get; set; }

    /// <summary>شماره صفحه (۱-پایه) — فقط برای سازگاری؛ به skip تبدیل می‌شود.</summary>
    public int? Page { get; set; }

    /// <summary>اندازه صفحه — فقط برای سازگاری؛ به take تبدیل می‌شود.</summary>
    public int? PageSize { get; set; }

    /// <summary>آیا کاربر skip/take را مستقیم ارسال کرده است؟</summary>
    public bool UsesSkipTake => Skip.HasValue || Take.HasValue;

    /// <summary>مقدار نهایی take (بین ۱ و <see cref="MaxTake"/>).</summary>
    public int TakeValue
    {
        get
        {
            var take = UsesSkipTake ? Take : PageSize;
            var v = take ?? DefaultTake;
            if (v < 1) v = DefaultTake;
            return v > MaxTake ? MaxTake : v;
        }
    }

    /// <summary>مقدار نهایی skip (بزرگ‌تر یا مساوی صفر).</summary>
    public int SkipValue
    {
        get
        {
            if (UsesSkipTake)
                return Skip is > 0 ? Skip.Value : 0;

            var page = Page is > 0 ? Page.Value : 1;
            return (page - 1) * TakeValue;
        }
    }

    /// <summary>ساخت درخواست صفحه‌بندی از skip/take.</summary>
    public static PagingRequest FromSkipTake(int skip, int take) => new() { Skip = skip, Take = take };

    /// <summary>ساخت درخواست صفحه‌بندی از page/pageSize.</summary>
    public static PagingRequest FromPage(int page, int pageSize) => new() { Page = page, PageSize = pageSize };

    /// <summary>رشته Query برای ارسال به API (skip/take).</summary>
    public string ToQueryString() => $"skip={SkipValue}&take={TakeValue}";
}
