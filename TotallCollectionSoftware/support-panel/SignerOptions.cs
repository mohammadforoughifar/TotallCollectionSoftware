namespace LicenseSigner;

/// <summary>
/// همهٔ تنظیم‌ها از پیکربندی/متغیر محیطی می‌آیند. <b>هیچ رازی در این کلاس ذخیره نمی‌شود</b>؛
/// گذرواژهٔ کلید و رمز SMTP فقط با نام متغیر محیطی این‌جا هستند.
/// </summary>
public sealed class SignerOptions
{
    public string ToolPath { get; set; } = "";
    public string DotnetPath { get; set; } = "dotnet";
    public string PrivateKeyPath { get; set; } = "";
    public string PublicKeyPath { get; set; } = "";
    public string KeyId { get; set; } = "";
    public string WorkFolder { get; set; } = "";
    public string PresetsPath { get; set; } = "";
    public string DatabaseConnection { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 60;
    public string KeyPasswordEnvironmentVariable { get; set; } = "LICENSETOOL_KEY_PASSWORD";
    public string SmtpPasswordEnvironmentVariable { get; set; } = "LICENSESIGNER_SMTP_PASSWORD";
    public string Token { get; set; } = "";

    /// <summary>
    /// حالت پیش‌نمایش/دمو: پنل پشت یک پراکسی HTTPS (مثل محیط پیش‌نمایش) اجرا می‌شود.
    /// در این حالت کوکی‌ها <c>SameSite=None; Secure</c> می‌شوند تا داخل iframe کار کنند.
    /// این حالت <b>فقط برای نمایش</b> است و نباید روی اینترنت عمومی روشن بماند.
    /// </summary>
    public bool PreviewMode { get; set; }

    public string EffectiveWorkFolder()
    {
        var folder = string.IsNullOrWhiteSpace(WorkFolder)
            ? Path.Combine(Path.GetTempPath(), "signer-work")
            : WorkFolder;
        Directory.CreateDirectory(folder);
        return folder;
    }

    public int EffectiveTimeoutSeconds() => TimeoutSeconds is >= 5 and <= 600 ? TimeoutSeconds : 60;

    /// <summary>
    /// رشتهٔ اتصال به ترتیب اولویت: <c>Signer:DatabaseConnection</c> (env) →
    /// <c>ConnectionStrings:Panel</c> از appsettings → SQLite پیش‌فرض در پوشهٔ کاری.
    /// </summary>
    public string ResolveConnectionString(string? connectionStringsPanel = null)
    {
        if (!string.IsNullOrWhiteSpace(DatabaseConnection)) return DatabaseConnection;
        if (!string.IsNullOrWhiteSpace(connectionStringsPanel)) return connectionStringsPanel!;
        var path = Path.Combine(EffectiveWorkFolder(), "support.db");
        return $"Data Source={path}";
    }

    /// <summary>
    /// مسیر نسبی SQLite را به مسیر مطلق نسبت به ریشهٔ برنامه تبدیل می‌کند تا اجرای
    /// برنامه از هر پوشه‌ای همان فایل پایگاه‌داده را باز کند. رشته‌های غیر SQLite
    /// (مثل SQL Server) دست‌نخورده برمی‌گردند.
    /// </summary>
    public static string NormalizeSqlitePath(string connection, string baseDirectory)
    {
        if (connection.Contains("Server=", StringComparison.OrdinalIgnoreCase)) return connection;

        var parts = connection.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i].Trim();
            if (!part.StartsWith("Data Source", StringComparison.OrdinalIgnoreCase)) continue;
            var eq = part.IndexOf('=');
            if (eq < 0) continue;

            var value = part[(eq + 1)..].Trim();
            if (value.Length == 0 || value == ":memory:" || Path.IsPathRooted(value)) return connection;

            parts[i] = part[..(eq + 1)] + Path.GetFullPath(Path.Combine(baseDirectory, value));
            return string.Join(';', parts);
        }

        return connection;
    }
}
