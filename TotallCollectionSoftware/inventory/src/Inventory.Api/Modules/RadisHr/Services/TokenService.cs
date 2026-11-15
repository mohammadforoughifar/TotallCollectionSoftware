using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using RadisHr.Shared.Models;

namespace RadisHr.Api.Services;

/// <summary>
/// صدور توکن پنل منابع انسانی (RADIS-HR).
///
/// <para>
/// ⚠️ بند ۱۱ (ردیف ۲۳): این کلاس دیگر کلید ثابت ندارد. اگر <c>Jwt:Key</c> تنظیم شده باشد
/// همان استفاده می‌شود؛ در غیر این صورت کلید از <see cref="ISigningKeys.Derive"/> با
/// هدف <c>radis-hr-legacy</c> مشتق می‌شود — یعنی همان کلید نصب، با دامنهٔ جدا. نتیجه:
/// نه استثنا می‌دهد (مثل حالت «کلید نبود ⇒ کرش»)، و نه توکنی با رشتهٔ عمومیِ داخل کد
/// صادر می‌کند که هر کسی بتواند بسازد.
/// </para>
/// </summary>
public class TokenService
{
    private readonly IConfiguration _config;
    private readonly Inventory.Api.Security.ISigningKeys _keys;

    public TokenService(IConfiguration config, Inventory.Api.Security.ISigningKeys keys)
    {
        _config = config;
        _keys = keys;
    }

    /// <summary>کلید امضا — پیکربندی یا مشتق‌شده از کلید همین نصب (بدون رشتهٔ ثابت).</summary>
    public string Key => _config["Jwt:Key"]
                         ?? Convert.ToBase64String(_keys.Derive("radis-hr-legacy"));
    public string Issuer => _config["Jwt:Issuer"] ?? "RadisHr";
    public string Audience => _config["Jwt:Audience"] ?? "RadisHrClient";
    public int LifetimeHours => int.TryParse(_config["Jwt:LifetimeHours"], out var h) ? h : 12;

    public (string Token, DateTime ExpiresAt) Create(AppUser user)
    {
        var meta = RolePages.Map.TryGetValue(user.UserKey, out var m) ? m : default;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserKey),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Role, user.UserKey),
            new("roleTitle", user.RoleTitle),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        if (meta.Pages != null)
            foreach (var page in meta.Pages) claims.Add(new Claim("page", page));

        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddHours(LifetimeHours);

        var token = new JwtSecurityToken(Issuer, Audience, claims, expires: expires, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
