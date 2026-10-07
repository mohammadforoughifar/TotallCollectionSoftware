namespace Inventory.Api.Services.Invoicing;

/// <summary>
/// Moadian server configuration captured when the ERP process starts. Changes to
/// appsettings therefore require a restart; environment variables keep their normal
/// .NET configuration priority at startup.
/// </summary>
/// <param name="BaseUrl">Moadian:BaseUrl — آدرس پایهٔ سامانه (فقط وقتی WebServiceAddress خالی باشد استفاده می‌شود).</param>
/// <param name="ApiVersion">Moadian:ApiVersion — فقط مقدار خالی (سبک v2) یا v1 توسط SDK 0.0.34 پشتیبانی می‌شود.</param>
/// <param name="PrivateKeyRoot">Files:MoadianPrivateKeyRoot — ریشهٔ ذخیره‌سازی کلیدهای خصوصی (پیش‌فرض: wwwroot/uploads/moadian).</param>
/// <param name="ClientType">Moadian:ClientType — TSP یا SELF_TSP؛ مسیر سرویس (tsp یا self-tsp) را تعیین می‌کند.</param>
/// <param name="SignatureKeyId">Moadian:SignatureKeyId — شناسهٔ کلید امضا در کارپوشه (اختیاری).</param>
public sealed record MoadianRuntimeSettings(
    string? BaseUrl,
    string? ApiVersion,
    string? PrivateKeyRoot,
    string? ClientType = null,
    string? SignatureKeyId = null);
