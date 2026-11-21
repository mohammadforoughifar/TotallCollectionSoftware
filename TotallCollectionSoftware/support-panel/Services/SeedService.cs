using SupportPanel.Data;
using Microsoft.EntityFrameworkCore;

namespace SupportPanel.Services;

/// <summary>
/// آماده‌سازی اولین اجرا: مایگریشن، کاربر مدیر (گذرواژه فقط روی کنسول) و قالب ایمیل.
/// موتور لایسنس حذف شده؛ پس هیچ پیش‌تنظیم/درخواست نمایشی ساخته نمی‌شود.
/// </summary>
public sealed class SeedService(SupportDbContext db, PanelOptions options, ILogger<SeedService> log)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        if (!await db.Users.AnyAsync(ct))
        {
            var password = SupportAuthService.GeneratePassword();
            var admin = new SupportUser
            {
                UserName = "admin",
                DisplayName = "مدیر پنل",
                Role = SupportRole.Admin,
                PasswordHash = SupportAuthService.HashPassword(password),
                PasswordIterations = SupportAuthService.Iterations,
                MustChangePassword = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            db.Users.Add(admin);
            await db.SaveChangesAsync(ct);

            // گذرواژهٔ آغازین فقط روی کنسول — نه در لاگ، نه در DB.
            Console.WriteLine();
            Console.WriteLine("==================================================================");
            Console.WriteLine("  اولین اجرا — کاربر مدیر ساخته شد:");
            Console.WriteLine("    نام کاربری : admin");
            Console.WriteLine($"    گذرواژه    : {password}");
            Console.WriteLine("  این گذرواژه فقط همین یک‌بار نمایش داده می‌شود و در اولین ورود");
            Console.WriteLine("  تعویض آن اجباری است. آن را در جایی امن یادداشت کنید.");
            Console.WriteLine("==================================================================");
            Console.WriteLine();
            log.LogWarning("کاربر مدیر اولیه ساخته شد و گذرواژهٔ آغازین روی کنسول چاپ شد (در لاگ ثبت نمی‌شود).");
        }

        // دادهٔ نمایشی فقط در حالت پیش‌نمایش — تا فهرست مشتریان خالی نباشد.
        if (options.PreviewMode) await SeedDemoAsync(ct);

        if (!await db.MailTemplates.AnyAsync(ct))
        {
            db.MailTemplates.Add(new MailTemplate
            {
                Key = "support-template",
                Subject = "پاسخ پشتیبانی — {{CustomerName}}",
                Body = "با سلام،\nدرخواست شما بررسی شد.\n\nبا احترام، پشتیبانی نرم‌افزار",
                IsActive = true,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>سه مشتری نمایشی برای پیش‌نمایش (هیچ داده‌ای جز مشتری ساخته نمی‌شود).</summary>
    private async Task SeedDemoAsync(CancellationToken ct)
    {
        if (await db.Customers.AnyAsync(c => c.Code == "DEMO-01", ct)) return;

        var now = DateTime.UtcNow;
        db.Customers.AddRange(
            new Customer { Code = "DEMO-01", Name = "شرکت نمونهٔ آلفا", Phone = "02144000001",
                Email = "alpha@example.invalid", Address = "تهران، خیابان نمونه، پلاک ۱", CreatedAt = now.AddDays(-40) },
            new Customer { Code = "DEMO-02", Name = "گروه صنعتی بتا", Phone = "02144000002",
                Email = "beta@example.invalid", Address = "اصفهان، بلوار صنعت، پلاک ۲", CreatedAt = now.AddDays(-22) },
            new Customer { Code = "DEMO-03", Name = "بازرگانی گاما", Phone = "02144000003",
                Email = "gamma@example.invalid", CreatedAt = now.AddDays(-9) });

        await db.SaveChangesAsync(ct);
        log.LogInformation("دادهٔ نمایشی پیش‌نمایش ساخته شد (۳ مشتری).");
    }
}
