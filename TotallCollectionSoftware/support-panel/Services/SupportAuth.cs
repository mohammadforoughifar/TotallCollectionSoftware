using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LicenseSigner.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace LicenseSigner.Services;

/// <summary>
/// احراز هویت پنل: PBKDF2-SHA256 (۲۱۰٬۰۰۰ دور)، قفل ۵ تلاش/۱۵ دقیقه،
/// تعویض اجباری گذرواژهٔ آغازین. گذرواژهٔ خام هیچ‌جا ذخیره/لاگ نمی‌شود.
/// </summary>
public sealed class SupportAuthService(SupportDbContext db, ILogger<SupportAuthService> log)
{
    public const int Iterations = 210_000;
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(15);
    public const int MinPasswordLength = 10;

    public static string HashPassword(string password, int iterations = Iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string stored, out int iterations)
    {
        iterations = Iterations;
        if (string.IsNullOrWhiteSpace(stored)) return false;
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256") return false;
        if (!int.TryParse(parts[1], out iterations)) return false;
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public async Task<(bool Ok, string? Error, SupportUser? User)> SignInAsync(string userName, string password,
        CancellationToken ct = default)
    {
        userName = (userName ?? "").Trim();
        var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == userName, ct);
        if (user is null || !user.IsActive)
        {
            log.LogWarning("تلاش ورود ناموفق برای کاربر ناشناخته/غیرفعال");
            return (false, "نام کاربری یا گذرواژه نادرست است.", null);
        }

        var now = DateTime.UtcNow;
        if (user.LockedUntil is { } lockedUntil && lockedUntil > now)
            return (false, $"حساب تا {JalaliDate.Full(lockedUntil)} قفل است.", null);

        if (!VerifyPassword(password ?? "", user.PasswordHash, out var iterations))
        {
            user.FailedCount++;
            if (user.FailedCount >= MaxFailedAttempts)
            {
                user.LockedUntil = now + LockoutWindow;
                user.FailedCount = 0;
            }

            await db.SaveChangesAsync(ct);
            log.LogWarning("تلاش ورود ناموفق برای {User}", user.UserName);
            return (false, "نام کاربری یا گذرواژه نادرست است.", null);
        }

        // هش قدیمی با دور کمتر؟ بی‌صدا به‌روز می‌شود.
        if (iterations < Iterations) user.PasswordHash = HashPassword(password!, Iterations);

        user.FailedCount = 0;
        user.LockedUntil = null;
        user.LastLoginAt = now;
        await db.SaveChangesAsync(ct);
        return (true, null, user);
    }

    public async Task<SupportUser> CreateUserAsync(string userName, string displayName, SupportRole role,
        string? initialPassword = null, CancellationToken ct = default)
    {
        userName = (userName ?? "").Trim();
        if (userName.Length < 3) throw new InvalidOperationException("نام کاربری باید حداقل ۳ نویسه باشد.");
        if (await db.Users.AnyAsync(u => u.UserName == userName, ct))
            throw new InvalidOperationException("این نام کاربری قبلاً ثبت شده است.");

        var password = initialPassword ?? GeneratePassword();
        var user = new SupportUser
        {
            UserName = userName,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? userName : displayName.Trim(),
            Role = role,
            PasswordHash = HashPassword(password, Iterations),
            PasswordIterations = Iterations,
            MustChangePassword = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        log.LogWarning("کاربر {User} با نقش {Role} ساخته شد", user.UserName, user.Role);
        return user;
    }

    public async Task<(bool Ok, string? Error)> ChangePasswordAsync(int userId, string current, string next,
        string confirm, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return (false, "کاربر پیدا نشد.");
        if (!VerifyPassword(current ?? "", user.PasswordHash, out _)) return (false, "گذرواژهٔ فعلی نادرست است.");
        if ((next ?? "").Length < MinPasswordLength) return (false, $"گذرواژهٔ تازه باید حداقل {MinPasswordLength} نویسه باشد.");
        if (next != confirm) return (false, "تکرار گذرواژهٔ تازه نمی‌خواند.");
        if (next == current) return (false, "گذرواژهٔ تازه نباید با فعلی یکی باشد.");

        user.PasswordHash = HashPassword(next!, Iterations);
        user.PasswordIterations = Iterations;
        user.MustChangePassword = false;
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool Ok, string? Error, string? NewPassword)> ResetPasswordAsync(int userId,
        CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return (false, "کاربر پیدا نشد.", null);
        var password = GeneratePassword();
        user.PasswordHash = HashPassword(password, Iterations);
        user.PasswordIterations = Iterations;
        user.MustChangePassword = true;
        user.FailedCount = 0;
        user.LockedUntil = null;
        await db.SaveChangesAsync(ct);
        return (true, null, password);
    }

    public static string GeneratePassword(int length = 20)
    {
        const string alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = RandomNumberGenerator.GetBytes(length);
        var sb = new StringBuilder(length);
        foreach (var b in bytes) sb.Append(alphabet[b % alphabet.Length]);
        return sb.ToString();
    }
}

/// <summary>ساخت Claims از کاربر (کوکی مبتنی بر نقش).</summary>
public static class ClaimsFactory
{
    public static ClaimsPrincipal Create(SupportUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.GivenName, user.DisplayName),
            new(ClaimTypes.Role, user.Role.ToString())
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    public static int UserId(this ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    public static string UserName(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.Name) ?? "system";

    public static string DisplayName(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.GivenName) ?? principal.UserName();

    public static SupportRole Role(this ClaimsPrincipal principal) =>
        Enum.TryParse<SupportRole>(principal.FindFirstValue(ClaimTypes.Role), out var role) ? role : SupportRole.Operator;

    public static bool CanSign(this ClaimsPrincipal principal) =>
        principal.Role() is SupportRole.Signer or SupportRole.Admin;

    public static bool IsAdmin(this ClaimsPrincipal principal) => principal.Role() == SupportRole.Admin;
}
