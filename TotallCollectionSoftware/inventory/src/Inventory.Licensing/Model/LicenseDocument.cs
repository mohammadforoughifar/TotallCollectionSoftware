using System.Text.Json.Serialization;

namespace Inventory.Licensing.Model;

/// <summary>
/// سند لایسنس — نسخهٔ ۳ (version = 3).
///
/// ساختار سه‌سطحی: <b>مشتری ← لایسنس ← آیتم‌ها</b>.
/// یک مشتری می‌تواند چند سند داشته باشد (پایه + Add-on) و هر سند چند آیتم.
/// یک ماژول هم می‌تواند توسط چند آیتم پوشش داده شود؛ نتیجهٔ مؤثر هرگز از یک سند
/// تنها گرفته نمی‌شود، بلکه از ادغام همهٔ اسناد معتبر حاصل می‌شود
/// (نگاه کنید به <see cref="Entitlement.EntitlementMerger"/>).
///
/// <para>
/// ⚠️ این کلاس تنها «شکل داده» است. هیچ اعتبار ایمنی در آن نیست؛ هر سندی که از مسیر
/// فایل می‌آید باید ابتدا امضایش بررسی شود (Crypto) و بعد ساختارش اعتبارسنجی شود
/// (Validation). اعتماد به دادهٔ خام این کلاس = دور زدن لایسنس.
/// </para>
/// </summary>
public sealed class LicenseDocument
{
    /// <summary>نسخهٔ قالب سند. همیشه ۳ است؛ هر مقدار دیگری نامعتبر است.</summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = LicenseFormat.CurrentVersion;

    /// <summary>شناسهٔ کلید عمومی که امضای این سند با آن بررسی می‌شود.</summary>
    [JsonPropertyName("keyId")]
    public string KeyId { get; set; } = "";

    /// <summary>
    /// شمارهٔ سریال یکتا — کلید «هر فایل فقط یک‌بار اعمال شود».
    /// دو بار آپلود یک فایل، دو لایسنس نمی‌سازد.
    /// </summary>
    [JsonPropertyName("serial")]
    public string Serial { get; set; } = "";

    /// <summary>شناسهٔ مشتری (کد قراردادی). همهٔ اسناد یک نصب باید همین را داشته باشند.</summary>
    [JsonPropertyName("customerId")]
    public string CustomerId { get; set; } = "";

    /// <summary>نام مشتری برای نمایش در صفحهٔ وضعیت لایسنس.</summary>
    [JsonPropertyName("customerName")]
    public string? CustomerName { get; set; }

    /// <summary>نوع استقرار. در OnPrem باید با InstallationId همراه باشد.</summary>
    [JsonPropertyName("deployment")]
    public DeploymentKind Deployment { get; set; } = DeploymentKind.OnPrem;

    /// <summary>شناسهٔ نصب — فقط برای OnPrem. در SaaS خالی است.</summary>
    [JsonPropertyName("installationId")]
    public string? InstallationId { get; set; }

    /// <summary>زمان صدور سند (UTC).</summary>
    [JsonPropertyName("issuedAt")]
    public DateTime IssuedAt { get; set; }

    /// <summary>یادداشت آزاد صادرکننده (در امضا محافظت می‌شود، ولی منطقی نیست).</summary>
    [JsonPropertyName("note")]
    public string? Note { get; set; }

    /// <summary>آیتم‌های لایسنس — شرط می‌شود حداقل یکی باشد.</summary>
    [JsonPropertyName("items")]
    public List<LicenseItem> Items { get; set; } = new();
}

/// <summary>
/// یک آیتم لایسنس — کوچک‌ترین واحد فروش.
/// مثال: «انبارداری دائمی، ۵ کاربر نام‌دار، بدون سهمیه» یا
/// «نامه اداری اشتراکی یک‌ساله با سقف ۲۰۰۰ نامه در ماه».
/// </summary>
public sealed class LicenseItem
{
    /// <summary>شناسهٔ یکتا در کل سندهای یک مشتری (برای ردیابی در گزارش‌ها).</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>لیست کد ماژول‌ها (از <see cref="Modules.ModuleCodes"/>).</summary>
    [JsonPropertyName("scope")]
    public List<string> Scope { get; set; } = new();

    /// <summary>مدل زمانی.</summary>
    [JsonPropertyName("term")]
    public LicenseTerm Term { get; set; } = new();

    /// <summary>سیاست کاربر.</summary>
    [JsonPropertyName("users")]
    public LicenseUsers Users { get; set; } = new();

    /// <summary>سهمیه‌ها — خالی یعنی این آیتم سهمیه‌ای تعریف نمی‌کند.</summary>
    [JsonPropertyName("quotas")]
    public List<LicenseQuota> Quotas { get; set; } = new();

    /// <summary>
    /// فقط برای آیتمی که <b>جایگزین یک Trial</b> می‌شود:
    /// اگر true باشد، مصرف دورهٔ آزمایشی از سقف آیتم جدید کسر می‌شود (مشتری که در
    /// دورهٔ آزمایشی ۵۰ فاکتور زده، از سقف ۱۰۰۰تایی فقط ۹۵۰تا دارد)؛
    /// اگر false باشد مصرف Trial صفر حساب می‌شود.
    /// </summary>
    [JsonPropertyName("carryOverUsage")]
    public bool CarryOverUsage { get; set; }

    /// <summary>یادداشت آزاد صادرکننده.</summary>
    [JsonPropertyName("note")]
    public string? Note { get; set; }

    /// <summary>شناسهٔ سند والد — پس از بارگذاری پر می‌شود (در امضا نوشته نمی‌شود).</summary>
    [JsonIgnore]
    public string Serial { get; set; } = "";
}

/// <summary>مدل زمانی یک آیتم.</summary>
public sealed class LicenseTerm
{
    [JsonPropertyName("type")]
    public LicenseTermType Type { get; set; } = LicenseTermType.Subscription;

    /// <summary>شروع اعتبار — برای Subscription و Trial اجباری.</summary>
    [JsonPropertyName("validFrom")]
    public DateTime? ValidFrom { get; set; }

    /// <summary>
    /// پایان اعتبار (روز کامل — تا ۲۳:۵۹:۵۹ همان روز معتبر است).
    /// برای Subscription و Trial اجباری؛ برای Perpetual باید خالی باشد.
    /// </summary>
    [JsonPropertyName("validUntil")]
    public DateTime? ValidUntil { get; set; }

    /// <summary>
    /// آخرین نسخه‌ای که مشتری حق دریافتش را دارد — فقط برای Perpetual.
    /// مبنای مقایسهٔ تاریخ Build در آپدیت است (مرحله ۶).
    /// </summary>
    [JsonPropertyName("updatesUntil")]
    public DateTime? UpdatesUntil { get; set; }
}

/// <summary>سیاست کاربر یک آیتم.</summary>
public sealed class LicenseUsers
{
    [JsonPropertyName("mode")]
    public LicenseUserMode Mode { get; set; } = LicenseUserMode.None;

    /// <summary>حداکثر تعداد کاربر — وقتی Mode != None باید بزرگ‌تر از صفر باشد.</summary>
    [JsonPropertyName("max")]
    public int Max { get; set; }

    [JsonPropertyName("appliesTo")]
    public LicenseUserScope AppliesTo { get; set; } = LicenseUserScope.Global;

    /// <summary>
    /// حداکثر تعداد حساب‌های <b>معاف از صندلی</b>.
    /// معافیت به نقش Admin داده نمی‌شود؛ مدیر سیستم حداکثر همین تعداد حساب مشخص را
    /// از داخل برنامه (مرحله ۳) معاف می‌کند. مقدار از سند امضاشده می‌آید تا از
    /// appsettings قابل افزایش نباشد.
    /// </summary>
    [JsonPropertyName("exempt")]
    public int Exempt { get; set; }
}

/// <summary>یک سهمیه روی یک متریک.</summary>
public sealed class LicenseQuota
{
    /// <summary>کد متریک (از <see cref="Metrics.MetricCodes"/>).</summary>
    [JsonPropertyName("metric")]
    public string Metric { get; set; } = "";

    /// <summary>سقف مصرف در هر دوره.</summary>
    [JsonPropertyName("limit")]
    public long Limit { get; set; }

    [JsonPropertyName("period")]
    public QuotaPeriod Period { get; set; } = QuotaPeriod.Lifetime;

    /// <summary>
    /// اگر true باشد، این سهمیه به سقف آیتم‌های دیگر <b>اضافه</b> می‌شود
    /// (بستهٔ افزایشی). هر packId فقط یک‌بار اعمال می‌شود.
    /// </summary>
    [JsonPropertyName("stackable")]
    public bool Stackable { get; set; }

    /// <summary>شناسهٔ بستهٔ افزایشی — وقتی Stackable=true اجباری است.</summary>
    [JsonPropertyName("packId")]
    public string? PackId { get; set; }
}

/// <summary>پوشش سند امضاشده (payload + signature).</summary>
public sealed class SignedLicenseDocument
{
    [JsonPropertyName("version")]
    public int EnvelopeVersion { get; set; } = 1;

    [JsonPropertyName("keyId")]
    public string KeyId { get; set; } = "";

    /// <summary>الگوریتم امضا: RS256 (RSA-2048/PKCS#1-v1.5) یا ES256 (ECDSA P-256).</summary>
    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = "RS256";

    /// <summary>Base64 از بایت‌های UTF-8 سند JSON. امضا روی همین بایت‌ها زده می‌شود.</summary>
    [JsonPropertyName("payload")]
    public string Payload { get; set; } = "";

    /// <summary>Base64 از امضای بایت‌های payload.</summary>
    [JsonPropertyName("signature")]
    public string Signature { get; set; } = "";
}

/// <summary>ثابت‌های قالب — منبع واحد نسخه.</summary>
public static class LicenseFormat
{
    /// <summary>نسخهٔ جاری قالب سند.</summary>
    public const int CurrentVersion = 3;

    /// <summary>نسخهٔ پوشش امضا.</summary>
    public const int CurrentEnvelopeVersion = 1;

    public const string AlgorithmRsaSha256 = "RS256";
    public const string AlgorithmEcdsaP256Sha256 = "ES256";
}
