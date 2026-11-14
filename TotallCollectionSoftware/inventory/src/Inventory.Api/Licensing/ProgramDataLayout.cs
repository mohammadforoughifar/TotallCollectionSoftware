namespace Inventory.Api.Licensing;

/// <summary>
/// مسیرهای ProgramData که برنامه در راه‌اندازی لازم دارد.
/// </summary>
public sealed record ProgramDataPaths(string Root, string Security, string License, string Projects, string Logs);

/// <summary>
/// ساخت و بررسی پوشه‌های ProgramData در راه‌اندازی.
///
/// <para>
/// <b>چرا fail-fast؟</b> قبلاً اگر ساخت پوشه ممکن نبود، برنامه بی‌صدا به
/// <c>App_Data</c> کنار خودش می‌افتاد. نتیجه: کلید JWT و کلید پروژه‌ها داخل پوشهٔ
/// برنامه ساخته می‌شدند و با اولین انتشار روی IIS پاک می‌شدند — یعنی همهٔ کاربران
/// بیرون می‌افتادند و پیوست‌های رمزنگاری‌شده قابل بازیابی نبودند. «خطای روشن در
/// راه‌اندازی» بسیار ارزان‌تر از «دادهٔ ازدست‌رفته در سکوت» است.
/// </para>
///
/// <para>
/// ACL این پوشه‌ها در اسکریپت به‌روزرسانی (گام ۶) تنظیم می‌شود؛ این‌جا فقط
/// «هست» و «قابل‌نوشتن» بررسی می‌شود.
/// </para>
/// </summary>
public static class ProgramDataLayout
{
    /// <summary>متغیر محیطی جایگزین ریشه (برای تست و نصب‌های غیرویندوزی).</summary>
    public const string RootEnvVar = "TOTALL_DATA_ROOT";

    /// <summary>
    /// مسیرها را از پیکربندی/محیط حل می‌کند (بدون ساخت).
    /// </summary>
    /// <param name="configuredDataRoot">مقدار <c>Totall:DataRoot</c> در پیکربندی، اگر بود.</param>
    /// <param name="configuredLicenseFolder">مقدار <c>Licensing:StorageFolder</c>، اگر بود.</param>
    public static ProgramDataPaths Resolve(string? configuredDataRoot, string? configuredLicenseFolder)
    {
        var root = FirstNonEmpty(
                       configuredDataRoot,
                       Environment.GetEnvironmentVariable(RootEnvVar))
                   ?? Path.Combine(
                       Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData,
                           Environment.SpecialFolderOption.Create),
                       "Totall");

        // پوشهٔ لایسنس سنتی جای دیگری است؛ برای سازگاری با نسخه‌های نصب‌شده تغییر نمی‌کند.
        var license = FirstNonEmpty(configuredLicenseFolder)
                      ?? Path.Combine(
                          Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData,
                              Environment.SpecialFolderOption.Create),
                          "TotallCollection", "License");

        return new ProgramDataPaths(
            Root: root,
            Security: Path.Combine(root, "security"),
            License: license,
            Projects: Path.Combine(root, "projects"),
            Logs: Path.Combine(root, "logs"));
    }

    /// <summary>
    /// پوشه‌ها را می‌سازد و قابل‌نوشتن بودن‌شان را با یک فایل آزمایشی می‌سنجد.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// اگر ساخت یا نوشتن ممکن نبود — پیام شامل مسیر و کار لازم برای رفع است.
    /// </exception>
    public static ProgramDataPaths Ensure(ProgramDataPaths paths)
    {
        foreach (var dir in new[] { paths.Security, paths.License, paths.Projects, paths.Logs })
        {
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(BuildMessage(dir, "ساخت پوشه ممکن نشد", ex), ex);
            }

            var probe = Path.Combine(dir, ".write-probe");
            try
            {
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(BuildMessage(dir, "پوشه قابل‌نوشتن نیست", ex), ex);
            }
        }

        return paths;
    }

    private static string BuildMessage(string dir, string what, Exception ex) =>
        $"""
        [راه‌اندازی متوقف شد] {what}: {dir}
        علت: {ex.GetType().Name}: {ex.Message}

        این پوشه محل نگه‌داری کلید امضای JWT، کلید رمزنگاری پیوست‌ها، فایل لایسنس و لاگ‌هاست.
        برنامه عمداً به مسیر جایگزین (کنار پوشهٔ انتشار) نمی‌افتد؛ چون با اولین به‌روزرسانی
        پاک می‌شود و باعث خروج همهٔ کاربران و از دست رفتن دسترسی به پیوست‌های رمزنگاری‌شده می‌گردد.

        کار لازم (ویندوز): به هویت App Pool روی این مسیر دسترسی Modify بدهید:
            icacls "{dir}" /grant "IIS AppPool\\<نام App Pool>:(OI)(CI)M"
        یا متغیر محیطی {RootEnvVar} را به مسیری قابل‌نوشتن تنظیم کنید.
        """;

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        return null;
    }
}
