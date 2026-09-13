using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>
/// صفحه‌بندی سمت دیتابیس — Skip/Take و Count به SQL منتقل می‌شوند،
/// بنابراین فقط رکوردهای صفحهٔ جاری از دیتابیس خوانده می‌شوند.
/// همهٔ سرویس‌ها و کنترلرهایی که روی <c>IQueryable</c> کار می‌کنند از این هلپر استفاده می‌کنند
/// تا شکل خروجی (<see cref="PagedResult{T}"/>) در کل پروژه یکسان بماند.
/// </summary>
public static class PagedQuery
{
    /// <summary>
    /// اجرای پرس‌وجو به‌صورت صفحه‌بندی‌شده.
    /// page &lt; 1 ⇒ ۱ و pageSize خارج از بازهٔ ۱..۲۰۰ ⇒ <see cref="Pager.DefaultPageSize"/>.
    /// </summary>
    public static async Task<PagedResult<T>> ToPagedAsync<T>(
        this IQueryable<T> query,
        int page,
        int pageSize,
        int defaultPageSize = Pager.DefaultPageSize,
        CancellationToken ct = default)
    {
        var (p, size) = Pager.Normalize(page, pageSize, defaultPageSize);

        var total = await query.CountAsync(ct);
        var items = total == 0
            ? new List<T>()
            : await query.Skip((p - 1) * size).Take(size).ToListAsync(ct);

        return new PagedResult<T>
        {
            TotalCount = total,
            Page = p,
            PageSize = size,
            Items = items
        };
    }

    /// <summary>
    /// صفحه‌بندی فهرستی که قبلاً از دیتابیس خوانده شده (وقتی فیلتر/مرتب‌سازی در حافظه انجام می‌شود)
    /// و نگاشت هم‌زمان هر رکورد به DTO.
    /// </summary>
    public static PagedResult<TResult> PageSelect<TSource, TResult>(
        IReadOnlyList<TSource> items,
        int page,
        int pageSize,
        Func<TSource, TResult> selector,
        int defaultPageSize = Pager.DefaultPageSize)
    {
        var (p, size) = Pager.Normalize(page, pageSize, defaultPageSize);
        return new PagedResult<TResult>
        {
            TotalCount = items.Count,
            Page = p,
            PageSize = size,
            Items = items.Skip((p - 1) * size).Take(size).Select(selector).ToList()
        };
    }
}
