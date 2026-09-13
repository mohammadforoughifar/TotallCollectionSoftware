using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

/// <summary>
/// ابزار دریافت «کامل» فهرست‌های صفحه‌بندی‌شده از API.
/// همهٔ متدهای GetAll در API اکنون <see cref="PagedResult{T}"/> برمی‌گردانند؛
/// در صفحه‌هایی که واقعاً به همهٔ رکوردها نیاز دارند (کمبوها، فیلتر/مرتب‌سازی سمت کلاینت)
/// به‌جای ارسال یک pageSize بزرگ، همهٔ صفحه‌ها پشت‌سرهم گرفته می‌شوند تا هیچ رکوردی جا نیفتد.
/// </summary>
public static class PagedFetch
{
    /// <summary>اندازهٔ هر درخواست (حداکثر مجاز سمت سرور).</summary>
    public const int ChunkSize = Pager.MaxPageSize;

    /// <summary>سقف تعداد صفحه‌ها برای جلوگیری از حلقهٔ بی‌پایان در صورت پاسخ نامعتبر سرور.</summary>
    private const int MaxPages = 500;

    /// <summary>
    /// گرفتن همهٔ رکوردهای یک اندپوینت صفحه‌بندی‌شده.
    /// <paramref name="fetchPage"/> باید صفحهٔ خواسته‌شده را با (page, pageSize) از API بگیرد.
    /// </summary>
    public static async Task<List<T>> AllAsync<T>(Func<int, int, Task<PagedResult<T>>> fetchPage, int pageSize = ChunkSize)
    {
        var all = new List<T>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var res = await fetchPage(page, pageSize);
            if (res?.Items is not { Count: > 0 }) break;

            all.AddRange(res.Items);

            // اندازهٔ واقعی صفحه از پاسخ سرور خوانده می‌شود (ممکن است سرور مقدار را نرمال کرده باشد)
            var effectiveSize = res.PageSize > 0 ? res.PageSize : pageSize;

            if (res.TotalCount <= 0 || all.Count >= res.TotalCount || res.Items.Count < effectiveSize) break;
        }

        return all;
    }

    /// <summary>
    /// گرفتن یک صفحهٔ مشخص — برای صفحه‌هایی که واقعاً UI صفحه‌بندی دارند.
    /// </summary>
    public static Task<PagedResult<T>> PageAsync<T>(Func<int, int, Task<PagedResult<T>>> fetchPage, int page, int pageSize)
        => fetchPage(page, pageSize);
}
