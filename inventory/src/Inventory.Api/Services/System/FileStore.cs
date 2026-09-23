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

    private static string StorageModule(string module)
    {
        var key = (module ?? "").Trim();
        return key switch
        {
            "InnerLetters" => "office/innerletter",
            "IncomingLetters" => "office/incomingletter",
            "OutgoingLetters" => "office/outgoingletter",
            "OtoEmails" => "office/email",
            "Email" => "office/email",
            _ => key
        };
    }

    private string BuildDirectory(string module, int refId, string? subFolder = null)
    {
        var parts = new List<string> { _root, SafeModule(StorageModule(module)) };
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
        var dir = Path.Combine(_root, SafeModule(StorageModule(module)), ownerId?.ToString() ?? "common");
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
    /// تبدیل مسیر نسبی به مسیر کامل دیسک با پشتیبانی از سازگاری عقب‌رو (Fallback) برای فایل‌های قدیمی و نام‌های مستعار
    /// </summary>
    public string? ToFull(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var clean = relativePath.Replace('\\', '/').TrimStart('/');
        if (clean.Contains("..")) return null;

        // نرمال‌سازی prefix uploads/
        var uploadClean = clean.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase)
            ? clean["uploads/".Length..]
            : clean;

        // لیست جفت‌مسیرهای معادل جهت پشتیبانی کامل از فایل‌های قبلی دیتابیس
        var aliases = new List<string> { uploadClean, clean };
        // مسیرهای قدیمی ماژول‌های اتوماسیون را با مسیر استاندارد office تطبیق می‌دهیم.
        var officeAliases = new[]
        {
            (Old: "InnerLetters/", New: "office/innerletter/"),
            (Old: "IncomingLetters/", New: "office/incomingletter/"),
            (Old: "OutgoingLetters/", New: "office/outgoingletter/"),
            (Old: "OtoEmails/", New: "office/email/")
        };
        foreach (var a in officeAliases)
        {
            if (uploadClean.StartsWith(a.Old, StringComparison.OrdinalIgnoreCase)) aliases.Add(a.New + uploadClean[a.Old.Length..]);
            if (uploadClean.StartsWith(a.New, StringComparison.OrdinalIgnoreCase)) aliases.Add(a.Old + uploadClean[a.New.Length..]);
        }
        if (uploadClean.StartsWith("فایل های صادره/", StringComparison.OrdinalIgnoreCase))
            aliases.Add("office/outgoingletter/" + uploadClean["فایل های صادره/".Length..]);
        else if (uploadClean.StartsWith("office/outgoingletter/", StringComparison.OrdinalIgnoreCase))
            aliases.Add("فایل های صادره/" + uploadClean["office/outgoingletter/".Length..]);

        if (uploadClean.StartsWith("فایل های ایمیل/", StringComparison.OrdinalIgnoreCase))
            aliases.Add("office/email/" + uploadClean["فایل های ایمیل/".Length..]);
        else if (uploadClean.StartsWith("office/email/", StringComparison.OrdinalIgnoreCase))
            aliases.Add("فایل های ایمیل/" + uploadClean["office/email/".Length..]);

        if (uploadClean.StartsWith("innerletter/", StringComparison.OrdinalIgnoreCase))
            aliases.Add("office/innerletter/" + uploadClean["innerletter/".Length..]);
        else if (uploadClean.StartsWith("office/innerletter/", StringComparison.OrdinalIgnoreCase))
            aliases.Add("innerletter/" + uploadClean["office/innerletter/".Length..]);

        if (uploadClean.StartsWith("work-orders/", StringComparison.OrdinalIgnoreCase))
            aliases.Add("itassets/workorders/" + uploadClean["work-orders/".Length..]);
        else if (uploadClean.StartsWith("itassets/workorders/", StringComparison.OrdinalIgnoreCase))
            aliases.Add("work-orders/" + uploadClean["itassets/workorders/".Length..]);

        if (uploadClean.StartsWith("it-requests/", StringComparison.OrdinalIgnoreCase))
            aliases.Add("itassets/requests/" + uploadClean["it-requests/".Length..]);
        else if (uploadClean.StartsWith("itassets/requests/", StringComparison.OrdinalIgnoreCase))
            aliases.Add("it-requests/" + uploadClean["itassets/requests/".Length..]);

        if (uploadClean.StartsWith("SecureFiles/", StringComparison.OrdinalIgnoreCase))
            aliases.Add("projects/" + uploadClean["SecureFiles/".Length..]);
        else if (uploadClean.StartsWith("projects/", StringComparison.OrdinalIgnoreCase))
            aliases.Add("SecureFiles/" + uploadClean["projects/".Length..]);

        var rootFull = Path.GetFullPath(_root);
        var webRootFull = Path.GetFullPath(_webRoot);
        var contentFull = Path.GetFullPath(_contentRoot);

        foreach (var candidate in aliases.Distinct())
        {
            var p1 = Path.GetFullPath(Path.Combine(_root, candidate));
            if (p1.StartsWith(rootFull, StringComparison.Ordinal) && File.Exists(p1)) return p1;

            var p2 = Path.GetFullPath(Path.Combine(_webRoot, candidate));
            if (p2.StartsWith(webRootFull, StringComparison.Ordinal) && File.Exists(p2)) return p2;

            var p3 = Path.GetFullPath(Path.Combine(_contentRoot, candidate));
            if (p3.StartsWith(contentFull, StringComparison.Ordinal) && File.Exists(p3)) return p3;
        }

        // در صورت نبود فیزیکی، مسیر پیش‌فرض در uploads بازگردانده می‌شود
        var defaultPath = Path.GetFullPath(Path.Combine(_root, uploadClean));
        return defaultPath.StartsWith(rootFull, StringComparison.Ordinal) ? defaultPath : null;
    }

    public string ToRelative(string fullPath)
    {
        return Path.GetRelativePath(_root, fullPath).Replace('\\', '/');
    }

    // ==================== روش‌های پشتیبانی و همگام‌سازی پوشه‌های قدیمی ====================

    public async Task<string> SaveWebRootAsync(string folder, int refId, Stream stream, string originalName)
    {
        return await SaveAsync(folder, refId, stream, originalName);
    }

    public byte[]? ReadWebRoot(string? relativePath) => ReadBytes(relativePath);
    public void DeleteWebRoot(string? relativePath) => Delete(relativePath);
    public long SizeWebRoot(string? relativePath) => Size(relativePath);
}
