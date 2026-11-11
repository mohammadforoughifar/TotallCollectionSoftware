using Inventory.Licensing.Crypto;

namespace Inventory.Api.Licensing.Providers;

/// <summary>
/// نگه‌دارندهٔ حلقهٔ کلیدهای عمومی امضا — Singleton.
///
/// <para>
/// کلیدها یک‌بار در راه‌اندازی از منابع Embedded خودِ اسمبلی خوانده می‌شوند.
/// هیچ کلید خصوصی‌ای در برنامهٔ مشتری نیست و هیچ مسیری برای «افزودن کلید از بیرون»
/// وجود ندارد؛ وگرنه هر کسی می‌توانست کلید خودش را اضافه کند و لایسنس جعلی بسازد.
/// </para>
/// </summary>
public sealed class PublicKeyRingProvider
{
    private readonly ILogger<PublicKeyRingProvider> _log;

    public PublicKeyRingProvider(ILogger<PublicKeyRingProvider> log)
    {
        _log = log;
        Ring = PublicKeyRing.FromEmbedded();

        foreach (var error in Ring.LoadErrors)
            _log.LogError("[License] بارگذاری کلیدهای عمومی امضا: {Error}", error);

        if (Ring.KeyIds.Count > 0)
            _log.LogInformation("[License] کلیدهای عمومی امضا: {Keys}", string.Join(", ", Ring.KeyIds));
    }

    /// <summary>حلقهٔ کلیدها.</summary>
    public PublicKeyRing Ring { get; }
}
