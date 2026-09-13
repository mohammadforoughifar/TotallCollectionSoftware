namespace Inventory.Shared.Dtos;

/// <summary>
/// ابزار یکپارچهٔ صفحه‌بندی برای همهٔ سرویس‌ها و کنترلرها.
/// قرارداد پروژه: خروجی هر متد «گرفتن همه» (<c>GetAll</c>) باید <see cref="PagedResult{T}"/> باشد.
/// </summary>
public static class Pager
{
    /// <summary>اندازهٔ صفحهٔ پیش‌فرض وقتی مقدار نامعتبر/صفر ارسال شود.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>بیشترین اندازهٔ صفحهٔ مجاز (جلوگیری از بارگذاری کل جدول).</summary>
    public const int MaxPageSize = 200;

    /// <summary>
    /// نرمال‌سازی شمارهٔ صفحه و اندازهٔ صفحه.
    /// page &lt; 1 ⇒ 1 ؛ pageSize خارج از بازهٔ ۱..۲۰۰ ⇒ اندازهٔ پیش‌فرض.
    /// </summary>
    public static (int Page, int PageSize) Normalize(int page, int pageSize, int defaultPageSize = DefaultPageSize)
    {
        if (page < 1) page = 1;

        var fallback = defaultPageSize is >= 1 and <= MaxPageSize ? defaultPageSize : DefaultPageSize;
        if (pageSize is < 1 or > MaxPageSize) pageSize = fallback;

        return (page, pageSize);
    }

    /// <summary>صفحه‌بندی یک فهرست آماده در حافظه (مناسب وقتی فیلتر/محاسبات سمت برنامه انجام می‌شود).</summary>
    public static PagedResult<T> Page<T>(IReadOnlyList<T> items, int page, int pageSize, int defaultPageSize = DefaultPageSize)
    {
        var (p, size) = Normalize(page, pageSize, defaultPageSize);
        return new PagedResult<T>
        {
            TotalCount = items.Count,
            Page = p,
            PageSize = size,
            Items = items.Skip((p - 1) * size).Take(size).ToList()
        };
    }
}
