using Microsoft.AspNetCore.Http;

namespace Inventory.Api.Services;

/// <summary>
/// ذخیره‌سازی فایل‌ها روی دیسک — همه‌ی فایل‌ها در پوشه‌ی uploads/ روت API قرار می‌گیرند
/// و در دیتابیس فقط مسیر نسبی (اطلاعات) ثبت می‌شود.
/// ساختار: uploads/{module}/{refId}/{guid}_{نام-اصلی}
/// </summary>
public class FileStore
{
    private readonly string _root;
    private readonly string _webRoot;
    private readonly string _contentRoot;

    public FileStore(IWebHostEnvironment env)
    {
        // همه‌ی فایل‌های آپلودی در wwwroot/uploads ذخیره می‌شوند و با سرو استاتیک wwwroot در دسترس هستند
        _contentRoot = env.ContentRootPath;
        _root = Path.Combine(env.ContentRootPath, "wwwroot", "uploads");
        _webRoot = Path.Combine(env.ContentRootPath, "wwwroot");
        Directory.CreateDirectory(_root);
    }

    public string RootPath => _root;

    // ==================== پوشه‌های اختصاصی مستقیم زیر wwwroot ====================
    // مثال درخواست کارفرما: «wwwroot/فایل های صادره» برای پیوست نامه صادره و
    // «wwwroot/فایل های ایمیل» برای پیوست‌های ایمیل سازمانی.
    // دانلود این فایل‌ها فقط از مسیر API با احراز هویت انجام می‌شود (Program.cs سرو استاتیک
    // این پوشه‌ها را می‌بندد) و FilePath در دیتابیس نسبیِ از جذر wwwroot ذخیره می‌شود.

    /// <summary>ذخیره فایل در پوشه‌ای مستقیم زیر wwwroot — مثل («فایل های صادره», letterId)</summary>
    public async Task<string> SaveWebRootAsync(string folder, int refId, Stream stream, string originalName)
    {
        var dir = WebRootDir(folder, refId);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{Guid.NewGuid():N}_{SanitizeName(originalName)}");
        await using (var fs = File.Create(file))
        {
            await stream.CopyToAsync(fs);
        }
        return ToWebRootRelative(file);
    }

    /// <summary>خواندن فایل از پوشه اختصاصی wwwroot — مسیر نسبی از جذر wwwroot. در صورت نبود null.</summary>
    public byte[]? ReadWebRoot(string? relativePath)
    {
        var full = ToWebRootFull(relativePath);
        if (full is null || !File.Exists(full)) return null;
        try { return File.ReadAllBytes(full); }
        catch { return null; }
    }

    /// <summary>حذف فایل از پوشه اختصاصی wwwroot (در صورت وجود)</summary>
    public void DeleteWebRoot(string? relativePath)
    {
        var full = ToWebRootFull(relativePath);
        if (full is not null && File.Exists(full))
        {
            try { File.Delete(full); } catch { }
        }
    }

    /// <summary>حجم فایل اختصاصی wwwroot</summary>
    public long SizeWebRoot(string? relativePath)
    {
        var full = ToWebRootFull(relativePath);
        return full is not null && File.Exists(full) ? new FileInfo(full).Length : 0;
    }

    private string WebRootDir(string folder, int refId)
    {
        var clean = (folder ?? "").Replace('\\', '/').Trim('/').Replace("..", "");
        if (string.IsNullOrWhiteSpace(clean)) clean = "misc";
        return Path.Combine(_webRoot, clean, refId.ToString());
    }

    private string? ToWebRootFull(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var clean = relativePath.Replace('\\', '/').TrimStart('/');
        if (clean.Contains("..")) return null;
        var full = Path.GetFullPath(Path.Combine(_webRoot, clean));
        if (!full.StartsWith(Path.GetFullPath(_webRoot), StringComparison.Ordinal)) return null;
        return full;
    }

    private string ToWebRootRelative(string fullPath) =>
        Path.GetRelativePath(_webRoot, fullPath).Replace('\\', '/');

    /// <summary>
    /// فایل جدید را روی دیسک می‌نویسد و مسیر نسبی برمی‌گرداند.
    /// subFolder برای ساخت مسیرهای واضح مثل innerletter/pishnevis/{refId} استفاده می‌شود.
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
    /// فایل موجود (مثلاً پیوست پیش‌نویس) را به پوشه جدید منتقل می‌کند و مسیر نسبی جدید برمی‌گرداند.
    /// اگر فایل موجود نباشد یا انتقال ناموفق باشد null برمی‌گرداند.
    /// </summary>
    public string? Move(string? relativePath, string module, int refId, string? subFolder = null)
    {
        var full = ToFull(relativePath);
        if (full is null || !File.Exists(full)) return null;

        var dir = BuildDirectory(module, refId, subFolder);
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, Path.GetFileName(full));

        // اگر فایل از قبل در مقصد است، کاری لازم نیست
        if (Path.GetFullPath(dest) == Path.GetFullPath(full)) return relativePath;

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

    /// <summary>ساخت مسیر کامل در پوشه uploads: uploads/{module}[/{subFolder}]/{refId}</summary>
    private string BuildDirectory(string module, int refId, string? subFolder = null)
    {
        var parts = new List<string> { SafeModule(module) };
        if (!string.IsNullOrWhiteSpace(subFolder))
            parts.Add(SafeModule(subFolder));
        parts.Add(refId.ToString());
        return Path.Combine(parts.ToArray());
    }

    /// <summary>خواندن فایل از دیسک. اگر موجود نباشد، null (صاحب‌کار به blob قدیمی در DB برمی‌گردد).</summary>
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

    /// <summary>فایل آپلودی را با نام امن روی دیسک ذخیره می‌کند (برای عکس‌ها و مشابه).</summary>
    public async Task<string> SaveRawAsync(string module, byte[] data, string ext, int? ownerId = null)
    {
        var dir = Path.Combine(_root, SafeModule(module), ownerId?.ToString() ?? "common");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{Guid.NewGuid():N}{(ext.StartsWith('.') ? ext : "." + ext)}");
        await File.WriteAllBytesAsync(file, data);
        return ToRelative(file);
    }

    private string? ToFull(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var clean = relativePath.Replace('\\', '/').TrimStart('/');
        var full = Path.GetFullPath(Path.Combine(_root, clean));
        // مسیرهای عادی باید داخل uploads باشند (ضد path traversal)؛
        // مسیرهای قدیمیِ «../../Module/...» هم فقط اگر داخل ContentRoot باشند پذیرفته می‌شوند.
        var insideUploads = full.StartsWith(Path.GetFullPath(_root), StringComparison.Ordinal);
        var insideContent = full.StartsWith(Path.GetFullPath(_contentRoot), StringComparison.Ordinal);
        if (!insideUploads && !insideContent) return null;
        return full;
    }

    private string ToRelative(string fullPath) =>
        Path.GetRelativePath(_root, fullPath).Replace('\\', '/');

    private static string SafeModule(string m)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in m ?? "")
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_') sb.Append(ch);
        return sb.Length > 0 ? sb.ToString() : "misc";
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
}
