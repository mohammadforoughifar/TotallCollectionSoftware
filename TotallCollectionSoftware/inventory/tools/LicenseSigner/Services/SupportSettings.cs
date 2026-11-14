using System.Text.Json;
using LicenseSigner.Data;
using Microsoft.EntityFrameworkCore;

namespace LicenseSigner.Services;

/// <summary>تنظیم SMTP — رمز عمداً داخل این کلاس نیست.</summary>
public sealed record SmtpSettings(string Host, int Port, string User, string From, string FromName, bool UseStartTls,
    int TimeoutSeconds)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From);
}

/// <summary>قالب ایمیل تحویل.</summary>
public sealed record MailTemplateValue(string Subject, string Body);

/// <summary>
/// تنظیمات پنل در جدول <see cref="Setting"/>. <b>رمز SMTP در DB ذخیره نمی‌شود</b>؛ فقط
/// موجود بودنش از متغیر محیطی گزارش می‌شود.
/// </summary>
public sealed class SupportSettingsService(SupportDbContext db, SignerOptions options)
{
    private const string SmtpKey = "smtp";
    private const string TemplateKey = "delivery-template";

    public bool SmtpPasswordAvailable =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(options.SmtpPasswordEnvironmentVariable));

    public async Task<SmtpSettings> GetSmtpAsync(CancellationToken ct = default)
    {
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == SmtpKey, ct);
        if (row is null || string.IsNullOrWhiteSpace(row.Value))
            return new SmtpSettings("", 587, "", "", "پشتیبانی لایسنس", true, 30);

        try
        {
            var value = JsonSerializer.Deserialize<SmtpSettings>(row.Value);
            return value ?? new SmtpSettings("", 587, "", "", "پشتیبانی لایسنس", true, 30);
        }
        catch (JsonException)
        {
            return new SmtpSettings("", 587, "", "", "پشتیبانی لایسنس", true, 30);
        }
    }

    public async Task SaveSmtpAsync(SmtpSettings settings, SupportUser actor, AuditChainService audit,
        CancellationToken ct = default)
    {
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == SmtpKey, ct);
        var json = JsonSerializer.Serialize(settings);
        if (row is null) db.Settings.Add(new Setting { Key = SmtpKey, Value = json, UpdatedAt = DateTime.UtcNow });
        else
        {
            row.Value = json;
            row.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(actor.UserName, "SETTINGS_SMTP", "Setting", SmtpKey,
            JsonSerializer.Serialize(new { settings.Host, settings.Port, settings.From }), actor.Id, ct);
    }

    public async Task<MailTemplateValue> GetTemplateAsync(CancellationToken ct = default)
    {
        var row = await db.MailTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Key == TemplateKey, ct);
        return row is null
            ? new MailTemplateValue("لایسنس نرم‌افزار — سریال {{Serial}}",
                "با سلام،\nفایل لایسنس پیوست است. شناسهٔ نصب: {{InstallationId}}\nبرای فعال‌سازی، فایل را از بخش «لایسنس» نرم‌افزار بارگذاری کنید.")
            : new MailTemplateValue(row.Subject, row.Body);
    }

    public async Task SaveTemplateAsync(string subject, string body, SupportUser actor, AuditChainService audit,
        CancellationToken ct = default)
    {
        var row = await db.MailTemplates.FirstOrDefaultAsync(t => t.Key == TemplateKey, ct);
        if (row is null)
            db.MailTemplates.Add(new MailTemplate
            {
                Key = TemplateKey, Subject = subject, Body = body, UpdatedAt = DateTime.UtcNow
            });
        else
        {
            row.Subject = subject;
            row.Body = body;
            row.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(actor.UserName, "SETTINGS_TEMPLATE", "MailTemplate", TemplateKey, null, actor.Id, ct);
    }
}
