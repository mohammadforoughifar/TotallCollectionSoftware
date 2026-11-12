using System.Security.Claims;
using Inventory.Api.Licensing.Services;

namespace Inventory.Api.Licensing.Infrastructure;

/// <summary>
/// دروازهٔ صندلی برای همهٔ درخواست‌های HTTP احراز‌شده — بند ۸ صورت‌جلسه.
///
/// <para>
/// کارش سه چیز است:
/// <list type="number">
/// <item>توکن بدون <c>sid</c> (توکن قدیمی یا توکنی که از مسیر دیگری صادر شده) رد می‌شود.</item>
/// <item>نشستِ <c>sid</c> باید <c>Active</c> و متعلق به همان کاربر باشد؛ وگرنه ۴۰۳/۴۰۱.</item>
/// <item>فقط «فعالیت واقعی» <c>LastSeen</c> را تازه می‌کند (پولینگ و هاب‌ها نه) — بند ۳.</item>
/// </list>
/// </para>
///
/// <para>
/// مسیرهای ورود/خروج، وضعیت لایسنس، سلامت و استاتیک‌ها از این دروازه رد نمی‌شوند.
/// </para>
/// </summary>
public sealed class SeatGateMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SeatGateMiddleware> _log;

    public SeatGateMiddleware(RequestDelegate next, ILogger<SeatGateMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext context, SeatService seats)
    {
        var path = context.Request.Path.Value ?? "/";

        if (SeatActivityRules.IsGateSkipped(path))
        {
            await _next(context);
            return;
        }

        var user = context.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        if (!int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
        {
            await _next(context);
            return;
        }

        var username = user.FindFirstValue(ClaimTypes.Name) ?? "";
        var sid = user.FindFirstValue("sid");
        var isActivity = SeatActivityRules.IsActivity(context.Request.Method, path);

        var grant = await seats.AuthorizeRequestAsync(userId, sid, username, isActivity, context.RequestAborted);

        if (!grant.Allowed)
        {
            context.Response.StatusCode = SeatGrant.StatusFor(grant.Code);
            await context.Response.WriteAsJsonAsync(new
            {
                code = grant.Code,
                message = grant.Message
            }, context.RequestAborted);
            return;
        }

        if (grant.Degraded) context.Response.Headers["X-License-Seat"] = "degraded";

        if (isActivity) _log.LogTrace("[License] فعالیت کاربر {UserId} روی {Method} {Path}", userId,
            context.Request.Method, path);

        await _next(context);
    }
}
