namespace Inventory.Shared.Dtos;

/// <summary>CPInfo: identity and contact fields for a Moadian service provider.</summary>
public class MoadianServiceProviderProfileDto
{
    public int Id { get; set; }
    public string? Type { get; set; }
    public string? EconomicNumber { get; set; }
    public string? PersianName { get; set; }
    public string? EnglishName { get; set; }
    public string NationalId { get; set; } = "";
    public string? PostalCode { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>ConnectInfo: the non-secret connection record of a service provider (one per provider).</summary>
public class MoadianProviderConnectionProfileDto
{
    public int Id { get; set; }
    public int ServiceProviderId { get; set; }
    public string? ProviderName { get; set; }
    public string WebServiceAddress { get; set; } = "";
    public string TaxMemoryId { get; set; } = "";
    /// <summary>Indicates whether a private PEM path is registered server-side; the path and bytes are never returned.</summary>
    public bool HasPrivateKey { get; set; }
    public bool IsDeleted { get; set; }
    /// <summary>زمان آخرین تست اتصال (فقط اطلاعات — نتیجهٔ کامل در API برگردانده می‌شود).</summary>
    public DateTime? LastTestAt { get; set; }
    public bool? LastTestSucceeded { get; set; }
    /// <summary>خلاصهٔ غیرحساس نتیجهٔ آخرین تست (بدون کلید، مسیر کلید و توکن).</summary>
    public string? LastTestMessage { get; set; }
}

/// <summary>Non-secret status returned after a private-key file operation.</summary>
public sealed class MoadianPrivateKeyStatusDto
{
    public bool HasPrivateKey { get; set; }
}

/// <summary>یک گام از تست اتصال (برقراری ارتباط / احراز هویت / بررسی حافظهٔ مالیاتی).</summary>
public sealed class MoadianConnectionTestStepDto
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Ok { get; set; }
    public int? HttpStatus { get; set; }
    /// <summary>پیام غیرحساس؛ هرگز شامل کلید خصوصی، مسیر کلید یا توکن نیست.</summary>
    public string? Detail { get; set; }
    public long DurationMs { get; set; }
}

/// <summary>نتیجهٔ تست اتصال و احراز هویت یک نسخهٔ ConnectInfo — فقط داده‌های غیرحساس.</summary>
public sealed class MoadianConnectionTestResultDto
{
    public bool Success { get; set; }
    /// <summary>جمع‌بندی وضعیت به فارسی برای نمایش مستقیم به کاربر.</summary>
    public string Message { get; set; } = "";
    public string? BaseUrl { get; set; }
    public string? ApiVersion { get; set; }
    public string? ClientType { get; set; }
    /// <summary>شناسه‌ای که با آن احراز هویت انجام شد (شناسه ملی یا شماره اقتصادی خدمات‌دهنده).</summary>
    public string? ClientIdUsed { get; set; }
    public string? IdentityField { get; set; }
    public string? TaxMemoryId { get; set; }
    public string? TaxpayerName { get; set; }
    public string? FiscalStatus { get; set; }
    public string? PublicKeyId { get; set; }
    public List<MoadianConnectionTestStepDto> Steps { get; set; } = new();
    /// <summary>آدرس‌های فراخوانی‌شده (برای عیب‌یابی مسیر سرویس) — بدون هیچ دادهٔ حساس.</summary>
    public List<string> Endpoints { get; set; } = new();
    public long DurationMs { get; set; }
}

/// <summary>Customer/buyer record scoped to a Moadian service provider.</summary>
public class MoadianCustomerProfileDto
{
    public int Id { get; set; }
    public int ServiceProviderId { get; set; }
    public string? ProviderName { get; set; }
    public string? Type { get; set; }
    public string? Name { get; set; }
    public string? NationalId { get; set; }
    public string? EconomicNumber { get; set; }
    public string? Phone { get; set; }
    public string? PostalCode { get; set; }
    public string? Address { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>Goods/service record scoped to a Moadian service provider.</summary>
public class MoadianGoodsOrServiceProfileDto
{
    public int Id { get; set; }
    public int ServiceProviderId { get; set; }
    public string? ProviderName { get; set; }
    public string? Name { get; set; }
    public string? UniqueIdentifier { get; set; }
    public string UnitOfMeasurement { get; set; } = "";
    public string? UnitName { get; set; }
    public int ValueAddedPercentage { get; set; }
    public decimal Price { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>Operator-maintained unit of measurement.</summary>
public class MoadianUnitOfMeasurementDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsDeleted { get; set; }
}
