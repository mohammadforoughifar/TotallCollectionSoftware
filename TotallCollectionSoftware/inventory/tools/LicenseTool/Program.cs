using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Inventory.Licensing.Crypto;
using Inventory.Licensing.Entitlement;
using Inventory.Licensing.Model;
using Inventory.Licensing.Modules;
using Inventory.Licensing.Periods;

namespace LicenseTool;

/// <summary>
/// ابزار صدور لایسنس (خط فرمان، کراس‌پلتفرم) — بخشی از مرحله ۴ سامانهٔ لایسنس.
///
/// <para>
/// <b>چرا ابزار و نه WinForms؟</b> چون صدور لایسنس کاری است که ما انجام می‌دهیم، نه
/// مشتری؛ باید روی لینوکس/ویندوز و در همین محیط قابل تست باشد و بشود آن را در پایپ‌لاین
/// اجرا کرد. کلید خصوصی فقط روی ماشین ما می‌ماند و هیچ‌وقت در مخزن یا بستهٔ نصب نیست.
/// </para>
///
/// <para>
/// <b>قاعدهٔ ایمنی:</b> هیچ فایلی بدون <c>--yes</c> و بدون دیدن پیش‌نمایش امضا نمی‌شود.
/// پیش‌نمایش، نتیجهٔ <b>ادغام</b> لایسنس‌های موجود با سند تازه است — همان چیزی که
/// مشتری روی صفحهٔ وضعیت می‌بیند.
/// </para>
/// </summary>
public static class Program
{
    private const string Signing = "RS256";

    private static readonly JsonSerializerOptions EnvelopeWriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions ProfileReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintHelp();
            return args.Length == 0 ? 2 : 0;
        }

        try
        {
            var command = args[0];
            var rest = args.Skip(1).ToArray();
            return command switch
            {
                "keygen" => Keygen(rest),
                "issue" => Issue(rest),
                "bridge" => Bridge(rest),
                "verify" => Verify(rest),
                "inspect-request" => InspectRequest(rest),
                "companies" => Companies(rest),
                "keys" => Keys(rest),
                "doctor" => Doctor(rest),
                _ => Unknown(command)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ {ex.GetType().Name}: {ex.Message}");
            if (Environment.GetEnvironmentVariable("LICENSETOOL_DEBUG") == "1") Console.Error.WriteLine(ex.ToString());
            return 2;
        }
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"❌ دستور ناشناخته: {command}");
        PrintHelp();
        return 2;
    }

    // ==================== keygen ====================

    /// <summary>
    /// ساخت جفت‌کلید امضا. کلید خصوصی <b>رمزنگاری‌شده</b> نوشته می‌شود اگر رمز در
    /// متغیر محیطی باشد؛ در غیر این صورت هشدار «بدون رمز» چاپ می‌شود.
    ///
    /// <para>
    /// این دستور برای کلید تولیدی نیست که در CI/سندباکس اجرا شود؛ فقط ابزارش این‌جاست.
    /// </para>
    /// </summary>
    private static int Keygen(string[] args)
    {
        var keyId = Arg(args, "--key-id");
        var outDir = Arg(args, "--out-dir");
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(outDir))
        {
            Console.Error.WriteLine("❌ استفاده: keygen --key-id <id> --out-dir <dir>");
            return 2;
        }

        Directory.CreateDirectory(outDir);

        using var rsa = RSA.Create(3072);
        var publicPem = rsa.ExportSubjectPublicKeyInfoPem();
        var fingerprint = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant();

        var password = Environment.GetEnvironmentVariable("LICENSETOOL_KEY_PASSWORD");
        var privatePath = Path.Combine(outDir, $"{keyId}.private.pem");
        string privatePem;
        if (string.IsNullOrEmpty(password))
        {
            privatePem = rsa.ExportPkcs8PrivateKeyPem();
            Console.Error.WriteLine("⚠️  LICENSETOOL_KEY_PASSWORD خالی است — کلید خصوصی بدون رمز ذخیره می‌شود.");
        }
        else
        {
            privatePem = rsa.ExportEncryptedPkcs8PrivateKeyPem(password,
                new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100_000));
        }

        File.WriteAllText(privatePath, privatePem);
        RestrictPermissions(privatePath, isPrivateKey: true);

        var publicPath = Path.Combine(outDir, $"{keyId}.pem");
        File.WriteAllText(publicPath, publicPem);
        RestrictPermissions(publicPath, isPrivateKey: false);

        Console.WriteLine("جفت‌کلید ساخته شد.");
        Console.WriteLine($"  کلید عمومی : {publicPath}");
        Console.WriteLine($"  کلید خصوصی: {privatePath}  (هرگز در مخزن نگذارید)");
        Console.WriteLine($"  اثر انگشت  : {fingerprint}");
        Console.WriteLine();
        Console.WriteLine("ردیف زیر را در Keys/manifest.json اضافه کنید (debugOnly=false تنها برای کلید تولیدی):");
        Console.WriteLine("""
          {
            "keyId": "<id>",
            "algorithm": "RS256",
            "file": "<id>.pem",
            "sha256": "<اثر انگشت بالا>",
            "note": "کلید تولیدی — کلید خصوصی بیرون از مخزن",
            "debugOnly": false
          }
        """);
        Console.WriteLine("سپس بیلد Release بگیرید و با `LicenseCheck --keys` بررسی کنید تعداد کلیدها یک باشد.");
        return 0;
    }

    // ==================== issue ====================

    private static int Issue(string[] args)
    {
        var profilePath = Arg(args, "--profile");
        var outPath = Arg(args, "--out");
        if (string.IsNullOrWhiteSpace(profilePath) || string.IsNullOrWhiteSpace(outPath))
        {
            Console.Error.WriteLine("❌ استفاده: issue --profile <profile.json> [--request req.json | --installation-id <id>] [--existing a.lic ...] --private-key <pem> --out <out.lic> [--yes]");
            return 2;
        }

        var profile = LoadProfile(profilePath);
        var installationId = ResolveInstallationId(args);

        var document = BuildDocument(profile, installationId, out var warnings);

        // ⚠️ محافظ ریشه‌ای: اگر کلید امضا در بیلد Release محصول بارگذاری نشود (کلید توسعه یا
        // کلیدی که در Keys/manifest.json نیست)، مشتری همان خطای «کلید امضا شناخته‌شده نیست»
        // را می‌گیرد. این هشدار پیش از شمارش/تحویل، جلوی آن چرخهٔ شکست را می‌گیرد.
        WarnIfCustomerCannotVerify(document.KeyId);

        var existing = LoadExisting(args);
        var preview = BuildPreview(existing.Append(document).ToList());
        PrintPreview(document, preview, warnings, existing.Count);

        if (!args.Contains("--yes"))
        {
            Console.WriteLine();
            Console.WriteLine("⛔ بدون --yes امضا نشد. پیش‌نمایش بالا را ببینید و دوباره با --yes اجرا کنید.");
            return 3;
        }

        var bytes = SignAndSerialize(document, args);

        // ⛔ دروازهٔ تحویل: همان فایلی که ساخته شد را با «کلیدهای جاسازی‌شده در محصول» می‌سنجیم.
        // دلیل وجودش یک خطای واقعی است: پنل فایل را با کلید عمومیِ ماشین پشتیبانی تأیید می‌کرد
        // (پس سالم به‌نظر می‌رسید) ولی کلید عمومیِ جاسازی‌شده در نصب مشتری کلید دیگری بود و
        // مشتری خطای «امضای فایل لایسنس معتبر نیست» می‌گرفت.
        if (!ProductGate(bytes, args, out var gateError))
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("⛔ فایل نوشته نشد: " + gateError);
            return 7;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllBytes(outPath, bytes);

        AppendJournal(document, outPath, installationId, bytes);

        Console.WriteLine();
        Console.WriteLine($"✅ لایسنس صادر شد: {outPath}");
        Console.WriteLine($"   سریال: {document.Serial}   |   SHA256 فایل: {Sha256Hex(bytes)}");
        return 0;
    }

    /// <summary>
    /// هشدار پیش از صدور: آیا نصب مشتری می‌تواند امضای این کلید را تأیید کند؟
    /// مرجع، همان حلقهٔ کلیدهای جاسازی‌شده در محصول است (نه حدس).
    /// </summary>
    private static void WarnIfCustomerCannotVerify(string? keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId)) return;
        try
        {
            var ring = PublicKeyRing.FromEmbedded();
            if (ring.KeyIds.Contains(keyId!, StringComparer.OrdinalIgnoreCase)) return;

            Console.Error.WriteLine();
            Console.Error.WriteLine($"⚠️ هشدار مهم: کلید «{keyId}» در حلقهٔ اعتماد بیلد محصول نیست.");
            Console.Error.WriteLine("   لایسنسی که با این کلید امضا شود، روی نصب مشتری پیام «کلید امضا شناخته‌شده نیست» می‌دهد.");
            Console.Error.WriteLine(ring.KeyIds.Count == 0
                ? "   این بیلد ابزار هیچ کلید تولیدی ندارد (احتمالاً بیلد Debug یا manifest ناقص)."
                : "   کلیدهای معتبر این بیلد: " + string.Join("، ", ring.KeyIds));
            if (ring.LoadErrors.Count > 0)
                Console.Error.WriteLine("   یادداشت بارگذاری کلیدها: " + string.Join(" ", ring.LoadErrors.Take(3)));
            Console.Error.WriteLine("   راه‌حل: با --key-id یکی از کلیدهای بالا امضا کنید، یا کلید فعلی را در manifest محصول ثبت و بیلد کنید.");
            Console.Error.WriteLine("   بررسی نصب مشتری: LicenseTool keys --assembly <Inventory.Licensing.dll> --expect " + keyId);
            Console.Error.WriteLine();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"⚠️ بررسی کلیدهای محصول ممکن نشد: {ex.Message}");
        }
    }

    // ==================== bridge ====================

    /// <summary>
    /// «لایسنس پل» — برای نصب‌هایی که امروز بدون لایسنس کار می‌کنند و باید تا صدور
    /// لایسنس واقعی سر پا بمانند: همهٔ ۳۳ ماژول، اشتراکی، بدون سقف کاربر و بدون سهمیه.
    /// </summary>
    private static int Bridge(string[] args)
    {
        var days = int.TryParse(Arg(args, "--days"), out var d) ? Math.Clamp(d, 1, 365) : 30;
        var outPath = Arg(args, "--out");
        if (string.IsNullOrWhiteSpace(outPath))
        {
            Console.Error.WriteLine("❌ استفاده: bridge --days N --installation-id <id> --private-key <pem> --out <out.lic> [--yes]");
            return 2;
        }

        var installationId = ResolveInstallationId(args);
        var profile = new IssueProfile
        {
            Customer = new CustomerProfile
            {
                Id = Arg(args, "--customer-id") ?? "CUST-BRIDGE",
                Name = Arg(args, "--customer-name") ?? "لایسنس پل"
            },
            Deployment = DeploymentKind.OnPrem,
            Note = $"لایسنس پل {days} روزه"
        };
        profile.Items.Add(new ItemProfile
        {
            Id = "BRIDGE",
            Scope = ModuleCodes.All.Select(m => m.Code).ToList(),
            Term = new TermProfile { Type = LicenseTermType.Subscription, Months = days * 12 / 365 + 1 },
            Users = new UsersProfile { Mode = LicenseUserMode.None }
        });

        // «days» دقیق‌تر از «ماه» است: تاریخ پایان را صریح می‌گذاریم.
        var start = PersianPeriodKey.NowIran().Date;
        profile.Items[0].Term = new TermProfile { Type = LicenseTermType.Subscription, From = start, Until = start.AddDays(days) };

        var document = BuildDocument(profile, installationId, out var warnings);
        PrintPreview(document, BuildPreview(new List<LicenseDocument> { document }), warnings, 0);

        if (!args.Contains("--yes"))
        {
            Console.WriteLine("\n⛔ بدون --yes امضا نشد.");
            return 3;
        }

        var bytes = SignAndSerialize(document, args);

        if (!ProductGate(bytes, args, out var gateError))
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("⛔ فایل نوشته نشد: " + gateError);
            return 7;
        }

        File.WriteAllBytes(outPath, bytes);
        AppendJournal(document, outPath, installationId, bytes);
        Console.WriteLine($"\n✅ لایسنس پل صادر شد: {outPath} (تا {document.Items[0].Term.ValidUntil:yyyy-MM-dd})");
        return 0;
    }

    // ==================== verify ====================

    private static int Verify(string[] args)
    {
        var file = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : Arg(args, "--file");
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
        {
            Console.Error.WriteLine("❌ استفاده: verify <file.lic> [--installation-id <id>] [--public-key <pem>] [--manifest <manifest.json>]");
            return 2;
        }

        var text = File.ReadAllText(file);
        // شناسهٔ کلید از خودِ پوشش خوانده می‌شود تا «--public-key» تنها کافی باشد.
        string? envelopeKeyId = null;
        try { envelopeKeyId = LicenseJson.ReadEnvelope(text)?.KeyId; } catch { /* ساختار در Verify گزارش می‌شود */ }
        var ring = BuildRing(args, envelopeKeyId);
        var result = LicenseVerifier.Verify(text, ring);

        if (!result.Ok || result.Document is null)
        {
            Console.Error.WriteLine($"❌ امضا/ساختار نامعتبر: {result.ErrorText}");
            return 4;
        }

        var doc = result.Document;
        Console.WriteLine("✅ امضا معتبر است.");
        Console.WriteLine($"   سریال: {doc.Serial} | مشتری: {doc.CustomerId} ({doc.CustomerName})");
        Console.WriteLine($"   حالت: {doc.Deployment} | نصب: {doc.InstallationId ?? "—"}");
        Console.WriteLine($"   صدور: {doc.IssuedAt:yyyy-MM-dd HH:mm} | آیتم‌ها: {doc.Items.Count}");
        if (doc.Companies.Count > 0)
        {
            Console.WriteLine($"   شرکت‌های مجاز: {doc.Companies.Count}");
            foreach (var c in doc.Companies)
            {
                var mods = c.Modules.Count == 0 ? "همهٔ ماژول‌های سند" : string.Join(", ", c.Modules);
                var seats = c.MaxUsers is { } n ? $" | کاربر: {n}" : "";
                Console.WriteLine($"     • {c.CompanyId} ({c.Name ?? "بی‌نام"}) | {mods}{seats}");
            }
        }
        else
        {
            Console.WriteLine("   شرکت‌های مجاز: — (این سند محدودیت شرکت ندارد)");
        }

        var expectInstallation = Arg(args, "--installation-id");
        if (!string.IsNullOrWhiteSpace(expectInstallation))
        {
            var match = string.Equals(doc.InstallationId, expectInstallation, StringComparison.OrdinalIgnoreCase);
            Console.WriteLine(match ? "   ✔ شناسهٔ نصب مطابق است." : $"   ❌ شناسهٔ نصب نمی‌خواند (انتظار {expectInstallation}).");
            if (!match) return 5;
        }

        var issues = Inventory.Licensing.Validation.LicenseValidator.Validate(doc,
            new Inventory.Licensing.Validation.LicenseValidationContext
            {
                ExpectedInstallationId = expectInstallation,
                Now = PersianPeriodKey.NowIran().Date
            });
        foreach (var issue in issues)
            Console.WriteLine($"   • [{issue.Severity}] {issue.Code}: {issue.Message}");

        return issues.Any(i => i.Severity == LicenseIssueSeverity.Error) ? 6 : 0;
    }

    // ==================== product gate + doctor ====================

    /// <summary>
    /// اثر انگشت sha256 (hex) کلید عمومی از متن PEM — همان معیاری که در <c>Keys/manifest.json</c>
    /// ثبت می‌شود و هنگام بارگذاری در نصب مشتری بررسی می‌شود.
    /// </summary>
    private static string? FingerprintOfPem(string pem)
    {
        try
        {
            using var rsa = KeyImport.RsaFromPem(pem);
            return Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
        }
        catch { return null; }
    }

    /// <summary>اثر انگشت کلید عمومیِ متناظر با کلید خصوصی داده‌شده (بدون چاپ هیچ بخشی از کلید خصوصی).</summary>
    private static string? FingerprintOfPrivateKey(string[] args)
    {
        try
        {
            using var rsa = LoadPrivateKey(args);
            return Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
        }
        catch { return null; }
    }

    private static string Short(string? fingerprint) =>
        string.IsNullOrWhiteSpace(fingerprint) ? "—" : fingerprint[..16] + "…";

    /// <summary>
    /// دروازهٔ تحویل: آیا فایلِ همین حالا امضاشده، روی نصب مشتریِ <b>همین بیلد</b> پذیرفته می‌شود؟
    ///
    /// <para>
    /// خطای واقعی که این دروازه را لازم کرد: پنل لایسنس فایل را با کلید عمومیِ ماشین پشتیبانی
    /// تأیید می‌کرد و «سالم» ذخیره می‌شد، ولی نصب مشتری کلید عمومی دیگری (کلید جاسازی‌شده در بیلد
    /// منتشرشده) داشت؛ نتیجه: <c>LICENSE_REJECTED — امضای فایل لایسنس معتبر نیست</c>.
    /// تفاوت «شناسهٔ کلید» تنها کافی نیست: باید <b>اثر انگشت کلید</b> هم یکی باشد.
    /// </para>
    /// </summary>
    private static bool ProductGate(byte[] licenseBytes, string[] args, out string failure)
    {
        failure = "";
        if (args.Contains("--allow-foreign-key")) return true;

        PublicKeyRing ring;
        try
        {
            ring = PublicKeyRing.FromEmbedded();
        }
        catch (Exception ex)
        {
            failure = $"بررسی کلیدهای محصول ممکن نشد: {ex.Message}";
            return false;
        }

        var text = Encoding.UTF8.GetString(licenseBytes);
        string keyId = "";
        try { keyId = LicenseJson.ReadEnvelope(text)?.KeyId ?? ""; } catch { /* ساختار در Verify گزارش می‌شود */ }

        if (ring.KeyIds.Count == 0)
        {
            failure = "این بیلد ابزار هیچ کلید تولیدی جاسازی‌شده‌ای ندارد (بیلد Debug یا manifest ناقص)؛ " +
                      "نمی‌توان تأیید کرد که نصب مشتری امضا را می‌پذیرد. با بیلد Release ابزار اجرا کنید.";
            return false;
        }

        if (!ring.KeyIds.Contains(keyId, StringComparer.OrdinalIgnoreCase))
        {
            failure = $"کلید «{keyId}» در حلقهٔ اعتماد بیلد محصول نیست؛ نصب مشتری همان خطای «کلید امضا شناخته‌شده نیست» را می‌دهد. " +
                      "کلیدهای این بیلد: " + string.Join("، ", ring.KeyIds) + ".";
            return false;
        }

        var result = LicenseVerifier.Verify(text, ring);
        if (result.Ok) return true;

        var embedded = ring.Entries.FirstOrDefault(e => string.Equals(e.KeyId, keyId, StringComparison.OrdinalIgnoreCase));
        var embeddedFp = embedded is null ? null : FingerprintOfPem(embedded.PublicKeyPem);
        var mineFp = FingerprintOfPrivateKey(args);

        failure = "فایل امضاشده با کلیدهای جاسازی‌شده در محصول تأیید نشد: " + result.ErrorText + Environment.NewLine +
                  $"   • کلید امضا (ساخته‌شده از کلید خصوصی): {Short(mineFp)}" + Environment.NewLine +
                  $"   • کلید جاسازی‌شده در محصول برای «{keyId}»: {Short(embeddedFp)}" + Environment.NewLine +
                  (mineFp is not null && embeddedFp is not null && !string.Equals(mineFp, embeddedFp, StringComparison.OrdinalIgnoreCase)
                      ? "   ⇒ این دو کلید یکی نیستند: کلید خصوصیِ امضا با کلید عمومیِ داخل نصب مشتری جفت نیست." + Environment.NewLine +
                        "   راه‌حل: یا با کلید خصوصیِ همان جفتِ قبلی امضا کنید، یا کلید عمومیِ فعلی را در «Keys/manifest.json» " +
                        "ثبت کنید (پنل: تنظیمات ← کلیدهای امضا ← ثبت در manifest محصول)، بیلد Release تازه بگیرید و همان را روی نصب مشتری جایگزین کنید."
                      : "   ⇒ payload/امضا در مسیر ساخت دست‌کاری شده یا کلید عمومی در دسترس نیست.")
                  + Environment.NewLine +
                  "   برای بررسی کامل: LicenseTool doctor --license <file.lic> --private-key <pem>";
        return false;
    }

    /// <summary>
    /// تشخیص یک‌مرحله‌ای خطای «امضای فایل لایسنس معتبر نیست» / «کلید شناخته‌شده نیست»:
    /// کلیدهای جاسازی‌شدهٔ این بیلد، کلید امضا، و (اگر فایل لایسنس داده شود) نتیجهٔ واقعی بررسی.
    ///
    /// <para>هیچ کلید خصوصی چاپ نمی‌شود؛ فقط اثر انگشت کلید عمومی و نتیجهٔ بررسی.</para>
    /// </summary>
    private static int Doctor(string[] args)
    {
        var licensePath = Arg(args, "--license") ?? (args.Length > 0 && !args[0].StartsWith("--") ? args[0] : null);
        var publicPath = Arg(args, "--public-key");
        var privatePath = Arg(args, "--private-key") ?? Environment.GetEnvironmentVariable("LICENSETOOL_PRIVATE_KEY");

        Console.WriteLine("■ کلیدهای جاسازی‌شده در این بیلد محصول (Inventory.Licensing.dll):");
        PublicKeyRing ring;
        try
        {
            ring = PublicKeyRing.FromEmbedded();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ خواندن کلیدهای جاسازی‌شده ممکن نشد: {ex.Message}");
            return 2;
        }

        if (ring.KeyIds.Count == 0)
        {
            Console.WriteLine("   (هیچ کلیدی) — این بیلد Release قابل استفاده برای صدور نیست.");
        }
        else
        {
            foreach (var entry in ring.Entries)
                Console.WriteLine($"   • {entry.KeyId,-16} {entry.Algorithm,-6} اثر انگشت: {FingerprintOfPem(entry.PublicKeyPem)}");
        }

        if (ring.LoadErrors.Count > 0)
        {
            Console.WriteLine("   یادداشت بارگذاری: " + string.Join(" | ", ring.LoadErrors.Take(5)));
        }

        string? signerFp = null;
        if (!string.IsNullOrWhiteSpace(privatePath))
        {
            if (!File.Exists(privatePath))
            {
                Console.Error.WriteLine($"❌ فایل کلید خصوصی نیست: {privatePath}");
                return 2;
            }

            signerFp = FingerprintOfPrivateKey(args);
            if (signerFp is null)
            {
                Console.Error.WriteLine("❌ خواندن کلید خصوصی ممکن نشد (رمز اشتباه؟ متغیر LICENSETOOL_KEY_PASSWORD را بررسی کنید).");
                return 2;
            }

            Console.WriteLine();
            Console.WriteLine($"■ کلید امضا (از کلید خصوصی): {signerFp}");
            var matches = ring.Entries
                .Where(e => string.Equals(FingerprintOfPem(e.PublicKeyPem), signerFp, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.KeyId)
                .ToList();

            if (matches.Count > 0)
            {
                Console.WriteLine($"   ✔ این کلید با کلید «{matches[0]}» در بیلد محصول می‌خواند؛ لایسنس‌های این کلید پذیرفته می‌شوند.");
            }
            else
            {
                Console.WriteLine("   ❌ این کلید با هیچ کلید جاسازی‌شده‌ای در این بیلد نمی‌خواند.");
                Console.WriteLine("      ⇒ لایسنسی که با این کلید امضا شود، روی نصب مشتری رد می‌شود (همان خطای امضای نامعتبر).");
                Console.WriteLine("      ⇒ رفع: یا کلید خصوصیِ جفتِ نصب مشتری را بیاورید، یا کلید عمومیِ این کلید را در");
                Console.WriteLine("         Keys/manifest.json ثبت کنید و نسخهٔ Release مشتری را با آن بیلد و جایگزین کنید.");
            }
        }

        if (!string.IsNullOrWhiteSpace(publicPath))
        {
            if (!File.Exists(publicPath))
            {
                Console.Error.WriteLine($"❌ فایل کلید عمومی نیست: {publicPath}");
                return 2;
            }

            var fp = FingerprintOfPem(File.ReadAllText(publicPath));
            Console.WriteLine();
            Console.WriteLine($"■ کلید عمومی داده‌شده: {fp}  ({publicPath})");
            if (signerFp is not null)
                Console.WriteLine(string.Equals(fp, signerFp, StringComparison.OrdinalIgnoreCase)
                    ? "   ✔ با کلید امضا یکی است."
                    : "   ❌ با کلید امضا یکی نیست — پنل اگر با این کلید عمومی تأیید می‌کند، نتیجهٔ تأییدش گمراه‌کننده است.");
        }

        if (string.IsNullOrWhiteSpace(licensePath)) return signerFp is null ? 0 : (ring.Entries.Any(e => string.Equals(FingerprintOfPem(e.PublicKeyPem), signerFp, StringComparison.OrdinalIgnoreCase)) ? 0 : 7);

        if (!File.Exists(licensePath))
        {
            Console.Error.WriteLine($"❌ فایل لایسنس نیست: {licensePath}");
            return 2;
        }

        var text = File.ReadAllText(licensePath);
        SignedLicenseDocument? envelope = null;
        try { envelope = LicenseJson.ReadEnvelope(text); } catch { /* زیر گزارش می‌شود */ }

        Console.WriteLine();
        Console.WriteLine($"■ فایل لایسنس: {licensePath}");
        if (envelope is null)
        {
            Console.Error.WriteLine("   ❌ ساختار پوشش امضا خوانده نشد (فایل خراب یا رمزگذاری‌شده است).");
            return 4;
        }

        Console.WriteLine($"   شناسهٔ کلید: {envelope.KeyId} | الگوریتم: {envelope.Algorithm} | نسخهٔ پوشش: {envelope.EnvelopeVersion}");
        byte[] payload;
        try { payload = Convert.FromBase64String(envelope.Payload); }
        catch
        {
            Console.Error.WriteLine("   ❌ payload کدگذاری Base64 معتبری ندارد.");
            return 4;
        }

        Console.WriteLine($"   SHA256 محتوای امضاشده: {Sha256Hex(payload)} | حجم payload: {payload.Length} بایت");

        var verify = LicenseVerifier.Verify(text, ring);
        if (verify.Ok && verify.Document is not null)
        {
            Console.WriteLine("   ✔ روی این بیلد محصول پذیرفته می‌شود.");
            Console.WriteLine($"     سریال: {verify.Document.Serial} | نصب: {verify.Document.InstallationId ?? "—"}");
            return 0;
        }

        Console.WriteLine("   ❌ روی این بیلد محصول رد می‌شود: " + verify.ErrorText);
        var fpOfKey = ring.Entries
            .Where(e => string.Equals(e.KeyId, envelope.KeyId, StringComparison.OrdinalIgnoreCase))
            .Select(e => FingerprintOfPem(e.PublicKeyPem))
            .FirstOrDefault();
        Console.WriteLine($"     اثر انگشت کلید جاسازی‌شده برای «{envelope.KeyId}»: {fpOfKey ?? "— (این شناسه در بیلد نیست)"}");
        if (signerFp is not null)
            Console.WriteLine($"     اثر انگشت کلید امضا: {signerFp}");
        if (fpOfKey is not null && signerFp is not null && !string.Equals(fpOfKey, signerFp, StringComparison.OrdinalIgnoreCase))
            Console.WriteLine("     ⇒ قطعی: کلید امضا با کلید جاسازی‌شده در محصول یکی نیست (این علت خطاست).");
        return 7;
    }

    /// <summary>
    /// بازرسی «کلیدهای جاسازی‌شده» در یک اسمبلی ساخته‌شده — بدون اجرا و بدون بارگذاری کد.
    ///
    /// <para>
    /// این دستور مشکل پرتکرار «کلید امضای … روی این نصب شناخته‌شده نیست» را ریشه‌ای تشخیص
    /// می‌دهد: همان فایل <c>Inventory.Licensing.dll</c> که روی سرور/مشتری اجرا می‌شود را
    /// می‌دهید و می‌بینید واقعاً چه کلیدهایی داخلش جاسازی شده‌اند (keyId، اثر انگشت، فایل).
    /// اگر <c>sup-202611</c> در این فهرست نباشد، مشکل در بیلد/نصب است، نه در فایل لایسنس.
    /// </para>
    /// </summary>
    private static int Keys(string[] args)
    {
        var assemblyPath = Arg(args, "--assembly");
        var dir = Arg(args, "--dir");

        if (string.IsNullOrWhiteSpace(assemblyPath) && !string.IsNullOrWhiteSpace(dir))
        {
            if (!Directory.Exists(dir))
            {
                Console.Error.WriteLine($"❌ پوشه نیست: {dir}");
                return 2;
            }

            var found = Directory.EnumerateFiles(dir, "Inventory.Licensing.dll", SearchOption.AllDirectories)
                .ToList();
            if (found.Count == 0)
            {
                Console.Error.WriteLine($"❌ در «{dir}» فایل Inventory.Licensing.dll پیدا نشد.");
                Console.Error.WriteLine("   اگر انتشار Single-File است، همان exe را بدهید: --assembly <app.exe>");
                return 2;
            }

            if (found.Count > 1)
            {
                Console.WriteLine($"⚠️ {found.Count} نسخهٔ Inventory.Licensing.dll در این پوشه هست؛ همه بررسی می‌شوند:");
                foreach (var f in found) Console.WriteLine($"   - {f}");
                Console.WriteLine();
            }

            var worst = 0;
            foreach (var file in found)
                worst = Math.Max(worst, InspectKeysOf(file, args));
            return worst;
        }

        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            Console.Error.WriteLine("❌ استفاده: keys --assembly <Inventory.Licensing.dll | app.exe>  یا  keys --dir <پوشهٔ انتشار>");
            return 2;
        }

        if (!File.Exists(assemblyPath))
        {
            Console.Error.WriteLine($"❌ فایل نیست: {assemblyPath}");
            return 2;
        }

        return InspectKeysOf(assemblyPath, args);
    }

    /// <summary>خواندن منابع جاسازی‌شده از PE بدون بارگذاری اسمبلی (بی‌خطر و سریع).</summary>
    private static int InspectKeysOf(string path, string[] args)
    {
        Console.WriteLine($"■ اسمبلی: {path}");
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            var md = pe.GetMetadataReader();

            var resourcesDirectory = pe.PEHeaders.CorHeader?.ResourcesDirectory ?? default;
            string? manifestJson = null;
            var pems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var allNames = new List<string>();

            foreach (var handle in md.ManifestResources)
            {
                var resource = md.GetManifestResource(handle);
                var name = md.GetString(resource.Name);
                allNames.Add(name);

                if (!resource.Implementation.IsNil) continue;   // در فایل دیگری است

                var bytes = ReadEmbeddedResource(pe, resourcesDirectory.RelativeVirtualAddress, resource.Offset);
                if (bytes is null) continue;
                var text = Encoding.UTF8.GetString(bytes);

                if (name.EndsWith("Keys.manifest.json", StringComparison.OrdinalIgnoreCase))
                    manifestJson = text;
                else if (name.EndsWith(".pem", StringComparison.OrdinalIgnoreCase))
                    pems[name] = text;
            }

            if (manifestJson is null)
            {
                Console.Error.WriteLine("  ⛔ در این اسمبلی Keys/manifest.json جاسازی نشده است.");
                Console.Error.WriteLine("     یعنی این بیلد هیچ کلید عمومی‌ای ندارد ⇒ هر لایسنس امضاشده‌ای «ناشناخته» می‌شود.");
                Console.Error.WriteLine($"     (تعداد منابع اسمبلی: {allNames.Count})");
                Console.Error.WriteLine("     ⚠️ اگر انتشار Single-File است و این فایل داخل exe است، همان exe را با --assembly بدهید.");
                return 6;
            }

            using var manifestDoc = JsonDocument.Parse(manifestJson);
            var entries = new List<(string KeyId, string Algorithm, string File, string? Sha256, bool DebugOnly)>();
            if (manifestDoc.RootElement.TryGetProperty("keys", out var keysElement) && keysElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var key in keysElement.EnumerateArray())
                {
                    entries.Add((
                        key.TryGetProperty("keyId", out var k) ? k.GetString() ?? "" : "",
                        key.TryGetProperty("algorithm", out var a) ? a.GetString() ?? "RS256" : "RS256",
                        key.TryGetProperty("file", out var f) ? f.GetString() ?? "" : "",
                        key.TryGetProperty("sha256", out var h) ? h.GetString() : null,
                        key.TryGetProperty("debugOnly", out var d) && d.ValueKind == JsonValueKind.True));
                }
            }

            Console.WriteLine($"  ردیف‌های manifest: {entries.Count}");
            var expectedKeyId = Arg(args, "--expect");
            var productionKeys = 0;

            foreach (var entry in entries)
            {
                var pemName = allNames.FirstOrDefault(n => n.EndsWith("Keys." + entry.File, StringComparison.OrdinalIgnoreCase));
                var present = pemName is not null && pems.ContainsKey(pemName);
                string? fingerprint = null;
                if (present)
                {
                    try
                    {
                        var der = KeyImport.DerFromPem(pems[pemName!]);
                        fingerprint = Convert.ToHexString(SHA256.HashData(der)).ToLowerInvariant();
                    }
                    catch { fingerprint = null; }
                }

                var allowed = PublicKeyRing.IsKeyAllowedInBuild(entry.DebugOnly);
                var fpOk = entry.Sha256 is null || (fingerprint is not null && fingerprint.StartsWith(entry.Sha256!, StringComparison.OrdinalIgnoreCase));
                if (!entry.DebugOnly && allowed && present && fpOk) productionKeys++;

                var mark = !present ? "⛔ فایل کلید نیست"
                         : !fpOk ? "⛔ اثر انگشت نمی‌خواند"
                         : !allowed ? "⛔ فقط Debug (در Release بارگذاری نمی‌شود)"
                         : "✔";
                Console.WriteLine($"    {mark} {entry.KeyId,-14} {entry.Algorithm,-6} {entry.File,-24} " +
                                  $"debugOnly={entry.DebugOnly}");
                if (fingerprint is not null) Console.WriteLine($"        اثر انگشت: {fingerprint}");
            }

            Console.WriteLine();
            if (!string.IsNullOrWhiteSpace(expectedKeyId))
            {
                var match = entries.FirstOrDefault(e => string.Equals(e.KeyId, expectedKeyId, StringComparison.OrdinalIgnoreCase));
                if (match.KeyId is null)
                {
                    Console.Error.WriteLine($"  ⛔ کلید «{expectedKeyId}» در manifest این بیلد نیست ⇒ خطای «کلید شناخته‌شده نیست» طبیعی است.");
                    return 6;
                }
                Console.WriteLine($"  ✔ کلید «{expectedKeyId}» در manifest این بیلد هست.");
            }

            if (productionKeys == 0)
            {
                Console.Error.WriteLine("  ⛔ این بیلد هیچ کلید تولیدی معتبری ندارد ⇒ هیچ لایسنس واقعی روی آن فعال نمی‌شود.");
                return 6;
            }

            Console.WriteLine($"  ✔ کلیدهای تولیدی معتبر در این بیلد: {productionKeys}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ خواندن اسمبلی ناموفق بود: {ex.GetType().Name}: {ex.Message}");
            return 2;
        }
    }

    /// <summary>خواندن دادهٔ یک ManifestResource جاسازی‌شده با نقشهٔ RVA → فایل.</summary>
    private static byte[]? ReadEmbeddedResource(PEReader pe, int resourcesRva, long offset)
    {
        if (resourcesRva == 0) return null;
        var data = pe.GetSectionData((int)(resourcesRva + offset));
        if (data.Length == 0) return null;

        var bytes = data.GetContent().ToArray();     // ImmutableArray<byte> → byte[]
        if (bytes.Length < 4) return null;
        var length = BitConverter.ToInt32(bytes, 0);
        if (length <= 0 || length > bytes.Length - 4) return null;

        var payload = new byte[length];
        Array.Copy(bytes, 4, payload, 0, length);
        return payload;
    }

    /// <summary>
    /// نمای «به‌ازای هر شرکت» از یک یا چند فایل لایسنس.
    ///
    /// <para>
    /// جریان کار: هر شرکت فعال، ادمین خودش را دارد و ادمین همان شرکت درخواست می‌فرستد؛ پشتیبان
    /// سند را امضا می‌کند و ادمین شرکت بارگذاری می‌کند. این دستور نشان می‌دهد روی یک نصب، هر شرکت
    /// با کدام سند چه ماژول‌ها و چند صندلی دارد — و آیا سندی هست که شرکت را نام نبرده باشد.
    /// </para>
    /// </summary>
    private static int Companies(string[] args)
    {
        // مقدار گزینه‌ها نباید به‌عنوان نام فایل لایسنس شمرده شود.
        var valueOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "--public-key", "--manifest", "--key-id", "--installation-id" };
        var files = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (valueOptions.Contains(a)) { i++; continue; }
            if (a.StartsWith("--")) continue;
            files.Add(a);
        }
        if (files.Count == 0)
        {
            Console.Error.WriteLine("❌ استفاده: companies <file.lic> [file2.lic ...] [--public-key <pem>|--manifest <json>]");
            return 2;
        }

        var documents = new List<LicenseDocument>();
        foreach (var file in files)
        {
            if (!File.Exists(file))
            {
                Console.Error.WriteLine($"❌ فایل نیست: {file}");
                return 2;
            }

            string? envelopeKeyId = null;
            try { envelopeKeyId = LicenseJson.ReadEnvelope(File.ReadAllText(file))?.KeyId; } catch { /* زیر گزارش می‌شود */ }
            var ring = BuildRing(args, envelopeKeyId);
            var result = LicenseVerifier.Verify(File.ReadAllText(file), ring);
            if (!result.Ok || result.Document is null)
            {
                Console.Error.WriteLine($"❌ {Path.GetFileName(file)}: {result.ErrorText}");
                return 4;
            }

            var issues = Inventory.Licensing.Validation.LicenseValidator.Validate(result.Document,
                new Inventory.Licensing.Validation.LicenseValidationContext { Now = PersianPeriodKey.NowIran().Date });
            var errors = issues.Where(i => i.Severity == LicenseIssueSeverity.Error).ToList();
            if (errors.Count > 0)
            {
                Console.Error.WriteLine($"❌ {Path.GetFileName(file)} ساختار معتبر ندارد:");
                foreach (var e in errors) Console.Error.WriteLine($"   • [{e.Code}] {e.Message}");
                return 6;
            }

            documents.Add(result.Document);
        }

        var resolver = CompanyLicenseResolver.Resolve(documents);
        Console.WriteLine($"فایل‌های بررسی‌شده: {documents.Count} | شرکت‌های دارای لایسنس: {resolver.ByCompany.Count}");
        Console.WriteLine();

        foreach (var companyId in resolver.CompanyIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var ent = resolver.For(companyId);
            var info = ent.FindCompany(companyId);
            var modules = ent.Modules
                .Where(kv => kv.Value.IsReadable && !kv.Value.IsAlwaysOn)
                .Select(kv => ModuleCodes.TitleOf(kv.Key))
                .OrderBy(x => x)
                .ToList();

            Console.WriteLine($"■ {companyId} — {info?.Name ?? "(بی‌نام)"}");
            Console.WriteLine($"   وضعیت: {ent.Status} | صندلی: {resolver.SeatCap(companyId)?.ToString() ?? "بدون سقف"}" +
                              $" | سریال: {string.Join(", ", ent.AppliedSerials)}");
            Console.WriteLine($"   ماژول‌ها ({(modules.Count == 0 ? "هیچ" : modules.Count.ToString())}): " +
                              (modules.Count == 0 ? "—" : string.Join("، ", modules)));
            if (info is { HasModuleRestriction: true })
                Console.WriteLine($"   محدود به ماژول‌های سند: {string.Join(", ", info.Modules)}");

            var warnings = ent.Issues.Where(i => i.Severity != LicenseIssueSeverity.Error).ToList();
            foreach (var w in warnings.Take(5))
                Console.WriteLine($"   • [{w.Code}] {w.Message}");
            Console.WriteLine();
        }

        if (resolver.Unscoped is not null)
        {
            Console.WriteLine($"⚠️ {resolver.Unscoped.AppliedSerials.Count} سند بدون فهرست شرکت روی این نصب هست: " +
                              string.Join(", ", resolver.Unscoped.AppliedSerials));
            Console.WriteLine("   این اسناد به هیچ شرکتی نسبت داده نمی‌شوند؛ برای فعال‌سازی درست، سند باید companyId داشته باشد.");
        }

        return 0;
    }

    private static int InspectRequest(string[] args)
    {
        var path = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : Arg(args, "--file");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Console.Error.WriteLine("❌ استفاده: inspect-request <license-request.json>");
            return 2;
        }

        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var root = json.RootElement;
        Console.WriteLine("درخواست فعال‌سازی:");
        foreach (var property in root.EnumerateObject())
        {
            if (property.NameEquals("note")) continue;
            Console.WriteLine($"  {property.Name,-18} : {property.Value}");
        }
        Console.WriteLine();
        Console.WriteLine("برای صدور: issue --profile profile.json --request <همین فایل> --private-key <pem> --out <out.lic>");
        return 0;
    }

    // ==================== helpers ====================

    private sealed class IssueProfile
    {
        public CustomerProfile Customer { get; set; } = new();
        public DeploymentKind Deployment { get; set; } = DeploymentKind.OnPrem;
        public List<string> UsePresets { get; set; } = new();
        public List<ItemProfile> Items { get; set; } = new();
        public List<LicenseQuota> Quotas { get; set; } = new();

        /// <summary>
        /// شرکت‌های مجاز (شرکت‌هایی که در نرم‌افزار اصلی تعریف شده‌اند).
        /// خالی = بدون محدودیت شرکت. هر شرکت: شناسه، نام، سقف کاربر و ماژول‌های مجاز.
        /// </summary>
        public List<CompanyProfile> Companies { get; set; } = new();

        public string? Note { get; set; }
    }

    /// <summary>یک شرکت در پروفایل صدور — به <c>LicenseCompany</c> سند نگاشت می‌شود.</summary>
    private sealed class CompanyProfile
    {
        public string CompanyId { get; set; } = "";
        public string? Name { get; set; }
        public int? MaxUsers { get; set; }
        public List<string> Modules { get; set; } = new();
        public string? Note { get; set; }
    }

    private sealed class CustomerProfile
    {
        public string Id { get; set; } = "";
        public string? Name { get; set; }
    }

    private sealed class ItemProfile
    {
        public string? Id { get; set; }
        public List<string> Scope { get; set; } = new();

        /// <summary>قابلیت‌های داخلی ماژول‌ها (مثل letter.incoming زیر اتوماسیون اداری).</summary>
        public List<string> Features { get; set; } = new();
        public TermProfile Term { get; set; } = new();
        public UsersProfile Users { get; set; } = new();
        public List<LicenseQuota> Quotas { get; set; } = new();
    }

    private sealed class TermProfile
    {
        public LicenseTermType Type { get; set; } = LicenseTermType.Subscription;
        public int? Months { get; set; }
        public DateTime? From { get; set; }
        public DateTime? Until { get; set; }
        public DateTime? UpdatesUntil { get; set; }
    }

    private sealed class UsersProfile
    {
        public LicenseUserMode Mode { get; set; } = LicenseUserMode.None;
        public int Max { get; set; }
        public LicenseUserScope AppliesTo { get; set; } = LicenseUserScope.Global;
        public int Exempt { get; set; }
        public List<string> ExemptUsers { get; set; } = new();
    }

    private static IssueProfile LoadProfile(string path)
    {
        var profile = JsonSerializer.Deserialize<IssueProfile>(File.ReadAllText(path), ProfileReadOptions)
                      ?? throw new InvalidOperationException("پروفایل خوانده نشد.");

        Normalize(profile);

        if (profile.UsePresets.Count > 0)
        {
            var presetsPath = Path.Combine(AppContext.BaseDirectory, "presets.json");
            if (!File.Exists(presetsPath))
                throw new InvalidOperationException($"presets.json کنار ابزار نیست: {presetsPath}");

            using var presets = JsonDocument.Parse(File.ReadAllText(presetsPath));
            var map = presets.RootElement.GetProperty("presets");
            var explicitIds = profile.Items.Select(i => i.Id).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var name in profile.UsePresets)
            {
                if (!map.TryGetProperty(name, out var preset))
                    throw new InvalidOperationException($"پیش‌تنظیم ناشناخته: {name}");
                if (explicitIds.Contains(name)) continue;   // آیتم صریح، preset را جایگزین می‌کند
                var item = JsonSerializer.Deserialize<ItemProfile>(preset.GetRawText(), ProfileReadOptions)!;
                item.Id = name;
                Normalize(item);
                profile.Items.Add(item);
            }
        }

        if (profile.Items.Count == 0)
            throw new InvalidOperationException("پروفایل هیچ آیتمی ندارد.");

        return profile;
    }

    /// <summary>
    /// پاک‌سازی مقادیر تهی در پروفایل.
    ///
    /// <para>
    /// چرا لازم است؟ در JSON، مقدار صریح <c>null</c> جای مقدار اولیهٔ کلاس را می‌گیرد؛
    /// پس پروفایلی که <c>"features": null</c> یا <c>"companies": null</c> دارد باعث
    /// <c>NullReferenceException</c> در حلقه‌های صدور می‌شد — و کاربر فقط پیام
    /// «ابزار ورودی را نپذیرفت (کد ۲)» را می‌دید. این‌جا هر فهرست تهی به فهرست خالی
    /// تبدیل می‌شود تا پروفایلِ دست‌ساز یا قدیمی هم بی‌دردسر خوانده شود.
    /// </para>
    /// </summary>
    private static void Normalize(IssueProfile profile)
    {
        profile.UsePresets ??= new List<string>();
        profile.Items ??= new List<ItemProfile>();
        profile.Quotas ??= new List<LicenseQuota>();
        profile.Companies ??= new List<CompanyProfile>();
        profile.Customer ??= new CustomerProfile();
        foreach (var item in profile.Items) Normalize(item);
        foreach (var company in profile.Companies)
        {
            company.CompanyId ??= "";
            company.Modules ??= new List<string>();
        }
    }

    private static void Normalize(ItemProfile? item)
    {
        if (item is null) return;
        item.Scope ??= new List<string>();
        item.Features ??= new List<string>();
        item.Quotas ??= new List<LicenseQuota>();
        item.Term ??= new TermProfile();
        item.Users ??= new UsersProfile();
        item.Users.ExemptUsers ??= new List<string>();
    }

    private static string? ResolveInstallationId(string[] args)
    {
        var requestPath = Arg(args, "--request");
        if (!string.IsNullOrWhiteSpace(requestPath))
        {
            if (!File.Exists(requestPath))
                throw new InvalidOperationException($"فایل درخواست نیست: {requestPath}");
            using var doc = JsonDocument.Parse(File.ReadAllText(requestPath));
            if (doc.RootElement.TryGetProperty("installationId", out var id))
                return id.GetString();
        }
        return Arg(args, "--installation-id");
    }

    private static LicenseDocument BuildDocument(IssueProfile profile, string? installationId, out List<string> warnings)
    {
        warnings = new List<string>();
        if (profile.Deployment == DeploymentKind.OnPrem && string.IsNullOrWhiteSpace(installationId))
            throw new InvalidOperationException("در حالت OnPrem باید --installation-id یا --request داده شود.");
        if (profile.Deployment == DeploymentKind.Saas && !string.IsNullOrWhiteSpace(installationId))
            warnings.Add("در حالت SaaS شناسهٔ نصب نوشته نمی‌شود (Tenant = Installation).");

        var today = PersianPeriodKey.NowIran().Date;
        var document = new LicenseDocument
        {
            KeyId = Arg(Environment.GetCommandLineArgs(), "--key-id") ?? "dev-2026a",
            Serial = Arg(Environment.GetCommandLineArgs(), "--serial") ?? NewSerial(),
            CustomerId = profile.Customer.Id,
            CustomerName = profile.Customer.Name,
            Deployment = profile.Deployment,
            InstallationId = profile.Deployment == DeploymentKind.OnPrem ? installationId : null,
            IssuedAt = DateTime.UtcNow,
            Note = profile.Note
        };

        var index = 1;
        foreach (var item in profile.Items)
        {
            var id = string.IsNullOrWhiteSpace(item.Id) ? $"ITEM-{index}" : item.Id!;
            index++;

            var scope = item.Scope.Select(ModuleCodes.Normalize).Where(ModuleCodes.IsKnown).Distinct().ToList();
            foreach (var requested in item.Scope)
                if (!ModuleCodes.IsKnown(ModuleCodes.Normalize(requested)))
                    warnings.Add($"ماژول ناشناخته «{requested}» در آیتم {id} نادیده گرفته شد.");

            var term = new LicenseTerm { Type = item.Term.Type };
            if (item.Term.Type == LicenseTermType.Perpetual)
            {
                term.UpdatesUntil = item.Term.UpdatesUntil;
                if (item.Term.Until is not null)
                    warnings.Add($"آیتم {id}: Perpetual نباید validUntil داشته باشد؛ نادیده گرفته شد.");
            }
            else
            {
                term.ValidFrom = item.Term.From ?? today;
                term.ValidUntil = item.Term.Until
                                  ?? (item.Term.Months is > 0 ? term.ValidFrom.Value.AddMonths(item.Term.Months.Value) : null);
                if (term.ValidUntil is null)
                    throw new InvalidOperationException($"آیتم {id}: برای {item.Term.Type} باید months یا until داده شود.");
                if (term.ValidUntil <= term.ValidFrom)
                    throw new InvalidOperationException($"آیتم {id}: تاریخ پایان باید بعد از شروع باشد.");
            }

            var users = new LicenseUsers
            {
                Mode = item.Users.Mode,
                Max = item.Users.Mode == LicenseUserMode.None ? 0 : item.Users.Max,
                AppliesTo = item.Users.AppliesTo,
                Exempt = item.Users.Mode == LicenseUserMode.None ? 0 : item.Users.Exempt,
                ExemptUsers = item.Users.Mode == LicenseUserMode.None ? new() : item.Users.ExemptUsers.ToList()
            };
            if (users.Mode != LicenseUserMode.None && users.Max <= 0)
                throw new InvalidOperationException($"آیتم {id}: حالت {users.Mode} بدون max معنا ندارد.");
            if (users.ExemptUsers.Count > users.Exempt)
                warnings.Add($"آیتم {id}: تعداد نام‌های معاف ({users.ExemptUsers.Count}) از سقف exempt ({users.Exempt}) بیشتر است؛ فقط {users.Exempt} نام اثر می‌کند.");

            // قابلیت‌ها: فقط قابلیت‌هایی که ماژول والدشان در scope همین آیتم است.
            var features = new List<string>();
            foreach (var rawFeature in item.Features)
            {
                var featureCode = FeatureCodes.Normalize(rawFeature);
                if (!FeatureCodes.IsKnown(featureCode))
                {
                    warnings.Add($"آیتم {id}: قابلیت ناشناخته «{rawFeature}» نادیده گرفته شد.");
                    continue;
                }

                if (!scope.Contains(FeatureCodes.ModuleOf(featureCode), StringComparer.OrdinalIgnoreCase))
                {
                    warnings.Add($"آیتم {id}: قابلیت «{FeatureCodes.TitleOf(featureCode)}» بدون ماژول والدش " +
                                 $"«{ModuleCodes.TitleOf(FeatureCodes.ModuleOf(featureCode))}» در scope بود؛ نادیده گرفته شد.");
                    continue;
                }

                if (!features.Contains(featureCode, StringComparer.OrdinalIgnoreCase)) features.Add(featureCode);
            }

            document.Items.Add(new LicenseItem
            {
                Id = id,
                Scope = scope,
                Features = features,
                Term = term,
                Users = users,
                Quotas = item.Quotas.Concat(profile.Quotas).ToList(),
                Note = null
            });
        }

        // ---------- شرکت‌های مجاز ----------
        foreach (var company in profile.Companies)
        {
            var cid = company.CompanyId?.Trim() ?? "";
            if (cid.Length == 0)
            {
                warnings.Add("یک شرکت بدون companyId در پروفایل بود و نادیده گرفته شد.");
                continue;
            }

            if (document.Companies.Any(c => string.Equals(c.CompanyId, cid, StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add($"شرکت «{cid}» در پروفایل تکراری بود؛ فقط اولی نوشته شد.");
                continue;
            }

            var modules = new List<string>();
            foreach (var raw in company.Modules)
            {
                var code = ModuleCodes.Normalize(raw);
                if (!ModuleCodes.IsKnown(code))
                {
                    warnings.Add($"شرکت «{cid}»: ماژول ناشناخته «{raw}» نادیده گرفته شد.");
                    continue;
                }
                if (ModuleCodes.IsAlwaysOn(code)) continue;      // همیشه‌فعال، نیازی به ذکر ندارد
                if (!modules.Contains(code, StringComparer.OrdinalIgnoreCase)) modules.Add(code);
            }

            document.Companies.Add(new LicenseCompany
            {
                CompanyId = cid,
                Name = string.IsNullOrWhiteSpace(company.Name) ? null : company.Name!.Trim(),
                MaxUsers = company.MaxUsers is null or < 0 ? null : company.MaxUsers,
                Modules = modules,
                Note = company.Note
            });
        }

        // تعارض سیاست کاربر: در یک سند، آیتم‌هایی که کاربر تعریف می‌کنند باید هم‌خانواده باشند.
        var userModes = document.Items.Where(i => i.Users.Mode != LicenseUserMode.None).Select(i => i.Users.Mode).Distinct().ToList();
        if (userModes.Count > 1)
            warnings.Add("در یک سند، دو آیتم با سیاست کاربر متفاوت (Named و Concurrent) وجود دارد؛ نتیجهٔ ادغام ممکن است محدودتر از انتظار باشد.");

        return document;
    }

    private static List<LicenseDocument> LoadExisting(string[] args)
    {
        var list = new List<LicenseDocument>();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "--existing") continue;
            var path = args[i + 1];
            if (!File.Exists(path)) throw new InvalidOperationException($"فایل موجود نیست: {path}");
            var result = LicenseVerifier.Verify(File.ReadAllText(path), BuildRing(args));
            if (!result.Ok || result.Document is null)
                throw new InvalidOperationException($"فایل موجود قابل استفاده نیست ({path}): {result.ErrorText}");
            list.Add(result.Document);
        }
        return list;
    }

    private static EffectiveEntitlement BuildPreview(IReadOnlyCollection<LicenseDocument> documents)
        => EntitlementMerger.Merge(documents);

    private static void PrintPreview(LicenseDocument document, EffectiveEntitlement merged, List<string> warnings, int existingCount)
    {
        Console.WriteLine("پیش‌نمایش سند (بدون امضا):");
        Console.WriteLine($"  مشتری: {document.CustomerId} ({document.CustomerName}) | حالت: {document.Deployment}" +
                          (document.InstallationId is null ? "" : $" | نصب: {document.InstallationId}"));
        Console.WriteLine($"  سریال: {document.Serial} | آیتم‌ها: {document.Items.Count}" +
                          (existingCount > 0 ? $" | لایسنس‌های موجود در ادغام: {existingCount}" : ""));
        Console.WriteLine();
        Console.WriteLine("  آیتم‌ها:");
        foreach (var item in document.Items)
            Console.WriteLine($"    • {item.Id}: {string.Join(", ", item.Scope)} | {item.Term.Type} " +
                              $"{item.Term.ValidFrom:yyyy-MM-dd}→{(item.Term.ValidUntil?.ToString("yyyy-MM-dd") ?? "—")} | " +
                              $"کاربر: {item.Users.Mode} {item.Users.Max} | سهمیه: {item.Quotas.Count}");

        var previewFeatures = document.Items.SelectMany(i => i.Features).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (previewFeatures.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  قابلیت‌های تفکیک‌شده ({previewFeatures.Count}):");
            foreach (var feature in previewFeatures)
                Console.WriteLine($"    • {FeatureCodes.TitleOf(feature)} ({feature}) — در ماژول {ModuleCodes.TitleOf(FeatureCodes.ModuleOf(feature))}");
        }

        if (document.Companies.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  شرکت‌های مجاز ({document.Companies.Count}):");
            foreach (var c in document.Companies)
            {
                var mods = c.Modules.Count == 0 ? "همهٔ ماژول‌های سند" : string.Join(", ", c.Modules);
                var seats = c.MaxUsers is { } n ? $" | کاربر: {n}" : " | کاربر: تابع سقف سند";
                Console.WriteLine($"    • {c.CompanyId} ({c.Name ?? "بی‌نام"}) | {mods}{seats}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("  وضعیت ماژول‌ها بعد از ادغام با لایسنس‌های موجود:");
        foreach (var module in ModuleCodes.All)
        {
            var state = merged.GetState(module.Code);
            var mark = state is ModuleState.Valid or ModuleState.ExpiringSoon ? "فعال" : state == ModuleState.Disabled ? "غیرفعال" : state.ToString();
            if (module.AlwaysOn || state != ModuleState.Disabled)
                Console.WriteLine($"    {mark,-10} {module.Code,-14} {module.Title}");
        }

        var issues = merged.Issues.Count;
        Console.WriteLine();
        Console.WriteLine($"  هشدارهای ادغام: {issues}");
        foreach (var issue in merged.Issues.Take(15))
            Console.WriteLine($"    • [{issue.Severity}] {issue.Code}: {issue.Message}");
        if (warnings.Count > 0)
        {
            Console.WriteLine($"  هشدارهای پروفایل: {warnings.Count}");
            foreach (var w in warnings) Console.WriteLine($"    • {w}");
        }
    }

    private static byte[] SignAndSerialize(LicenseDocument document, string[] args)
    {
        var payload = LicenseJson.ToPayloadBytes(document);
        using var rsa = LoadPrivateKey(args);
        var signature = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        document.KeyId = Arg(args, "--key-id") ?? document.KeyId;
        var envelope = new SignedLicenseDocument
        {
            EnvelopeVersion = 1,
            KeyId = document.KeyId,
            Algorithm = Signing,
            Payload = Convert.ToBase64String(payload),
            Signature = Convert.ToBase64String(signature)
        };

        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, EnvelopeWriteOptions));
    }

    private static RSA LoadPrivateKey(string[] args)
    {
        var path = Arg(args, "--private-key") ?? Environment.GetEnvironmentVariable("LICENSETOOL_PRIVATE_KEY");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidOperationException(
                "کلید خصوصی داده نشد. از --private-key یا متغیر محیطی LICENSETOOL_PRIVATE_KEY استفاده کنید (هرگز در مخزن نگذارید).");

        var pem = File.ReadAllText(path);
        var rsa = RSA.Create();
        var password = Environment.GetEnvironmentVariable("LICENSETOOL_KEY_PASSWORD");
        if (pem.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal))
        {
            if (string.IsNullOrEmpty(password))
                throw new InvalidOperationException("کلید خصوصی رمزنگاری‌شده است ولی LICENSETOOL_KEY_PASSWORD خالی است.");
            rsa.ImportFromEncryptedPem(pem, password);
        }
        else
        {
            rsa.ImportFromPem(pem);
        }
        return rsa;
    }

    private static PublicKeyRing BuildRing(string[] args, string? envelopeKeyId = null)
    {
        var publicKeyPath = Arg(args, "--public-key");
        var keyId = Arg(args, "--key-id") ?? envelopeKeyId ?? "dev-2026a";
        if (string.IsNullOrWhiteSpace(publicKeyPath))
        {
            var manifest = Arg(args, "--manifest")
                           ?? Path.Combine(AppContext.BaseDirectory, "Keys", "manifest.json");
            if (File.Exists(manifest))
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(manifest))!;
                var entries = new List<PublicKeyEntry>();
                using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
                foreach (var key in doc.RootElement.GetProperty("keys").EnumerateArray())
                {
                    var file = key.GetProperty("file").GetString()!;
                    var pemPath = Path.Combine(dir, file);
                    if (!File.Exists(pemPath)) continue;
                    entries.Add(new PublicKeyEntry
                    {
                        KeyId = key.GetProperty("keyId").GetString()!,
                        Algorithm = key.TryGetProperty("algorithm", out var alg) ? alg.GetString() ?? Signing : Signing,
                        PublicKeyPem = File.ReadAllText(pemPath),
                        Sha256 = key.TryGetProperty("sha256", out var sha) ? sha.GetString() : null
                    });
                }
                if (entries.Count > 0) return PublicKeyRing.FromEntries(entries);
            }

            throw new InvalidOperationException(
                "کلید عمومی پیدا نشد. --public-key یا --manifest بدهید.");
        }

        return PublicKeyRing.FromEntries(new[]
        {
            new PublicKeyEntry
            {
                KeyId = keyId,
                Algorithm = Signing,
                PublicKeyPem = File.ReadAllText(publicKeyPath)
            }
        });
    }

    private static void AppendJournal(LicenseDocument document, string outPath, string? installationId, byte[] fileBytes)
    {
        var path = Environment.GetEnvironmentVariable("LICENSETOOL_JOURNAL");
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".licensetool", "issued-licenses.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        var entry = new
        {
            serial = document.Serial,
            at = document.IssuedAt.ToString("o"),
            customerId = document.CustomerId,
            customerName = document.CustomerName,
            deployment = document.Deployment.ToString(),
            installationId,
            file = Path.GetFileName(outPath),
            sha256 = Sha256Hex(fileBytes),
            items = document.Items.Select(i => new
            {
                id = i.Id,
                scope = i.Scope,
                term = i.Term.Type.ToString(),
                from = i.Term.ValidFrom?.ToString("yyyy-MM-dd"),
                until = i.Term.ValidUntil?.ToString("yyyy-MM-dd"),
                users = i.Users.Mode + ":" + i.Users.Max,
                quotas = i.Quotas.Count
            })
        };

        File.AppendAllText(path, JsonSerializer.Serialize(entry, new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }) + Environment.NewLine);
        Console.WriteLine($"   دفتر صدور: {path}");
    }

    private static string NewSerial()
        => "LIC-" + DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-" +
           Convert.ToHexString(RandomNumberGenerator.GetBytes(4));

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void RestrictPermissions(string path, bool isPrivateKey)
    {
        if (OperatingSystem.IsWindows()) return;   // ACL ویندوز در اسکریپت انتشار تنظیم می‌شود
        try
        {
            File.SetUnixFileMode(path, isPrivateKey
                ? UnixFileMode.UserRead | UnixFileMode.UserWrite
                : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }
        catch { /* بی‌صدا: بهترین تلاش */ }
    }

    private static string? Arg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
        LicenseTool — صدور و بازبینی لایسنس (فقط تیم ما، بیرون از بستهٔ نصب مشتری)

        دستورها:
          keygen --key-id <id> --out-dir <dir>
                 ساخت جفت‌کلید. کلید خصوصی با LICENSETOOL_KEY_PASSWORD رمز می‌شود.

          issue --profile <profile.json> [--request req.json | --installation-id <id>]
                [--existing a.lic ...] [--private-key <pem>] --out <out.lic> [--yes]

          bridge --days N --installation-id <id> --private-key <pem> --out <out.lic> [--yes]
                 لایسنس پل: همهٔ ۳۳ ماژول، اشتراکی، بدون سقف کاربر و سهمیه.

          keys --assembly <Inventory.Licensing.dll> [--expect sup-202611]
          keys --dir <پوشهٔ انتشار> [--expect sup-202611]
                 چه کلیدهایی واقعاً داخل این بیلد جاسازی شده‌اند؟ (تشخیص ریشه‌ای خطای «کلید ناشناخته»)

          companies <a.lic> [b.lic ...] [--public-key <pem>|--manifest <json>]
                 نمای «هر شرکت چه ماژول و چند صندلی دارد» (چند شرکت روی یک نصب).

          doctor [--license <file.lic>] [--private-key <pem>] [--public-key <pem>]
                 تشخیص یک‌مرحله‌ای «امضای فایل لایسنس معتبر نیست»: کلیدهای جاسازی‌شدهٔ این بیلد،
                 کلید امضا، و نتیجهٔ واقعی بررسی فایل. (کلید خصوصی چاپ نمی‌شود.)

          verify <file.lic> [--installation-id <id>] [--public-key <pem>|--manifest <json>]
          inspect-request <license-request.json>

        نکته‌ها:
          • بدون --yes هیچ فایلی امضا نمی‌شود (پیش‌نمایش ادغام را ببینید).
          • پس از امضا، فایل با «کلیدهای جاسازی‌شدهٔ محصول» سنجیده می‌شود؛ اگر نخواند نوشته نمی‌شود
            (کد خروج ۷). استثنای توسعه: --allow-foreign-key (نصب مشتری آن را رد می‌کند).
          • کلید خصوصی: --private-key یا LICENSETOOL_PRIVATE_KEY (هرگز در مخزن نگذارید).
          • دفتر صدور: LICENSETOOL_JOURNAL یا ~/.licensetool/issued-licenses.jsonl
        """);
    }
}
