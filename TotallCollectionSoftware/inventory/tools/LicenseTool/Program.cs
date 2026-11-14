using System.Globalization;
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
                _ => Unknown(command)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ {ex.GetType().Name}: {ex.Message}");
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
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllBytes(outPath, bytes);

        AppendJournal(document, outPath, installationId, bytes);

        Console.WriteLine();
        Console.WriteLine($"✅ لایسنس صادر شد: {outPath}");
        Console.WriteLine($"   سریال: {document.Serial}   |   SHA256 فایل: {Sha256Hex(bytes)}");
        return 0;
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
                profile.Items.Add(item);
            }
        }

        if (profile.Items.Count == 0)
            throw new InvalidOperationException("پروفایل هیچ آیتمی ندارد.");

        return profile;
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

            document.Items.Add(new LicenseItem
            {
                Id = id,
                Scope = scope,
                Term = term,
                Users = users,
                Quotas = item.Quotas.Concat(profile.Quotas).ToList(),
                Note = null
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

          verify <file.lic> [--installation-id <id>] [--public-key <pem>|--manifest <json>]
          inspect-request <license-request.json>

        نکته‌ها:
          • بدون --yes هیچ فایلی امضا نمی‌شود (پیش‌نمایش ادغام را ببینید).
          • کلید خصوصی: --private-key یا LICENSETOOL_PRIVATE_KEY (هرگز در مخزن نگذارید).
          • دفتر صدور: LICENSETOOL_JOURNAL یا ~/.licensetool/issued-licenses.jsonl
        """);
    }
}
