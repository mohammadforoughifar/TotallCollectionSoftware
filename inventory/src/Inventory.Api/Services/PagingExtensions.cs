using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services;

/// <summary>
/// ابزارهای صفحه‌بندی سراسری بک‌اند با روش Skip/Take.
/// - <see cref="ToPagedResultAsync{T}(IQueryable{T}, PagingRequest, CancellationToken)"/> : شمارش + Skip/Take روی دیتابیس (ترجیحی).
/// - <see cref="ToPagedResult{T}(IReadOnlyCollection{T}, PagingRequest)"/> : Skip/Take در حافظه برای لیست‌هایی که ترتیب/فیلترشان محاسباتی است.
/// </summary>
public static class PagingExtensions
{
    /// <summary>اعمال Skip/Take روی کوئری دیتابیس (کوئری باید قبلاً مرتب شده باشد).</summary>
    public static IQueryable<T> ApplyPaging<T>(this IQueryable<T> query, PagingRequest? paging)
    {
        paging ??= new PagingRequest();
        return query.Skip(paging.SkipValue).Take(paging.TakeValue);
    }

    /// <summary>اعمال Skip/Take روی لیست در حافظه.</summary>
    public static IEnumerable<T> ApplyPaging<T>(this IEnumerable<T> source, PagingRequest? paging)
    {
        paging ??= new PagingRequest();
        return source.Skip(paging.SkipValue).Take(paging.TakeValue);
    }

    /// <summary>شمارش کل + دریافت صفحه از دیتابیس (کوئری باید قبلاً مرتب شده باشد).</summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(this IQueryable<T> query, PagingRequest? paging, CancellationToken ct = default)
    {
        paging ??= new PagingRequest();
        var total = await query.CountAsync(ct);
        var items = total == 0
            ? new List<T>()
            : await query.Skip(paging.SkipValue).Take(paging.TakeValue).ToListAsync(ct);
        return new PagedResult<T> { Items = items, TotalCount = total };
    }

    /// <summary>Skip/Take روی لیست آماده در حافظه.</summary>
    public static PagedResult<T> ToPagedResult<T>(this IReadOnlyCollection<T> source, PagingRequest? paging)
    {
        paging ??= new PagingRequest();
        return new PagedResult<T>
        {
            TotalCount = source.Count,
            Items = source.Skip(paging.SkipValue).Take(paging.TakeValue).ToList()
        };
    }

    /// <summary>ساخت نتیجه صفحه‌بندی‌شده از آیتم‌های یک صفحه و تعداد کل (برای پروژکشن‌های anonymous).</summary>
    public static PagedResult<T> ToPagedResult<T>(this IEnumerable<T> pageItems, int totalCount)
        => new() { Items = pageItems.ToList(), TotalCount = totalCount };

    /// <summary>تبدیل آیتم‌های یک نتیجه صفحه‌بندی‌شده به نوع دیگر (TotalCount حفظ می‌شود).</summary>
    public static PagedResult<TOut> Map<TIn, TOut>(this PagedResult<TIn> source, Func<TIn, TOut> map)
        => new() { Items = source.Items.Select(map).ToList(), TotalCount = source.TotalCount };
}
