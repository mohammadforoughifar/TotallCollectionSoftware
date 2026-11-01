using System.Globalization;

namespace Inventory.Api.Services.Office;

/// <summary>
/// «فایل‌های ورودی چاپ» یک شرکت را با یک قاعدهٔ مشترک برای هر سه چاپ پیدا می‌کند:
/// نامهٔ داخلی، نامهٔ صادره و صورتجلسه.
///
/// <para>پیش از این، هر سه سرویس مسیرها را جداگانه می‌ساختند و نتیجه این بود که یک شرکت
/// (مثلاً ۱۰۰۵) در یکی از سه چاپ درست پیدا می‌شد و در دو تای دیگر نه:</para>
/// <list type="bullet">
///   <item>نامهٔ داخلی پوشهٔ داخل مخزن (<c>inventory/src/Inventory.Api/Reports/…</c>) را
///   می‌گشت، ولی نامهٔ صادره و صورتجلسه فقط ContentRoot/BaseDirectory را — پس قالب تازه‌ای
///   که در مخزن اضافه شده بود، تا اولین Publish کامل دیده نمی‌شد.</item>
///   <item>نامهٔ صادره نامِ بدون مسیر («aria-1005-letterhead.pdf») را داخل
///   <c>wwwroot/uploads/letterheads</c> می‌جست، ولی نامهٔ داخلی و صورتجلسه فقط
///   <c>wwwroot/&lt;همان متن&gt;</c> را — پس فایل ثبت‌شده پیدا نمی‌شد و چاپ یا بدون سربرگ
///   انجام می‌شد یا با خطای «سربرگ پیدا نشد» متوقف می‌شد.</item>
/// </list>
///
/// <para>اکنون هر سه سرویس از همین کلاس می‌پرسند، با این ترتیب اولویت:</para>
/// <list type="number">
///   <item>مسیر صریح تنظیمات (<c>Reporting:*:TemplateRoot</c>) — مطلق یا نسبت به ریشه‌ها.</item>
///   <item>پوشهٔ داخل مخزن سورس: <c>{بالاتر}/inventory/src/Inventory.Api/Reports/{Kind}/Companies/{Id}</c>
///         (همان مسیری که قالب‌ها در آن ویرایش می‌شوند و کارفرما گفته است).</item>
///   <item>ContentRoot — حالت <c>dotnet run</c> از پوشهٔ پروژه.</item>
///   <item>BaseDirectory — نسخهٔ منتشرشده (Publish).</item>
///   <item>پوشهٔ جاری (<c>Directory.GetCurrentDirectory()</c>).</item>
/// </list>
///
/// <para>برای سربرگ: مقدار ثبت‌شده روی رکورد شرکت ممکن است «مسیر کامل نسبی»
/// (<c>uploads/letterheads/x.pdf</c>) یا فقط «نام فایل» (<c>x.pdf</c>) باشد — هر دو پوشش داده
/// می‌شوند، به‌همراه جست‌وجوی بدون‌حساسیت‌به‌حالت/نویسه‌های فارسی داخل
/// <c>wwwroot/uploads/letterheads</c> (ي/ك عربی، نیم‌فاصله و فاصله نادیده گرفته می‌شوند).</para>
/// </summary>
internal static class PrintAssetResolver
{
    internal const string OutgoingLetters = "OutgoingLetters";
    internal const string InnerLetters = "InnerLetters";
    internal const string MeetingMinutes = "MeetingMinutes";

    /// <summary>سقف بالا رفتن از پوشهٔ اجرا برای رسیدن به ریشهٔ مخزن.</summary>
    private const int AncestorDepth = 12;

    private static readonly string[] RenderableExtensions = { ".pdf", ".png", ".jpg", ".jpeg" };

    // ==================== قالب MRT ====================

    /// <summary>
    /// پوشه‌های ممکن قالب یک شرکت، به ترتیب اولویت. <paramref name="roots"/> ریشه‌های اجرا
    /// است (ContentRoot، BaseDirectory، پوشهٔ جاری و در صورت نیاز WebRoot) — هر کدام
    /// می‌تواند <c>null</c> باشد.
    /// </summary>
    internal static IReadOnlyList<string> TemplateFolders(
        string kind, int companyId, string? templateRoot, params string?[] roots)
    {
        var company = companyId.ToString(CultureInfo.InvariantCulture);
        var result = new List<string>();

        void Add(string? folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return;
            result.Add(TrimSeparators(folder!));
        }

        var cleanRoots = Clean(roots);

        // ۱) مسیر صریح تنظیمات. اگر انتهای مسیر خودِ پوشهٔ شرکت باشد، نام شرکت تکرار نمی‌شود.
        var configured = (templateRoot ?? string.Empty).Trim();
        if (configured.Length > 0)
        {
            var normalized = TrimSeparators(configured.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar));
            if (Path.IsPathRooted(normalized))
            {
                Add(WithCompany(normalized, company));
            }
            else
            {
                foreach (var root in cleanRoots) Add(WithCompany(Path.Combine(root, normalized), company));
            }
        }

        // ۲) خودِ مخزن سورس — قبل از کپی‌های بیلد، تا ویرایش قالب بلافاصله در چاپ دیده شود.
        foreach (var ancestor in Ancestors(cleanRoots))
        {
            Add(Path.Combine(ancestor, "inventory", "src", "Inventory.Api", "Reports", kind, "Companies", company));
            Add(Path.Combine(ancestor, "src", "Inventory.Api", "Reports", kind, "Companies", company));
            Add(Path.Combine(ancestor, "Reports", kind, "Companies", company));
        }

        // ۳) ریشه‌های اجرا.
        foreach (var root in cleanRoots)
            Add(Path.Combine(root, "Reports", kind, "Companies", company));

        return Distinct(result);
    }

    /// <summary>مسیرهای ممکن فایل قالب، به ترتیب اولویت (پوشه‌ها × نام فایل).</summary>
    internal static IReadOnlyList<string> TemplateCandidates(
        string kind, int companyId, string fileName, string? templateRoot, params string?[] roots)
        => TemplateFolders(kind, companyId, templateRoot, roots)
            .Select(folder => Path.Combine(folder, fileName)).ToArray();

    /// <summary>
    /// نسخهٔ «پوشهٔ طراحی» قالب‌ها (پوشهٔ <c>Report/</c> در ریشهٔ مخزن) — همین‌جا فایل‌های
    /// Stimulsoft با نام قدیمی نگهداری می‌شوند و آخرین جای جست‌وجو است.
    /// </summary>
    internal static IReadOnlyList<string> DesignFolderCandidates(
        string reportFolder, int companyId, string fileName, params string?[] roots)
    {
        var company = companyId.ToString(CultureInfo.InvariantCulture);
        var list = new List<string>();
        foreach (var ancestor in Ancestors(Clean(roots)))
            list.Add(Path.Combine(ancestor, "Report", reportFolder, company, fileName));
        return Distinct(list);
    }

    /// <summary>اولین مسیر موجود از فهرست (null = هیچ‌کدام).</summary>
    internal static string? FirstExisting(IEnumerable<string> candidates)
        => candidates.FirstOrDefault(File.Exists);

    // ==================== فایل سربرگ ثبت‌شده ====================

    /// <summary>
    /// مسیرهای ممکن فایل سربرگِ ثبت‌شده روی رکورد شرکت. <paramref name="fileName"/> می‌تواند
    /// <c>uploads/letterheads/x.pdf</c>، <c>letterheads/x.pdf</c> یا فقط <c>x.pdf</c> باشد.
    /// </summary>
    internal static IReadOnlyList<string> LetterheadCandidates(string? fileName, params string?[] roots)
    {
        var clean = (fileName ?? string.Empty).Trim().Replace('\\', '/').TrimStart('/');
        if (clean.Length == 0) return Array.Empty<string>();
        // فقط بالا رفتن از پوشه ممنوع است؛ نامی مثل «نامه..pdf» مجاز می‌ماند.
        if (clean.Split('/').Any(segment => segment == "..")) return Array.Empty<string>();

        var name = Path.GetFileName(clean);
        if (name.Length == 0) return Array.Empty<string>();
        var withoutUploads = clean.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase)
            ? clean["uploads/".Length..]
            : clean;

        var list = new List<string>();
        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path)) list.Add(path!);
        }

        var cleanRoots = Clean(roots);
        foreach (var root in cleanRoots)
        {
            Add(Path.Combine(root, clean));
            Add(Path.Combine(root, "uploads", withoutUploads));
            Add(Path.Combine(root, "uploads", "letterheads", name));
            Add(Path.Combine(root, "wwwroot", "uploads", "letterheads", name));
        }

        // فایل ممکن است فقط در مخزن باشد (سرور توسعه) و در پوشهٔ اجرا کپی نشده باشد.
        foreach (var ancestor in Ancestors(cleanRoots))
        {
            Add(Path.Combine(ancestor, "inventory", "src", "Inventory.Api", "wwwroot", "uploads", "letterheads", name));
            Add(Path.Combine(ancestor, "src", "Inventory.Api", "wwwroot", "uploads", "letterheads", name));
        }

        return Distinct(list);
    }

    /// <summary>
    /// فایل سربرگ قابل‌چاپ؛ اگر مسیر دقیق پیدا نشد، پوشهٔ <c>uploads/letterheads</c> با مقایسهٔ
    /// نامِ نرمال‌شده گشته می‌شود (ي/ك عربی، نیم‌فاصله، فاصله و خط تیره نادیده گرفته می‌شوند).
    /// </summary>
    internal static string? Letterhead(string? fileName, params string?[] roots)
    {
        var candidates = LetterheadCandidates(fileName, roots);
        var found = FirstExisting(candidates);
        if (found != null) return found;

        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
        if (name.Length == 0) return null;
        var wanted = Normalize(name);

        foreach (var folder in LetterheadFolders(Clean(roots)))
        {
            if (!Directory.Exists(folder)) continue;
            try
            {
                var match = Directory.EnumerateFiles(folder)
                    .Where(IsRenderable)
                    .FirstOrDefault(file => Normalize(Path.GetFileName(file)) == wanted);
                if (match != null) return match;
            }
            catch
            {
                // پوشهٔ بدون دسترسی خواندن، جست‌وجو را متوقف نمی‌کند.
            }
        }

        return null;
    }

    /// <summary>پوشه‌های ممکن سربرگ‌ها (برای جست‌وجوی نامِ نرمال‌شده و پیام خطا).</summary>
    internal static IReadOnlyList<string> LetterheadFolders(params string?[] roots)
    {
        var list = new List<string>();
        foreach (var root in Clean(roots))
        {
            list.Add(Path.Combine(root, "uploads", "letterheads"));
            list.Add(Path.Combine(root, "wwwroot", "uploads", "letterheads"));
        }

        foreach (var ancestor in Ancestors(Clean(roots)))
        {
            list.Add(Path.Combine(ancestor, "inventory", "src", "Inventory.Api", "wwwroot", "uploads", "letterheads"));
            list.Add(Path.Combine(ancestor, "src", "Inventory.Api", "wwwroot", "uploads", "letterheads"));
        }

        return Distinct(list).Where(Directory.Exists).ToArray();
    }

    /// <summary>پسوندهای قابل‌چاپ (PDF/تصویر) — مقدارهای دیگر (مثل خودِ MRT) پذیرفته نمی‌شوند.</summary>
    internal static bool IsRenderable(string? path)
        => path is { Length: > 0 }
           && RenderableExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>مسیر موجود و قابل‌چاپ (null = سربرگ ثبت‌شده پیدا نشد).</summary>
    internal static string? RenderableLetterhead(string? fileName, params string?[] roots)
    {
        var path = Letterhead(fileName, roots);
        return IsRenderable(path) ? path : null;
    }

    // ==================== رسم سربرگ روی صفحه ====================

    /// <summary>
    /// مستطیل رسم فایل سربرگ روی کاغذ. اگر نسبت ابعاد فایل با کاغذ یکی باشد، همان کل صفحه است
    /// (رفتار همیشگی). اگر فایل با ارتفاع/عرض اشتباه ذخیره شده باشد (مثلاً
    /// <c>aria-1005-letterhead.pdf</c> که ۸۶۰٫۶pt ارتفاع دارد به‌جای ۸۴۱٫۸۹pt)، کشیدنِ آن روی
    /// کل صفحه تصویر را ۲٪ می‌فشرد و «خط نقطه‌چین» چند point از جای محاسبه‌شدهٔ مقدارها دور
    /// می‌افتاد. در این حالت با حفظ نسبت و «لنگر بالا» رسم می‌شود: عرض دقیقاً برابر صفحه
    /// می‌ماند (پس نقطه‌چین‌ها افقی جابه‌جا نمی‌شوند) و اضافهٔ عمودی از پایین بریده می‌شود.
    ///
    /// <para>اختلاف بیش از ۳٪ یعنی کاغذ دیگری است (مثل Letter به‌جای A4)؛ آن‌جا رفتار قبلی
    /// (کشیدن کامل) حفظ می‌شود تا ظاهر هیچ شرکت دیگری عوض نشود.</para>
    /// </summary>
    internal static (double X, double Y, double Width, double Height) LetterheadRect(
        double pageWidth, double pageHeight, double assetWidth, double assetHeight)
    {
        if (pageWidth <= 0 || pageHeight <= 0 || assetWidth <= 0 || assetHeight <= 0)
            return (0, 0, pageWidth, pageHeight);

        var pageAspect = pageWidth / pageHeight;
        var assetAspect = assetWidth / assetHeight;
        var drift = Math.Abs(assetAspect - pageAspect) / pageAspect;
        if (drift <= 0.005 || drift > 0.03) return (0, 0, pageWidth, pageHeight);

        var scale = Math.Max(pageWidth / assetWidth, pageHeight / assetHeight);
        var width = assetWidth * scale;
        var height = assetHeight * scale;
        return ((pageWidth - width) / 2, 0, width, height);
    }

    // ==================== پیام خطا ====================

    /// <summary>فهرست کوتاه مسیرهای بررسی‌شده برای پیام خطا/لاگ (تا ۱۰ مورد).</summary>
    internal static string Describe(IEnumerable<string>? paths, int max = 10)
    {
        var list = paths?.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray() ?? Array.Empty<string>();
        if (list.Length == 0) return "—";
        var shown = string.Join(" | ", list.Take(max));
        return list.Length > max ? $"{shown} | … (+{list.Length - max})" : shown;
    }

    // ==================== ابزارهای داخلی ====================

    private static IReadOnlyList<string> Clean(params string?[] roots)
    {
        var list = new List<string> { Directory.GetCurrentDirectory() };
        list.AddRange(roots.Where(root => !string.IsNullOrWhiteSpace(root)).Select(root => root!));

        var result = new List<string>();
        foreach (var raw in list)
        {
            try
            {
                var full = Path.GetFullPath(raw);
                if (!result.Contains(full, StringComparer.OrdinalIgnoreCase)) result.Add(full);
            }
            catch
            {
                // مسیر نامعتبر (کاراکتر غیرمجاز و…) نادیده گرفته می‌شود.
            }
        }

        return result;
    }

    private static IEnumerable<string> Ancestors(IEnumerable<string> starts)
    {
        var seen = new List<string>();
        foreach (var start in starts)
        {
            DirectoryInfo? directory;
            try
            {
                directory = new DirectoryInfo(start);
            }
            catch
            {
                continue;
            }

            for (var depth = 0; directory != null && depth < AncestorDepth; depth++, directory = directory.Parent)
            {
                if (seen.Contains(directory.FullName, StringComparer.OrdinalIgnoreCase)) continue;
                seen.Add(directory.FullName);
                yield return directory.FullName;
            }
        }
    }

    private static string WithCompany(string folder, string company)
    {
        var leaf = Path.GetFileName(folder);
        return string.Equals(leaf, company, StringComparison.OrdinalIgnoreCase)
            ? folder
            : Path.Combine(folder, company);
    }

    private static string TrimSeparators(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static IReadOnlyList<string> Distinct(IEnumerable<string> values)
    {
        var result = new List<string>();
        foreach (var value in values)
            if (!result.Contains(value, StringComparer.OrdinalIgnoreCase)) result.Add(value);
        return result;
    }

    /// <summary>نام فایل بدون تفاوت‌های رایج فارسی/عربی و بدون جداکننده‌ها — برای مقایسه.</summary>
    private static string Normalize(string fileName)
    {
        var builder = new System.Text.StringBuilder(fileName.Length);
        foreach (var ch in fileName)
        {
            switch (ch)
            {
                case '\u064A': // ي عربی
                case '\u0649':
                    builder.Append('\u06CC'); // ی فارسی
                    break;
                case '\u0643': // ك عربی
                    builder.Append('\u06A9'); // ک فارسی
                    break;
                case '\u0629':
                case '\u06C0':
                    builder.Append('\u0647');
                    break;
                case '\u200C': // نیم‌فاصله
                case '\u200E':
                case '\u200F':
                case ' ':
                case '-':
                case '_':
                case '.':
                case '(':
                case ')':
                    break;
                default:
                    builder.Append(char.ToLowerInvariant(ch));
                    break;
            }
        }

        return builder.ToString();
    }
}
