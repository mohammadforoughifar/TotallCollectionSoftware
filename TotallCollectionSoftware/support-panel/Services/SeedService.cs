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

        // دادهٔ نمایشی فقط در حالت پیش‌نمایش — تا داشبورد و صف خالی نباشند.
        if (options.PreviewMode) await SeedDemoAsync(ct);

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

    /// <summary>
    /// دادهٔ نمایشی برای پیش‌نمایش: سه مشتری و چهار درخواست در وضعیت‌های مختلف.
    /// هیچ لایسنسی امضا نمی‌کند و هیچ کلیدی نمی‌سازد؛ فقط برای پر بودن صفحه‌هاست.
    /// </summary>
    private async Task SeedDemoAsync(CancellationToken ct)
    {
        if (await db.Customers.AnyAsync(c => c.Code == "DEMO-01", ct)) return;

        var now = DateTime.UtcNow;
        var alpha = new Customer { Code = "DEMO-01", Name = "شرکت نمونهٔ آلفا", Phone = "02144000001",
            Email = "alpha@example.invalid", Address = "تهران، خیابان نمونه، پلاک ۱", CreatedAt = now.AddDays(-40) };
        var beta = new Customer { Code = "DEMO-02", Name = "گروه صنعتی بتا", Phone = "02144000002",
            Email = "beta@example.invalid", Address = "اصفهان، بلوار صنعت، پلاک ۲", CreatedAt = now.AddDays(-22) };
        var gamma = new Customer { Code = "DEMO-03", Name = "بازرگانی گاما", Phone = "02144000003",
            Email = "gamma@example.invalid", CreatedAt = now.AddDays(-9) };
        db.Customers.AddRange(alpha, beta, gamma);
        await db.SaveChangesAsync(ct);

        db.Requests.AddRange(
            DemoRequest(alpha, "REQ-DEMO-01", RequestType.NewActivation, RequestStatus.New, now.AddDays(-2),
                "demo-install-alpha-01", "درخواست فعال‌سازی نسخهٔ کامل"),
            DemoRequest(beta, "REQ-DEMO-02", RequestType.Renewal, RequestStatus.Reviewing, now.AddDays(-5),
                "demo-install-beta-01", "تمدید سالانه — فاکتور پرداخت شده"),
            DemoRequest(beta, "REQ-DEMO-03", RequestType.AddModule, RequestStatus.NeedsInfo, now.AddDays(-8),
                "demo-install-beta-02", "افزودن ماژول انبار — منتظر تأیید مشتری"),
            DemoRequest(gamma, "REQ-DEMO-04", RequestType.Trial, RequestStatus.Approved, now.AddDays(-12),
                "demo-install-gamma-01", "نسخهٔ آزمایشی ۳۰ روزه"));

        await db.SaveChangesAsync(ct);
        log.LogInformation("دادهٔ نمایشی پیش‌نمایش ساخته شد (۳ مشتری، ۴ درخواست).");
    }

    private static LicenseRequest DemoRequest(Customer customer, string no, RequestType type, RequestStatus status,
        DateTime at, string installationId, string note)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            requestVersion = 2,
            requestType = type.ToString(),
            installationId,
            customerName = customer.Name,
            contactName = "کاربر نمونه",
            contactPhone = customer.Phone,
            contactEmail = customer.Email,
            customerNote = note,
            requestedAt = at.ToString("yyyy-MM-ddTHH:mm:ssZ")
        });

        return new LicenseRequest
        {
            RequestNo = no,
            CustomerId = customer.Id,
            InstallationId = installationId,
            Type = type,
            Status = status,
            Channel = RequestChannel.Paste,
            RequestVersion = 2,
            PayloadJson = payload,
            PayloadSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(payload))).ToLowerInvariant(),
            ContactName = "کاربر نمونه",
            ContactPhone = customer.Phone,
            ContactEmail = customer.Email,
            CustomerNote = note,
            Deployment = "OnPrem",
            MachineName = "DEMO-PC",
            EngineVersion = "1.0.0",
            RequestedAtUtc = at,
            CreatedAt = at,
            Events = { new RequestEvent { Kind = "CREATED", ToStatus = status.ToString(),
                Text = "ثبت نمایشی برای پیش‌نمایش", ByName = "demo", At = at } }
        };
    }
}
