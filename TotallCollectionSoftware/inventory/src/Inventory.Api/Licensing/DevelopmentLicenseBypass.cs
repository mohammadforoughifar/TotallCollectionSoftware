using Inventory.Licensing.Entitlement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Inventory.Api.Licensing;

/// <summary>
/// «دور زدن لایسنس در محیط توسعه» — تصمیمِ میزبان، نه موتور.
///
/// <para>
/// <b>قاعده (تنها راه فعال شدن):</b> <c>IHostEnvironment.IsDevelopment()</c> باشد و کلید
/// <c>Licensing:DevelopmentBypass</c> در تنظیمات صریحاً <c>false</c> نباشد (پیش‌فرض: روشن).
/// </para>
///
/// <para>
/// <b>چرا کلید تنظیمات هم لازم است؟</b> چون گاهی خودِ ما باید «مسیر واقعی لایسنس» را در
/// Development تست کنیم (آپلود سند، حالت فقط‌خواندنی، انقضا). آن روز کافی است
/// <c>"Licensing": { "DevelopmentBypass": false }</c> در <c>appsettings.Development.json</c>
/// باشد.
/// </para>
///
/// <para>
/// <b>چرا Production هرگز دور زده نمی‌شود؟</b> سه لایه:
/// <list type="number">
///   <item>شرط <c>IsDevelopment()</c> در همین کلاس — مستقل از تنظیمات.</item>
///   <item>اگر کسی کلید را در Production روی <c>true</c> بگذارد، در لاگ <b>خطا</b> ثبت می‌شود
///         و مقدار <b>نادیده</b> گرفته می‌شود (حتی اگر فایل تنظیمات مشتری دست‌کاری شود).</item>
///   <item>وضعیت ساختگی با کد <see cref="DevelopmentEntitlement.IssueCode"/> در صفحهٔ وضعیت و
///         لاگ علامت‌گذاری می‌شود؛ پس پنهانی نیست.</item>
/// </list>
/// </para>
/// </summary>
public sealed class DevelopmentLicenseBypass
{
    /// <summary>کلید تنظیمات.</summary>
    public const string ConfigKey = "Licensing:DevelopmentBypass";

    private readonly string _environmentName;

    private DevelopmentLicenseBypass(bool isActive, string environmentName, string? inactiveReason)
    {
        IsActive = isActive;
        _environmentName = environmentName;
        InactiveReason = inactiveReason;
    }

    /// <summary>آیا بررسی لایسنس باید نادیده گرفته شود؟</summary>
    public bool IsActive { get; }

    /// <summary>اگر غیرفعال است، چرا (برای لاگ و تست).</summary>
    public string? InactiveReason { get; }

    /// <summary>نام محیط، برای لاگ.</summary>
    public string EnvironmentName => _environmentName;

    /// <summary>تصمیم را از محیط و تنظیمات بگیر.</summary>
    public static DevelopmentLicenseBypass Evaluate(
        IHostEnvironment environment, IConfiguration configuration, ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);

        var envName = environment.EnvironmentName ?? "(unknown)";
        var raw = configuration[ConfigKey];

        bool? flag = null;
        if (!string.IsNullOrWhiteSpace(raw) && bool.TryParse(raw.Trim(), out var parsed)) flag = parsed;
        else if (!string.IsNullOrWhiteSpace(raw))
            log?.LogWarning("[License] مقدار {Key}=\"{Raw}\" قابل خواندن نبود؛ پیش‌فرض محیط استفاده می‌شود.",
                ConfigKey, raw);

        if (!environment.IsDevelopment())
        {
            if (flag == true)
                log?.LogError(
                    "[License] ⛔ {Key}=true در محیط «{Env}» نادیده گرفته شد؛ دور زدن لایسنس فقط در Development کار می‌کند " +
                    "و در Production هیچ اثری ندارد.", ConfigKey, envName);

            return new DevelopmentLicenseBypass(false, envName,
                flag == true ? "کلید فقط در محیط Development اثر دارد." : null);
        }

        if (flag == false)
            return new DevelopmentLicenseBypass(false, envName, "کلید Licensing:DevelopmentBypass=false است.");

        return new DevelopmentLicenseBypass(true, envName, null);
    }

    /// <summary>وضعیت کامل ساختگی برای محیط توسعه.</summary>
    public EffectiveEntitlement Build() => DevelopmentEntitlement.Build();

    /// <summary>پیام هشدار یکسان برای لاگ استارتاپ و کش.</summary>
    public string WarningMessage =>
        $"[License] ⚠️ حالت توسعه: بررسی لایسنس غیرفعال است (محیط={_environmentName}). " +
        "همهٔ ماژول‌ها باز، بدون سقف سهمیه و بدون سقف صندلی. این وضعیت در Production وجود ندارد.";
}
