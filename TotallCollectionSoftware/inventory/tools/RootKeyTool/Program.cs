using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Inventory.Licensing.Crypto;

// ─────────────────────────────────────────────────────────────────────────────
// ابزار ساخت «کلید ریشه» امضای لایسنس
//
//   new        : ساخت جفت‌کلید تازه + (اختیاری) جای‌گذاری کلید عمومی در مخزن و
//                به‌روزرسانی manifest.json و appsettings محصول
//   fingerprint: efeر انگشت SHA-256 یک فایل PEM (کلید عمومی یا خصوصی)
//   inspect    : بررسی یک فایل مجوز اعتماد (*.trust.json) با کلید ریشهٔ داده‌شده
//
// قاعدهٔ ثابت: کلید خصوصی هرگز داخل پوشهٔ مخزن نوشته نمی‌شود.
// ─────────────────────────────────────────────────────────────────────────────

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine("""
    RootKeyTool — کلید ریشهٔ امضای لایسنس

      new --key-id <شناسه> --out <پوشهٔ گاوصندوق> [--password <رمز>] [--repo <ریشهٔ مخزن>]
          [--years <اعتبار گواهی؟ ندارد>] [--no-password] [--force]

      fingerprint <فایل.pem>
      inspect --trust <فایل.trust.json> --root <کلید عمومی ریشه.pem> [--installation <شناسه>]
    """);
    return 0;
}

var verb = args[0];
var opt = ParseOptions(args.Skip(1).ToArray());

try
{
    return verb switch
    {
        "new" => NewKey(),
        "fingerprint" => Fingerprint(),
        "inspect" => Inspect(),
        _ => Unknown(verb)
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"✖ خطا: {ex.Message}");
    return 2;
}

int Unknown(string v) { Console.Error.WriteLine($"✖ دستور ناشناخته: {v}"); return 3; }

int NewKey()
{
    var keyId = Require("key-id");
    var outFolder = Require("out");
    var password = opt.TryGetValue("password", out var p) ? p : null;

    // گذرواژه از متغیر محیطی: امن‌تر از آرگومان خط فرمان (در لیست پروسه‌ها دیده نمی‌شود).
    if (opt.TryGetValue("password-env", out var envName) && !string.IsNullOrWhiteSpace(envName))
    {
        password = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrEmpty(password))
            throw new InvalidOperationException($"متغیر محیطی «{envName}» خالی است یا وجود ندارد.");
    }

    var noPassword = opt.ContainsKey("no-password");
    if (noPassword) password = null;
    else if (string.IsNullOrEmpty(password))
    {
        Console.Write("گذرواژهٔ کلید خصوصی (خالی = بدون گذرواژه، توصیه نمی‌شود): ");
        password = ReadPassword();
    }

    Directory.CreateDirectory(outFolder);
    var privatePath = Path.Combine(outFolder, $"{keyId}.private.pem");
    var publicPath = Path.Combine(outFolder, $"{keyId}.pem");

    if ((File.Exists(privatePath) || File.Exists(publicPath)) && !opt.ContainsKey("force"))
        throw new InvalidOperationException(
            $"فایل کلید «{keyId}» از قبل هست ({privatePath}). برای بازنویسی --force بدهید. " +
            "بازنویسی، همهٔ لایسنس‌های امضاشده با کلید قبلی را باطل می‌کند.");

    using var rsa = RSA.Create(3072);
    var privatePem = string.IsNullOrEmpty(password)
        ? rsa.ExportPkcs8PrivateKeyPem()
        : rsa.ExportEncryptedPkcs8PrivateKeyPem(password,
            new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 200_000));
    var publicPem = rsa.ExportSubjectPublicKeyInfoPem();
    var fingerprint = PublicKeyRing.Fingerprint(publicPem, "RS256")!;

    File.WriteAllText(privatePath, privatePem, new UTF8Encoding(false));
    File.WriteAllText(publicPath, publicPem, new UTF8Encoding(false));
    TryRestrict(privatePath);

    Console.WriteLine();
    Console.WriteLine("═══ کلید ریشه ساخته شد ═══");
    Console.WriteLine($"  شناسهٔ کلید (keyId) : {keyId}");
    Console.WriteLine($"  کلید خصوصی         : {privatePath}");
    Console.WriteLine($"  کلید عمومی         : {publicPath}");
    Console.WriteLine($"  اثر انگشت SHA-256  : {fingerprint}");
    Console.WriteLine($"  الگوریتم / طول کلید: RS256 / 3072");

    if (opt.TryGetValue("repo", out var repo) && !string.IsNullOrWhiteSpace(repo))
        PlaceIntoRepo(repo, keyId, publicPem, fingerprint);

    Console.WriteLine();
    Console.WriteLine("قدم بعد:");
    Console.WriteLine("  ۱) کلید خصوصی را دو نسخه پشتیبان بگیرید (نه داخل پوشهٔ برنامه/مخزن).");
    Console.WriteLine("  ۲) بیلد Release محصول را بگیرید و روی نصب مشتری منتشر کنید.");
    Console.WriteLine("  ۳) در پنل پشتیبان، Signer:RootKeyId و Signer:RootPrivateKeyPath را به همین کلید بدهید.");
    Console.WriteLine("  ۴) فایل درخواست تازه از مشتری بگیرید و امضا کنید.");
    return 0;
}

void PlaceIntoRepo(string repoRoot, string keyId, string publicPem, string fingerprint)
{
    var keysFolder = Path.Combine(repoRoot, "inventory", "src", "Inventory.Licensing", "Keys");
    var manifestPath = Path.Combine(keysFolder, "manifest.json");
    var appSettingsPath = Path.Combine(repoRoot, "inventory", "src", "Inventory.Api", "appsettings.json");

    if (!File.Exists(manifestPath))
        throw new FileNotFoundException($"manifest.json کلیدها پیدا نشد: {manifestPath}");

    var fileName = $"{keyId}.pem";
    File.WriteAllText(Path.Combine(keysFolder, fileName), publicPem, new UTF8Encoding(false));
    Console.WriteLine($"  ✔ کلید عمومی در مخزن: {Path.Combine(keysFolder, fileName)}");

    // manifest: ردیف تازه اضافه می‌شود، ردیف‌های قبلی حذف نمی‌شوند (لایسنس‌های قدیمی معتبر بمانند)
    var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
    var keys = manifest["keys"]!.AsArray();
    var existing = keys.FirstOrDefault(k => string.Equals((string?)k!["keyId"], keyId, StringComparison.OrdinalIgnoreCase));
    if (existing is not null) keys.Remove(existing);
    keys.Add(new JsonObject
    {
        ["keyId"] = keyId,
        ["algorithm"] = "RS256",
        ["file"] = fileName,
        ["sha256"] = fingerprint,
        ["note"] = "کلید ریشهٔ تولیدی — کلید خصوصی بیرون از مخزن (ماشین پشتیبانی)",
        ["debugOnly"] = false
    });
    Backup(manifestPath);
    File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    Console.WriteLine($"  ✔ manifest.json به‌روز شد: {manifestPath}");

    if (File.Exists(appSettingsPath))
    {
        // ⚠️ appsettings.json محصول «کامنت» دارد و JSON استاندارد نیست؛ پس تجزیه/بازنویسی
        // کامل انجام نمی‌شود (کامنت‌های توضیحی از بین می‌رفت). فقط مقدار دو کلید عوض می‌شود.
        var text = File.ReadAllText(appSettingsPath);
        var before = text;

        text = ReplaceValue(text, "SigningKeyId", keyId);
        text = ReplaceValue(text, "SigningKeyFingerprintSha256", fingerprint);

        if (!text.Contains("\"SigningKeyId\"", StringComparison.Ordinal))
        {
            text = System.Text.RegularExpressions.Regex.Replace(text,
                "(\"Licensing\"\\s*:\\s*\\{)",
                "$1\n    \"SigningKeyId\": \"" + keyId + "\",\n    \"SigningKeyFingerprintSha256\": \"" + fingerprint + "\",",
                System.Text.RegularExpressions.RegexOptions.None);
        }

        if (text != before)
        {
            Backup(appSettingsPath);
            File.WriteAllText(appSettingsPath, text, new UTF8Encoding(false));
            Console.WriteLine($"  ✔ appsettings محصول به‌روز شد (SigningKeyId={keyId}، کامنت‌ها دست‌نخورده)");
        }
        else
        {
            Console.WriteLine($"  ! appsettings محصول تغییر نکرد؛ دستی بگذارید: Licensing:SigningKeyId={keyId} و Fingerprint={fingerprint}");
        }
    }
    else
    {
        Console.WriteLine($"  ! appsettings محصول پیدا نشد؛ دستی بگذارید: Licensing:SigningKeyId={keyId} و Fingerprint={fingerprint}");
    }
}

int Fingerprint()
{
    var file = Require("file");
    var pem = File.ReadAllText(file);
    var isPrivate = pem.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase);

    string publicPem;
    if (isPrivate)
    {
        var password = opt.TryGetValue("password", out var p) ? p : null;
        var privateType = TrustCertificateAccessor.LoadPrivateKey(pem, password);
        using var rsa = privateType;
        publicPem = rsa.ExportSubjectPublicKeyInfoPem();
    }
    else publicPem = pem;

    var fp = PublicKeyRing.Fingerprint(publicPem, "RS256");
    Console.WriteLine($"{Path.GetFileName(file)}");
    Console.WriteLine($"  نوع        : {(isPrivate ? "کلید خصوصی" : "کلید عمومی")}");
    Console.WriteLine($"  اثر انگشت  : {fp}");
    return fp is null ? 1 : 0;
}

int Inspect()
{
    var trustPath = Require("trust");
    var rootPath = Require("root");
    var installation = opt.TryGetValue("installation", out var i) ? i : null;

    var rootPem = File.ReadAllText(rootPath);
    var rootId = opt.TryGetValue("root-key-id", out var rid) ? rid : "root";
    var rootFp = PublicKeyRing.Fingerprint(rootPem, "RS256")!;
    var ring = PublicKeyRing.FromEntries(new[]
    {
        new PublicKeyEntry { KeyId = rootId, Algorithm = "RS256", PublicKeyPem = rootPem, Sha256 = rootFp }
    });

    var doc = TrustCertificate.Parse(File.ReadAllText(trustPath));
    if (doc is null) { Console.Error.WriteLine("✖ فایل مجوز اعتماد خوانده نشد."); return 1; }

    var ok = TrustCertificate.TryVerify(doc, ring.EmbeddedKey, installation, DateTime.UtcNow,
        out var key, out var error);

    Console.WriteLine($"مجوز اعتماد: {doc.KeyId} (نصب {doc.InstallationId})");
    Console.WriteLine($"  کلید عمومی : {doc.Sha256}");
    Console.WriteLine($"  امضاکننده  : {doc.IssuedBy}");
    Console.WriteLine($"  وضعیت      : {(ok ? "✔ معتبر" : "✖ نامعتبر — " + error)}");
    return ok ? 0 : 1;
}

// ─────────────────────────── کمکی‌ها ───────────────────────────

/// <summary>جای‌گذاری مقدار یک کلید JSON بدون دست‌زدن به کامنت‌ها و ترتیب فایل.</summary>
static string ReplaceValue(string json, string key, string value)
{
    var pattern = "\"" + System.Text.RegularExpressions.Regex.Escape(key) + "\"\\s*:\\s*\"[^\"]*\"";
    return System.Text.RegularExpressions.Regex.Replace(json, pattern,
        "\"" + key + "\": \"" + value + "\"",
        System.Text.RegularExpressions.RegexOptions.None);
}


string Require(string name) =>
    opt.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v)
        ? v
        : throw new ArgumentException($"گزینهٔ --{name} لازم است.");

static Dictionary<string, string> ParseOptions(string[] rest)
{
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < rest.Length; i++)
    {
        if (!rest[i].StartsWith("--", StringComparison.Ordinal)) continue;
        var name = rest[i][2..];
        var value = i + 1 < rest.Length && !rest[i + 1].StartsWith("--", StringComparison.Ordinal) ? rest[++i] : "";
        map[name] = value;
    }
    return map;
}

static string ReadPassword()
{
    var sb = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
        sb.Append(key.KeyChar);
    }
    Console.WriteLine();
    return sb.ToString();
}

static void Backup(string path)
{
    var backup = path + ".bak-" + DateTime.Now.ToString("yyyyMMddHHmmss");
    File.Copy(path, backup, overwrite: true);
    Console.WriteLine($"  • پشتیبان: {Path.GetFileName(backup)}");
}

static void TryRestrict(string path)
{
    try
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
    catch { /* بی‌صدا */ }
}

/// <summary>دسترسی به بارگذاری کلید خصوصیِ کتابخانه (همان قاعده، بدون تکرار کد).</summary>
static class TrustCertificateAccessor
{
    public static RSA LoadPrivateKey(string pem, string? password)
    {
        var der = Inventory.Licensing.Crypto.KeyImport.DerFromPem(pem);
        var rsa = RSA.Create();
        if (pem.Contains("ENCRYPTED PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("کلید خصوصی رمزدار است؛ --password لازم است.");
            rsa.ImportEncryptedPkcs8PrivateKey(Encoding.UTF8.GetBytes(password), der, out _);
        }
        else if (pem.Contains("BEGIN RSA PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
            rsa.ImportRSAPrivateKey(der, out _);
        else
            rsa.ImportPkcs8PrivateKey(der, out _);
        return rsa;
    }
}
