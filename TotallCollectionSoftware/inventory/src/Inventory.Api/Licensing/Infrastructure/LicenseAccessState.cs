using Inventory.Licensing.Abstractions;
using Inventory.Licensing.Entitlement;

namespace Inventory.Api.Licensing.Infrastructure;

/// <summary>
/// یک‌جا و مشترک: «آیا الان حالت فقط‌خواندنی است؟»
///
/// <para>
/// چرا مشترک؟ چون <see cref="LicenseGateMiddleware"/> (HTTP) و
/// <see cref="HubLicenseFilter"/> (SignalR) باید <b>دقیقاً یک</b> جواب بدهند؛ اگر دو
/// نسخهٔ جدا داشته باشند، روزی یکی عوض می‌شود و دیگری جا می‌ماند — و آن روز، راه دور
/// زدن قاعده پیدا می‌شود (مثلاً از کانال هاب کاری انجام می‌شود که HTTP نمی‌گذارد).
/// </para>
///
/// <para>
/// ⚠️ <b>مهلت «نصب موجود» حذف شد</b> (بازبینی چهارم): هیچ نصبی به خودش مهلت نمی‌دهد.
/// جایگزینش «لایسنس پل» امضاشده است: یک Subscription کوتاه که ما صادر می‌کنیم و همهٔ
/// ماژول‌ها را باز می‌کند. تفاوت بنیادی: آن مهلت، <b>امضای ما</b> را داشت؛ این یکی
/// تصمیم خودِ نرم‌افزار روی ماشین مشتری بود.
/// </para>
/// </summary>
public static class LicenseAccessState
{
    /// <summary>وضعیت جاری؛ خطای خواندن = <c>Invalid</c> (محافظه‌کارانه).</summary>
    public static LicenseOverallStatus Resolve(ILicenseService licenses, ILogger? log = null)
    {
        try
        {
            return licenses.GetOverallStatus();
        }
        catch (Exception ex)
        {
            log?.LogError(ex, "[License] خواندن وضعیت لایسنس ناموفق بود؛ «Invalid» فرض می‌شود.");
            return LicenseOverallStatus.Invalid;
        }
    }

    /// <summary>
    /// آیا نوشتن باید بسته باشد؟ (Missing یا Invalid، یا ساعت بیش از ۲۴ ساعت عقب‌تر از لنگر)
    ///
    /// <para>
    /// قفل ساعت (گام ۴-۴) عمداً همین‌جا اعمال می‌شود: HTTP و هاب باید یک جواب بدهند و
    /// «فقط‌خواندنی» یعنی هیچ مسیر نوشتنی باز نیست. این قفل <b>موقت</b> است: به‌محض
    /// درست‌شدن ساعت، <see cref="Services.ClockGuardState.Blocked"/> خودکار false می‌شود.
    /// </para>
    /// </summary>
    public static bool IsReadOnly(LicenseOverallStatus status)
        => status is LicenseOverallStatus.Missing or LicenseOverallStatus.Invalid
           || Services.ClockGuardState.Blocked;

    public static Task<bool> IsReadOnlyAsync(ILicenseService licenses, CancellationToken cancellationToken = default)
        => Task.FromResult(IsReadOnly(Resolve(licenses)));
}
