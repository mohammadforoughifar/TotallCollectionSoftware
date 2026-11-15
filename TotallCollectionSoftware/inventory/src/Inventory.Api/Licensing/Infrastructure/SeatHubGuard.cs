using System.Security.Claims;
using Inventory.Api.Licensing.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Api.Licensing.Infrastructure;

/// <summary>
/// گارد صندلی برای اتصال هاب‌های کاربری — بند ۶ صورت‌جلسه.
///
/// <para>
/// چرا لازم است؟ هاب‌ها روی <c>/hubs/*</c> سرو می‌شوند و دروازهٔ HTTP آن‌ها را نمی‌بیند
/// (WebSocket بعد از handshake دیگر از میان‌افزار رد نمی‌شود). بدون این گارد، کاربرِ
/// بیرون‌انداخته‌شده می‌توانست کانال بلادرنگ را باز نگه دارد.
/// </para>
///
/// <para>
/// <b>فقط می‌خواند:</b> نه صندلی می‌گیرد، نه <c>LastSeen</c> را می‌زند، نه نشست تازه
/// می‌سازد. اتصال هاب «فعالیت» نیست؛ وگرنه یک تب باز با اعلان‌های بلادرنگ، صندلی را
/// تا ابد زنده نگه می‌داشت.
/// </para>
///
/// <para>
/// <c>RemoteAgentHub</c> عمداً گارد ندارد: ایجنت با <c>deviceKey</c> وصل می‌شود، نه با
/// توکن کاربر، و هیچ صندلی‌ای مصرف نمی‌کند.
/// </para>
/// </summary>
public static class SeatHubGuard
{
    /// <summary>
    /// اعتبارسنجی اتصال. در صورت رد، <see cref="HubException"/> پرتاب می‌شود (SignalR
    /// آن را به کلاینت می‌رساند و اتصال بسته می‌شود).
    /// </summary>
    public static async Task ValidateAsync(Hub hub)
    {
        var context = hub.Context;
        var user = context.User;
        if (user?.Identity?.IsAuthenticated != true) return;   // هاب‌های ناشناس (ایجنت)

        if (!int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0) return;

        var seats = context.GetHttpContext()?.RequestServices.GetService(typeof(SeatService)) as SeatService;
        if (seats is null) return;   // ثبت نشده (مثلاً آزمون واحد) — گارد بی‌اثر است

        var username = user.FindFirstValue(ClaimTypes.Name) ?? "";
        var sid = user.FindFirstValue("sid");
        var grant = await seats.ValidateHubAsync(userId, sid, username, ContextToken(context));

        if (!grant.Allowed)
            throw new HubException($"{grant.Code}: {grant.Message}");
    }

    private static CancellationToken ContextToken(HubCallerContext context)
        => context.GetHttpContext()?.RequestAborted ?? CancellationToken.None;
}
