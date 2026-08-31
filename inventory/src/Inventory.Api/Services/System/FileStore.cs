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

    public FileStore(IWebHostEnvironment env)
    {
        // همه‌ی فایل‌های آپلودی در wwwroot/uploads ذخیره می‌شوند و با سرو استاتیک wwwroot در دسترس هستند
        _root = Path.Combine(env.ContentRootPath, "wwwroot", "uploads");
        Directory.CreateDirectory(_root);
    }

    public string RootPath => _root;

    /// <summary>فایل جدید را روی دیسک می‌نویسد و مسیر نسبی برمی‌گرداند.</summary>
    public async Task<string> SaveAsync(string module, int refId, Stream stream, string originalName)
    {
        var safe = SanitizeName(originalName);
        var dir = Path.Combine(_root, SafeModule(module), refId.ToString());
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{Guid.NewGuid():N}_{safe}");
        await using (var fs = File.Create(file))
        {
            await stream.CopyToAsync(fs);
        }
        return ToRelative(file);
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
        // فقط مسیر نسبی امن داخل uploads بپذیر (ضد path traversal)
        var clean = relativePath.Replace('\\', '/').TrimStart('/');
        if (clean.Contains("..")) return null;
        var full = Path.GetFullPath(Path.Combine(_root, clean));
        if (!full.StartsWith(Path.GetFullPath(_root), StringComparison.Ordinal)) return null;
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
