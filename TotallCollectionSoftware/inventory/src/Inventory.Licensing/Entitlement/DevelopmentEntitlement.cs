using Inventory.Licensing.Model;
using Inventory.Licensing.Modules;

namespace Inventory.Licensing.Entitlement;

/// <summary>
/// ساخت «وضعیت ساختگیِ کامل» برای <b>محیط توسعه</b>.
///
/// <para>
/// چرا؟ توسعه‌دهنده روی ماشین خودش نه سند امضاشده دارد، نه فایل درخواست، نه کلید خصوصی؛
/// و نباید صبحِ هر روز با «فقط‌خواندنی»، بنر قرمز و ده‌ها خطای لاگ روبه‌رو شود. این کلاس
/// همان Entitlementی را می‌سازد که یک سند معتبر با همهٔ ماژول‌ها تولید می‌کرد:
/// <b>همهٔ ماژول‌ها معتبر، بدون سقف سهمیه، بدون سقف صندلی، بدون هشدار ساعت</b>.
/// </para>
///
/// <para>
/// ⚠️ <b>این کلاس خودش تصمیم نمی‌گیرد که کجا فعال شود.</b> تصمیم «آیا ما در محیط توسعه
/// هستیم؟» در لایهٔ میزبان است (<c>DevelopmentLicenseBypass</c> در Inventory.Api) و آن‌جا
/// فقط با <c>IHostEnvironment.IsDevelopment()</c> روشن می‌شود. دلیل جدا بودن: موتور لایسنس
/// عمداً هیچ وابستگی‌ای به ASP.NET Core ندارد (ابزار صدور و LicenseCheck هم از آن استفاده
/// می‌کنند) و نباید کلمهٔ «Development» را بشناسد.
/// </para>
///
/// <para>
/// برای اینکه در گزارش‌ها گم نشود، یک Issue با کد <see cref="IssueCode"/> و شدت
/// <see cref="LicenseIssueSeverity.Info"/> اضافه می‌شود؛ صفحهٔ وضعیت و لاگ آن را نشان می‌دهند
/// ولی هیچ بنر قرمزی نمی‌سازد.
/// </para>
/// </summary>
public static class DevelopmentEntitlement
{
    /// <summary>کد رخداد/Issue مخصوص حالت توسعه — در UI و لاگ دیده می‌شود.</summary>
    public const string IssueCode = "LICENSE_DEV_BYPASS";

    /// <summary>سریال ساختگی؛ عمداً با پیشوند غیرقابل‌اشتباه.</summary>
    public const string Serial = "DEV-BYPASS";

    /// <summary>شناسهٔ مشتری ساختگی.</summary>
    public const string CustomerId = "DEV-LOCAL";

    /// <summary>نام نمایشی مشتری در صفحهٔ وضعیت.</summary>
    public const string CustomerName = "محیط توسعه (لایسنس دور زده شد)";

    private const string IssueMessage =
        "این نصب در محیط Development اجرا می‌شود؛ بررسی لایسنس عمداً غیرفعال است و همهٔ ماژول‌ها باز هستند. " +
        "برای دیدن رفتار واقعی لایسنس، Licensing:DevelopmentBypass=false را در appsettings.Development.json بگذارید. " +
        "این حالت هرگز در Production اثر ندارد.";

    /// <summary>ساخت وضعیت کاملِ ساختگی.</summary>
    /// <param name="now">زمان محاسبه (پیش‌فرض: اکنون).</param>
    public static EffectiveEntitlement Build(DateTime? now = null)
    {
        var modules = new Dictionary<string, ModuleEntitlement>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in ModuleCodes.All)
        {
            modules[definition.Code] = new ModuleEntitlement
            {
                Module = definition.Code,
                State = ModuleState.Valid,
                IsAlwaysOn = definition.AlwaysOn,
                PrimaryItemId = Serial,
                PrimaryTermType = LicenseTermType.Perpetual.ToString(),
                ItemIds = new[] { Serial }
            };
        }

        return new EffectiveEntitlement
        {
            Status = LicenseOverallStatus.Valid,
            CustomerId = CustomerId,
            CustomerName = CustomerName,
            ComputedAt = now ?? DateTime.Now,

            // هیچ آیتم واقعی‌ای وجود ندارد: سند امضاشده‌ای در کار نیست.
            Items = Array.Empty<ItemEntitlement>(),

            Modules = modules,

            // سقف سهمیه خالی = «بی‌نهایت» (QuotaService برای متریک بدون سقف، بی‌درنگ مجاز می‌دهد).
            Limits = Array.Empty<MetricLimit>(),

            // Mode=None ⇒ هیچ سقف صندلی اعمال نمی‌شود (SeatService با !HasUserLimit عبور می‌کند).
            Seats = new UserSeatPolicy(),

            Issues = new[]
            {
                new LicenseIssue(IssueCode, LicenseIssueSeverity.Info, IssueMessage)
            },

            // آیتم دائمی ساختگی: بررسی نسخه/آپدیت را هم از سر راه برمی‌دارد.
            HasPerpetualItem = true,
            UpdatesUntil = null,

            AppliedSerials = new[] { Serial }
        };
    }
}
