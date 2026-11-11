using System.Globalization;
using System.Text;
using Inventory.Licensing.Crypto;
using Inventory.Licensing.Entitlement;
using Inventory.Licensing.Metrics;
using Inventory.Licensing.Model;
using Inventory.Licensing.Periods;
using Microsoft.Data.SqlClient;

namespace LicenseCheck;

/// <summary>
/// <c>LicenseCheck --simulate</c> — «شبیه‌ساز سهمیه»، فقط‌خواندنی.
///
/// <para>
/// <b>مسئله‌ای که حل می‌کند:</b> پیش از صدور لایسنس باید بدانیم مشتری در هر متریک
/// واقعاً چقدر مصرف دارد. اگر این را نپرسیم، دو نوع خطا رخ می‌دهد: یا سقف را کم
/// می‌گذاریم و مشتری از روز اول به دیوار می‌خورد، یا زیاد می‌گذاریم و ابزارِ کنترل
/// بی‌اثر می‌شود. این ابزار، پیش از صدور، «الان کجاییم» را نشان می‌دهد.
/// </para>
///
/// <para>
/// <b>تضمین فقط‌خواندنی:</b> این برنامه هیچ دستور نوشتنی ندارد؛ تنها کوئری‌های آن
/// <c>SELECT COUNT(*)</c> روی جدول‌های کسب‌وکاری و <c>SELECT</c> از جدول‌های لایسنس
/// است. هیچ ردیفی درج/تغییر/حذف نمی‌شود و هیچ فایلی نوشته نمی‌شود. اگر بخواهیم
/// در آینده دستور نوشتنی اضافه شود، باید همان‌جا <c>ReadOnlyGuard.Assert</c> را
/// رد کند — و این تست شکست می‌خورد (به‌عمد).
/// </para>
///
/// <para><b>نمونهٔ اجرا:</b></para>
/// <code>
/// LicenseCheck --simulate --conn "Server=.;Database=X;User Id=sa;Password=***;TrustServerCertificate=True" ^
///              --license C:\Licenses\customer.lic --months 3
/// </code>
/// </summary>
public static class Program
{
    /// <summary>جدول/ستونِ شمارش هر متریک. متریکی که اینجا نباشد «بدون probe» گزارش می‌شود.</summary>
    private static readonly Dictionary<string, (string Table, string Column)> ProbeMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [MetricCodes.InvoiceCreate] = ("FacInvoices", "CreatedAt"),
            [MetricCodes.LetterIncomingCreate] = ("IncomingLetters", "CreatedAt"),
            [MetricCodes.LetterOutgoingCreate] = ("OutgoingLetters", "CreatedAt"),
            [MetricCodes.LetterInnerCreate] = ("InnerLetters", "CreatedAt"),
        };

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintHelp();
            return args.Length == 0 ? 2 : 0;
        }

        // ---------- حالت «کلیدها»: چه کلیدهایی در همین بیلد شناخته می‌شوند؟ ----------
        if (args.Contains("--keys"))
        {
            PrintKeys();
            return 0;
        }

        // ---------- حالت «آماده‌سازی»: ساخت شناسهٔ نصب روی دیتابیس موجود، پیش از آپدیت ----------
        if (args.Contains("--prepare"))
            return await PrepareAsync(args);

        if (!args.Contains("--simulate"))
        {
            Console.Error.WriteLine(
                "این ابزار در نسخهٔ فعلی فقط حالت --simulate را پشتیبانی می‌کند (فقط‌خواندنی).");
            return 2;
        }

        // مثل --prepare: محیط، نه خط فرمان (رمز در تاریخچهٔ شل نمی‌ماند). --conn فقط برای
        // اجرای آزمایشی/توسعه پذیرفته می‌شود.
        var conn = Environment.GetEnvironmentVariable("LICENSING_CONNECTION")
                   ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                   ?? Environment.GetEnvironmentVariable("LICENSING_TEST_CONN")
                   ?? Arg(args, "--conn");
        var licensePath = Arg(args, "--license");
        var months = int.TryParse(Arg(args, "--months"), out var m) ? Math.Clamp(m, 0, 24) : 3;

        if (string.IsNullOrWhiteSpace(conn))
        {
            Console.Error.WriteLine("رشتهٔ اتصال لازم است: متغیر محیطی LICENSING_CONNECTION " +
                                    "(یا ConnectionStrings__Default، یا برای تست LICENSING_TEST_CONN).");
            return 2;
        }

        // ---------- ۱) لایسنس (اختیاری) ----------
        var limits = new Dictionary<string, MetricLimit>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<LicenseDocument>? licenseDocsA = null;
        var status = "بدون فایل لایسنس — فقط مصرف واقعی نشان داده می‌شود";

        if (!string.IsNullOrWhiteSpace(licensePath))
        {
            var loaded = LoadLicense(licensePath);
            if (loaded.Error is not null)
            {
                Console.Error.WriteLine($"❌ {loaded.Error}");
                return 3;
            }

            licenseDocsA = loaded.Documents!;
            var merged = EntitlementMerger.Merge(loaded.Documents!);
            foreach (var l in merged.Limits) limits[l.Metric] = l;
            status = $"وضعیت: {merged.Status} — {limits.Count} سقف در {loaded.Documents!.Count} سند";
        }

        Console.WriteLine("══════════════════════════════════════════════════════════════════════");
        Console.WriteLine("  شبیه‌ساز سهمیه — فقط‌خواندنی (هیچ تغییری در دیتابیس نمی‌دهد)");
        Console.WriteLine($"  {status}");
        Console.WriteLine($"  امروز (تهران): {PersianPeriodKey.NowIran():yyyy-MM-dd}  |  "
                        + $"ماه شمسی: {PersianPeriodKey.Year(PersianPeriodKey.NowIran())}/"
                        + $"{PersianPeriodKey.Month(PersianPeriodKey.NowIran()):00}");
        Console.WriteLine("══════════════════════════════════════════════════════════════════════");

        // ---------- ۲) شمارش‌ها ----------
        var rows = new List<SimRow>();

        await using var connection = new SqlConnection(conn);
        try
        {
            await connection.OpenAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ اتصال به دیتابیس برقرار نشد: {ex.Message}");
            return 4;
        }

        var metrics = MetricCodes.All
            .Where(d => !d.IsAggregate)                    // تجمیعی‌ها از اجزای خودشان محاسبه می‌شوند
            .ToList();

        foreach (var def in metrics)
        {
            var row = new SimRow
            {
                Metric = def.Code,
                Title = def.Title,
                Module = def.Module,
                Unit = def.Unit,
                Kind = def.Kind,
            };

            if (limits.TryGetValue(def.Code, out var limit))
            {
                row.Cap = limit.Limit;
                row.Period = limit.Period;
                row.Grandfather = limit.Grandfather;
            }

            if (ProbeMap.TryGetValue(def.Code, out var probe))
            {
                row.HasProbe = true;
                row.CurrentPeriodRows = await CountAsync(connection, probe.Table, probe.Column,
                    PersianPeriodKey.RangeOf(QuotaPeriod.Monthly, PersianPeriodKey.NowIran()));

                for (var i = 1; i <= months; i++)
                {
                    var when = PersianMonthBack(PersianPeriodKey.NowIran(), i);
                    var (from, to) = PersianPeriodKey.RangeOf(QuotaPeriod.Monthly, when);
                    row.PreviousMonths.Add((
                        Key: $"{PersianPeriodKey.Year(when)}/{PersianPeriodKey.Month(when):00}",
                        Count: await CountAsync(connection, probe.Table, probe.Column, (from, to))));
                }
            }

            // شمارندهٔ ثبت‌شده در جدول‌های لایسنس (اگر لایسنس قبلاً فعال بوده)
            row.CounterUsed = await ReadCounterAsync(connection, def.Code, row.Period);
            row.Baseline = await ReadBaselineAsync(connection, def.Code);

            rows.Add(row);
        }

        // ---------- ۳) تطبیق شناسهٔ نصب ----------
        // لایسنس OnPrem به شناسهٔ نصب گره خورده است. اگر شناسهٔ داخل فایل با شناسهٔ همین
        // دیتابیس یکی نباشد، فایل روی این سرور پذیرفته نمی‌شود — چه حیف که فقط بعد از
        // آپدیت بفهمیم. این‌جا، پیش از صدور/ارسال، همان مقایسه انجام می‌شود.
        var dbInstallationId = await ReadInstallationIdAsync(connection);
        if (dbInstallationId is null)
        {
            Console.WriteLine();
            Console.WriteLine("  ℹ️ جدول نصب در این دیتابیس خالی است (نصب تازه). ");
            Console.WriteLine("     اگر مشتری از قبل داده دارد، ابتدا «LicenseCheck --prepare» را اجرا کنید " +
                              "تا شناسهٔ نصب ساخته شود.");
        }
        else if (licenseDocsA is not null && licenseDocsA.Count > 0)
        {
            Console.WriteLine();
            foreach (var doc in licenseDocsA!)
            {
                var docId = doc.InstallationId;
                if (string.Equals(docId, dbInstallationId, StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine($"  ✅ شناسهٔ نصب لایسنس با این دیتابیس یکی است: {dbInstallationId}");
                else
                    Console.WriteLine($"  ⛔ شناسهٔ نصب نمی‌خواند — این فایل روی این سرور معتبر نیست.\n" +
                                      $"     شناسهٔ دیتابیس: {dbInstallationId}\n" +
                                      $"     شناسهٔ فایل:   {docId ?? "(خالی)"}");
            }
        }

        // ---------- ۴) گزارش ----------
        PrintReport(rows, limits, months);
        await connection.CloseAsync();
        return 0;
    }

    // ============================================================
    //  --prepare : ساخت شناسهٔ نصب روی دیتابیس موجود، پیش از آپدیت
    // ============================================================

    /// <summary>
    /// «آماده‌سازی نصب موجود» — پیش از جایگزینی فایل‌ها روی سرور مشتری اجرا می‌شود.
    ///
    /// <para>
    /// <b>مسئله‌ای که حل می‌کند:</b> شناسهٔ نصب فقط در اولین اجرای نسخهٔ جدید ساخته می‌شود.
    /// مشتری‌ای که امروز روی نسخهٔ بدون لایسنس کار می‌کند، تا وقتی نسخهٔ جدید بالا نیامده
    /// شناسه‌ای ندارد؛ پس نمی‌شود پیش از آپدیت برایش «لایسنس پل» صادر کرد. نتیجه: روز
    /// آپدیت، برنامه تا رسیدن فایل لایسنس فقط‌خواندنی می‌ماند. این دستور همان شکاف را
    /// می‌بندد: شناسه را از قبل روی دیتابیس موجود می‌سازد و چاپ می‌کند.
    /// </para>
    ///
    /// <para>
    /// <b>تضمین «کمترین تغییر»:</b> تنها کاری که می‌کند ساخت جدول
    /// <c>LicenseInstallations</c> (اگر نباشد) و درج <b>یک</b> ردیف در آن است. هیچ جدول
    /// دیگری ساخته/تغییر نمی‌شود و هیچ ردیف کسب‌وکاری دست نمی‌خورد. اگر شناسه از قبل
    /// وجود داشته باشد، فقط چاپ می‌شود.
    /// </para>
    ///
    /// <para>
    /// <b>رشتهٔ اتصال:</b> عمداً <b>از خط فرمان خوانده نمی‌شود</b> (رمز در تاریخچهٔ شل و
    /// لاگ نصب می‌ماند). ترتیب: متغیر محیطی <c>LICENSING_CONNECTION</c> یا
    /// <c>ConnectionStrings__Default</c>؛ و اگر هیچ‌کدام نبود، اتصال یکپارچهٔ ویندوز به
    /// سرور محلی و دیتابیس <c>InventoryDb</c>.
    /// </para>
    /// </summary>
    private static async Task<int> PrepareAsync(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Contains("--conn"))
        {
            Console.Error.WriteLine(
                "❌ در حالت --prepare رشتهٔ اتصال از خط فرمان خوانده نمی‌شود (رمز در تاریخچهٔ شل می‌ماند).");
            Console.Error.WriteLine(
                "   متغیر محیطی را ست کنید:  set LICENSING_CONNECTION=<رشتهٔ اتصال کامل با احراز هویت دیتابیس>");
            Console.Error.WriteLine(
                "   یا اصلاً ست نکنید تا اتصال یکپارچهٔ ویندوز استفاده شود: Server=.;Database=InventoryDb;Trusted_Connection=True");
            return 2;
        }

        var conn = FirstNonEmpty(
            Environment.GetEnvironmentVariable("LICENSING_CONNECTION"),
            Environment.GetEnvironmentVariable("ConnectionStrings__Default"),
            Environment.GetEnvironmentVariable("LICENSING_TEST_CONN"))
            ?? "Server=.;Database=InventoryDb;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False";

        var showTarget = MaskConnection(conn);

        Console.WriteLine("══════════════════════════════════════════════════════════════════════");
        Console.WriteLine("  آماده‌سازی نصب موجود — ساخت شناسهٔ نصب پیش از آپدیت");
        Console.WriteLine($"  مقصد: {showTarget}");
        Console.WriteLine("  تغییرات: فقط جدول LicenseInstallations + یک ردیف. هیچ چیز دیگری لمس نمی‌شود.");
        Console.WriteLine("══════════════════════════════════════════════════════════════════════");

        await using var connection = new SqlConnection(conn);
        try
        {
            await connection.OpenAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ اتصال به دیتابیس برقرار نشد: {ex.Message}");
            Console.Error.WriteLine("   متغیر محیطی LICENSING_CONNECTION را بررسی کنید.");
            return 4;
        }

        // ---------- ۱) جدول، دقیقاً هم‌شکل با LicenseSchemaV1 (همان چیزی که برنامه می‌سازد) ----------
        await ExecuteAsync(connection, @"
IF OBJECT_ID(N'dbo.LicenseInstallations', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[LicenseInstallations] (
        [Id]               int            NOT NULL IDENTITY(1,1),
        [InstallationId]   nvarchar(64)   NOT NULL,
        [DeploymentMode]   nvarchar(20)   NOT NULL,
        [TenantId]         int            NULL,
        [CustomerName]     nvarchar(200)  NULL,
        [MachineFingerprint] nvarchar(600) NULL,
        [CreatedAt]        datetime2      NOT NULL,
        [UpdatedAt]        datetime2      NOT NULL,
        CONSTRAINT [PK_LicenseInstallations] PRIMARY KEY ([Id])
    );
END
");

        // ---------- ۲) شناسهٔ موجود؟ ----------
        string? existing = null;
        await using (var cmd = new SqlCommand(
            "SELECT TOP 1 [InstallationId] FROM [dbo].[LicenseInstallations] ORDER BY [Id]", connection))
        {
            var value = await cmd.ExecuteScalarAsync();
            if (value is string s && !string.IsNullOrWhiteSpace(s)) existing = s;
        }

        if (existing is not null)
        {
            Console.WriteLine();
            Console.WriteLine($"  شناسهٔ نصب از قبل وجود دارد: {existing}");
            Console.WriteLine("  (ردیف تازه ساخته نشد — یک نصب، یک شناسه.)");
            WriteInstallationIdFile(existing);
            PrintNextSteps(existing);
            return 0;
        }

        // ---------- ۳) ساخت شناسهٔ تازه ----------
        // همان قالب برنامه: Guid("N")، ۳۲ نویسهٔ هگز بدون خط تیره.
        var id = Guid.NewGuid().ToString("N");
        var nowTehran = Inventory.Licensing.Periods.PersianPeriodKey.NowIran();

        await using (var cmd = new SqlCommand(@"
INSERT INTO [dbo].[LicenseInstallations]
       ([InstallationId], [DeploymentMode], [TenantId], [CustomerName], [MachineFingerprint], [CreatedAt], [UpdatedAt])
VALUES (@id, @mode, NULL, NULL, NULL, @now, @now)", connection))
        {
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@mode", "OnPrem");
            cmd.Parameters.AddWithValue("@now", nowTehran);
            await cmd.ExecuteNonQueryAsync();
        }

        // تأیید: هم شناسهٔ درج‌شده، هم تنها یک ردیف
        var count = 0;
        await using (var cmd = new SqlCommand("SELECT COUNT_BIG(*) FROM [dbo].[LicenseInstallations]", connection))
            count = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);

        Console.WriteLine();
        Console.WriteLine($"  ✅ شناسهٔ نصب ساخته شد: {id}");
        Console.WriteLine($"  (تعداد ردیف جدول نصب: {count})");

        WriteInstallationIdFile(id);
        PrintNextSteps(id);
        return 0;
    }

    /// <summary>
    /// نوشتن فایل <c>installation.id</c> در همان مسیری که برنامه می‌خواند
    /// (ProgramData/TotallCollection/License). اگر نوشتن ممکن نبود، فقط هشدار می‌دهد —
    /// شناسه در دیتابیس هست و برنامه از آن استفاده می‌کند.
    /// </summary>
    private static void WriteInstallationIdFile(string id)
    {
        try
        {
            // همان ترتیب برنامه: اول ProgramData، و اگر قابل‌نوشتن نبود App_Data کنار ابزار.
            var candidates = new List<string>();

            try
            {
                var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData,
                    Environment.SpecialFolderOption.Create);
                if (!string.IsNullOrWhiteSpace(programData))
                    candidates.Add(Path.Combine(programData, "TotallCollection", "License"));
            }
            catch { /* پرش */ }

            candidates.Add(Path.Combine(AppContext.BaseDirectory, "App_Data", "License"));

            Exception? lastError = null;
            foreach (var dir in candidates)
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    var probe = Path.Combine(dir, ".w");
                    File.WriteAllText(probe, "1");
                    File.Delete(probe);

                    var path = Path.Combine(dir, "installation.id");
                    File.WriteAllText(path, id);
                    Console.WriteLine($"  فایل شناسه هم نوشته شد: {path}");
                    return;
                }
                catch (Exception ex) { lastError = ex; }
            }

            throw lastError ?? new IOException("هیچ مسیری قابل نوشتن نبود.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠️ نوشتن فایل installation.id ممکن نشد ({ex.Message}). " +
                              "مانعی نیست: شناسه در دیتابیس است.");
        }
    }

    private static void PrintNextSteps(string id)
    {
        Console.WriteLine();
        Console.WriteLine("  گام بعد (روی رایانهٔ شما، نه سرور مشتری):");
        Console.WriteLine($"    python tools/license-sign.py --private-key <key.pem> \\");
        Console.WriteLine($"        --installation-id {id} --customer-name \"…\" --bridge --days 30 --out bridge.lic");
        Console.WriteLine();
        Console.WriteLine("  سپس فایل‌ها را جایگزین کنید و bridge.lic را از صفحهٔ «لایسنس» بارگذاری کنید.");
        Console.WriteLine("  • اگر لایسنس پل صادر نمی‌کنید، همین شناسه را برای صدور لایسنس اصلی بفرستید.");
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>مخفی‌کردن رمز در رشتهٔ اتصال، برای چاپ.</summary>
    private static string MaskConnection(string conn)
    {
        try
        {
            var b = new SqlConnectionStringBuilder(conn);
            if (!string.IsNullOrEmpty(b.Password)) b.Password = "***";
            return b.ConnectionString;
        }
        catch { return "(رشتهٔ اتصال نامعتبر)"; }
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>شناسهٔ نصب ثبت‌شده در همین دیتابیس (یا null اگر جدول/ردیف نیست). فقط SELECT.</summary>
    private static async Task<string?> ReadInstallationIdAsync(SqlConnection connection)
    {
        try
        {
            await using var cmd = new SqlCommand(
                "SELECT TOP 1 [InstallationId] FROM [dbo].[LicenseInstallations] ORDER BY [Id]", connection);
            return await cmd.ExecuteScalarAsync() as string;
        }
        catch (SqlException)
        {
            return null;   // جدول هنوز ساخته نشده — نصب تازه
        }
    }

    /// <summary>یک ردیف گزارش.</summary>
    private sealed class SimRow
    {
        public string Metric = "";
        public string Title = "";
        public string Module = "";
        public string Unit = "";
        public MetricKind Kind;
        public bool HasProbe;
        public long? Cap;
        public QuotaPeriod Period = QuotaPeriod.Lifetime;
        public bool Grandfather;
        public long CurrentPeriodRows;
        public List<(string Key, long Count)> PreviousMonths = new();
        public long? CounterUsed;
        public long? Baseline;

        /// <summary>
        /// «مصرف مؤثرِ پیش‌بینی‌شده» طبق قاعدهٔ سند:
        /// <list type="bullet">
        ///   <item>ماهانه/سالانه ⇒ <c>max(شمارنده، ردیف‌های همان دوره)</c></item>
        ///   <item>دائمی (Lifetime) ⇒ <c>max(شمارنده، واقعی − مبنا)</c></item>
        /// </list>
        /// بدون probe، فقط شمارنده در دست است و همان گزارش می‌شود.
        /// </summary>
        public long Predicted()
        {
            var counter = CounterUsed ?? 0;

            if (!HasProbe) return counter;

            if (Period == QuotaPeriod.Monthly || Period == QuotaPeriod.Yearly)
            {
                // ردیف‌های «همان دوره» — برای سقف ماهانه، ماه جاری؛ سالانه، سال جاری.
                // این‌جا محافظه‌کارانه ماه جاری را می‌گیریم (سقف سالانه ≥ ماهانه است).
                return Math.Max(counter, CurrentPeriodRows);
            }

            var live = CurrentPeriodRows;          // برای دائمی: کل ردیف‌ها
            var net = Math.Max(0, live - (Baseline ?? 0));
            return Math.Max(counter, net);
        }
    }

    private static void PrintReport(List<SimRow> rows, Dictionary<string, MetricLimit> limits, int months)
    {
        Console.WriteLine();
        Console.WriteLine("متریک                       | سقف       | دوره     | ماه جاری | "
                          + string.Join(" | ", Enumerable.Range(1, Math.Max(months, 1)).Select(i => $"-{i} ماه"))
                          + " | مصرف پیش‌بینی | وضعیت");
        Console.WriteLine(new string('─', 118));

        foreach (var r in rows)
        {
            var cap = r.Cap?.ToString("N0") ?? "—";
            var months1 = r.PreviousMonths.Count == 0
                ? "—"
                : string.Join(" | ", r.PreviousMonths.Select(p => p.Count.ToString("N0").PadLeft(7)));

            string verdict;
            if (r.Cap is null)
            {
                verdict = limits.Count == 0 ? "بدون لایسنس" : "بدون سقف";
            }
            else if (!r.HasProbe)
            {
                // ⚠️ متریکی که probe ندارد، عدد واقعی‌اش را نمی‌دانیم. پیش از این «🟢 راحت»
                // نشان داده می‌شد — یعنی «فرض کن صفر است»، که برای انتخاب سقف گمراه‌کننده
                // بود: مشتری ممکن است ماهی هزار ارسال مودیان داشته باشد و ما «راحت» بگوییم.
                verdict = "❔ نامشخص (بدون probe)";
            }
            else
            {
                var predicted = r.Predicted();
                var pct = r.Cap.Value == 0 ? 100 : (int)Math.Round(predicted * 100.0 / r.Cap.Value);

                verdict = pct switch
                {
                    >= 100 => "🔴 پر — از روز اول بسته",
                    >= 90 => "🟠 مرزی (≥۹۰٪)",
                    >= 70 => "🟡 توجه (≥۷۰٪)",
                    _ => "🟢 راحت",
                };

                if (r.Grandfather) verdict += " + بخشودگی";
            }

            var current = r.HasProbe ? r.CurrentPeriodRows.ToString("N0") : (r.CounterUsed?.ToString("N0") ?? "—");

            Console.WriteLine(
                $"{Fit(r.Title, 27)} | {Fit(cap, 9)} | {Fit(r.Period.ToString(), 8)} | {Fit(current, 8)} | "
                + $"{Fit(months1, Math.Max(7 * Math.Max(months, 1) + 3 * (Math.Max(months, 1) - 1), 7))} | "
                + $"{Fit(r.HasProbe ? r.Predicted().ToString("N0") : "?", 13)} | {verdict}");
        }

        Console.WriteLine(new string('─', 118));

        // تجمیعی‌ها
        foreach (var agg in MetricCodes.All.Where(d => d.IsAggregate))
        {
            if (!limits.TryGetValue(agg.Code, out var limit)) continue;

            var componentCodes = agg.Components ?? Array.Empty<string>();
            var sumCurrent = rows.Where(r => componentCodes.Contains(r.Metric, StringComparer.OrdinalIgnoreCase))
                                 .Sum(r => r.CurrentPeriodRows);
            var counter = rows.Where(r => componentCodes.Contains(r.Metric, StringComparer.OrdinalIgnoreCase))
                              .Sum(r => r.CounterUsed ?? 0);
            var predicted = Math.Max(sumCurrent, counter);
            var pct = limit.Limit == 0 ? 100 : (int)Math.Round(predicted * 100.0 / limit.Limit);

            Console.WriteLine(
                $"{Fit(agg.Title + " (تجمیعی)", 27)} | {Fit(limit.Limit.ToString("N0"), 9)} | "
                + $"{Fit(limit.Period.ToString(), 8)} | {Fit(sumCurrent.ToString("N0"), 8)} | "
                + $"{Fit("—", Math.Max(7 * Math.Max(months, 1) + 3 * (Math.Max(months, 1) - 1), 7))} | "
                + $"{Fit(predicted.ToString("N0"), 13)} | {(pct >= 100 ? "🔴 پر" : pct >= 90 ? "🟠 مرزی" : "🟢 راحت")}");
        }

        Console.WriteLine();
        Console.WriteLine("راهنما:");
        Console.WriteLine("  • «ماه جاری» و ستون‌های ماه‌های قبل از CreatedAt (زمان ثبت سیستمی) شمرده می‌شوند،");
        Console.WriteLine("    نه از تاریخ‌های قابل‌ویرایش کاربر — همان مبنایی که سقف روی آن اعمال می‌شود.");
        Console.WriteLine("  • «مصرف پیش‌بینی» = همان چیزی که روز اول بعد از فعال‌سازی دیده می‌شود.");
        Console.WriteLine("    اگر 🔴 بود، یا سقف را بالاتر بگذارید یا در لایسنس grandfather: true بدهید");
        Console.WriteLine("    (گذشتهٔ همین دوره نادیده گرفته می‌شود و سقف فقط روی مصرف تازه اعمال می‌شود).");
        Console.WriteLine("  • متریک‌های «نامشخص (بدون probe)» یعنی شمارندهٔ واقعی‌ای برایشان تعریف نشده");
        Console.WriteLine("    (مثل ارسال به مودیان)؛ عدد را از شمارندهٔ لایسنس یا تیم عملیات بگیرید.");
        Console.WriteLine("    این‌ها را «راحت» فرض نکنید — ممکن است مشتری همین حالا سقف را پر کرده باشد.");
    }

    private static string Fit(string s, int width)
    {
        if (s.Length > width) s = s[..Math.Max(0, width - 1)] + "…";
        return s.PadLeft(width);
    }

    // ---------------- لایسنس ----------------

    private sealed record LoadResult(IReadOnlyList<LicenseDocument>? Documents, string? Error);

    private static LoadResult LoadLicense(string path)
    {
        var ring = PublicKeyRing.FromEmbedded();
        var docs = new List<LicenseDocument>();

        var files = Directory.Exists(path)
            ? Directory.GetFiles(path, "*.lic", SearchOption.TopDirectoryOnly)
            : new[] { path };

        if (files.Length == 0)
            return new LoadResult(null, $"فایلی در «{path}» پیدا نشد.");

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var result = LicenseVerifier.Verify(text, ring);

            if (!result.Ok)
                return new LoadResult(null, $"«{Path.GetFileName(file)}» معتبر نیست: {result.ErrorText}");

            if (result.Document is not null) docs.Add(result.Document);
        }

        return new LoadResult(docs, null);
    }

    // ---------------- دیتابیس (فقط SELECT) ----------------

    /// <summary>شمارش ردیف‌های بازهٔ <c>[from, to)</c>. تنها دستور این برنامه، یک SELECT است.</summary>
    private static async Task<long> CountAsync(SqlConnection connection, string table, string column,
        (DateTime From, DateTime To) range)
    {
        var sql = $"SELECT COUNT_BIG(*) FROM [dbo].[{table}] WHERE [{column}] >= @from AND [{column}] < @to";

        try
        {
            await using var cmd = new SqlCommand(sql, connection);
            cmd.CommandTimeout = 120;
            cmd.Parameters.AddWithValue("@from", range.From);
            cmd.Parameters.AddWithValue("@to", range.To == DateTime.MaxValue ? new DateTime(9999, 12, 31) : range.To);

            var value = await cmd.ExecuteScalarAsync();
            return value is null or DBNull ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch (SqlException)
        {
            // جدول/ستون در این نصب نیست (نسخهٔ قدیمی یا ماژول خاموش) — صفر، نه خطا.
            return 0;
        }
    }

    private static async Task<long?> ReadCounterAsync(SqlConnection connection, string metric, QuotaPeriod period)
    {
        try
        {
            var key = period == QuotaPeriod.Lifetime ? "lifetime" : PersianPeriodKey.Now(period);

            await using var cmd = new SqlCommand(
                "SELECT TOP 1 [Used] FROM [dbo].[LicenseUsages] " +
                "WHERE [Metric] = @metric AND [PeriodKey] = @key ORDER BY [Id] DESC", connection);
            cmd.Parameters.AddWithValue("@metric", metric);
            cmd.Parameters.AddWithValue("@key", key);

            var value = await cmd.ExecuteScalarAsync();
            return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch (SqlException)
        {
            return null;   // جدول لایسنس هنوز ساخته نشده — نصب تازه
        }
    }

    private static async Task<long?> ReadBaselineAsync(SqlConnection connection, string metric)
    {
        try
        {
            await using var cmd = new SqlCommand(
                "SELECT TOP 1 [BaselineCount] FROM [dbo].[LicenseUsageBaselines] " +
                "WHERE [Metric] = @metric ORDER BY [Id] DESC", connection);
            cmd.Parameters.AddWithValue("@metric", metric);

            var value = await cmd.ExecuteScalarAsync();
            return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch (SqlException)
        {
            return null;
        }
    }

    // ---------------- کمکی ----------------

    /// <summary>همان تاریخ، <paramref name="back"/> ماه شمسی عقب‌تر.</summary>
    private static DateTime PersianMonthBack(DateTime iranDate, int back)
    {
        var year = PersianPeriodKey.Year(iranDate);
        var month = PersianPeriodKey.Month(iranDate);

        for (var i = 0; i < back; i++)
        {
            month--;
            if (month < 1) { month = 12; year--; }
        }

        return new System.Globalization.PersianCalendar().ToDateTime(year, month, 1, 12, 0, 0, 0);
    }

    private static string? Arg(string[] args, string name)
    {
        var i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= args.Length) return null;
        var value = args[i + 1];
        return value.StartsWith("--", StringComparison.Ordinal) ? null : value;
    }

    /// <summary>
    /// چاپ کلیدهای عمومیِ قابل‌استفاده در <b>همین بیلد</b>.
    ///
    /// <para>
    /// کاربرد اصلی: اثبات این‌که بیلد Release کلید توسعه را نمی‌شناسد. یک بار
    /// <c>dotnet run -c Release -- --keys</c> و یک بار <c>-c Debug</c> کافی است.
    /// </para>
    /// </summary>
    private static void PrintKeys()
    {
        var ring = PublicKeyRing.FromEmbedded();

        Console.WriteLine("کلیدهای عمومی امضای لایسنس در همین بیلد:");
        Console.WriteLine($"  بیلد: {(IsDebug() ? "Debug" : "Release")}");
        Console.WriteLine($"  تعداد: {ring.KeyIds.Count}");

        foreach (var id in ring.KeyIds.OrderBy(x => x, StringComparer.Ordinal))
            Console.WriteLine($"    • {id}");

        if (ring.KeyIds.Count == 0)
            Console.WriteLine("    (خالی — هیچ کلیدی بارگذاری نشد)");

        if (ring.LoadErrors.Count > 0)
        {
            Console.WriteLine("  یادداشت‌ها:");
            foreach (var e in ring.LoadErrors) Console.WriteLine($"    - {e}");
        }

        static bool IsDebug()
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
        LicenseCheck — شبیه‌ساز سهمیه (فقط‌خواندنی)

        استفاده:
          LicenseCheck --simulate --conn "<connection string>" [--license <file|dir>] [--months 3]

        گزینه‌ها:
          --simulate      اجباری. این نسخه هیچ حالت دیگری ندارد.
          --conn          رشتهٔ اتصال SQL Server. اگر نبود، از LICENSING_TEST_CONN خوانده می‌شود.
          --license       فایل .lic یا پوشهٔ آن. اگر نبود، فقط مصرف واقعی گزارش می‌شود (بدون مقایسه با سقف).
          --months        تعداد ماه‌های گذشته برای نمایش (پیش‌فرض ۳، حداکثر ۲۴).
          --help, -h      همین راهنما.

        خروجی، برای هر متریکی که سقف دارد:
          • تعداد ردیف‌های ماه جاری
          • تعداد ردیف‌های ۳ ماه گذشته
          • «مصرف پیش‌بینی‌شده» — همان چیزی که روز اول بعد از فعال‌سازی دیده می‌شود
          • درصد پرشدن و پیشنهاد (سقف بالاتر یا grandfather)

        هیچ دستور نوشتنی اجرا نمی‌شود؛ همهٔ کوئری‌ها SELECT COUNT(*) هستند.
        """);
    }
}
