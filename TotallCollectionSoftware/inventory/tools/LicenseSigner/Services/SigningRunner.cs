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
    bool PrivateKeyFound, bool PublicKeyFound, string? PresetsPath);

/// <summary>
/// چرخهٔ امضا از داخل پنل: پروفایل پیش‌نویس → <c>LicenseTool issue</c> → بازبینی فوری →
/// ردیف <see cref="IssuedLicense"/> + ممیزی. تنها مسیری که «صادرشده» می‌سازد همین است.
/// </summary>
public sealed class SigningRunner(SupportDbContext db, SignerOptions options, LicenseToolRunner runner,
    DraftService drafts, LicenseInspector inspector, AuditChainService audit, RequestService requests,
    ILogger<SigningRunner> log)
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
            options.EffectiveWorkFolder(), privateKey, publicKey, ResolvePresetsPath(tool));
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

        foreach (var (_, path) in await drafts.PreviousLicenseFilesAsync(request.InstallationId, ct))
        {
            args.Add("--existing");
            args.Add(path);
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

        var args = new List<string>(prepared.Args) { "--yes" };
        if (!string.IsNullOrWhiteSpace(keyId) && !args.Contains("--key-id"))
        {
            args.Add("--key-id");
            args.Add(keyId!);
        }

        try
        {
            var run = await runner.RunAsync(prepared.ToolPath!, args, keyPassword, ct);
            if (!run.Ok)
                return new SignOutcome
                {
                    Error = (run.ExitCode == 2 ? "امضا انجام نشد: ابزار ورودی را نپذیرفت (کد ۲)." : $"امضا انجام نشد (کد {run.ExitCode}).")
                            + Tail(run.Text),
                    Preview = run.Text
                };

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
