using Microsoft.AspNetCore.Http;

namespace Inventory.Api.Services;

// ============================================================
//  ذخیره‌سازی پیوست‌های نامه داخلی روی دیسک
//  ------------------------------------------------------------
//  مسیر فیزیکی فایل‌ها:
//      wwwroot/uploads/innerletter/{letterId}/{guid}_{نام اصلی}
//      wwwroot/uploads/innerletter/pishnevis/{pishnevisId}/{guid}_{نام اصلی}
//  در جدول AppAttachment فقط «اطلاعات فایل» (نام، نوع، حجم، مسیر نسبی)
//  ذخیره می‌شود و ستون Data خالی می‌ماند. رکوردهای قدیمی که فایلشان داخل
//  دیتابیس است همچنان از Data خوانده می‌شوند (سازگاری به عقب).
// ============================================================

public class LetterAttachmentStore
{
    /// <summary>ریشه‌ی پیوست‌های نامه داخلی: wwwroot/uploads/innerletter</summary>
    private readonly string _root;

    public LetterAttachmentStore(IWebHostEnvironment env)
    {
        _root = Path.Combine(env.ContentRootPath, "wwwroot", "uploads", "innerletter");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "pishnevis"));
    }

    public string RootPath => _root;

    /// <summary>پوشه‌ی نامه (یا پیش‌نویس) — در صورت نبود ساخته می‌شود</summary>
    private string FolderFor(int refId, bool pishnevis)
    {
        var dir = pishnevis
            ? Path.Combine(_root, "pishnevis", refId.ToString())
            : Path.Combine(_root, refId.ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>مسیر نسبی داخل uploads/ (همان چیزی که در AppAttachment.FilePath ذخیره می‌شود)</summary>
    private static string Relative(string full, string uploadsRoot) =>
        Path.GetRelativePath(uploadsRoot, full).Replace('\\', '/');

    private string UploadsRoot => Path.GetFullPath(Path.Combine(_root, ".."));

    /// <summary>ذخیره‌ی فایل آپلودی روی دیسک — مسیر نسبی برمی‌گرداند (innerletter/…)</summary>
    public async Task<string> SaveAsync(int refId, bool pishnevis, Stream stream, string originalName)
    {
        var dir = FolderFor(refId, pishnevis);
        var file = Path.Combine(dir, $"{Guid.NewGuid():N}_{Sanitize(originalName)}");
        await using (var fs = File.Create(file))
            await stream.CopyToAsync(fs);
        return Relative(file, UploadsRoot);
    }

    /// <summary>مسیر مطلق امن از روی مسیر نسبی (ضد path traversal)</summary>
    public string? FullPath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var clean = relativePath.Replace('\\', '/').TrimStart('/');
        if (clean.Contains("..")) return null;
        var full = Path.GetFullPath(Path.Combine(UploadsRoot, clean));
        return full.StartsWith(Path.GetFullPath(_root), StringComparison.Ordinal) ? full : null;
    }

    public bool Exists(string? relativePath)
    {
        var f = FullPath(relativePath);
        return f is not null && File.Exists(f);
    }

    public long Size(string? relativePath)
    {
        var f = FullPath(relativePath);
        return f is not null && File.Exists(f) ? new FileInfo(f).Length : 0;
    }

    /// <summary>استریم خواندن فایل (برای دانلود/مشاهده) — null اگر فایل نباشد</summary>
    public Stream? OpenRead(string? relativePath)
    {
        var f = FullPath(relativePath);
        if (f is null || !File.Exists(f)) return null;
        try { return File.OpenRead(f); } catch { return null; }
    }

    public byte[]? ReadBytes(string? relativePath)
    {
        var f = FullPath(relativePath);
        if (f is null || !File.Exists(f)) return null;
        try { return File.ReadAllBytes(f); } catch { return null; }
    }

    public void Delete(string? relativePath)
    {
        var f = FullPath(relativePath);
        if (f is not null && File.Exists(f))
        {
            try { File.Delete(f); } catch { }
        }
    }

    /// <summary>جابه‌جایی فایل پیش‌نویس به پوشه‌ی نامه‌ی ارسال‌شده — مسیر نسبی جدید</summary>
    public string? Move(string? relativePath, int newLetterId)
    {
        var src = FullPath(relativePath);
        if (src is null || !File.Exists(src)) return relativePath;
        var dir = FolderFor(newLetterId, pishnevis: false);
        var dest = Path.Combine(dir, Path.GetFileName(src));
        try
        {
            File.Move(src, dest, overwrite: true);
            return Relative(dest, UploadsRoot);
        }
        catch { return relativePath; }
    }

    /// <summary>نوع محتوا بر اساس پسوند — برای مشاهده‌ی درون‌مرورگری (inline)</summary>
    public static string GuessContentType(string fileName)
    {
        var e = (Path.GetExtension(fileName) ?? "").TrimStart('.').ToLowerInvariant();
        return e switch
        {
            "pdf" => "application/pdf",
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "gif" => "image/gif",
            "webp" => "image/webp",
            "svg" => "image/svg+xml",
            "bmp" => "image/bmp",
            "txt" or "log" => "text/plain; charset=utf-8",
            "csv" => "text/csv; charset=utf-8",
            "html" or "htm" => "text/html; charset=utf-8",
            "mp4" => "video/mp4",
            "mp3" => "audio/mpeg",
            "doc" => "application/msword",
            "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "xls" => "application/vnd.ms-excel",
            "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "ppt" => "application/vnd.ms-powerpoint",
            "pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            "zip" => "application/zip",
            "rar" => "application/vnd.rar",
            "7z" => "application/x-7z-compressed",
            _ => "application/octet-stream"
        };
    }

    /// <summary>آیا این نوع فایل در مرورگر قابل نمایش است؟ (پیش‌نمایش بدون دانلود)</summary>
    public static bool IsInlineViewable(string fileName)
    {
        var e = (Path.GetExtension(fileName) ?? "").TrimStart('.').ToLowerInvariant();
        return e is "pdf" or "png" or "jpg" or "jpeg" or "gif" or "webp" or "svg" or "bmp"
                 or "txt" or "log" or "csv" or "mp4" or "mp3";
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder();
        foreach (var ch in Path.GetFileName(name ?? "file"))
            sb.Append(invalid.Contains(ch) ? '_' : ch);
        var s = sb.ToString().Trim();
        if (string.IsNullOrEmpty(s)) s = "file";
        return s.Length > 100 ? s[..100] : s;
    }
}
