using System.Collections.Concurrent;
using System.Xml.Linq;
using SkiaSharp;

namespace Inventory.Api.Services.Office;

public sealed class InnerLetterPrintException(string message, Exception? inner = null) : Exception(message, inner)
{
}

/// <summary>
/// خواندن «فقط تصویر سربرگ» از قالب MRT — موتور Stimulsoft و دیکشنری دادهٔ قالب هرگز اجرا نمی‌شود.
///
/// <para>
/// چرا کش دارد؟ قالب هر شرکت یک فایل ۶۲۰ کیلوبایتی است که تصویر ۲۴۸۱×۳۵۰۸ (۴۵۶KB) را به‌صورت
/// Base64 در خود دارد. قبلاً این فایل در **هر بار چاپ** دو بار از نو خوانده و از نو تفسیر
/// (XML parse) می‌شد و تصویر ۸٫۷ مگاپیکسلی هم هر بار از نو دیکد می‌شد؛ نتیجه: یک جهش
/// پردازشی/حافظه‌ای چند صد میلی‌ثانیه‌ای (و روی سیستم‌های ضعیف، حس «هنگ کردن») برای هر چاپ.
/// اکنون هر فایل **یک بار در عمر پروسه** خوانده و آماده می‌شود و تا وقتی
/// (LastWriteTimeUtc, Length) فایل عوض نشده، همان نتیجهٔ آماده استفاده می‌شود.
/// </para>
///
/// <para>
/// چرا تصویر «آماده» می‌شود؟ سربرگ ۳۰۰dpi است؛ رندر PDF فقط به ۱۵۰ تا ۲۰۰dpi نیاز دارد
/// (نامهٔ اداری، تصویر پس‌زمینه، متن روی آن چاپ نمی‌شود). یک بار کوچک‌کردن تصویر با Skia و
/// کش‌کردن نتیجه باعث می‌شود: (۱) QuestPDF هر بار مجبور نباشد تصویر را تغییر اندازه و از نو
/// JPEG کند، (۲) حجم PDF و حافظهٔ لازم برای نمایش/چاپ در مرورگر چند برابر کمتر شود.
/// مقدار DPI با کلید <c>Reporting:InnerLetters:LetterheadDpi</c> قابل تنظیم است (۰ = بدون تغییر).
/// </para>
/// </summary>
internal static class InnerLetterheadTemplates
{
    /// <summary>DPI پیش‌فرض آماده‌سازی سربرگ (۳۰۰ اصلی، ۲۰۰ برای چاپ کافی و سبک‌تر است).</summary>
    internal const int DefaultArtworkDpi = 200;

    /// <summary>کیفیت JPEG خروجی آماده‌سازی — بالاتر از ۹۰ عملاً حجم را زیاد می‌کند و سودی ندارد.</summary>
    private const int ArtworkJpegQuality = 88;

    internal static string HomepaPath(string contentRoot, bool a5) => Path.Combine(contentRoot,
        "Reports", "InnerLetters", "Companies", "1003", $"Inner-{(a5 ? "A5" : "A4")}.mrt");

    /// <summary>
    /// مسیر قالب اختصاصی هر شرکت: Reports/InnerLetters/Companies/{companyId}/Inner-{A4|A5}.mrt
    /// (پیش‌تر شناسهٔ ۱۰۰۳ در کد ثابت بود و هر شرکتی که شناسه‌اش ۱۰۰۳ بود —
    /// مثلاً نصب‌شده روی پایگاه‌دادهٔ نمونه — سربرگ شرکت دیگری را می‌گرفت.)
    /// </summary>
    internal static string CompanyTemplatePath(string contentRoot, int companyId, bool a5) => Path.Combine(
        contentRoot, "Reports", "InnerLetters", "Companies",
        companyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        $"Inner-{(a5 ? "A5" : "A4")}.mrt");

    private sealed record ArtworkEntry(long Length, DateTime WriteUtc, byte[] Bytes);
    private sealed record LayoutEntry(long Length, DateTime WriteUtc, Outgoing.OutgoingLetterDesignLayout? Layout);

    private static readonly ConcurrentDictionary<string, ArtworkEntry> ArtworkCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, LayoutEntry> LayoutCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// خواندن تصویر سربرگ جاسازی‌شده در قالب همان شرکت. اگر قالب نباشد یا سربرگ
    /// جاسازی‌شده نداشته باشد <c>null</c> برمی‌گردد (نه خطا) تا مسیرهای دیگر امتحان شوند.
    /// نتیجه برای هر نسخهٔ فایل کش می‌شود؛ تکرار چاپ دیگر فایل ۶۲۰KB را دوباره نمی‌خواند.
    /// </summary>
    internal static byte[]? TryLoadForCompany(string contentRoot, string baseDirectory, int companyId, bool a5,
        int dpi = DefaultArtworkDpi)
    {
        if (companyId <= 0) return null;
        var paths = new[] { CompanyTemplatePath(contentRoot, companyId, a5), CompanyTemplatePath(baseDirectory, companyId, a5) }
            .Distinct()
            .ToArray();
        foreach (var path in paths)
        {
            var bytes = TryLoadArtwork(path, a5, dpi);
            if (bytes != null) return bytes;
        }

        return null;
    }

    /// <summary>
    /// هندسهٔ کادرهای شماره/تاریخ/پیوست از خودِ قالب نامهٔ داخلی همان شرکت (همان کادرهای
    /// قالب صادره که در این فایل تکرار شده‌اند). خواندن فقط XML است؛ موتور Stimulsoft اجرا
    /// نمی‌شود. اگر قالب نصب‌شده این کادرها را نداشته باشد <c>null</c> برمی‌گردد و رندر از
    /// مختصات کالیبرهٔ <see cref="Outgoing.HomepaLetterheadLayout"/> استفاده می‌کند.
    /// نتیجهٔ تفسیر برای هر نسخهٔ فایل کش می‌شود (قبلاً در هر چاپ، فایل دوباره پارس می‌شد).
    /// </summary>
    internal static Outgoing.OutgoingLetterDesignLayout? TryLoadMetaLayout(
        string contentRoot, string baseDirectory, int companyId, bool a5)
    {
        if (companyId <= 0) return null;
        var paths = new[]
        {
            CompanyTemplatePath(contentRoot, companyId, a5),
            CompanyTemplatePath(baseDirectory, companyId, a5)
        }.Distinct();
        foreach (var path in paths)
        {
            var layout = TryLoadLayout(path);
            if (layout == null) continue;
            if (HasMetaBoxes(layout)) return layout;
        }

        return null;
    }

    /// <summary>تصویر آمادهٔ سربرگ ۱۰۰۳ (کش‌شده). در صورت نبود/خرابی قالب، استثنای روشن می‌دهد.</summary>
    internal static byte[] LoadHomepa(string contentRoot, string baseDirectory, bool a5,
        int dpi = DefaultArtworkDpi)
    {
        var paths = new[] { HomepaPath(contentRoot, a5), HomepaPath(baseDirectory, a5) }.Distinct().ToArray();
        var path = paths.FirstOrDefault(File.Exists)
            ?? throw new InnerLetterPrintException($"قالب سربرگ نامه داخلی شرکت ۱۰۰۳ پیدا نشد: {string.Join(" | ", paths)}");
        try
        {
            return TryLoadArtwork(path, a5, dpi)
                ?? throw new InvalidDataException("Watermark.ImageBytes خالی است.");
        }
        catch (Exception ex) when (ex is not InnerLetterPrintException)
        {
            throw new InnerLetterPrintException($"خواندن سربرگ نامه داخلی شرکت ۱۰۰۳ ناموفق بود: {path}", ex);
        }
    }

    /// <summary>
    /// تصویر سربرگ یک فایل قالب، آمادهٔ چاپ. کلید کش: مسیر فایل؛ اعتبارسنجی: اندازه و زمان
    /// تغییر فایل؛ یعنی اگر قالب را روی سرور عوض کنید، نسخهٔ تازه خودکار خوانده می‌شود.
    /// </summary>
    private static byte[]? TryLoadArtwork(string path, bool a5, int dpi)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return null;

        // DPI هم بخشی از کلید است تا تغییر تنظیمات، تصویر آمادهٔ نسخهٔ قبلی را برنگرداند.
        var key = $"{path}|{dpi}";
        if (ArtworkCache.TryGetValue(key, out var cached)
            && cached.Length == info.Length && cached.WriteUtc == info.LastWriteTimeUtc)
            return cached.Bytes;

        byte[]? bytes;
        using (var stream = File.OpenRead(path))
        {
            var document = XDocument.Load(stream);
            var encoded = document.Descendants().Where(e => e.Name.LocalName == "Watermark")
                .SelectMany(e => e.Elements()).FirstOrDefault(e => e.Name.LocalName == "ImageBytes")?.Value;
            if (string.IsNullOrWhiteSpace(encoded)) return null;
            var decoded = Convert.FromBase64String(encoded);
            bytes = decoded.Length == 0 ? null : PrepareArtwork(decoded, a5, dpi);
        }

        if (bytes == null) return null;
        ArtworkCache[key] = new ArtworkEntry(info.Length, info.LastWriteTimeUtc, bytes);
        return bytes;
    }

    private static Outgoing.OutgoingLetterDesignLayout? TryLoadLayout(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return null;

        if (LayoutCache.TryGetValue(path, out var cached)
            && cached.Length == info.Length && cached.WriteUtc == info.LastWriteTimeUtc)
            return cached.Layout;

        var layout = Outgoing.OutgoingLetterDesignLayout.TryRead(path);
        LayoutCache[path] = new LayoutEntry(info.Length, info.LastWriteTimeUtc, layout);
        return layout;
    }

    private static bool HasMetaBoxes(Outgoing.OutgoingLetterDesignLayout layout) => layout.HeaderBoxes.Any(box =>
        box.Slot is Outgoing.OutgoingLetterDesignLayout.Slot.Number
            or Outgoing.OutgoingLetterDesignLayout.Slot.Date
            or Outgoing.OutgoingLetterDesignLayout.Slot.Attachment);

    /// <summary>
    /// یک بار کوچک‌کردن تصویر سربرگ به DPI هدف (عرض صفحه × DPI). اگر تصویر از قبل کوچک‌تر
    /// باشد یا Skia در دسترس نباشد، همان بایت‌های اصلی برگردانده می‌شود (چاپ فقط کندتر می‌شود،
    /// خراب نمی‌شود).
    /// </summary>
    private static byte[] PrepareArtwork(byte[] encoded, bool a5, int targetDpi)
    {
        if (targetDpi <= 0) return encoded;
        try
        {
            using var bitmap = SKBitmap.Decode(encoded);
            if (bitmap == null) return encoded;

            // عرض کاغذ: A4 = ۸٫۲۷ اینچ و A5 = ۵٫۸۳ اینچ؛ تصویر سربرگ تمام صفحه را می‌پوشاند.
            var pageWidthInches = a5 ? 5.83f : 8.27f;
            var targetWidth = (int)Math.Round(pageWidthInches * targetDpi);
            if (bitmap.Width <= targetWidth + 8) return encoded;

            var scale = targetWidth / (float)bitmap.Width;
            var targetHeight = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
            using var resized = bitmap.Resize(new SKImageInfo(targetWidth, targetHeight), SKFilterQuality.High);
            if (resized == null) return encoded;

            using var image = SKImage.FromBitmap(resized);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, ArtworkJpegQuality);
            var prepared = data?.ToArray();
            return prepared is { Length: > 0 } && prepared.Length < encoded.Length ? prepared : encoded;
        }
        catch
        {
            return encoded;
        }
    }

    /// <summary>
    /// پاک‌کردن کش‌ها — فقط برای تست/ابزارهای نگهداری. در زمان اجرا لازم نیست: تغییر فایل
    /// قالب خودکار شناسایی می‌شود.
    /// </summary>
    internal static void ClearCache()
    {
        ArtworkCache.Clear();
        LayoutCache.Clear();
    }
}
