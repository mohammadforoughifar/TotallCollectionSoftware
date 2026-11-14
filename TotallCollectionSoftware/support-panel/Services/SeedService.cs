using System.Text.Json;
using LicenseSigner.Data;
using Microsoft.EntityFrameworkCore;

namespace LicenseSigner.Services;

/// <summary>
/// آماده‌سازی اولین اجرا: کاربر مدیر (گذرواژه فقط روی کنسول)، پیش‌تنظیم‌های بسته و قالب ایمیل.
/// </summary>
public sealed class SeedService(SupportDbContext db, SignerOptions options, ILogger<SeedService> log)
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

        await SeedPresetsAsync(ct);

        if (!await db.MailTemplates.AnyAsync(ct))
        {
            db.MailTemplates.Add(new MailTemplate
            {
                Key = "delivery-template",
                Subject = "لایسنس نرم‌افزار — سریال {{Serial}}",
                Body = "با سلام،\nفایل لایسنس پیوست است. شناسهٔ نصب: {{InstallationId}}\n" +
                       "برای فعال‌سازی، فایل را از بخش «لایسنس» نرم‌افزار بارگذاری کنید.",
                IsActive = true,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>پیش‌تنظیم‌ها از فایل کنار LicenseTool خوانده می‌شوند (منبع واحد).</summary>
    private async Task SeedPresetsAsync(CancellationToken ct)
    {
        if (await db.Presets.AnyAsync(ct)) return;

        var candidates = new List<string>
        {
            options.PresetsPath,
            Path.Combine(AppContext.BaseDirectory, "presets.json"),
            Path.Combine(options.EffectiveWorkFolder(), "presets.json")
        };

        var path = candidates.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));
        if (path is null)
        {
            log.LogWarning("presets.json پیدا نشد؛ بستهٔ آماده‌ای پیش‌بارگذاری نشد.");
            return;
        }

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, ct));
        if (!doc.RootElement.TryGetProperty("presets", out var presets)) return;

        foreach (var preset in presets.EnumerateObject())
        {
            var title = preset.Value.TryGetProperty("title", out var t) ? t.GetString() : preset.Name;
            db.Presets.Add(new Preset
            {
                Code = preset.Name,
                Title = title ?? preset.Name,
                Json = preset.Value.GetRawText(),
                IsActive = true
            });
        }

        await db.SaveChangesAsync(ct);
        log.LogInformation("پیش‌تنظیم‌ها از {Path} پیش‌بارگذاری شد", path);
    }
}
