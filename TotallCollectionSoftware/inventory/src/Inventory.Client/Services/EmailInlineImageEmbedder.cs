using System.Text.RegularExpressions;
using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

/// <summary>نتیجهٔ «خودکفا کردن» تصاویرِ درونِ متنِ ایمیل.</summary>
public sealed class InlineEmbedResult
{
    /// <summary>HTML نهایی بدنه (تصاویر جاسازی‌شده).</summary>
    public string Html { get; set; } = "";
    /// <summary>تعداد تصاویری که به‌صورت data URI داخل متن جاسازی شد.</summary>
    public int Embedded { get; set; }
    /// <summary>تعداد تصاویری که جاسازی نشد (قالب غیرتصویری/حجم زیاد/خطای دانلود).</summary>
    public int Skipped { get; set; }
}

/// <summary>
/// تصاویرِ درونِ متنِ ایمیل (inline) در بدنهٔ ذخیره‌شده با آدرسِ نسبیِ
/// <c>/api/email/attachments/{id}/download</c> ارجاع داده می‌شوند. این آدرس فقط در
/// «صفحهٔ ایمیل» کار می‌کند، چون آن‌جا هم مطلق می‌شود و هم توکنِ دسترسی به آن
/// اضافه می‌شود (تگ img نمی‌تواند هدر Bearer بفرستد).
/// <para>
/// وقتی همان بدنه به «متن نامه» منتقل می‌شد، تصویر در نامه دیده نمی‌شد:
/// آدرسِ نسبی به میزبانِ خودِ برنامه (WASM) می‌خورد نه سرور API، و اگر مطلق هم
/// می‌شد بدون توکن ۴۰۱ می‌گرفت. راه‌حل: بایت‌های تصویر دانلود و به‌صورت
/// <c>data:image/…;base64,…</c> در همان تگ جاسازی می‌شود. نتیجه «خودکفا» است:
/// در فرم ثبت، در نمایش نامه، در چاپ و در ذخیرهٔ بعدی بدون توکن و بدون وابستگی
/// به عمرِ توکن کار می‌کند.
/// </para>
/// </summary>
public static class EmailInlineImageEmbedder
{
    /// <summary>سقف حجم هر تصویر (۱٫۵ مگابایت) تا متن نامه باد نکند.</summary>
    private const long MaxSingleBytes = 1_500_000;

    /// <summary>سقف مجموع تصاویرِ جاسازی‌شده در یک نامه (۴ مگابایت).</summary>
    private const long MaxTotalBytes = 4_000_000;

    /// <summary>آدرسِ تصویرِ درون‌متنِ ایمیل؛ تا انتهای مقدارِ صفت خوانده می‌شود.</summary>
    private static readonly Regex InlineUrl = new(
        @"/api/email/attachments/(?<id>\d+)/download[^""'\s<>)]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>تگ‌هایی که بعد از شکستِ جاسازی، src خالی دارند و باید به یادداشت تبدیل شوند.</summary>
    private static readonly Regex BrokenImage = new(
        @"<img\b[^>]*\bsrc\s*=\s*""""[^>]*>|<img\b(?![^>]*\bsrc\s*=)[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// تصاویرِ درونِ متن را داخل HTML بدنه جاسازی می‌کند.
    /// بدنه باید از قبل پاک‌سازی (Sanitize) شده باشد تا فقط تگ‌های مجاز بمانند.
    /// </summary>
    public static async Task<InlineEmbedResult> EmbedAsync(string? html, EmailMessageDetailDto? email, IEmailClientService svc)
    {
        var result = new InlineEmbedResult { Html = html ?? "" };
        if (email == null || string.IsNullOrWhiteSpace(html)) return result;

        var matches = InlineUrl.Matches(result.Html);
        if (matches.Count == 0) return result;

        var ids = matches
            .Select(m => int.TryParse(m.Groups["id"].Value, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();
        if (ids.Count == 0) return result;

        var dataUris = new Dictionary<int, string>();
        long totalBytes = 0;

        foreach (var id in ids)
        {
            var att = email.Attachments.FirstOrDefault(a => a.AttachmentId == id);

            // ۱) کنترل حجمِ اعلامی، پیش از دانلود
            if (att != null && att.Size > MaxSingleBytes) continue;
            if (totalBytes >= MaxTotalBytes) continue;

            byte[] data;
            try
            {
                var (bytes, _) = await svc.DownloadAttachmentAsync(id);
                data = bytes ?? Array.Empty<byte>();
            }
            catch { continue; }
            if (data.Length == 0) continue;

            // ۲) کنترل حجمِ واقعی، پس از دانلود
            if (data.Length > MaxSingleBytes || totalBytes + data.Length > MaxTotalBytes) continue;

            var mime = DetectImageMime(att?.ContentType, data);
            // فقط قالب‌هایی که پاک‌سازی‌کنندهٔ متن نامه (RichLetterHtml) قبول می‌کند
            if (mime is null) continue;

            dataUris[id] = $"data:{mime};base64,{Convert.ToBase64String(data)}";
            totalBytes += data.Length;
        }

        if (dataUris.Count == 0) return result;

        result.Html = InlineUrl.Replace(result.Html, m =>
        {
            if (int.TryParse(m.Groups["id"].Value, out var id) && dataUris.TryGetValue(id, out var uri))
            {
                result.Embedded++;
                return uri;
            }
            result.Skipped++;
            // src خالی می‌ماند تا در پاس بعدی تگ به یادداشت تبدیل شود
            return "";
        });

        if (result.Skipped > 0)
            result.Html = BrokenImage.Replace(
                result.Html,
                "<span style=\"color:#64748b;font-size:12px\">[تصویر درون‌متن ایمیل قابل انتقال نبود — پیام اصلی را در پست الکترونیک ببینید]</span>");

        return result;
    }

    /// <summary>
    /// نوعِ MIME تصویر: اول از ContentTypeِ پیوست، وگرنه از «امضای فایل» (چند بایت اول).
    /// خروجی فقط یکی از قالب‌های مجازِ پاک‌سازی‌کننده است، وگرنه null.
    /// </summary>
    private static string? DetectImageMime(string? contentType, byte[] data)
    {
        var fromAttachment = NormalizeMime(contentType);
        if (fromAttachment != null) return fromAttachment;
        return SniffMime(data);
    }

    private static string? NormalizeMime(string? contentType)
    {
        var mime = (contentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
        return mime switch
        {
            "image/png" => "image/png",
            "image/jpeg" or "image/jpg" or "image/pjpeg" => "image/jpeg",
            "image/gif" => "image/gif",
            "image/webp" => "image/webp",
            _ => null
        };
    }

    private static string? SniffMime(byte[] data)
    {
        if (data.Length < 12) return null;
        // PNG: 89 50 4E 47
        if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return "image/png";
        // JPEG: FF D8 FF
        if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return "image/jpeg";
        // GIF: GIF8
        if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38) return "image/gif";
        // WEBP: RIFF....WEBP
        if (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 &&
            data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50) return "image/webp";
        return null;
    }
}
