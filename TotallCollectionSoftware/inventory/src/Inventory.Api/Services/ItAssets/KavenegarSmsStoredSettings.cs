using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.ItAssets;

/// <summary>
/// خواندن تنظیمات پیامک از جدول تنظیمات برنامه، به همان روشی که توکن «سفیر بله» خوانده
/// می‌شود. هدف این است که کلید و سوئیچ ارسال با هر publish از بین نروند.
/// </summary>
public static class KavenegarSmsStoredSettings
{
    /// <summary>
    /// تنظیمات ذخیره‌شده در دیتابیس، یا <c>null</c> اگر هنوز کلیدی ذخیره نشده باشد.
    /// تا وقتی کلید خالی است، فراخوان باید مقدارهای فایل را نگه دارد؛ بنابراین نصب‌های
    /// فعلی با این تغییر قطع نمی‌شوند.
    ///
    /// وقتی کلید ذخیره شده باشد، هر سه مقدار <b>یکجا</b> جای فایل را می‌گیرند تا ترکیبی از
    /// دو منبع ساخته نشود؛ مثلاً کلید حساب تازه با سوئیچ قدیمی. نام الگوها از این قاعده
    /// مستثناست و همیشه از فایل می‌آید، چون محرمانه نیست و با publish از دست نمی‌رود.
    /// </summary>
    public static KavenegarSmsOptions? Read(AppDbContext db, KavenegarSmsOptions file)
    {
        try
        {
            var row = db.AppSettings.AsNoTracking()
                .Select(s => new { s.KavenegarApiKey, s.KavenegarSender, s.KavenegarSdkSendEnabled })
                .FirstOrDefault();
            if (row is null || string.IsNullOrWhiteSpace(row.KavenegarApiKey)) return null;

            return new KavenegarSmsOptions
            {
                ApiKey = row.KavenegarApiKey.Trim(),
                Sender = (row.KavenegarSender ?? "").Trim(),
                SdkSendEnabled = row.KavenegarSdkSendEnabled,
                TimeoutSeconds = file.TimeoutSeconds,
                RepairAcceptTemplate = file.RepairAcceptTemplate,
                RepairReadyTemplate = file.RepairReadyTemplate,
                RepairDeliveredTemplate = file.RepairDeliveredTemplate,
                WorkOrderRegistrationTemplate = file.WorkOrderRegistrationTemplate,
                WorkOrderReferralTemplate = file.WorkOrderReferralTemplate,
                WorkOrderReplyTemplate = file.WorkOrderReplyTemplate
            };
        }
        catch
        {
            // دیتابیسی که هنوز ستون‌های تازه را ندارد (پیش از اجرای AppSettingsSchemaV1)
            // نباید ارسال پیامک را از کار بیندازد؛ در این حالت مقدارهای فایل معتبرند.
            return null;
        }
    }
}
