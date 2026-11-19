using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LicenseSigner.Data;
using Microsoft.EntityFrameworkCore;

namespace LicenseSigner.Services;

/// <summary>نتیجهٔ امضا/پیش‌نمایش.</summary>
public sealed record SignOutcome
{
    public bool Signed { get; init; }
    public string? Error { get; init; }
    public string? Serial { get; init; }
    public string? Sha256 { get; init; }
    public long SizeBytes { get; init; }
    public bool VerifiedByPublicKey { get; init; }
    public bool VerifiedByInstallation { get; init; }
    public int? IssuedLicenseId { get; init; }
    public List<string> Warnings { get; init; } = new();
    public string Preview { get; init; } = "";
}

/// <summary>وضعیت آمادگی امضا (ابزار/کلیدها/پوشه).</summary>
public sealed record SigningStatus(bool Ready, string Reason, string? ToolPath, string WorkFolder,
    bool PrivateKeyFound, bool PublicKeyFound, string? PresetsPath,
    string? PrivateKeyPath = null, string? PublicKeyPath = null, string? KeyId = null);

/// <summary>
/// چرخهٔ امضا از داخل پنل: پروفایل پیش‌نویس → <c>LicenseTool issue</c> → بازبینی فوری →
/// ردیف <see cref="IssuedLicense"/> + ممیزی. تنها مسیری که «صادرشده» می‌سازد همین است.
/// </summary>
public sealed class SigningRunner(SupportDbContext db, SignerOptions options, LicenseToolRunner runner,
    DraftService drafts, LicenseInspector inspector, AuditChainService audit, RequestService requests,
    SignerKeyService keys, ILogger<SigningRunner> log)
{
    public string LicenseFolder => Path.Combine(options.EffectiveWorkFolder(), "licenses");

    public string? ResolveToolPath()
    {
        if (!string.IsNullOrWhiteSpace(options.ToolPath) && File.Exists(options.ToolPath)) return options.ToolPath;

        // جست‌وجوی معمول کنار مخزن (برای اجرای توسعه).
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "LicenseTool.dll"),
            Path.Combine(options.EffectiveWorkFolder(), "LicenseTool.dll")
        };
        foreach (var candidate in candidates)
            if (File.Exists(candidate)) return candidate;

        return null;
    }

    private string? ResolvePresetsPath(string? toolPath)
    {
        if (!string.IsNullOrWhiteSpace(options.PresetsPath) && File.Exists(options.PresetsPath))
            return options.PresetsPath;
        if (toolPath is null) return null;
        var beside = Path.Combine(Path.GetDirectoryName(toolPath)!, "presets.json");
        return File.Exists(beside) ? beside : null;
    }

    private static bool LooksLikeMissingPublicKey(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        (text.Contains("کلید عمومی پیدا نشد", StringComparison.Ordinal)
         || text.Contains("--public-key یا --manifest", StringComparison.Ordinal));

    private static bool LooksLikeWrongPassword(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Contains("password may be incorrect", StringComparison.OrdinalIgnoreCase)
               || text.Contains("EncryptedPrivateKeyInfo", StringComparison.OrdinalIgnoreCase)
               || text.Contains("کلید خصوصی رمزنگاری‌شده است ولی", StringComparison.Ordinal)
               || text.Contains("رمزنگاری‌شده است", StringComparison.Ordinal);
    }

    /// <summary>انتهای متن ابزار برای پیام خطای قابل‌فهم (بدون هیچ رازی؛ ابزار کلید چاپ نمی‌کند).</summary>
    private static string Tail(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var trimmed = text.Trim();
        if (trimmed.Length > 400) trimmed = "…" + trimmed[^400..];
        return " " + trimmed;
    }

    public SigningStatus GetStatus()
    {
        var tool = ResolveToolPath();
        var privateKey = !string.IsNullOrWhiteSpace(options.PrivateKeyPath) && File.Exists(options.PrivateKeyPath);
        var publicKey = !string.IsNullOrWhiteSpace(options.PublicKeyPath) && File.Exists(options.PublicKeyPath);

        var reason = tool is null ? "ابزار LicenseTool پیدا نشد (Signer:ToolPath)."
            : !privateKey ? "کلید خصوصی پیدا نشد (Signer:PrivateKeyPath)."
            : !publicKey ? "کلید عمومی پیدا نشد (Signer:PublicKeyPath)."
            : "";

        return new SigningStatus(tool is not null && privateKey && publicKey, reason, tool,
            options.EffectiveWorkFolder(), privateKey, publicKey, ResolvePresetsPath(tool),
            options.PrivateKeyPath, options.PublicKeyPath, options.KeyId);
    }

    private sealed record Prepared(string? Error, string? ToolPath, List<string> Args, string WorkFolder,
        List<string> Warnings, Action Cleanup, string? OutPath, string? RequestPath);

    private async Task<Prepared> PrepareAsync(LicenseRequest request, LicenseDraft draft, CancellationToken ct)
    {
        var warnings = new List<string>();
        var tool = ResolveToolPath();
        if (tool is null)
            return new Prepared("ابزار LicenseTool پیدا نشد (Signer:ToolPath).", null, new List<string>(), "", warnings,
                () => { }, null, null);

        if (string.IsNullOrWhiteSpace(draft.ProfileJson))
            return new Prepared("پروفایل پیش‌نویس خالی است؛ دوباره ذخیره کنید.", null, new List<string>(), "", warnings,
                () => { }, null, null);

        // ابزار presets.json را فقط کنار فایل اجرایی خودش می‌جوید (گزینهٔ CLI ندارد).
        var presetsBesideTool = File.Exists(Path.Combine(Path.GetDirectoryName(tool)!, "presets.json"));
        if (draft.PresetsCsv is not null && !presetsBesideTool)
            return new Prepared("presets.json کنار فایل اجرایی LicenseTool نیست و ابزار آن را برای بسته‌های آماده لازم دارد؛ " +
                                "فایل presets.json را کنار LicenseTool.dll کپی کنید (یا بستهٔ آماده را از پیش‌نویس بردارید).",
                null, new List<string>(), "", warnings, () => { }, null, null);

        var folder = Path.Combine(options.EffectiveWorkFolder(), "signing");
        Directory.CreateDirectory(folder);
        var stamp = Guid.NewGuid().ToString("N");
        var profilePath = Path.Combine(folder, $"profile-{stamp}.json");
        var requestPath = Path.Combine(folder, $"request-{stamp}.json");
        var outPath = Path.Combine(folder, $"out-{stamp}.lic");

        await File.WriteAllTextAsync(profilePath, draft.ProfileJson, ct);
        await File.WriteAllTextAsync(requestPath, request.PayloadJson, ct);

        var args = new List<string>
        {
            "issue",
            "--profile", profilePath,
            "--request", requestPath,
            "--private-key", options.PrivateKeyPath,
            "--out", outPath
        };

        if (!string.IsNullOrWhiteSpace(options.KeyId))
        {
            args.Add("--key-id");
            args.Add(options.KeyId);
        }

        var previousFiles = await drafts.PreviousLicenseFilesAsync(request.InstallationId, ct);
        if (previousFiles.Count > 0)
        {
            // برای بازبینی لایسنس‌های قبلی، ابزار به کلید عمومی نیاز دارد؛ وگرنه با
            // «InvalidOperationException: کلید عمومی پیدا نشد» متوقف می‌شود.
            if (!string.IsNullOrWhiteSpace(options.PublicKeyPath) && File.Exists(options.PublicKeyPath))
            {
                args.Add("--public-key");
                args.Add(options.PublicKeyPath);
            }
            else
            {
                return new Prepared("این نصب لایسنس فعال قبلی دارد و برای ادغام باید کلید عمومی در دسترس باشد " +
                                    "(Signer:PublicKeyPath)؛ مسیر را در «تنظیمات ← کلیدهای امضا» بررسی کنید.",
                    tool, new List<string>(), folder, warnings, () => { }, outPath, requestPath);
            }

            foreach (var (_, path) in previousFiles)
            {
                args.Add("--existing");
                args.Add(path);
            }
        }

        var previousCount = await db.IssuedLicenses.CountAsync(
            l => l.InstallationId == request.InstallationId && l.Status == LicenseSigner.Data.LicenseStatus.Current, ct);
        if (previousCount > 0)
            warnings.Add($"{JalaliDate.PersianDigits(previousCount.ToString())} لایسنس فعال قبلی در پیش‌نمایش ادغام شد.");

        void Cleanup()
        {
            foreach (var path in new[] { profilePath, requestPath, outPath })
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { /* بی‌صدا */ }
            }
        }

        return new Prepared(null, tool, args, folder, warnings, Cleanup, outPath, requestPath);
    }

    /// <summary>پیش‌نمایش ابزار بدون امضا (کد خروج ۳).</summary>
    public async Task<SignOutcome> PreviewAsync(int requestId, CancellationToken ct = default)
    {
        var request = await db.Requests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null) return new SignOutcome { Error = "درخواست پیدا نشد." };

        var draft = await drafts.CurrentAsync(requestId, ct);
        if (draft is null) return new SignOutcome { Error = "پیش‌نویس امضا وجود ندارد؛ ابتدا پیش‌نویس بسازید." };

        var prepared = await PrepareAsync(request, draft, ct);
        if (prepared.Error is not null) return new SignOutcome { Error = prepared.Error };

        try
        {
            var run = await runner.RunAsync(prepared.ToolPath!, prepared.Args, null, ct);
            if (run.ExitCode == 3)
                return new SignOutcome { Preview = run.Text, Warnings = prepared.Warnings };

            return new SignOutcome
            {
                Error = run.ExitCode switch
                {
                    0 => "ابزار بدون تأیید امضا کرد؛ این حالت نباید رخ دهد — متوقف شد.",
                    2 => "ابزار ورودی را نپذیرفت (کد ۲)." + Tail(run.Text),
                    -2 => "مهلت اجرای ابزار تمام شد.",
                    _ => $"پیش‌نمایش ابزار با کد {run.ExitCode} تمام شد."
                },
                Preview = run.Text
            };
        }
        finally
        {
            prepared.Cleanup();
        }
    }

    /// <summary>امضای واقعی — تنها جایی که یک <see cref="IssuedLicense"/> ساخته می‌شود.</summary>
    public async Task<SignOutcome> SignAsync(int requestId, SupportUser actor, string? keyId, string? keyPassword,
        bool supersedePrevious, CancellationToken ct = default)
    {
        var request = await db.Requests.Include(r => r.Customer).FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null) return new SignOutcome { Error = "درخواست پیدا نشد." };

        var hasIdentity = await db.IdentityChecks.AnyAsync(i => i.RequestId == requestId, ct);
        var check = Workflow.CheckSign(request.Status, actor.Role, hasIdentity);
        if (!check.Ok) return new SignOutcome { Error = check.Error };

        var draft = await drafts.CurrentAsync(requestId, ct);
        if (draft is null) return new SignOutcome { Error = "پیش‌نویسی وجود ندارد؛ ابتدا «پیش‌نویس امضا» بسازید." };

        var prepared = await PrepareAsync(request, draft, ct);
        if (prepared.Error is not null) return new SignOutcome { Error = prepared.Error };

        // ⛔ پیش‌بررسی «هم‌خوانی کلید امضا با کلیدهای داخل همین بیلد».
        // چرا لازم شد؟ چون اگر پنل یا LicenseTool از سورس قدیمی بیلد شده باشند، دروازهٔ ابزار
        // فایل را نمی‌نویسد و پیامِ ابزار شبیه «کلید محصول اشتباه است» دیده می‌شد؛ در حالی که
        // مشکل، کهنه بودن خودِ پنل/ابزار بود. این‌جا تفکیک می‌کنیم تا راه‌حل روشن باشد.
        var precheck = keys.CheckAgainstProductKeys(keyPassword);
        if (!precheck.Ok && precheck.SignerFingerprint is { Length: 64 })
        {
            return new SignOutcome
            {
                Error = "امضا انجام نشد: کلید امضا با کلیدهای جاسازی‌شده در همین بیلد پنل/ابزار یکی نیست. " +
                        $"اثر انگشت کلید امضا: {precheck.SignerFingerprint[..16]}… | کلیدهای این بیلد: " +
                        string.Join("، ", precheck.ProductKeys.Select(k => $"{k.KeyId}={k.Fingerprint[..12]}…")) + ". " +
                        "دو حالت دارد: (۱) کلید عمومی محصول را تازه ثبت کرده‌اید و پنل/LicenseTool را دوباره بیلد نکرده‌اید " +
                        "⇒ از آخرین سورس، LicenseTool و پنل را Release بیلد کنید و دوباره امضا کنید. " +
                        "(۲) واقعاً کلید امضا عوض شده ⇒ ردیف تازه با شناسهٔ تازه در manifest ثبت و نسخهٔ محصول را منتشر کنید. " +
                        "مقایسهٔ دقیق: تنظیمات ← کارت «تشخیص»."
            };
        }

        // ⛔ پیش‌بررسی «کلیدِ همین نصب مشتری».
        // فایل درخواست از بیلدهای تازه فهرست کلیدهای مورد اعتماد همان نصب را همراه دارد
        // (supportedSigningKeys). منبع حقیقت همان است، نه بیلد پشتیبان: اگر کلید پنل در آن
        // فهرست نباشد، فایل امضاشده روی نصب مشتری رد می‌شود؛ پس همان‌جا متوقف می‌شویم.
        var customerKeys = RequestParser.SupportedSigningKeys(request.PayloadJson);
        if (customerKeys.Count > 0)
        {
            var signerFingerprint = precheck.SignerFingerprint
                                    ?? keys.DescribeActiveKey().Fingerprint;

            var matches = signerFingerprint is { Length: 64 } && customerKeys.Any(k =>
                string.Equals(k.FingerprintSha256, signerFingerprint, StringComparison.OrdinalIgnoreCase));

            if (!matches)
            {
                var customerList = string.Join("، ", customerKeys.Select(k =>
                    k.FingerprintSha256.Length >= 12 ? $"{k.KeyId}={k.FingerprintSha256[..12]}…" : k.KeyId));
                var signerText = signerFingerprint is { Length: 16 } ? signerFingerprint[..16] + "…" : "ناخوانا";

                return new SignOutcome
                {
                    Error = "امضا انجام نشد: کلید امضا با کلیدهایی که همین نصب مشتری می‌پذیرد یکی نیست. " +
                            $"کلید پنل: {signerText} | کلیدهای همین نصب: {customerList}. " +
                            "این فایل درخواست از بیلد تازهٔ محصول آمده و فهرست کلیدهای مورد اعتمادش را همراه دارد؛ " +
                            "یعنی نصب مشتری، امضای این کلید را قبول نمی‌کند. " +
                            "راه‌حل: برای امضای این درخواست همان کلید رسمی را وصل کنید (تنظیمات ← کلیدهای امضا ← کلید خصوصی رسمی)، " +
                            "یا اگر کلید را عمداً عوض کرده‌اید، کلید عمومی تازه را در محصول ثبت و نسخهٔ تازه را روی همین نصب منتشر کنید."
                };
            }
        }

        // پیش‌بررسی رمز: اگر رمز با کلید نمی‌خواند، ابزار اجرا نمی‌شود و پیام روشن داده می‌شود.
        var formPassword = string.IsNullOrEmpty(keyPassword) ? null : keyPassword;
        var envPassword = keys.EnvironmentPassword;
        var effectivePassword = formPassword ?? envPassword;
        var passwordCheck = keys.CheckPassword(effectivePassword);
        if (passwordCheck is { KeyExists: true, Encrypted: true, Matches: false })
        {
            var sourceLabel = formPassword is not null
                ? "رمزی که در فرم وارد کردید"
                : envPassword is not null
                    ? $"رمز متغیر محیطی {options.KeyPasswordEnvironmentVariable} (چون فیلد رمز خالی بود)"
                    : "هیچ رمزی (فیلد فرم خالی و متغیر محیطی ست نشده)";
            return new SignOutcome
            {
                Error = "امضا انجام نشد: رمز کلید خصوصی درست نیست. " + $"آنچه در اختیار است: {sourceLabel}. " +
                        "رمز درست همان رمزی است که هنگام ساخت کلید در «تنظیمات ← کلیدهای امضا» دادید؛ " +
                        $"یا متغیر {options.KeyPasswordEnvironmentVariable} را با آن هم‌تراز کنید. " +
                        "برای امتحان، در تنظیمات دکمهٔ «بررسی رمز کلید فعال» را بزنید."
            };
        }

        var args = new List<string>(prepared.Args) { "--yes" };
        if (!string.IsNullOrWhiteSpace(keyId) && !args.Contains("--key-id"))
        {
            args.Add("--key-id");
            args.Add(keyId!);
        }

        try
        {
            var run = await runner.RunAsync(prepared.ToolPath!, args, keyPassword, ct);
            if (run.ExitCode == 7)
            {
                // کد ۷ = دروازهٔ تحویل ابزار: فایل ساخته شد ولی «کلیدهای جاسازی‌شده در محصول»
                // آن را تأیید نکردند؛ یعنی همین فایل روی نصب مشتری با خطای «امضای فایل لایسنس
                // معتبر نیست» رد می‌شد. ابزار فایل را ننوشته است.
                return new SignOutcome
                {
                    Error = "امضا انجام نشد: کلید امضا با کلید عمومیِ داخل محصول نمی‌خواند و فایل ذخیره نشد " +
                            "(اگر ذخیره می‌شد، نصب مشتری آن را با خطای «امضای فایل لایسنس معتبر نیست» رد می‌کرد). " +
                            "دو راه: با کلید خصوصیِ همان جفتِ قبلی امضا کنید، یا کلید عمومیِ فعلی را در " +
                            "«Keys/manifest.json» محصول ثبت کنید (تنظیمات ← کلیدهای امضا ← ثبت در manifest محصول)، " +
                            "بیلد Release تازه بگیرید و همان نسخه را روی نصب مشتری جایگزین کنید.",
                    Preview = run.Text
                };
            }

            if (!run.Ok)
            {
                // «رمز اشتباه» شایع‌ترین خطای کد ۲ است؛ پیام رمزنگاری خام به کاربر نشان داده نمی‌شود.
                if (LooksLikeMissingPublicKey(run.Text))
                {
                    return new SignOutcome
                    {
                        Error = "امضا انجام نشد: ابزار برای بازبینی/ادغام لایسنس‌های قبلی این نصب به «کلید عمومی» نیاز دارد " +
                                "و آن را پیدا نکرد. مسیر Signer:PublicKeyPath را بررسی کنید (تنظیمات ← کلیدهای امضا) " +
                                "یا فایل کلید عمومی را کنار ابزار در پوشهٔ Keys/manifest.json ثبت کنید.",
                        Preview = run.Text
                    };
                }

                if (LooksLikeWrongPassword(run.Text))
                {
                    var source = run.PasswordSource switch
                    {
                        "form" => "رمزی که در فرم وارد کردید",
                        "env" => $"رمز متغیر محیطی {options.KeyPasswordEnvironmentVariable} (چون فیلد رمز خالی بود)",
                        _ => "هیچ رمزی (فیلد فرم خالی و متغیر محیطی ست نشده)"
                    };
                    return new SignOutcome
                    {
                        Error = "امضا انجام نشد: رمز کلید خصوصی درست نیست. " +
                                $"آنچه به ابزار داده شد: {source}. " +
                                "همان رمزی را وارد کنید که هنگام ساخت کلید در «تنظیمات ← کلیدهای امضا» دادید، " +
                                $"یا متغیر {options.KeyPasswordEnvironmentVariable} را با آن هم‌تراز کنید. " +
                                "برای امتحان بدون امضا، در تنظیمات دکمهٔ «بررسی رمز کلید فعال» را بزنید.",
                        Preview = run.Text
                    };
                }

                return new SignOutcome
                {
                    Error = (run.ExitCode == 2 ? "امضا انجام نشد: ابزار ورودی را نپذیرفت (کد ۲)." : $"امضا انجام نشد (کد {run.ExitCode}).")
                            + Tail(run.Text),
                    Preview = run.Text
                };
            }

            if (prepared.OutPath is null || !File.Exists(prepared.OutPath))
                return new SignOutcome { Error = "ابزار موفق گزارش داد ولی فایل لایسنس ساخته نشد.", Preview = run.Text };

            var bytes = await File.ReadAllBytesAsync(prepared.OutPath, ct);
            var text = Encoding.UTF8.GetString(bytes);
            var (signatureOk, installationOk, inspection) = inspector.VerifyFor(text, request.InstallationId);
            if (!signatureOk || !installationOk)
                return new SignOutcome
                {
                    Error = signatureOk
                        ? "فایل امضاشده با شناسهٔ نصب این درخواست نمی‌خواند؛ ذخیره نشد."
                        : "امضای فایل تولیدشده معتبر نیست؛ ذخیره نشد (" + inspection.Error + ")",
                    Preview = run.Text
                };

            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var serial = inspection.Serial ?? LicenseInspector.SerialFromFile(text) ?? "unknown";
            var fileName = SafeFileName(serial);
            Directory.CreateDirectory(LicenseFolder);
            var target = Path.Combine(LicenseFolder, fileName);
            await File.WriteAllBytesAsync(target, bytes, ct);

            if (supersedePrevious)
            {
                var current = await db.IssuedLicenses
                    .Where(l => l.InstallationId == request.InstallationId && l.Status == LicenseSigner.Data.LicenseStatus.Current)
                    .ToListAsync(ct);
                foreach (var old in current) old.Status = LicenseSigner.Data.LicenseStatus.Superseded;
            }

            var issued = new IssuedLicense
            {
                RequestId = request.Id,
                CustomerId = request.CustomerId,
                InstallationId = request.InstallationId,
                Serial = serial,
                KeyId = string.IsNullOrWhiteSpace(keyId) ? options.KeyId : keyId!,
                Algorithm = "RS256",
                FileName = fileName,
                FilePath = Path.Combine("licenses", fileName),
                Sha256 = sha,
                SizeBytes = bytes.LongLength,
                PayloadJson = inspection.ItemsText,
                ValidFrom = DateTime.UtcNow.Date,
                ValidUntil = inspection.ValidUntil,
                UpdatesUntil = draft.UpdatesUntil,
                Status = LicenseSigner.Data.LicenseStatus.Current,
                VerifiedByPublicKey = true,
                VerifiedByInstallation = installationOk,
                ByName = actor.DisplayName,
                CreatedAt = DateTime.UtcNow
            };
            db.IssuedLicenses.Add(issued);

            request.Status = RequestStatus.Issued;
            request.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            if (supersedePrevious)
            {
                foreach (var old in await db.IssuedLicenses
                             .Where(l => l.InstallationId == request.InstallationId && l.Id != issued.Id
                                         && l.Status == LicenseSigner.Data.LicenseStatus.Superseded)
                             .ToListAsync(ct))
                    old.SupersededByLicenseId = issued.Id;
            }

            await requests.AddEventAsync(request.Id, "SIGN", null, null,
                $"لایسنس امضا شد: سریال {serial}، کلید {issued.KeyId}، SHA256 {sha[..16]}…", actor, ct);
            await db.SaveChangesAsync(ct);

            await audit.AppendAsync(actor.UserName, "SIGN", "IssuedLicense", issued.Id.ToString(),
                JsonSerializer.Serialize(new { serial, requestId = request.Id, request.InstallationId, sha256 = sha, keyId = issued.KeyId }),
                actor.Id, ct);

            log.LogInformation("لایسنس {Serial} برای درخواست {No} امضا شد.", serial, request.RequestNo);

            return new SignOutcome
            {
                Signed = true,
                Serial = serial,
                Sha256 = sha,
                SizeBytes = bytes.LongLength,
                VerifiedByPublicKey = true,
                VerifiedByInstallation = installationOk,
                IssuedLicenseId = issued.Id,
                Warnings = prepared.Warnings,
                Preview = run.Text
            };
        }
        finally
        {
            prepared.Cleanup();
        }
    }

    public static string SafeFileName(string serial)
    {
        var cleaned = new string(serial.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return (cleaned.Length == 0 ? "license" : cleaned) + ".lic";
    }
}
