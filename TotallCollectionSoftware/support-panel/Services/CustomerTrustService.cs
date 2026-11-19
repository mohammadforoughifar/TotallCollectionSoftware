using System.IO.Compression;
using LicenseSigner.Data;
using LicenseSigner.Services;
using System.Text;
using Inventory.Licensing.Crypto;

namespace LicenseSigner;

/// <summary>کلید امضای یک مشتری (ساخته/بازیابی‌شده در گاوصندوق).</summary>
public sealed record CustomerKeyInfo(string KeyId, string Folder, string PrivateKeyPath, string PublicKeyPath,
    string Fingerprint, bool Created);

/// <summary>نتیجهٔ صدور مجوز اعتماد.</summary>
public sealed record TrustIssueResult(bool Ok, string Message, string? TrustPath, CustomerKeyInfo? Key,
    IReadOnlyList<string> Warnings)
{
    public static TrustIssueResult Fail(string message) => new(false, message, null, null, Array.Empty<string>());
}

/// <summary>وضعیت مجوز اعتماد یک درخواست — برای نمایش در صفحهٔ جزئیات.</summary>
public sealed record TrustStatus(bool RootConfigured, string RootKeyId, string? RootKeyPath,
    bool HasTrustForActiveKey, string Message, IReadOnlyList<string> TrustFiles);

/// <summary>
/// کلید امضای «هر مشتری» + صدور «مجوز اعتماد» با کلید ریشه.
///
/// <para>
/// <b>مسئله‌ای که حل می‌کند:</b> تا پیش از این، هر مشتری باید با همان کلیدی لایسنس می‌گرفت که
/// داخل بیلد محصولش جاسازی شده بود؛ یعنی برای مشتری تازه یا کلید گم‌شده باید سورس دست‌کاری و
/// بیلد تازه منتشر می‌شد. حالا کلید هر مشتری همان‌جا در پنل ساخته می‌شود و «مجوز اعتمادِ»
/// امضاشده با کلید ریشه به مشتری داده می‌شود؛ نصب مشتری آن را در
/// <c>…\License\trusted-keys\</c> می‌گذارد و امضای کلید مشتری را می‌پذیرد.
/// </para>
///
/// <para>
/// <b>کلید خصوصی مشتری</b> در گاوصندوق (<c>…\customers\&lt;installationId&gt;\</c>) می‌ماند و
/// هرگز برای مشتری فرستاده نمی‌شود؛ فقط کلید عمومی + مجوز امضاشده می‌رود.
/// </para>
/// </summary>
public sealed class CustomerTrustService(SignerKeyService keys, SignerOptions options,
    ILogger<CustomerTrustService> log)
{
    /// <summary>ریشهٔ گاوصندوق: همان قاعدهٔ کلید پنل (TOTALL_LICENSE_KEYS ← C:\LicensingKeys ← ~).</summary>
    public string VaultRoot => keys.SuggestedFolder();

    /// <summary>شناسهٔ کلید ریشه (از Signer:RootKeyId).</summary>
    public string RootKeyId => string.IsNullOrWhiteSpace(options.RootKeyId) ? "" : options.RootKeyId.Trim();

    /// <summary>مسیر کلید خصوصی ریشه (از Signer:RootPrivateKeyPath).</summary>
    public string RootKeyPath => string.IsNullOrWhiteSpace(options.RootPrivateKeyPath)
        ? ""
        : Path.GetFullPath(options.RootPrivateKeyPath);

    /// <summary>پوشهٔ فایل‌های تحویلی مجوز اعتماد (کنار داده‌های پنل).</summary>
    public string TrustRoot => Path.Combine(options.EffectiveWorkFolder(), "trust");

    /// <summary>پوشهٔ مجوز اعتماد یک نصب.</summary>
    public string TrustFolder(string installationId) =>
        Path.Combine(TrustRoot, SafeSegment(installationId));

    /// <summary>پوشهٔ کلید یک نصب در گاوصندوق.</summary>
    public string CustomerFolder(string installationId) =>
        Path.Combine(VaultRoot, "customers", SafeSegment(installationId));

    /// <summary>شناسهٔ پیشنهادی کلید برای یک نصب: <c>cust-55dfa655</c> (از شناسهٔ نصب).</summary>
    public static string SuggestedCustomerKeyId(string installationId)
    {
        var digits = new string((installationId ?? "").Where(char.IsLetterOrDigit).Take(8).ToArray()).ToLowerInvariant();
        return string.IsNullOrEmpty(digits) ? "cust-key" : "cust-" + digits;
    }

    /// <summary>ساخت (یا بازیابی) کلید امضای یک مشتری در گاوصندوق.</summary>
    public async Task<(CustomerKeyInfo? Key, string Error)> EnsureCustomerKeyAsync(
        string installationId, string? preferredKeyId, string password, SupportUser actor,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(installationId))
            return (null, "شناسهٔ نصب درخواست خالی است.");
        if (string.IsNullOrWhiteSpace(password))
            return (null, "گذرواژهٔ کلید مشتری لازم است (کلید خصوصی رمزدار ذخیره می‌شود).");

        var keyId = (string.IsNullOrWhiteSpace(preferredKeyId)
            ? SuggestedCustomerKeyId(installationId)
            : preferredKeyId!.Trim()).ToLowerInvariant();

        var folder = CustomerFolder(installationId);
        Directory.CreateDirectory(folder);

        var privatePath = Path.Combine(folder, keyId + ".private.pem");
        var publicPath = Path.Combine(folder, keyId + ".pem");

        if (File.Exists(privatePath) && File.Exists(publicPath))
        {
            var fingerprint = PublicKeyRing.Fingerprint(File.ReadAllText(publicPath), "RS256") ?? "";
            return (new CustomerKeyInfo(keyId, folder, privatePath, publicPath, fingerprint, Created: false), "");
        }

        // ساخت از راه همان سرویس کلیدهای پنل: همان قواعد امنیتی (رمزدار، ثبت در DB، فعال‌سازی).
        var created = await keys.CreateAsync(keyId, folder, password, overwrite: false, actor, ct);
        if (!created.Ok)
            return (null, created.Message);

        var fp = PublicKeyRing.Fingerprint(File.ReadAllText(publicPath), "RS256") ?? "";
        return (new CustomerKeyInfo(keyId, folder, privatePath, publicPath, fp, Created: true), "");
    }

    /// <summary>
    /// صدور «مجوز اعتماد» برای یک نصب با کلید ریشه.
    /// </summary>
    /// <param name="validDays">۰ یا منفی ⇒ بدون تاریخ انقضا.</param>
    public async Task<TrustIssueResult> IssueTrustAsync(
        string installationId, CustomerKeyInfo key, string rootPassword, int validDays,
        string? note = null, CancellationToken ct = default)
    {
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(RootKeyId))
            return TrustIssueResult.Fail(
                "شناسهٔ کلید ریشه تنظیم نشده است. در appsettings پنل «Signer:RootKeyId» را بگذارید " +
                "(نمونه: sup-202612) و کلید خصوصی ریشه را با «Signer:RootPrivateKeyPath» وصل کنید.");

        if (string.IsNullOrWhiteSpace(RootKeyPath) || !File.Exists(RootKeyPath))
            return TrustIssueResult.Fail(
                $"کلید خصوصی ریشه پیدا نشد: «{RootKeyPath}». در تنظیمات پنل مسیر «Signer:RootPrivateKeyPath» را " +
                "به فایل کلید ریشه بدهید (بیرون از پوشهٔ برنامه). این ابزار برای ساختش: " +
                "dotnet run --project inventory\\tools\\RootKeyTool -- new --key-id " + RootKeyId + " --out C:\\LicensingKeys");

        string pem;
        try
        {
            pem = await File.ReadAllTextAsync(RootKeyPath, ct);
        }
        catch (Exception ex)
        {
            return TrustIssueResult.Fail($"خواندن کلید ریشه ممکن نشد: {ex.Message}");
        }

        TrustCertificateDocument doc;
        try
        {
            doc = TrustCertificate.Sign(
                keyId: key.KeyId,
                delegatedPublicKeyPem: await File.ReadAllTextAsync(key.PublicKeyPath, ct),
                installationId: installationId,
                rootKeyId: RootKeyId,
                rootPrivateKeyPem: pem,
                issuedAtUtc: DateTime.UtcNow,
                validUntilUtc: validDays > 0 ? DateTime.UtcNow.AddDays(validDays) : null,
                rootPassword: string.IsNullOrEmpty(rootPassword) ? null : rootPassword,
                note: note ?? "کلید امضای مشتری — صادرشده از پنل پشتیبانی");
        }
        catch (Exception ex)
        {
            return TrustIssueResult.Fail(
                "صدور مجوز اعتماد ناموفق بود: " + ex.Message +
                (ex is InvalidOperationException && ex.Message.Contains("گذرواژه")
                    ? " (گذرواژهٔ کلید ریشه را وارد کنید)"
                    : ""));
        }

        // ⛔ خودآزمون پیش از تحویل: با حلقهٔ مستقل، سند باید معتبر باشد.
        var selfRing = PublicKeyRing.FromEntries(new[]
        {
            new PublicKeyEntry { KeyId = RootKeyId, Algorithm = "RS256", PublicKeyPem = PublicKeyOfRoot(pem, rootPassword) }
        });
        if (!TrustCertificate.TryVerify(doc, selfRing.EmbeddedKey, installationId, DateTime.UtcNow,
                out _, out var verifyError))
        {
            return TrustIssueResult.Fail("مجوز صادر شد ولی خودآزمون رد داد: " + verifyError);
        }

        var folder = TrustFolder(installationId);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, TrustCertificate.FileNameFor(key.KeyId));
        await File.WriteAllTextAsync(path, TrustCertificate.ToJson(doc), new UTF8Encoding(false), ct);

        log.LogInformation("[Trust] مجوز اعتماد صادر شد: {KeyId} برای نصب {InstallationId} ⇒ {Path}",
            key.KeyId, installationId, path);

        var expiry = validDays > 0
            ? $"اعتبار تا {DateTime.UtcNow.AddDays(validDays):yyyy-MM-dd}"
            : "بدون تاریخ انقضا";

        return new TrustIssueResult(true,
            $"مجوز اعتماد برای کلید «{key.KeyId}» صادر شد ({expiry}) و با کلید ریشهٔ «{RootKeyId}» امضا شد.",
            path, key, warnings);
    }

    /// <summary>مجوزهای اعتماد موجود برای یک نصب.</summary>
    public IReadOnlyList<string> TrustFiles(string installationId)
    {
        var folder = TrustFolder(installationId);
        if (!Directory.Exists(folder)) return Array.Empty<string>();
        return Directory.EnumerateFiles(folder, "*.trust.json").OrderBy(f => f).ToList();
    }

    /// <summary>
    /// آیا کلید فعال پنل، برای این نصب مجوز اعتمادِ معتبر دارد؟
    /// (همان شرطی که به امضا اجازه می‌دهد، حتی اگر کلید در فهرست جاسازی‌شدهٔ محصول نباشد.)
    /// </summary>
    public bool HasValidTrust(string installationId, string? keyId, string? fingerprint)
    {
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(fingerprint)) return false;

        var folder = TrustFolder(installationId);
        if (!Directory.Exists(folder)) return false;

        foreach (var file in Directory.EnumerateFiles(folder, "*.trust.json"))
        {
            var doc = TrustCertificate.Parse(SafeRead(file) ?? "");
            if (doc is null) continue;
            if (!string.Equals(doc.KeyId, keyId, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(doc.Sha256, fingerprint, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(doc.InstallationId, installationId, StringComparison.OrdinalIgnoreCase)) continue;
            if (!RootIsConfigured()) continue;

            // امضا با کلید عمومی ریشه (از مسیر پیکربندی؛ کلید خصوصی خوانده نمی‌شود).
            var rootPub = SafeReadPublicOfRoot();
            if (rootPub is null) continue;

            var ring = PublicKeyRing.FromEntries(new[]
            {
                new PublicKeyEntry { KeyId = doc.IssuedBy, Algorithm = "RS256", PublicKeyPem = rootPub }
            });
            if (TrustCertificate.TryVerify(doc, ring.EmbeddedKey, installationId, DateTime.UtcNow,
                    out _, out _))
                return true;
        }

        return false;
    }

    /// <summary>وضعیت کوتاه برای نمایش در صفحهٔ درخواست.</summary>
    public TrustStatus Status(string installationId, ActiveKeyInfo active)
    {
        var rootConfigured = RootIsConfigured();
        var has = HasValidTrust(installationId, active.KeyId, active.Fingerprint);
        var files = TrustFiles(installationId);

        var message = !rootConfigured
            ? "کلید ریشه تنظیم نشده است؛ نمی‌توان مجوز اعتماد صادر کرد."
            : has
                ? $"کلید فعال «{active.KeyId}» برای این نصب مجوز اعتماد معتبر دارد."
                : files.Count == 0
                    ? "برای این نصب هنوز مجوز اعتمادی صادر نشده است."
                    : "فایل مجوز اعتماد هست ولی با کلید فعال فعلی یا با همین نصب هم‌خوان نیست.";

        return new TrustStatus(rootConfigured, RootKeyId, string.IsNullOrWhiteSpace(RootKeyPath) ? null : RootKeyPath,
            has, message, files);
    }

    /// <summary>
    /// ساخت بستهٔ تحویل: &#x202b;lic + مجوز(های) اعتماد + راهنمای یک‌صفحه‌ای&#x202b;
    /// </summary>
    public async Task<(bool Ok, string Message, string? Path)> BuildBundleAsync(
        string installationId, string licenseFilePath, string requestNo, CancellationToken ct = default)
    {
        var trustFiles = TrustFiles(installationId);
        if (trustFiles.Count == 0)
            return (false, "برای این نصب مجوز اعتمادی صادر نشده است؛ ابتدا «صدور مجوز اعتماد» را بزنید.", null);

        if (!File.Exists(licenseFilePath))
            return (false, $"فایل لایسنس پیدا نشد: {licenseFilePath}", null);

        var folder = Path.Combine(options.EffectiveWorkFolder(), "bundles", SafeSegment(installationId));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"bundle-{SafeSegment(requestNo)}.zip");

        try
        {
            if (File.Exists(path)) File.Delete(path);
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(licenseFilePath, "license.lic", CompressionLevel.Optimal);

                foreach (var trust in trustFiles)
                {
                    var entry = zip.CreateEntry("trusted-keys/" + Path.GetFileName(trust), CompressionLevel.Optimal);
                    await using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                    await writer.WriteAsync(await File.ReadAllTextAsync(trust, ct));
                }

                var readme = zip.CreateEntry("راهنمای-نصب.txt", CompressionLevel.Optimal);
                await using var guide = new StreamWriter(readme.Open(), new UTF8Encoding(true));
                await guide.WriteAsync(GuideFor(installationId, requestNo, trustFiles.Count));
            }

            return (true, $"بستهٔ تحویل ساخته شد: {Path.GetFileName(path)}", path);
        }
        catch (Exception ex)
        {
            return (false, $"ساخت بستهٔ تحویل ناموفق بود: {ex.Message}", null);
        }
    }

    private string GuideFor(string installationId, string requestNo, int trustCount) => $"""
        بستهٔ تحویل لایسنس — درخواست {requestNo}
        شناسهٔ نصب: {installationId}

        گام ۱ — فایل «license.lic» را در نرم‌افزار آپلود کنید
                 (صفحهٔ «وضعیت لایسنس» ← بارگذاری فایل لایسنس).

        گام ۲ — پوشهٔ مجوز اعتماد را بسازید و {trustCount} فایل *.trust.json را در آن بگذارید:
                 C:\ProgramData\TotallCollection\License\trusted-keys\
                 (اگر Licensing:StorageFolder تنظیم شده، همان مسیر «trusted-keys» کنارش.)

        گام ۳ — صفحهٔ «وضعیت لایسنس» را تازه کنید.
                 باید «کلید امضا» و «کلیدهای مورد اعتماد» را ببینید و وضعیت «معتبر» شود.

        توجه: مجوز اعتماد فقط برای همین شناسهٔ نصب معتبر است؛ کپی‌کردن آن روی سرور دیگر
        کار نمی‌کند. فایل کلید خصوصی هرگز برای شما فرستاده نمی‌شود.

        پشتیبانی: نرم‌افزار یکپارچه فروغ آریا
        """;

    // ─────────────── کمکی‌ها ───────────────

    private bool RootIsConfigured() =>
        !string.IsNullOrWhiteSpace(RootKeyId) && !string.IsNullOrWhiteSpace(RootKeyPath) && File.Exists(RootKeyPath);

    /// <summary>کلید عمومی ریشه از روی فایل کلید خصوصی (برای خودآزمون مجوز).</summary>
    private static string PublicKeyOfRoot(string privatePem, string? password)
    {
        using var rsa = TrustCertificateAccess.LoadPrivateKey(privatePem, password);
        return rsa.ExportSubjectPublicKeyInfoPem();
    }

    /// <summary>
    /// کلید عمومی ریشه — اول از فایل <c>&lt;rootKeyId&gt;.pem</c> کنار کلید خصوصی (که
    /// RootKeyTool می‌سازد)، و اگر نبود با استخراج از کلید خصوصی (فقط اگر گذرواژه در
    /// متغیر محیطی باشد). کلید خصوصی هیچ‌گاه در حافظهٔ بلندمدت نگه داشته نمی‌شود.
    /// </summary>
    private string? SafeReadPublicOfRoot()
    {
        try
        {
            if (!RootIsConfigured()) return null;

            var folder = Path.GetDirectoryName(RootKeyPath);
            if (!string.IsNullOrEmpty(folder))
            {
                var publicPath = Path.Combine(folder, RootKeyId + ".pem");
                if (File.Exists(publicPath))
                {
                    var pem = File.ReadAllText(publicPath);
                    return string.IsNullOrWhiteSpace(pem) ? null : pem;
                }
            }

            var password = RootPasswordFromEnvironment();
            return password is null ? null : PublicKeyOfRoot(File.ReadAllText(RootKeyPath), password);
        }
        catch
        {
            return null;
        }
    }

    private string? RootPasswordFromEnvironment()
    {
        var name = string.IsNullOrWhiteSpace(options.RootKeyPasswordEnvironmentVariable)
            ? "LICENSING_ROOT_KEY_PASSWORD"
            : options.RootKeyPasswordEnvironmentVariable;
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string? SafeRead(string path)
    {
        try { return File.ReadAllText(path); } catch { return null; }
    }

    private static string SafeSegment(string value)
    {
        var cleaned = new string((value ?? "").Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray());
        return cleaned.Length == 0 ? "unknown" : cleaned;
    }
}

/// <summary>دسترسی به بارگذاری کلید خصوصیِ کتابخانه (همان قاعدهٔ رمزدار/ساده).</summary>
internal static class TrustCertificateAccess
{
    public static System.Security.Cryptography.RSA LoadPrivateKey(string pem, string? password)
    {
        var der = KeyImport.DerFromPem(pem);
        var rsa = System.Security.Cryptography.RSA.Create();
        if (pem.Contains("ENCRYPTED PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(password))
                throw new InvalidOperationException("کلید خصوصی رمزدار است؛ گذرواژهٔ کلید لازم است.");
            rsa.ImportEncryptedPkcs8PrivateKey(Encoding.UTF8.GetBytes(password), der, out _);
        }
        else if (pem.Contains("BEGIN RSA PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
            rsa.ImportRSAPrivateKey(der, out _);
        else
            rsa.ImportPkcs8PrivateKey(der, out _);
        return rsa;
    }
}
