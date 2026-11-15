using System.Security.Cryptography;
using System.Text;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Licensing.Services;

/// <summary>وضعیت ساعت نسبت به لنگر (بالاترین زمانی که این نصب تا حالا دیده است).</summary>
public enum ClockHealth
{
    /// <summary>هنوز سنجیده نشده.</summary>
    Unknown = 0,

    /// <summary>ساعت سرور با لنگر هم‌خوان یا جلوتر است.</summary>
    Normal = 1,

    /// <summary>ساعت تا ۲۴ ساعت عقب کشیده شده — هشدار + بنر، نوشتن آزاد.</summary>
    MinorBackward = 2,

    /// <summary>ساعت بیش از ۲۴ ساعت عقب کشیده شده — فقط‌خواندنی تا رفع.</summary>
    MajorBackward = 3
}

/// <summary>نتیجهٔ سنجش ساعت.</summary>
public sealed record ClockVerdict(
    DateTime SystemNow,
    DateTime Anchor,
    DateTime? DataMax,
    DateTime EffectiveNow,
    TimeSpan Backward,
    ClockHealth Health,
    string Summary,
    IReadOnlyList<string> Stores)
{
    public static readonly ClockVerdict Unknown = new(
        DateTime.MinValue, DateTime.MinValue, null, DateTime.MinValue, TimeSpan.Zero,
        ClockHealth.Unknown, "ساعت هنوز سنجیده نشده است.", Array.Empty<string>());

    /// <summary>آیا نوشتن بسته است؟ فقط عقب‌کشیدن بیش از ۲۴ ساعت.</summary>
    public bool WritesBlocked => Health == ClockHealth.MajorBackward;

    /// <summary>آیا باید بنر/هشدار نشان داده شود؟</summary>
    public bool ShowBanner => Health is ClockHealth.MinorBackward or ClockHealth.MajorBackward;
}

/// <summary>نگه‌دارندهٔ ذخیره‌گاه‌های اضافی لنگر ساعت (فایل/رجیستری).</summary>
public interface IClockStore
{
    string Name { get; }
    string? Read();
    void Write(string value);
}

/// <summary>ذخیره‌گاه فایلی (ProgramData یا خانهٔ کاربر).</summary>
public sealed class FileClockStore : IClockStore
{
    public FileClockStore(string path) => Path = path;

    public string Name => "file:" + Path;

    public string Path { get; }

    public string? Read()
    {
        try { return File.Exists(Path) ? File.ReadAllText(Path).Trim() : null; }
        catch { return null; }
    }

    public void Write(string value)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var temp = Path + ".tmp";
            File.WriteAllText(temp, value);
            File.Move(temp, Path, overwrite: true);
        }
        catch { /* ذخیره‌گاه‌های اضافی «بهترین تلاش»اند */ }
    }
}

/// <summary>
/// ذخیره‌گاه رجیستری ویندوز (HKCU — بدون نیاز به دسترسی Administrator).
/// روی لینوکس این ذخیره‌گاه وجود ندارد و فقط فایل‌ها می‌مانند.
/// </summary>
public sealed class RegistryClockStore : IClockStore
{
    private const string SubKey = @"Software\TotallCollection\Licensing";
    private const string ValueName = "ClockAnchor";

    public string Name => "registry:HKCU";

    public static bool Supported => OperatingSystem.IsWindows();

    public string? Read()
    {
        if (!Supported) return null;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(SubKey);
            return key?.GetValue(ValueName)?.ToString();
        }
        catch { return null; }
    }

    public void Write(string value)
    {
        if (!Supported) return;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(SubKey);
            key?.SetValue(ValueName, value);
        }
        catch { /* بهترین تلاش */ }
    }
}

/// <summary>
/// نگه‌داری «بالاترین زمان معتبر دیده‌شده» در سه جا و مقایسه با ساعت سیستم.
///
/// <para>
/// <b>چرا؟</b> منطق لایسنس بر پایهٔ زمان است (انقضا، دورهٔ مصرف). اگر کسی ساعت سرور را
/// عقب بکشد، دورهٔ مصرف دوباره باز می‌شود و لایسنس منقضی دوباره زنده می‌شود. پس
/// بالاترین زمانی که تا حالا دیده‌ایم را نگه می‌داریم: از این به بعد «اکنون مؤثر» =
/// <c>max(ساعت سیستم، لنگر)</c> و عقب بردن ساعت هیچ سودی ندارد.
/// </para>
///
/// <para>
/// <b>سه ذخیره‌گاه</b> (تا با پاک‌کردن یکی، کنترل از کار نیفتد):
/// دیتابیس (<c>LicenseRuntime</c>، بدون تغییر اسکیمای)، فایل ProgramData،
/// و رجیستری <c>HKCU</c> (روی لینوکس: فایل خانهٔ کاربر).
/// مقدار در هر سه با <b>AES-GCM</b> (محرمانه + احراز اصالت) محافظت می‌شود؛ کلید
/// per-installation در <c>&lt;License&gt;/clock.key</c> ساخته می‌شود.
/// </para>
///
/// <para>
/// <b>رفتار:</b> تا ۲۴ ساعت عقب‌رفت ⇒ رخداد <c>CLOCK_SET_BACK</c> + بنر (نوشتن آزاد).
/// بیش از ۲۴ ساعت ⇒ <c>CLOCK_SET_BACK_BLOCK</c> + فقط‌خواندنی. به‌محض اینکه ساعت
/// واقعی به لنگر برسد، وضعیت خودکار به Normal برمی‌گردد (خودترمیمی؛ هیچ قفلی کهنه نمی‌شود).
/// </para>
///
/// <para>
/// ⚠️ <b>صداقت فنی:</b> این کنترل DRM نیست. کسی که هم‌زمان ساعت را عقب ببرد و هر سه
/// ذخیره‌گاه + کلید را پاک کند، می‌تواند لنگر را از نو بسازد. کنترل برای این‌هاست:
/// عقب بردن سادهٔ ساعت ویندوز، Restore بکاپ قدیمی، و خطای ساعت مجازی/VM.
/// </para>
/// </summary>
public sealed class ClockGuard
{
    public const string DbKey = "clock.anchor";
    public const string AlertKey = "clock.lastAlert";
    public const string MinorCode = "CLOCK_SET_BACK";
    public const string MajorCode = "CLOCK_SET_BACK_BLOCK";

    /// <summary>حاشیهٔ نوسان ساعت (همگام‌سازی NTP، کش سرور) — کمتر از این، «عقب‌رفت» نیست.</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(10);

    /// <summary>آستانهٔ فقط‌خواندنی.</summary>
    public static readonly TimeSpan BlockThreshold = TimeSpan.FromHours(24);

    /// <summary>هر چند وقت لنگر ارزش نوشتن تازه دارد (جلوگیری از نوشتن هر درخواست).</summary>
    private static readonly TimeSpan WriteEvery = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db;
    private readonly LicenseEventLogger _events;
    private readonly IReadOnlyList<IClockStore> _stores;
    private readonly byte[]? _key;
    private readonly ILogger<ClockGuard> _log;
    private readonly DevelopmentLicenseBypass? _bypass;

    public ClockGuard(AppDbContext db, LicensePaths paths, LicenseEventLogger events,
        ILogger<ClockGuard> log, DevelopmentLicenseBypass? bypass = null)
        : this(db, paths, events, log, BuildDefaultStores(paths), null, bypass) { }

    /// <summary>سازندهٔ قابل‌تست: ذخیره‌گاه‌ها و کلید قابل جایگزینی‌اند.</summary>
    public ClockGuard(AppDbContext db, LicensePaths paths, LicenseEventLogger events,
        ILogger<ClockGuard> log, IReadOnlyList<IClockStore> stores, byte[]? key,
        DevelopmentLicenseBypass? bypass = null)
    {
        _db = db;
        _events = events;
        _log = log;
        _stores = stores;
        _key = key ?? TryLoadOrCreateKey(paths, log);
        _bypass = bypass;
    }

    private static IReadOnlyList<IClockStore> BuildDefaultStores(LicensePaths paths)
    {
        var list = new List<IClockStore>
        {
            new FileClockStore(Path.Combine(paths.Root, "clock.anchor"))
        };

        if (RegistryClockStore.Supported) list.Add(new RegistryClockStore());
        else list.Add(new FileClockStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile,
                Environment.SpecialFolderOption.DoNotVerify), ".totallcollection", "clock.anchor")));

        return list;
    }

    /// <summary>آیا کلید حفاظت در دسترس است؟ (نبودش مانع کار برنامه نمی‌شود)</summary>
    public bool Enabled => _key is not null;

    /// <summary>
    /// سنجش کامل: خواندن لنگر از سه ذخیره‌گاه + بیشترین زمان داده، مقایسه با ساعت سیستم،
    /// و نوشتن لنگر تازه اگر ساعت جلو رفته باشد.
    /// </summary>
    public async Task<ClockVerdict> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        // ---- محیط توسعه: قفل ساعت کاملاً کنار می‌رود ----
        // چرا؟ چون توسعه‌دهنده مرتب ساعت ویندوز/VM را دست می‌زند (بازگردانی اسنپ‌شات، تغییر
        // منطقهٔ زمانی) و نتیجه‌اش در نسخهٔ واقعی «فقط‌خواندنی موقت» است؛ در توسعه این فقط
        // وقت تلف‌کردن است. وضعیت «Normal» منتشر می‌شود تا هیچ فیلتر نوشتنی و هیچ بنری
        // در Development فعال نشود.
        if (_bypass?.IsActive == true)
        {
            var devNow = Inventory.Api.Licensing.ClockAnchor.Now();
            var devVerdict = new ClockVerdict(devNow, devNow, null, devNow, TimeSpan.Zero,
                ClockHealth.Normal, "حالت توسعه — سنجش ساعت غیرفعال است.", new[] { "dev-bypass" });
            ClockGuardState.Publish(devVerdict);
            return devVerdict;
        }

        var systemNow = Inventory.Api.Licensing.ClockAnchor.Now();
        var observed = new List<(string Store, DateTime Value)>();

        // ---- ۱) لنگرهای ذخیره‌شده ----
        var (dbStore, dbRaw) = await ReadDbAsync(cancellationToken);
        var dbValue = Unprotect(dbRaw);
        var dbHasAnchor = dbValue is not null;
        if (dbValue is not null) observed.Add((dbStore, dbValue.Value));

        foreach (var store in _stores)
            if (Unprotect(store.Read()) is { } value) observed.Add((store.Name, value));

        // ---- ۲) بیشترین زمان داده (فاکتور/نامه/رخداد لایسنس) ----
        var dataMax = await ReadDataMaxAsync(cancellationToken);
        if (dataMax is { } dataValue) observed.Add(("data", dataValue));

        var anchor = observed.Count == 0 ? DateTime.MinValue : observed.Max(x => x.Value);
        var backward = anchor == DateTime.MinValue ? TimeSpan.Zero : anchor - systemNow;

        var health = backward <= Tolerance ? ClockHealth.Normal
            : backward > BlockThreshold ? ClockHealth.MajorBackward
            : ClockHealth.MinorBackward;

        // ---- ۳) اکنون مؤثر هرگز عقب‌تر از لنگر نیست ----
        var effectiveNow = anchor == DateTime.MinValue ? systemNow
            : (anchor > systemNow ? anchor : systemNow);

        // ---- ۴) نوشتن/به‌روزرسانی لنگر در هر سه ذخیره‌گاه ----
        //    شرط: نبودِ آن در دیتابیس، یا خالی‌بودن یکی از ذخیره‌گاه‌ها (تا هر سه هم‌گام شوند)،
        //    یا جلو رفتن معنادار ساعت. بدون شرط آخر، هر سنجش یک نوشتن می‌شد.
        var storeMissing = _stores.Any(store => Unprotect(store.Read()) is null);
        var wrote = false;
        if (health == ClockHealth.Normal
            && (!dbHasAnchor || storeMissing || anchor == DateTime.MinValue || systemNow - anchor >= WriteEvery))
            wrote = await WriteAnchorAsync(systemNow, cancellationToken);

        var stores = observed.Select(x => x.Store).Distinct().ToList();
        if (wrote) stores = stores.Concat(new[] { "db" }).Concat(_stores.Select(x => x.Name)).Distinct().ToList();
        var verdict = new ClockVerdict(systemNow, anchor, dataMax, effectiveNow, backward, health,
            BuildSummary(health, backward), stores);

        ClockGuardState.Publish(verdict);
        if (verdict.ShowBanner) await AlertOnceAsync(verdict, cancellationToken);
        else if (health == ClockHealth.Normal) await ClearAlertAsync(cancellationToken);

        if (health != ClockHealth.Normal)
            _log.LogWarning("[License] ساعت سرور {Hours:0.#} ساعت عقب‌تر از لنگر است (وضعیت: {Health}).",
                backward.TotalHours, health);

        return verdict;
    }

    private static string BuildSummary(ClockHealth health, TimeSpan backward) => health switch
    {
        ClockHealth.Normal => "ساعت سرور با لنگر هم‌خوان است.",
        ClockHealth.MinorBackward =>
            $"ساعت سرور {backward.TotalHours:0.#} ساعت عقب‌تر از آخرین زمان معتبر است. وضعیت لایسنس تغییر نکرد؛ " +
            "ساعت را درست کنید (تنظیم خودکار زمان/NTP).",
        ClockHealth.MajorBackward =>
            $"ساعت سرور {backward.TotalHours:0.#} ساعت عقب‌تر از آخرین زمان معتبر است — بیش از ۲۴ ساعت. " +
            "تا درست‌شدن ساعت، برنامه فقط‌خواندنی است و به‌محض اصلاح ساعت خودکار باز می‌شود.",
        _ => "ساعت هنوز سنجیده نشده است."
    };

    // ---------------------------------------------------------------- ذخیره‌گاه دیتابیس

    private async Task<(string Name, string? Value)> ReadDbAsync(CancellationToken cancellationToken)
    {
        var row = await _db.LicenseRuntime.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == DbKey, cancellationToken);
        return ("db", row?.Value);
    }

    private async Task<bool> WriteAnchorAsync(DateTime value, CancellationToken cancellationToken)
    {
        var protectedValue = Protect(value);
        if (protectedValue is null) return false;

        var row = await _db.LicenseRuntime.FirstOrDefaultAsync(x => x.Key == DbKey, cancellationToken);
        if (row is null)
            _db.LicenseRuntime.Add(new LicenseRuntime { Key = DbKey, Value = protectedValue, UpdatedAt = DateTime.Now });
        else
        {
            row.Value = protectedValue;
            row.UpdatedAt = DateTime.Now;
        }
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (Exception ex) { _log.LogWarning(ex, "[License] نوشتن لنگر ساعت در دیتابیس ناموفق بود."); }

        foreach (var store in _stores) store.Write(protectedValue);
        return true;
    }

    private async Task AlertOnceAsync(ClockVerdict verdict, CancellationToken cancellationToken)
    {
        // امضای هشدار = «مرتبهٔ» مشکل (نه مقدار دقیق ساعت): عقب‌رفتنِ پیوسته نباید هر
        // ۱۰ دقیقه یک رخداد تازه بسازد. با بازگشت به Normal، نشانه پاک می‌شود تا اگر
        // دوباره اتفاق افتاد، هشدار تازه ثبت شود.
        var signature = verdict.Health.ToString();
        var marker = await _db.LicenseRuntime.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == AlertKey, cancellationToken);
        if (marker is not null && string.Equals(marker.Value, signature, StringComparison.Ordinal)) return;

        var kind = verdict.WritesBlocked ? MajorCode : MinorCode;
        await _events.WriteAsync(kind,
            verdict.Summary + " (ساعت سیستم: " + verdict.SystemNow.ToString("yyyy-MM-dd HH:mm") +
            "، لنگر: " + verdict.Anchor.ToString("yyyy-MM-dd HH:mm") + ")",
            severity: verdict.WritesBlocked ? "Error" : "Warning",
            cancellationToken: cancellationToken);

        var tracked = await _db.LicenseRuntime.FirstOrDefaultAsync(x => x.Key == AlertKey, cancellationToken);
        if (tracked is null)
            _db.LicenseRuntime.Add(new LicenseRuntime { Key = AlertKey, Value = signature, UpdatedAt = DateTime.Now });
        else
        {
            tracked.Value = signature;
            tracked.UpdatedAt = DateTime.Now;
        }
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch { /* ثبت نشود هم سنجش انجام شده */ }
    }

    /// <summary>پاک‌کردن نشانهٔ هشدار بعد از رفع مشکل — تا تخلف بعدی هشدار تازه بدهد.</summary>
    private async Task ClearAlertAsync(CancellationToken cancellationToken)
    {
        try
        {
            var marker = await _db.LicenseRuntime.FirstOrDefaultAsync(x => x.Key == AlertKey, cancellationToken);
            if (marker is null) return;
            _db.LicenseRuntime.Remove(marker);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "[License] پاک‌کردن نشانهٔ هشدار ساعت ناموفق بود.");
        }
    }

    // ---------------------------------------------------------------- بیشترین زمان داده

    /// <summary>
    /// بیشترین زمانی که در دادهٔ مشتری دیده می‌شود. هر منبع جدا و در برابر خطا امن است:
    /// نبود جدول یا ترجمه‌نشدن پرس‌وجو نباید سنجش ساعت را از کار بیندازد.
    /// </summary>
    private async Task<DateTime?> ReadDataMaxAsync(CancellationToken cancellationToken)
    {
        var values = new List<DateTime>();

        async Task TryAsync(Func<Task<DateTime?>> read)
        {
            try
            {
                if (await read() is { } value && value > DateTime.MinValue) values.Add(value);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "[License] خواندن بیشترین زمان یکی از منابع داده ناموفق بود.");
            }
        }

        await TryAsync(async () => await _db.LicenseEvents.MaxAsync(x => (DateTime?)x.At, cancellationToken));
        await TryAsync(async () => await _db.OutgoingLetters.MaxAsync(x => x.CreatedAt, cancellationToken));
        await TryAsync(async () => await _db.InnerLetters.MaxAsync(x => x.CreatedAt, cancellationToken));
        await TryAsync(async () =>
        {
            var date = await _db.Invoices.OrderByDescending(x => x.Id)
                .Select(x => x.DateOrder).FirstOrDefaultAsync(cancellationToken);
            return date?.ToDateTime(TimeOnly.MinValue);
        });

        return values.Count == 0 ? null : values.Max();
    }

    // ---------------------------------------------------------------- حفاظت مقدار

    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("totall-clock-anchor-v1");

    /// <summary>رمزگذاری + احراز اصالت. خروجی: <c>v1.&lt;base64(nonce|tag|cipher)&gt;</c></summary>
    public string? Protect(DateTime value)
    {
        if (_key is null) return null;
        try
        {
            var plain = Encoding.UTF8.GetBytes(value.ToString("O"));
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var cipher = new byte[plain.Length];
            using var gcm = new AesGcm(_key, 16);
            gcm.Encrypt(nonce, plain, cipher, tag, AssociatedData);

            var buffer = new byte[nonce.Length + tag.Length + cipher.Length];
            Buffer.BlockCopy(nonce, 0, buffer, 0, nonce.Length);
            Buffer.BlockCopy(tag, 0, buffer, nonce.Length, tag.Length);
            Buffer.BlockCopy(cipher, 0, buffer, nonce.Length + tag.Length, cipher.Length);
            return "v1." + Convert.ToBase64String(buffer);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[License] رمزگذاری لنگر ساعت ناموفق بود.");
            return null;
        }
    }

    /// <summary>
    /// خواندن لنگر. هر مقدار نامعتبر/دست‌کاری‌شده ⇒ <c>null</c> (نادیده گرفته می‌شود) —
    /// عمداً استثنا پرتاب نمی‌کند: یک ذخیره‌گاه خراب نباید برنامه را بخواباند و نباید
    /// هم باعث قفل‌شدن مشتری شود (آن دو ذخیره‌گاه دیگر هم‌چنان سنجیده می‌شوند).
    /// </summary>
    public DateTime? Unprotect(string? text)
    {
        if (_key is null || string.IsNullOrWhiteSpace(text)) return null;
        if (!text.StartsWith("v1.", StringComparison.Ordinal)) return null;
        try
        {
            var buffer = Convert.FromBase64String(text[3..]);
            if (buffer.Length < 12 + 16 + 1) return null;

            var nonce = buffer.AsSpan(0, 12);
            var tag = buffer.AsSpan(12, 16);
            var cipher = buffer.AsSpan(28);
            var plain = new byte[cipher.Length];
            using var gcm = new AesGcm(_key, 16);
            gcm.Decrypt(nonce, cipher, tag, plain, AssociatedData);
            return DateTime.Parse(Encoding.UTF8.GetString(plain),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind);
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? TryLoadOrCreateKey(LicensePaths paths, ILogger log)
    {
        try
        {
            var path = Path.Combine(paths.Root, "clock.key");
            if (File.Exists(path))
            {
                var existing = Convert.FromBase64String(File.ReadAllText(path).Trim());
                if (existing.Length >= 32) return existing;
            }
            var key = RandomNumberGenerator.GetBytes(32);
            Directory.CreateDirectory(paths.Root);
            var temp = path + ".tmp";
            File.WriteAllText(temp, Convert.ToBase64String(key));
            File.Move(temp, path, overwrite: true);
            return key;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "[License] کلید حفاظت لنگر ساعت در دسترس نیست؛ سنجش ساعت با فایل‌های بدون کلید ادامه می‌یابد.");
            return null;
        }
    }
}

/// <summary>
/// آخرین نتیجهٔ سنجش ساعت — قابل‌خواندن از میان‌افزار/فیلترها بدون تزریق وابستگی.
/// پیش‌فرض «مسدود نیست» است تا نبود سنجش هیچ‌وقت مشتری را قفل نکند.
/// </summary>
public static class ClockGuardState
{
    private static ClockVerdict _current = ClockVerdict.Unknown;

    public static ClockVerdict Current => Volatile.Read(ref _current);

    /// <summary>آیا نوشتن به‌خاطر ساعت بسته است؟</summary>
    public static bool Blocked => Current.WritesBlocked;

    public static void Publish(ClockVerdict verdict) => Volatile.Write(ref _current, verdict);

    /// <summary>فقط برای تست: بازگرداندن به حالت اولیه.</summary>
    public static void Reset() => Volatile.Write(ref _current, ClockVerdict.Unknown);
}
