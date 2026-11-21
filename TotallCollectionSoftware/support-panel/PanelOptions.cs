namespace SupportPanel;

/// <summary>
/// پیکربندی پنل پشتیبانی. موتور لایسنس و امضای سند از این پنل حذف شده است؛ پس
/// هیچ مسیر کلید، ابزار امضا یا presets این‌جا نیست. <b>هیچ رازی در این کلاس
/// ذخیره نمی‌شود</b>؛ رمز SMTP فقط با نام متغیر محیطی این‌جاست.
/// </summary>
public sealed class PanelOptions
{
    /// <summary>رشتهٔ اتصال (اولویت بر <c>ConnectionStrings:Panel</c>؛ خالی = SQLite در پوشهٔ کاری).</summary>
    public string DatabaseConnection { get; set; } = "";

    /// <summary>پوشهٔ کاری (پایگاه‌دادهٔ SQLite، فایل‌های پشتیبان). خالی = پوشهٔ موقت.</summary>
    public string WorkFolder { get; set; } = "";

    /// <summary>نام متغیر محیطی رمز SMTP (رمز هرگز در appsettings یا DB نوشته نمی‌شود).</summary>
    public string SmtpPasswordEnvironmentVariable { get; set; } = "LICENSESIGNER_SMTP_PASSWORD";

    /// <summary>
    /// حالت پیش‌نمایش/دمو: پنل پشت یک پراکسی HTTPS اجرا می‌شود و کوکی‌ها
    /// <c>SameSite=None; Secure</c> می‌شوند تا داخل iframe کار کنند.
    /// فقط برای نمایش — روی اینترنت عمومی روشن نشود.
    /// </summary>
    public bool PreviewMode { get; set; }

    public string EffectiveWorkFolder()
    {
        var folder = string.IsNullOrWhiteSpace(WorkFolder)
            ? Path.Combine(Path.GetTempPath(), "support-panel-work")
            : WorkFolder;
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// رشتهٔ اتصال به ترتیب اولویت: <c>Panel:DatabaseConnection</c> (یا همان مقدار از محیط) →
    /// <c>ConnectionStrings:Panel</c> → SQLite پیش‌فرض در پوشهٔ کاری.
    /// </summary>
    public string ResolveConnectionString(string? connectionStringsPanel = null)
    {
        if (!string.IsNullOrWhiteSpace(DatabaseConnection)) return DatabaseConnection;
        if (!string.IsNullOrWhiteSpace(connectionStringsPanel)) return connectionStringsPanel!;
        return $"Data Source={Path.Combine(EffectiveWorkFolder(), "support.db")}";
    }

    /// <summary>
    /// مسیر نسبی SQLite را مطلق (نسبت به ریشهٔ برنامه) می‌کند تا اجرا از هر پوشه‌ای
    /// همان فایل پایگاه‌داده را باز کند. رشته‌های SQL Server دست‌نخورده برمی‌گردند.
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
