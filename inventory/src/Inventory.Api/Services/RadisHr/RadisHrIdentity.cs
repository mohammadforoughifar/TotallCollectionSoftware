using System.Security.Claims;

namespace RadisHr.Api.Services;

/// <summary>کلید نمایشی گردش‌کار قدیمی، برگرفته از هویت/مجوز واقعی Inventory.</summary>
public static class RadisHrIdentity
{
    public static string RoleKey(ClaimsPrincipal user) =>
        user.IsInRole("Admin") ? "ceo" : user.HasClaim("permission", "RadisHr.Access") ? "hr" : "";
}
