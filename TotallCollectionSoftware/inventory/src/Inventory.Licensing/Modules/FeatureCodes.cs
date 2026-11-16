namespace Inventory.Licensing.Modules;

/// <summary>یک «قابلیت» داخل یک ماژول — ریزتر از ماژول، درشت‌تر از مجوز RBAC.</summary>
/// <param name="Code">کد پایدار در فایل لایسنس (مثل <c>letter.incoming</c>).</param>
/// <param name="Module">ماژول والد (از <see cref="ModuleCodes"/>).</param>
/// <param name="Title">نام فارسی برای UI.</param>
public sealed record FeatureDefinition(string Code, string Module, string Title);

/// <summary>
/// کاتالوگ ثابت «قابلیت‌های داخلی ماژول» — منبع واحد حقیقت.
///
/// <para>
/// چرا لازم شد؟ بعضی ماژول‌ها یک بستهٔ کلی نیستند؛ مشتری ممکن است فقط بخشی از آن را بخرد.
/// مثال روشن: <b>اتوماسیون اداری</b> (<see cref="ModuleCodes.Letters"/>) که سه نوع نامه دارد:
/// وارده، صادره و داخلی. تا امروز لایسنس فقط می‌گفت «اتوماسیون اداری: دارد/ندارد» و تفکیک
/// نامهٔ وارده در سند وجود نداشت.
/// </para>
///
/// <para>
/// ⚠️ قاعده‌های سخت:
/// </para>
/// <list type="bullet">
/// <item>قابلیت فقط وقتی معنا دارد که <b>ماژول والدش</b> در همان آیتم لایسنس باشد (اعتبارسنجی می‌کند).</item>
/// <item>این فهرست در کد است، نه appsettings — وگرنه هر کسی می‌توانست قابلیت دلخواه اضافه کند.</item>
/// <item>قابلیت، «مجوز کاربر» نیست: لایسنس می‌گوید «خریداری شده»، RBAC می‌گوید «این کاربر اجازه دارد».</item>
/// </list>
/// </summary>
public static class FeatureCodes
{
    // ---------- اتوماسیون اداری ----------
    public const string LetterIncoming = "letter.incoming";
    public const string LetterOutgoing = "letter.outgoing";
    public const string LetterInner = "letter.inner";

    /// <summary>کاتالوگ کامل — ترتیب نمایش در UI هم همین است.</summary>
    public static readonly IReadOnlyList<FeatureDefinition> All = new List<FeatureDefinition>
    {
        new(LetterIncoming, ModuleCodes.Letters, "نامهٔ وارده"),
        new(LetterOutgoing, ModuleCodes.Letters, "نامهٔ صادره"),
        new(LetterInner,    ModuleCodes.Letters, "نامهٔ داخلی")
    };

    public static bool IsKnown(string? code)
        => !string.IsNullOrWhiteSpace(code) && All.Any(f => string.Equals(f.Code, code, StringComparison.OrdinalIgnoreCase));

    public static FeatureDefinition? Find(string? code)
        => string.IsNullOrWhiteSpace(code)
            ? null
            : All.FirstOrDefault(f => string.Equals(f.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>ماژول والد یک قابلیت؛ برای کد ناشناس رشتهٔ خالی.</summary>
    public static string ModuleOf(string? code) => Normalize(Find(code)?.Module);

    public static string TitleOf(string? code)
        => Find(code)?.Title ?? (string.IsNullOrWhiteSpace(code) ? "نامشخص" : code);

    /// <summary>قابلیت‌های یک ماژول (برای نمایش زیر آن در UI).</summary>
    public static IReadOnlyList<FeatureDefinition> ForModule(string? module)
    {
        var code = ModuleCodes.Normalize(module);
        if (code.Length == 0) return Array.Empty<FeatureDefinition>();
        return All.Where(f => string.Equals(f.Module, code, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public static string Normalize(string? code) => (code ?? "").Trim().ToLowerInvariant();
}
