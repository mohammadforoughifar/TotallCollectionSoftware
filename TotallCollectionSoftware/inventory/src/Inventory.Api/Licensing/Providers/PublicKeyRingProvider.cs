using Inventory.Licensing.Crypto;

namespace Inventory.Api.Licensing.Providers;

/// <summary>
/// نگه‌دارندهٔ حلقهٔ کلیدهای عمومی امضا — Singleton.
///
/// <para>
/// دو لایهٔ اعتماد دارد:
/// </para>
/// <list type="number">
/// <item>
/// <b>کلیدهای جاسازی‌شده</b> (manifest داخل اسمبلی): ریشهٔ اعتماد. هیچ کلید خصوصی‌ای در
/// برنامهٔ مشتری نیست و هیچ مسیری برای «افزودن کلید ریشه از بیرون» وجود ندارد؛ وگرنه هر
/// کسی می‌توانست کلید خودش را اضافه کند و لایسنس جعلی بسازد.
/// </item>
/// <item>
/// <b>کلیدهای نمایندگی</b> (فایل‌های <c>*.trust.json</c> در
/// <c>…\License\trusted-keys</c>): برای هر مشتری یک کلید امضای جدا. هر فایل باید امضای
/// یکی از کلیدهای ریشه را داشته باشد، برای <b>همین شناسهٔ نصب</b> صادر شده باشد، منقضی
/// نشده باشد و در <c>revoked.txt</c> نباشد. کلید نمایندگی نمی‌تواند کلید ریشه را بازنویسی
/// کند یا کلید نمایندگی دیگری بسازد (زنجیره یک‌سطحی است).
/// </item>
/// </list>
///
/// <para>
/// <b>چرا این تغییر؟</b> پیش از این، هر مشتری باید با همان کلید جاسازی‌شده لایسنس
/// می‌گرفت؛ نتیجه: دست‌کاری کلید در سورس و انتشار بیلد تازه برای هر مشتری/هر بار.
/// </para>
/// </summary>
public sealed class PublicKeyRingProvider
{
    private readonly ILogger<PublicKeyRingProvider> _log;
    private readonly object _sync = new();
    private readonly Dictionary<string, CachedRing> _cache = new(StringComparer.OrdinalIgnoreCase);

    public PublicKeyRingProvider(ILogger<PublicKeyRingProvider> log, LicensePaths paths, IConfiguration configuration)
    {
        _log = log;
        Ring = PublicKeyRing.FromEmbedded();

        foreach (var error in Ring.LoadErrors)
            _log.LogError("[License] بارگذاری کلیدهای عمومی امضا: {Error}", error);

        if (Ring.KeyIds.Count > 0)
            _log.LogInformation("[License] کلیدهای عمومی امضا (ریشه): {Keys}", string.Join(", ", Ring.KeyIds));

        var configured = configuration["Licensing:TrustedKeysFolder"];
        TrustedKeysFolder = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(paths.Root, "trusted-keys")
            : configured.Trim();

        try
        {
            Directory.CreateDirectory(TrustedKeysFolder);
        }
        catch (Exception ex)
        {
            // پوشهٔ کلید نمایندگی نبودن، برنامه را نمی‌خواباند؛ فقط کلیدهای ریشه کار می‌کنند.
            _log.LogWarning("[License] ساخت پوشهٔ کلیدهای نمایندگی ممکن نشد: {Folder} — {Error}",
                TrustedKeysFolder, ex.Message);
        }

        _log.LogInformation("[License] پوشهٔ کلیدهای نمایندگی: {Folder}", TrustedKeysFolder);
    }

    /// <summary>حلقهٔ ریشه (فقط کلیدهای جاسازی‌شده) — برای ابزار تشخیص و سازگاری.</summary>
    public PublicKeyRing Ring { get; }

    /// <summary>پوشهٔ فایل‌های <c>*.trust.json</c>.</summary>
    public string TrustedKeysFolder { get; }

    /// <summary>
    /// حلقهٔ کامل برای یک نصب: ریشه + کلیدهای نمایندگیِ همان نصب.
    ///
    /// <para>
    /// نتیجه کش می‌شود تا هر بررسی امضا، دیسک را نخواند؛ ولی کش با تغییر فایل‌های پوشه
    /// (افزودن/حذف/تغییر تاریخ) خودکار باطل می‌شود. پس «افزودن کلید تازه» بدون ری‌استارت
    /// هم اثر می‌کند — که برای فعال‌سازی از راه دور لازم است.
    /// </para>
    /// </summary>
    public PublicKeyRing GetRing(string? installationId)
    {
        if (string.IsNullOrWhiteSpace(installationId)) return Ring;

        var stamp = FolderStamp();
        lock (_sync)
        {
            if (_cache.TryGetValue(installationId, out var cached) && cached.Stamp == stamp)
                return cached.Ring;

            var ring = PublicKeyRing.FromEmbeddedAndTrusted(TrustedKeysFolder, installationId);

            foreach (var error in ring.LoadErrors.Where(e => e.Contains("مجوز اعتماد", StringComparison.Ordinal)
                                                             || e.Contains("کلید «", StringComparison.Ordinal)))
                _log.LogWarning("[License] کلید نمایندگی: {Error}", error);

            if (ring.DelegatedKeyIds.Count > 0)
                _log.LogInformation("[License] کلیدهای نمایندگی فعال برای نصب {InstallationId}: {Keys}",
                    installationId, string.Join(", ", ring.DelegatedKeyIds));

            _cache[installationId] = new CachedRing(ring, stamp);
            return ring;
        }
    }

    /// <summary>اثر انگشت وضعیت پوشه — تشخیص افزودن/حذف/تغییر فایل بدون خواندن محتوا.</summary>
    private string FolderStamp()
    {
        try
        {
            if (!Directory.Exists(TrustedKeysFolder)) return "missing";

            var parts = Directory.EnumerateFiles(TrustedKeysFolder)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Select(f =>
                {
                    var info = new FileInfo(f);
                    return $"{info.Name}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
                });

            return string.Join("|", parts);
        }
        catch
        {
            return "error";
        }
    }

    private sealed record CachedRing(PublicKeyRing Ring, string Stamp);
}
