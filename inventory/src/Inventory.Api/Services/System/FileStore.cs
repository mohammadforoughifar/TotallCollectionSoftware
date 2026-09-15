using Microsoft.AspNetCore.Http;

namespace Inventory.Api.Services;

/// <summary>
/// ذخیره‌سازی یکپارچه فایل‌ها روی دیسک — همه‌ی فایل‌ها بر اساس ماژول در پوشه‌ی wwwroot/uploads/{module}/ قرار می‌گیرند.
/// در دیتابیس فقط مسیر نسبی (اطلاعات) ثبت می‌شود.
/// ساختار: wwwroot/uploads/{module}/{refId}/{guid}_{نام-اصلی}
/// </summary>
public class FileStore
{
    private readonly string _root;
    private readonly string _webRoot;
    private readonly string _contentRoot;

    public FileStore(IWebHostEnvironment env)
    {
        _contentRoot = env.ContentRootPath;
        _webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        _root = Path.Combine(_webRoot, "uploads");
        Directory.CreateDirectory(_root);
    }

    public string RootPath => _root;
    public string WebRootPath => _webRoot;
    public string ContentRootPath => _contentRoot;

    /// <summary>
    /// ساخت امن مسیر ماژول با حفظ ساختار زيرپوشه‌ها (مانند office/innerletter یا hr/employee/documents)
    /// و جلوگیری از Path Traversal.
    /// </summary>
    private static string SafeModule(string m)
    {
        if (string.IsNullOrWhiteSpace(m)) return "misc";
        var parts = m.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var cleanParts = new List<string>();
        foreach (var part in parts)
        {
            if (part == "." || part == "..") continue;
            var sb = new System.Text.StringBuilder();
            foreach (var ch in part)
                if (char.IsLetterOrDigit(ch) || ch is '-' or '_') sb.Append(ch);
            if (sb.Length > 0) cleanParts.Add(sb.ToString());
        }
        return cleanParts.Count > 0 ? string.Join('/', cleanParts) : "misc";
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder();
        foreach (var ch in Path.GetFileName(name ?? "file"))
            sb.Append(invalid.Contains(ch) ? '_' : ch);
        var s = sb.ToString();
        return s.Length > 80 ? s[..80] : s;
    }

    private string BuildDirectory(string module, int refId, string? subFolder = null)
    {
        var parts = new List<string> { _root, SafeModule(module) };
        if (!string.IsNullOrWhiteSpace(subFolder))
            parts.Add(SafeModule(subFolder));
        parts.Add(refId.ToString());
        return Path.Combine(parts.ToArray());
    }

    /// <summary>
    /// ذخیره فایل جدید زیر wwwroot/uploads/{module}/[subFolder/]{refId}/{guid}_{originalName}
    /// </summary>
    public async Task<string> SaveAsync(string module, int refId, Stream stream, string originalName, string? subFolder = null)
    {
        var safe = SanitizeName(originalName);
        var dir = BuildDirectory(module, refId, subFolder);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{Guid.NewGuid():N}_{safe}");
        await using (var fs = File.Create(file))
        {
            await stream.CopyToAsync(fs);
        }
        return ToRelative(file);
    }

    /// <summary>
    /// ذخیره بایت‌ها یا عکس خام زیر wwwroot/uploads/{module}/{ownerId|common}/{guid}.{ext}
    /// </summary>
    public async Task<string> SaveRawAsync(string module, byte[] data, string ext, int? ownerId = null)
    {
        var dir = Path.Combine(_root, SafeModule(module), ownerId?.ToString() ?? "common");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{Guid.NewGuid():N}{(ext.StartsWith('.') ? ext : "." + ext)}");
        await File.WriteAllBytesAsync(file, data);
        return ToRelative(file);
    }

    /// <summary>
    /// انتقال فایل به پوشه/مسیر جدید زیر wwwroot/uploads
    /// </summary>
    public string? Move(string? relativePath, string module, int refId, string? subFolder = null)
    {
        var full = ToFull(relativePath);
        if (full is null || !File.Exists(full)) return null;

        var dir = BuildDirectory(module, refId, subFolder);
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, Path.GetFileName(full));

        if (Path.GetFullPath(dest) == Path.GetFullPath(full)) return ToRelative(dest);

        try
        {
            File.Move(full, dest, true);
        }
        catch
        {
            return null;
        }

        return ToRelative(dest);
    }

    public byte[]? ReadBytes(string? relativePath)
    {
        var full = ToFull(relativePath);
        if (full is null || !File.Exists(full)) return null;
        try { return File.ReadAllBytes(full); }
        catch { return null; }
    }

    public long Size(string? relativePath)
    {
        var full = ToFull(relativePath);
        return full is not null && File.Exists(full) ? new FileInfo(full).Length : 0;
    }

    public void Delete(string? relativePath)
    {
        var full = ToFull(relativePath);
        if (full is not null && File.Exists(full))
        {
            try { File.Delete(full); } catch { }
        }
    }

    /// <summary>
    /// تبدیل مسیر نسبی به مسیر کامل دیسک با پشتیبانی از سازگاری عقب‌رو (Fallback) برای فایل‌های قدیمی
    /// </summary>
    public string? ToFull(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var clean = relativePath.Replace('\\', '/').TrimStart('/');
        if (clean.Contains("..")) return null;

        // اگر مسیر با uploads/ شروع شده، آن را نرمال‌سازی می‌کنیم
        var uploadClean = clean.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase)
            ? clean["uploads/".Length..]
            : clean;

        // ۱) بررسی اول: وجود در uploads
        var fullUploads = Path.GetFullPath(Path.Combine(_root, uploadClean));
        var rootFull = Path.GetFullPath(_root);
        if (fullUploads.StartsWith(rootFull, StringComparison.Ordinal) && File.Exists(fullUploads))
            return fullUploads;

        // ۲) بررسی دوم: وجود مستقیم در wwwroot (مسیرهای قدیمی مانند «فایل های صادره» یا «SecureFiles»)
        var fullWebRoot = Path.GetFullPath(Path.Combine(_webRoot, clean));
        var webRootFull = Path.GetFullPath(_webRoot);
        if (fullWebRoot.StartsWith(webRootFull, StringComparison.Ordinal) && File.Exists(fullWebRoot))
            return fullWebRoot;

        // ۳) بررسی سوم: وجود مستقیم در ContentRoot
        var fullContent = Path.GetFullPath(Path.Combine(_contentRoot, clean));
        var contentFull = Path.GetFullPath(_contentRoot);
        if (fullContent.StartsWith(contentFull, StringComparison.Ordinal) && File.Exists(fullContent))
            return fullContent;

        // در صورت عدم وجود فیزیکی فعلی، مسیر استاندارد مقصد تحت uploads بازگردانده می‌شود
        return fullUploads.StartsWith(rootFull, StringComparison.Ordinal) ? fullUploads : null;
    }

    public string ToRelative(string fullPath)
    {
        return Path.GetRelativePath(_root, fullPath).Replace('\\', '/');
    }

    // ==================== روش‌های پشتیبانی و همگام‌سازی پوشه‌های قدیمی ====================

    public async Task<string> SaveWebRootAsync(string folder, int refId, Stream stream, string originalName)
    {
        // هدایت مستقیم کلیه ذخیره‌سازی‌ها به زیرساخت یکپارچه wwwroot/uploads/{folder}/{refId}
        return await SaveAsync(folder, refId, stream, originalName);
    }

    public byte[]? ReadWebRoot(string? relativePath) => ReadBytes(relativePath);
    public void DeleteWebRoot(string? relativePath) => Delete(relativePath);
    public long SizeWebRoot(string? relativePath) => Size(relativePath);
}
