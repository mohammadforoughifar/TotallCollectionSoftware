using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Inventory.Api.Data;
using Inventory.Api.Licensing.Providers;
using Inventory.Api.Licensing.Services;
using Inventory.Licensing.Abstractions;
using Inventory.Licensing.Crypto;
using Inventory.Licensing.Entitlement;
using Inventory.Licensing.Metrics;
using Inventory.Licensing.Model;
using Inventory.Licensing.Modules;
using Inventory.Licensing.Periods;
using Inventory.Licensing.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Licensing.Controllers;

/// <summary>
/// محدودکنندهٔ نرخ درخواست‌های فعال‌سازی.
///
/// <para>
/// چرا لازم است؟ endpoint فعال‌سازی و تولید فایل درخواست، وقتی لایسنس نامعتبر است
/// <b>بدون ورود</b> هم کار می‌کنند تا مشتری بتواند نصب را نجات دهد. همین «باز بودن»
/// بدون محدودیت، امکان حملهٔ سیل درخواست می‌داد.
/// </para>
///
/// <para>
/// سقف و پنجره <b>در کد</b> ثابت است و از appsettings خوانده نمی‌شود؛ مقدار قابل تنظیم
/// یعنی امکان بازکردن مسیر حمله با یک ویرایش فایل.
/// </para>
/// </summary>
public sealed class LicenseActivationThrottle
{
    private const int MaxAttempts = 10;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, Queue<DateTime>> _hits = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>آیا این کلید (معمولاً IP) اجازهٔ تلاش دیگر دارد؟</summary>
    public bool TryAcquire(string key, out int retryAfterSeconds)
    {
        retryAfterSeconds = 0;
        var now = DateTime.UtcNow;
        var queue = _hits.GetOrAdd(string.IsNullOrWhiteSpace(key) ? "unknown" : key, _ => new Queue<DateTime>());

        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() > Window) queue.Dequeue();

            if (queue.Count >= MaxAttempts)
            {
                var oldest = queue.Peek();
                retryAfterSeconds = Math.Max(1, (int)(Window - (now - oldest)).TotalSeconds);
                return false;
            }

            queue.Enqueue(now);
        }

        // پاک‌سازی خروجی‌های قدیمی — جلوگیری از رشد بی‌مرز دیکشنری
        if (_hits.Count > 500)
        {
            foreach (var entry in _hits.ToArray())
                if (now - entry.Value.LastOrDefault() > Window) _hits.TryRemove(entry.Key, out _);
        }

        return true;
    }
}

/// <summary>
/// API لایسنس — وضعیت، درخواست فایل، فعال‌سازی و مدیریت فایل‌های اعمال‌شده.
///
/// <para>
/// قاعدهٔ دسترسی (طبق تصمیم کارفرما):
/// <list type="bullet">
/// <item>وقتی لایسنس <b>نامعتبر/موجود نیست</b>: تولید فایل درخواست و آپلود لایسنس
/// بدون ورود هم کار می‌کند (وگرنه هیچ راه بازیابی وجود ندارد) — با محدودیت نرخ.</item>
/// <item>وقتی لایسنس <b>معتبر</b> است: همین endpointها فقط با نقش <c>Admin</c>.</item>
/// </list>
/// </para>
/// </summary>
[ApiController]
[Route("api/license")]
[AllowAnonymous] // دسترسی واقعی در هر اکشن با EnsureActivationAccessAsync بررسی می‌شود
public class LicenseController : ControllerBase
{
    private readonly ILicenseService _licenses;
    private readonly IInstallationIdentity _identity;
    private readonly PublicKeyRingProvider _keys;
    private readonly LicenseActivationThrottle _throttle;
    private readonly LicensePaths _paths;
    private readonly LicenseEventLogger _events;
    private readonly AppDbContext _db;
    private readonly LicensingOptions _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LicenseController> _log;

    public LicenseController(ILicenseService licenses, IInstallationIdentity identity,
        PublicKeyRingProvider keys, LicenseActivationThrottle throttle, LicensePaths paths,
        LicenseEventLogger events, AppDbContext db, LicensingOptions options,
        IConfiguration configuration, ILogger<LicenseController> log)
    {
        _licenses = licenses;
        _identity = identity;
        _keys = keys;
        _throttle = throttle;
        _paths = paths;
        _events = events;
        _db = db;
        _options = options;
        _configuration = configuration;
        _log = log;
    }

    // ==================== ۱) وضعیت ====================

    /// <summary>
    /// وضعیت کامل لایسنس — برای صفحهٔ «وضعیت لایسنس» و بنر هشدار.
    ///
    /// <para>
    /// این endpoint در فهرست «آزاد» دروازهٔ لایسنس است، چون صفحهٔ فعال‌سازی و بنر
    /// «لایسنس نامعتبر» باید بدون ورود هم کار کنند. پس دادهٔ کامل (نام مشتری، آیتم‌ها،
    /// سهمیه‌ها، شناسهٔ فایل‌ها) فقط به کاربر <b>احراز هویت‌شده</b> داده می‌شود و
    /// بازدیدکنندهٔ ناشناس فقط خلاصهٔ حداقلی را می‌بیند.
    /// </para>
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        var entitlement = await _licenses.GetEffectiveEntitlementAsync(cancellationToken);
        var installationId = await _identity.GetInstallationIdAsync(cancellationToken);

        if (entitlement.Status != LicenseOverallStatus.Valid || User.Identity?.IsAuthenticated != true)
            return Ok(new
            {
                status = entitlement.Status.ToString(),
                installationId,
                deployment = LicenseDeployment.FromConfiguration(_configuration).ToString(),
                issues = entitlement.Issues.Select(i => new { i.Code, severity = i.Severity.ToString(), i.Message }),
                canActivate = true
            });

        var usage = await BuildUsageRowsAsync(entitlement, cancellationToken);
        var grants = await _db.LicenseGrants.AsNoTracking()
            .Where(g => g.Status == "Active")
            .OrderBy(g => g.AppliedAt)
            .Select(g => new { g.Id, g.Serial, g.KeyId, g.FileName, g.AppliedAt, g.IsAddOn, g.ValidFrom, g.ValidUntil, g.CustomerName })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            status = entitlement.Status.ToString(),
            installationId,
            provider = "SignedFile",
            customerId = entitlement.CustomerId,
            customerName = entitlement.CustomerName,
            deployment = LicenseDeployment.FromConfiguration(_configuration).ToString(),
            computedAt = entitlement.ComputedAt,
            updatesUntil = entitlement.UpdatesUntil,
            hasPerpetualItem = entitlement.HasPerpetualItem,
            appliedSerials = entitlement.AppliedSerials,
            seats = new
            {
                mode = entitlement.Seats.Mode.ToString(),
                globalMax = entitlement.Seats.GlobalMax,
                maxExempt = entitlement.Seats.MaxExempt,
                moduleMax = entitlement.Seats.ModuleMax
            },
            items = entitlement.Items.Select(i => new
            {
                i.ItemId,
                i.Serial,
                termType = i.TermType.ToString(),
                i.ValidFrom,
                i.ValidUntil,
                i.UpdatesUntil,
                state = i.IsActive ? "Active" : i.IsExpired ? "Expired" : "NotStarted",
                userMode = i.UserMode.ToString(),
                i.UserMax,
                i.UserExempt,
                userScope = i.UserScope.ToString(),
                modules = i.Modules.Select(m => new { code = m, title = ModuleCodes.TitleOf(m) }),
                i.CarryOverUsage
            }),
            modules = ModuleCodes.All.Select(m =>
            {
                var module = entitlement.FindModule(m.Code);
                return new
                {
                    code = m.Code,
                    title = m.Title,
                    alwaysOn = m.AlwaysOn,
                    state = entitlement.GetState(m.Code).ToString(),
                    stateTitle = StateTitle(entitlement.GetState(m.Code)),
                    expiresAt = module?.ExpiresAt,
                    primaryItemId = module?.PrimaryItemId,
                    termType = module?.PrimaryTermType
                };
            }),
            limits = usage,
            issues = entitlement.Issues.Select(i => new { i.Code, severity = i.Severity.ToString(), i.Message }),
            grants
        });
    }

    /// <summary>سلامت لایسنس — بدون ورود، سبک، برای Health Check و صفحهٔ ورود.</summary>
    [HttpGet("health")]
    public async Task<IActionResult> Health(CancellationToken cancellationToken)
    {
        var entitlement = await _licenses.GetEffectiveEntitlementAsync(cancellationToken);
        return Ok(new
        {
            ok = entitlement.Status == LicenseOverallStatus.Valid,
            status = entitlement.Status.ToString(),
            // نام مشتری عمداً برنمی‌گردد: این endpoint بدون ورود هم قابل صدا زدن است.
            // فهرست ماژول‌های غیرفعال تا کلاینت بتواند منو را پنهان کند
            disabledModules = ModuleCodes.All
                .Where(m => !m.AlwaysOn && entitlement.GetState(m.Code) == ModuleState.Disabled)
                .Select(m => m.Code),
            readOnlyModules = ModuleCodes.All
                .Where(m => entitlement.GetState(m.Code) == ModuleState.ReadOnly)
                .Select(m => m.Code),
            warningModules = ModuleCodes.All
                .Where(m => entitlement.GetState(m.Code) is ModuleState.ExpiringSoon or ModuleState.Grace)
                .Select(m => m.Code),
            serverTime = DateTime.Now
        });
    }

    // ==================== ۲) شناسهٔ نصب و فایل درخواست ====================

    /// <summary>اطلاعات نصب — برای صفحهٔ فعال‌سازی و تولید فایل درخواست.</summary>
    [HttpGet("installation")]
    public async Task<IActionResult> Installation(CancellationToken cancellationToken)
    {
        if (await EnsureActivationAccessAsync() is { } forbid) return forbid;

        var installationId = await _identity.GetInstallationIdAsync(cancellationToken);
        var customerName = await _identity.GetCustomerNameAsync(cancellationToken);

        return Ok(new
        {
            installationId,
            customerName,
            deployment = LicenseDeployment.FromConfiguration(_configuration).ToString(),
            productVersion = _options.ProductVersion ?? typeof(LicenseController).Assembly.GetName().Version?.ToString(),
            machineName = Environment.MachineName,
            osVersion = Environment.OSVersion.VersionString,
            serverTime = DateTime.Now,
            licenseFolder = _paths.Root,
            keyIds = _keys.Ring.KeyIds
        });
    }

    /// <summary>
    /// تولید فایل درخواست (Challenge) — مشتری این فایل را برای ما می‌فرستد و ما فایل
    /// امضاشده را برمی‌گردانیم. هیچ چیزی از این فایل قابل جعل نیست که به نفع مشتری باشد؛
    /// فقط شناسهٔ نصب و اطلاعات ماشین در آن است.
    /// </summary>
    [HttpGet("request-file")]
    public async Task<IActionResult> RequestFile(CancellationToken cancellationToken)
    {
        if (await EnsureActivationAccessAsync() is { } forbid) return forbid;
        if (!TryThrottle(out var blocked)) return blocked!;

        var installationId = await _identity.GetInstallationIdAsync(cancellationToken);
        var customerName = await _identity.GetCustomerNameAsync(cancellationToken);

        var request = new
        {
            requestVersion = 1,
            kind = "license-request",
            installationId,
            deployment = LicenseDeployment.FromConfiguration(_configuration).ToString(),
            customerName,
            productVersion = _options.ProductVersion ?? typeof(LicenseController).Assembly.GetName().Version?.ToString(),
            machineName = Environment.MachineName,
            osVersion = Environment.OSVersion.VersionString,
            requestedAt = DateTime.Now,
            requestedAtUtc = DateTime.UtcNow,
            note = "این فایل را برای دریافت لایسنس ارسال کنید. شناسهٔ نصب، کلید اصلی اتصال لایسنس به همین سرور است."
        };

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true });
        var bytes = new UTF8Encoding(false).GetBytes(json);
        var fileName = $"license-request-{installationId[..Math.Min(8, installationId.Length)]}.json";

        await _events.WriteDataAsync("REQUEST_FILE_CREATED", "فایل درخواست لایسنس تولید شد.",
            new { installationId }, userId: CurrentUserIdOrNull());

        return File(bytes, "application/json", fileName);
    }

    // ==================== ۳) فعال‌سازی ====================

    /// <summary>
    /// بارگذاری فایل لایسنس امضاشده.
    ///
    /// <para>
    /// مسیر: بررسی امضا → اعتبارسنجی ساختار → بررسی تکراری نبودن سریال → ذخیره →
    /// ابطال کش (<b>بدون ری‌استارت</b>) → بازخوانی فوری.
    /// </para>
    /// </summary>
    [HttpPost("activate")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Activate([FromForm] IFormFile? file, CancellationToken cancellationToken)
    {
        if (await EnsureActivationAccessAsync() is { } forbid) return forbid;
        if (!TryThrottle(out var blocked)) return blocked!;

        if (file is null || file.Length <= 0)
            return BadRequest(new { code = "LICENSE_FILE_EMPTY", message = "فایلی انتخاب نشده یا خالی است." });

        if (file.Length > LicenseVerifier.MaxFileBytes)
            return BadRequest(new { code = "LICENSE_FILE_TOO_LARGE", message = "حجم فایل لایسنس بیش از حد مجاز است (حداکثر ۲۵۶ کیلوبایت)." });

        string text;
        try
        {
            using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            text = await reader.ReadToEndAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return BadRequest(new { code = "LICENSE_FILE_UNREADABLE", message = $"خواندن فایل ممکن نشد: {ex.Message}" });
        }

        // ---------- ۱) امضا ----------
        var verification = LicenseVerifier.Verify(text, _keys.Ring);
        if (!verification.Ok || verification.Document is null)
        {
            await _events.WriteAsync("LICENSE_REJECTED", "فایل لایسنس رد شد: " + verification.ErrorText,
                severity: "Warning", userId: CurrentUserIdOrNull());
            return BadRequest(new { code = "LICENSE_SIGNATURE_INVALID", message = verification.ErrorText });
        }

        var document = verification.Document;
        var installationId = await _identity.GetInstallationIdAsync(cancellationToken);

        // ---------- ۲) ساختار و تطابق نصب ----------
        var appliedSerials = await _db.LicenseGrants.AsNoTracking()
            .Where(g => g.Status == "Active").Select(g => g.Serial).ToListAsync(cancellationToken);

        var issues = LicenseValidator.Validate(document, new LicenseValidationContext
        {
            ExpectedInstallationId = installationId,
            ExpectedDeployment = LicenseDeployment.FromConfiguration(_configuration),
            Now = DateTime.Now,
            AppliedSerials = appliedSerials
        });

        var errors = issues.Where(i => i.Severity == LicenseIssueSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            var message = string.Join(" ", errors.Select(e => e.Message));
            await _events.WriteAsync("LICENSE_REJECTED", "فایل لایسنس رد شد: " + message,
                severity: "Warning", userId: CurrentUserIdOrNull(), serial: document.Serial);
            return BadRequest(new { code = errors[0].Code, message, errors });
        }

        // ---------- ۳) ذخیره ----------
        var existingCount = appliedSerials.Count;

        // بازهٔ نمایشی سند = اجتماع بازهٔ آیتم‌ها (فقط برای فهرست/گزارش؛ ملاک واقعی payload است)
        var fromValues = document.Items.Where(i => i.Term.ValidFrom is not null).Select(i => i.Term.ValidFrom!.Value).ToList();
        var untilValues = document.Items.Where(i => i.Term.ValidUntil is not null).Select(i => i.Term.ValidUntil!.Value).ToList();
        var grant = new LicenseGrant
        {
            Serial = document.Serial.Trim(),
            KeyId = verification.Envelope!.KeyId,
            Algorithm = verification.Envelope.Algorithm,
            Payload = verification.Envelope.Payload,
            Signature = verification.Envelope.Signature,
            FileName = Path.GetFileName(file.FileName),
            Status = "Active",
            // Add-on = سندی که پس از سند پایه اضافه می‌شود. همهٔ اسناد در ادغام برابرند؛
            // این پرچم فقط برای نمایش/گزارش است.
            IsAddOn = existingCount > 0,
            CustomerName = document.CustomerName,
            ValidFrom = fromValues.Count > 0 ? fromValues.Min() : null,
            // اگر همهٔ آیتم‌ها دائمی باشند، سند تاریخ پایان ندارد (null) — نه Max خالی.
            ValidUntil = untilValues.Count > 0 && untilValues.Count == document.Items.Count ? untilValues.Max() : null,
            AppliedAt = DateTime.Now,
            AppliedByUserId = CurrentUserIdOrNull(),
            ValidationNote = issues.Count == 0 ? null : string.Join(" | ", issues.Select(i => i.Message))
        };

        _db.LicenseGrants.Add(grant);

        // نام مشتری روی ردیف نصب هم ثبت می‌شود (برای فایل درخواست بعدی)
        var installationRow = await _db.LicenseInstallations.OrderBy(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        if (installationRow is not null && !string.IsNullOrWhiteSpace(document.CustomerName))
        {
            installationRow.CustomerName = document.CustomerName;
            installationRow.UpdatedAt = DateTime.Now;
        }

        // نکته: جدول LicenseItemRows (آینهٔ آیتم‌ها برای پنل مدیریت) عمداً این‌جا پر نمی‌شود.
        // مرجع همیشه همان payload امضاشده است؛ نوشتن آینه در همین‌جا یعنی دو منبع حقیقت
        // و ردیف‌هایی که GrantId آن‌ها ست نشده است. نوشتن آینه به پنل ادمین SaaS
        // (مرحله ۵) سپرده می‌شود.

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // کلید یکتا روی Serial — تقریباً همیشه یعنی همین فایل قبلاً اعمال شده
            _log.LogWarning(ex, "[License] ذخیرهٔ فایل لایسنس ناموفق بود.");
            return BadRequest(new
            {
                code = "SERIAL_ALREADY_APPLIED",
                message = $"فایل با شمارهٔ سریال «{document.Serial}» قبلاً روی این نصب اعمال شده است."
            });
        }

        // ---------- ۴) آرشیو فایل روی دیسک (پشتیبان؛ مرجع دیتابیس است) ----------
        try
        {
            var archivePath = Path.Combine(_paths.GrantsFolder, $"{document.Serial}.license");
            await System.IO.File.WriteAllTextAsync(archivePath, text, new UTF8Encoding(false), cancellationToken);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[License] آرشیو فایل لایسنس روی دیسک ناموفق بود (دیتابیس سالم است).");
        }

        // ---------- ۵) اعمال فوری، بدون ری‌استارت ----------
        _licenses.Invalidate();
        var entitlement = await _licenses.GetEffectiveEntitlementAsync(cancellationToken);

        // ---------- ۵.۱) ثبت «مبنای مصرف» ----------
        // دلیل: برای متریک‌های «تعداد فعال»، رکوردهایی که پیش از فعال‌سازی در دیتابیس
        // بوده‌اند (مثلاً وارد‌شده از سیستم قبلی) نباید سقف را پر کنند. این‌جا لحظهٔ
        // فعال‌سازی به‌عنوان مبنا ثبت می‌شود؛ بعد از آن، هر رکورد تازه شمرده می‌شود.
        try
        {
            var quota = HttpContext.RequestServices.GetRequiredService<QuotaService>();
            var captured = await ActiveCountProbes.CaptureAllAsync(
                _db, quota, document.Serial, "activation", cancellationToken);

            if (captured > 0)
                _log.LogInformation("[License] مبنای مصرف برای {Count} متریک ثبت شد (سریال {Serial}).",
                    captured, document.Serial);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "[License] ثبت مبنای مصرف ناموفق بود؛ سقف‌ها بدون مبنا اعمال می‌شوند.");
        }

        await _events.WriteDataAsync("LICENSE_APPLIED",
            $"فایل لایسنس «{document.Serial}» اعمال شد.",
            new { document.Serial, document.CustomerName, items = document.Items.Count },
            userId: CurrentUserIdOrNull(), serial: document.Serial, cancellationToken: cancellationToken);

        return Ok(new
        {
            ok = true,
            serial = document.Serial,
            customerName = entitlement.CustomerName,
            status = entitlement.Status.ToString(),
            activeModules = entitlement.Modules.Where(m => m.Value.IsReadable).Select(m => m.Key),
            warnings = issues.Where(i => i.Severity != LicenseIssueSeverity.Error).Select(i => i.Message),
            message = "فایل لایسنس با موفقیت اعمال شد. نیازی به ری‌استارت برنامه نیست."
        });
    }

    // ==================== ۴) مدیریت فایل‌های اعمال‌شده ====================

    /// <summary>فهرست فایل‌های اعمال‌شده (فقط مدیر).</summary>
    [HttpGet("grants")]
    public async Task<IActionResult> Grants(CancellationToken cancellationToken)
    {
        if (!IsAdmin()) return AdminOnly();

        var rows = await _db.LicenseGrants.AsNoTracking()
            .OrderBy(g => g.AppliedAt)
            .Select(g => new
            {
                g.Id, g.Serial, g.KeyId, g.Algorithm, g.FileName, g.Status, g.IsAddOn,
                g.CustomerName, g.ValidFrom, g.ValidUntil, g.AppliedAt, g.AppliedByUserId, g.ValidationNote
            })
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    /// <summary>
    /// غیرفعال‌کردن یک فایل (حذف نمی‌شود؛ <c>Revoked</c> می‌شود تا تاریخچه بماند).
    /// مصرف ثبت‌شدهٔ سهمیه‌ها پاک نمی‌شود.
    /// </summary>
    [HttpPost("grants/{id:int}/revoke")]
    public async Task<IActionResult> Revoke(int id, CancellationToken cancellationToken)
    {
        if (!IsAdmin()) return AdminOnly();

        var grant = await _db.LicenseGrants.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (grant is null) return NotFound(new { message = "فایل لایسنس پیدا نشد." });

        grant.Status = "Revoked";
        await _db.SaveChangesAsync(cancellationToken);

        _licenses.Invalidate();
        await _licenses.GetEffectiveEntitlementAsync(cancellationToken);

        await _events.WriteAsync("LICENSE_REVOKED", $"فایل لایسنس «{grant.Serial}» غیرفعال شد.",
            severity: "Warning", userId: CurrentUserIdOrNull(), serial: grant.Serial, cancellationToken: cancellationToken);

        return Ok(new { message = "فایل لایسنس غیرفعال شد." });
    }

    // ==================== کمکی‌ها ====================

    /// <summary>
    /// قاعدهٔ دسترسی مشترک: تا وقتی لایسنس نامعتبر است، فعال‌سازی آزاد است (با محدودیت
    /// نرخ)؛ وقتی معتبر شد، فقط مدیر.
    /// </summary>
    private async Task<IActionResult?> EnsureActivationAccessAsync()
    {
        var status = (await _licenses.GetEffectiveEntitlementAsync()).Status;
        if (status != LicenseOverallStatus.Valid) return null;

        if (User.Identity?.IsAuthenticated != true)
            return Unauthorized(new { code = "LICENSE_AUTH_REQUIRED", message = "برای مدیریت لایسنس باید وارد شوید." });

        return IsAdmin() ? null : AdminOnly();
    }

    /// <summary>مدیر = نقش Admin یا مجوز Settings.Manage (هم‌راستا با سایر بخش‌های مدیریتی).</summary>
    private bool IsAdmin()
        => User.Identity?.IsAuthenticated == true
           && (User.IsInRole("Admin") || User.HasClaim("permission", "Settings.Manage"));

    private ObjectResult AdminOnly()
        => StatusCode(StatusCodes.Status403Forbidden, new
        {
            code = "LICENSE_ADMIN_ONLY",
            message = "مدیریت لایسنس فقط برای مدیر سیستم مجاز است."
        });

    private int? CurrentUserIdOrNull()
        => int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    private bool TryThrottle(out IActionResult? blocked)
    {
        blocked = null;
        var key = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (_throttle.TryAcquire(key, out var retryAfter))
        {
            return true;
        }

        Response.Headers["Retry-After"] = retryAfter.ToString();
        blocked = StatusCode(StatusCodes.Status429TooManyRequests, new
        {
            code = "LICENSE_THROTTLED",
            message = $"تعداد تلاش‌های فعال‌سازی زیاد است. لطفاً {Math.Ceiling(retryAfter / 60d)} دقیقهٔ دیگر تلاش کنید."
        });
        return false;
    }

    private async Task<List<object>> BuildUsageRowsAsync(EffectiveEntitlement entitlement, CancellationToken cancellationToken)
    {
        var rows = new List<object>();

        foreach (var limit in entitlement.Limits)
        {
            var periodKey = PersianPeriodKey.Now(limit.Period);
            var used = await _db.LicenseUsages.AsNoTracking()
                .Where(u => u.Module == limit.Module && u.Metric == limit.Metric && u.PeriodKey == periodKey)
                .Select(u => (long?)u.Used)
                .FirstOrDefaultAsync(cancellationToken) ?? 0;

            rows.Add(new
            {
                module = limit.Module,
                moduleTitle = ModuleCodes.TitleOf(limit.Module),
                metric = limit.Metric,
                metricTitle = MetricCodes.TitleOf(limit.Metric),
                unit = MetricCodes.UnitOf(limit.Metric),
                period = limit.Period.ToString(),
                periodKey,
                periodTitle = PersianPeriodKey.Describe(limit.Period, periodKey),
                limit = limit.Limit,
                used,
                percent = limit.Limit <= 0 ? 0 : Math.Round(used * 100d / limit.Limit, 1)
            });
        }

        return rows;
    }

    private static string StateTitle(ModuleState state) => state switch
    {
        ModuleState.Valid => "معتبر",
        ModuleState.ExpiringSoon => "در آستانهٔ انقضا",
        ModuleState.Grace => "مهلت تمدید",
        ModuleState.ReadOnly => "فقط خواندنی",
        _ => "غیرفعال"
    };
}
