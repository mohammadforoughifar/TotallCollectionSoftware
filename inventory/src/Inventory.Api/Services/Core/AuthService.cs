using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Inventory.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Db = Inventory.Api.Data;

namespace Inventory.Api.Services;

/// <summary>پیاده‌سازی احراز هویت JWT و مدیریت کاربران.</summary>
public class AuthService : IAuthService
{
    /// <summary>کلید امضای توکن (در محیط عملیاتی از تنظیمات/متغیر محیطی بخوانید).</summary>
    public const string JwtKey = "InventoryApp-Jwt-Secret-Key-2026-!@#$%-Very-Long-Key-For-HS256";
    public const string JwtIssuer = "InventoryApp";

    private readonly Db.AppDbContext _db;
    private readonly IInventoryService _inventory;

    public AuthService(Db.AppDbContext db, IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    // ---------------- رمزنگاری ----------------

    public static string HashPassword(string password)
    {
        // PBKDF2 با نمک تصادفی
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split('.', 2);
        if (parts.Length != 2) return false;
        var salt = Convert.FromBase64String(parts[0]);
        var expected = Convert.FromBase64String(parts[1]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    // ---------------- ورود ----------------

    public async Task<LoginResponse?> LoginAsync(LoginRequest request)
    {
        var username = (request.Username ?? "").Trim();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username && u.IsActive);
        if (user is null || !VerifyPassword(request.Password ?? "", user.PasswordHash))
            return null;

        // ================== اتصال خودکار معرف ==================
        // کاربر با نقش معرف که هنوز معرفی به او متصل نشده: یک معرف هم‌نام ساخته و متصل می‌شود
        // تا پنل معرف بدون خطا کار کند. مدیر می‌تواند بعداً از ویرایش کاربر، معرف دیگری متصل کند.
        if (user.Role == "Referrer" && user.ReferrerId is not > 0)
        {
            var autoRef = new Db.Referrer { Name = user.Username, IsActive = true };
            _db.Referrers.Add(autoRef);
            await _db.SaveChangesAsync();
            user.ReferrerId = autoRef.Id;
            await _db.SaveChangesAsync();
            Console.WriteLine($"[Auth] معرف «{user.Username}» به‌صورت خودکار ساخته و به کاربر متصل شد.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role)
        };
        if (user.ReferrerId is > 0)
            claims.Add(new Claim("referrerId", user.ReferrerId.Value.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey));
        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtIssuer,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        var display = user.Username;
        if (user.ReferrerId is > 0)
        {
            var r = await _db.Referrers.FindAsync(user.ReferrerId);
            if (r is not null) display = r.Name;
        }

        // ================== دسترسی‌های مؤثر کاربر (RBAC) ==================
        var roleIds = await _db.UserRoles.Where(ur => ur.UserId == user.Id)
            .Select(ur => ur.RoleId).ToListAsync();

        var roleNames = await _db.Roles
            .Where(r => roleIds.Contains(r.Id) && r.IsActive)
            .Select(r => r.Name).ToListAsync();

        List<string> permissions;
        if (roleIds.Count > 0)
        {
            // اجتماع پرمیشن‌های همه‌ی نقش‌های فعال کاربر
            permissions = await _db.RolePermissions
                .Where(rp => roleIds.Contains(rp.RoleId))
                .Join(_db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Module + "." + p.Action)
                .Distinct().ToListAsync();
        }
        else
        {
            // کاربر قدیمی بدون نقش RBAC — دسترسی بر اساس نقش قدیمی (سازگاری عقب‌رو)
            permissions = user.Role switch
            {
                "Admin" => await _db.Permissions.Select(p => p.Module + "." + p.Action).ToListAsync(),
                "Operator" or "Accountant" => await _db.Permissions
                    .Where(p => (p.Module != "SystemUsers" && p.Module != "Settings" && p.Module != "ItRequests")
                                || p.Module == "Dashboards" || p.Module == "ReportPages"
                                || (p.Module == "ItRequests" && (p.Action == "Create" || p.Action == "ViewDepartment"))
                                || (p.Module == "LeaveRequests" && p.Action == "Request")
                                || (p.Module == "Attendance" && p.Action == "SelfCheckin"))
                    .Select(p => p.Module + "." + p.Action).ToListAsync(),
                _ => await _db.Permissions.Where(p => p.Module == "ReferrerPanel"
                                                      || (p.Module == "LeaveRequests" && p.Action == "Request")
                                                      || (p.Module == "Attendance" && p.Action == "SelfCheckin"))
                    .Select(p => p.Module + "." + p.Action).ToListAsync()
            };
        }

        return new LoginResponse
        {
            UserId = user.Id,
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            Username = user.Username,
            Role = user.Role,
            ReferrerId = user.ReferrerId,
            DisplayName = display,
            RoleNames = roleNames,
            Permissions = permissions
        };
    }

    // ---------------- مدیریت کاربران ----------------

    public async Task<List<UserDto>> GetUsersAsync()
    {
        var users = await _db.Users.OrderBy(u => u.Username).ToListAsync();
        var refNames = await _db.Referrers.ToDictionaryAsync(r => r.Id, r => r.Name);
        // نقش‌های RBAC هر کاربر لاگین
        var roleNames = await _db.Roles.ToDictionaryAsync(r => r.Id, r => r.Name);
        var userRoles = await _db.UserRoles.ToListAsync();
        return users.Select(u => new UserDto
        {
            Id = u.Id,
            Username = u.Username,
            Role = u.Role,
            RoleIds = userRoles.Where(ur => ur.UserId == u.Id).Select(ur => ur.RoleId).ToList(),
            RoleNames = userRoles.Where(ur => ur.UserId == u.Id)
                .Select(ur => roleNames.GetValueOrDefault(ur.RoleId, ""))
                .Where(n => n != "").ToList(),
            ReferrerId = u.ReferrerId,
            ReferrerName = u.ReferrerId.HasValue && refNames.TryGetValue(u.ReferrerId.Value, out var n) ? n : null,
            FirstName = u.FirstName,
            LastName = u.LastName,
            Mobile = u.Mobile,
            BaleChatId = u.BaleChatId,
            EitaaChatId = u.EitaaChatId,
            IsActive = u.IsActive,
            CreatedAt = u.CreatedAt
        }).ToList();
    }

    /// <summary>نگاشت نقش‌های RBAC به نقش قدیمی (برای احراز هویت/JWT).</summary>
    private static string DeriveLegacyRole(List<string> rbacRoleNames, string fallback)
    {
        if (rbacRoleNames.Any(n => n.Equals("Admin", StringComparison.OrdinalIgnoreCase) || n.Contains("مدیر") || n.Contains("ادمین")))
            return "Admin";
        if (rbacRoleNames.Any(n => n.Equals("Referrer", StringComparison.OrdinalIgnoreCase) || n.Contains("معرف")))
            return "Referrer";
        if (rbacRoleNames.Count > 0)
            return "Operator"; // سایر نقش‌ها (اپراتور، حسابدار و...) در سطح ورود، اپراتور محسوب می‌شوند
        return fallback;
    }

    public async Task<UserDto> SaveUserAsync(UserDto dto, bool callerIsAdmin = true)
    {
        var username = (dto.Username ?? "").Trim();
        if (string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException("نام کاربری را وارد کنید.");

        // ================== نقش‌های RBAC (انتخاب از بخش نقش‌ها و دسترسی‌ها) ==================
        // نقش قدیمی (Admin/Operator/Referrer) به‌صورت خودکار از نقش‌های انتخابی مشتق می‌شود
        List<string>? rbacNames = null;
        if (dto.RoleIds is { Count: > 0 } rids)
        {
            rbacNames = await _db.Roles.Where(r => rids.Contains(r.Id)).Select(r => r.Name).ToListAsync();
            dto.Role = DeriveLegacyRole(rbacNames, dto.Role);
        }

        if (dto.Role is not ("Admin" or "Operator" or "Referrer"))
            throw new InvalidOperationException("نقش کاربر نامعتبر است.");
        // معرف مرتبط اختیاری است — کاربر معرف می‌تواند بدون معرف ثبت شود
        // و بعداً از ویرایش کاربر، معرف به او متصل شود.

        // اپراتور: هیچ عملیاتی مرتبط با نقش ادمین مجاز نیست
        if (!callerIsAdmin && dto.Role == "Admin")
            throw new InvalidOperationException("اپراتور نمی‌تواند کاربر با نقش «مدیر» ایجاد کند یا نقش کاربری را به مدیر تغییر دهد.");

        var dup = await _db.Users.AnyAsync(u => u.Username == username && u.Id != dto.Id);
        if (dup) throw new InvalidOperationException("این نام کاربری قبلاً ثبت شده است.");

        Db.User entity;
        if (dto.Id == 0)
        {
            if (string.IsNullOrWhiteSpace(dto.Password))
                throw new InvalidOperationException("رمز عبور را وارد کنید.");
            entity = new Db.User { CreatedAt = DateTime.Now };
            _db.Users.Add(entity);
        }
        else
        {
            entity = await _db.Users.FindAsync(dto.Id)
                ?? throw new InvalidOperationException("کاربر یافت نشد.");

            // اپراتور نمی‌تواند کاربر ادمین موجود را ویرایش کند (نه نقش، نه رمز، نه وضعیت)
            if (!callerIsAdmin && entity.Role == "Admin")
                throw new InvalidOperationException("اپراتور اجازه ویرایش کاربران «مدیر» را ندارد.");
        }

        entity.Username = username;
        entity.Role = dto.Role;
        entity.FirstName = string.IsNullOrWhiteSpace(dto.FirstName) ? null : dto.FirstName.Trim();
        entity.LastName = string.IsNullOrWhiteSpace(dto.LastName) ? null : dto.LastName.Trim();
        entity.Mobile = string.IsNullOrWhiteSpace(dto.Mobile) ? null : dto.Mobile.Trim();
        entity.BaleChatId = string.IsNullOrWhiteSpace(dto.BaleChatId) ? null : dto.BaleChatId.Trim();
        entity.EitaaChatId = string.IsNullOrWhiteSpace(dto.EitaaChatId) ? null : dto.EitaaChatId.Trim();
        entity.ReferrerId = dto.Role == "Referrer" ? dto.ReferrerId : null;
        entity.IsActive = dto.IsActive;
        if (!string.IsNullOrWhiteSpace(dto.Password))
        {
            if (dto.Password.Length < 4)
                throw new InvalidOperationException("رمز عبور باید حداقل ۴ کاراکتر باشد.");
            entity.PasswordHash = HashPassword(dto.Password);
        }

        await _db.SaveChangesAsync();

        // ================== ذخیره نقش‌های RBAC کاربر ==================
        if (dto.RoleIds != null)
        {
            var oldRoles = await _db.UserRoles.Where(ur => ur.UserId == entity.Id).ToListAsync();
            _db.UserRoles.RemoveRange(oldRoles);
            foreach (var rid in dto.RoleIds.Distinct())
                _db.UserRoles.Add(new UserRole { UserId = entity.Id, RoleId = rid });
            await _db.SaveChangesAsync();
        }

        return (await GetUsersAsync()).First(u => u.Id == entity.Id);
    }

    public async Task DeleteUserAsync(int id, int currentUserId, bool callerIsAdmin = true)
    {
        if (id == currentUserId)
            throw new InvalidOperationException("نمی‌توانید حساب کاربری خودتان را حذف کنید.");
        var u = await _db.Users.FindAsync(id);
        if (u is null) return;

        // اپراتور نمی‌تواند کاربر ادمین را حذف کند
        if (!callerIsAdmin && u.Role == "Admin")
            throw new InvalidOperationException("اپراتور اجازه حذف کاربران «مدیر» را ندارد.");
        var adminCount = await _db.Users.CountAsync(x => x.Role == "Admin" && x.IsActive);
        if (u.Role == "Admin" && adminCount <= 1)
            throw new InvalidOperationException("حداقل یک مدیر فعال باید باقی بماند.");
        // حذف نقش‌های RBAC کاربر (جلوگیری از رکورد یتیم)
        var uroles = await _db.UserRoles.Where(ur => ur.UserId == u.Id).ToListAsync();
        _db.UserRoles.RemoveRange(uroles);
        _db.Users.Remove(u);
        await _db.SaveChangesAsync();
    }

    public async Task ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        var u = await _db.Users.FindAsync(userId)
            ?? throw new InvalidOperationException("کاربر یافت نشد.");
        if (!VerifyPassword(currentPassword ?? "", u.PasswordHash))
            throw new InvalidOperationException("رمز عبور فعلی اشتباه است.");
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 4)
            throw new InvalidOperationException("رمز عبور جدید باید حداقل ۴ کاراکتر باشد.");
        u.PasswordHash = HashPassword(newPassword);
        await _db.SaveChangesAsync();
    }

    // ---------------- داشبورد معرف ----------------

    public async Task<ReferrerDashboard> GetReferrerDashboardAsync(int referrerId)
    {
        var referrer = await _db.Referrers.FindAsync(referrerId)
            ?? throw new InvalidOperationException("معرف یافت نشد.");

        var wallets = await _inventory.GetReferrerWalletsAsync(null, "name", false);
        var wallet = wallets.FirstOrDefault(w => w.Id == referrerId);

        var txns = await _db.Transactions
            .Where(t => t.ReferrerId == referrerId && t.Type == TransactionType.Sale)
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .ToListAsync();

        var partyNames = await _db.Parties.ToDictionaryAsync(p => p.Id, p => p.Name);

        // پورسانت ماه جاری شمسی + آخرین فروش‌ها
        var (jy, jm, _) = PersianDate.FromGregorian(DateTime.Now);
        var monthStart = PersianDate.ToGregorian(jy, jm, 1);

        decimal monthCommission = 0;
        var recent = new List<ReferrerSaleRow>();
        foreach (var t in txns)
        {
            var order = await _inventory.GetOrderAsync(t.Id);
            var commission = order?.CommissionAmount ?? 0;
            if (t.Date >= monthStart) monthCommission += commission;
            if (recent.Count < 8)
            {
                recent.Add(new ReferrerSaleRow
                {
                    Number = t.Number,
                    Date = t.Date,
                    CustomerName = t.PartyId.HasValue && partyNames.TryGetValue(t.PartyId.Value, out var pn) ? pn : null,
                    Amount = t.Amount,
                    Commission = commission
                });
            }
        }

        var payments = await _inventory.GetReferrerPaymentsAsync(referrerId);

        return new ReferrerDashboard
        {
            ReferrerName = referrer.Name,
            Phone = referrer.Phone,
            CanViewProducts = referrer.CanViewProducts,
            CompanyName = referrer.CompanyName,
            GoodsCommissionPercent = referrer.GoodsCommissionPercent,
            ServiceCommissionPercent = referrer.ServiceCommissionPercent,
            OrderCount = txns.Count,
            TotalSales = txns.Sum(t => t.Amount),
            TotalCommission = wallet?.TotalCommission ?? 0,
            TotalPaid = wallet?.TotalPaid ?? 0,
            WalletBalance = wallet?.WalletBalance ?? 0,
            MonthCommission = monthCommission,
            RecentSales = recent,
            RecentPayments = payments.Take(8).ToList()
        };
    }
}
