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

    public string EffectiveWorkFolder()
    {
        var folder = string.IsNullOrWhiteSpace(WorkFolder)
            ? Path.Combine(Path.GetTempPath(), "signer-work")
            : WorkFolder;
        Directory.CreateDirectory(folder);
        return folder;
    }

    public int EffectiveTimeoutSeconds() => TimeoutSeconds is >= 5 and <= 600 ? TimeoutSeconds : 60;

    /// <summary>مسیر پایگاه‌داده: اگر رشتهٔ اتصال داده نشود، SQLite در پوشهٔ کاری.</summary>
    public string ResolveConnectionString()
    {
        if (!string.IsNullOrWhiteSpace(DatabaseConnection)) return DatabaseConnection;
        var path = Path.Combine(EffectiveWorkFolder(), "support.db");
        return $"Data Source={path}";
    }
}
