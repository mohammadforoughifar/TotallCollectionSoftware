using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// Moadian-specific issuer/seller profile. Kept separate from ERP company records;
/// this CRUD-only profile does not yet participate in invoice intake or sending.
/// </summary>
public class MoadianServiceProviderProfile
{
    public int Id { get; set; }
    [MaxLength(50)] public string? Type { get; set; }
    [MaxLength(50)] public string? EconomicNumber { get; set; }
    [MaxLength(50)] public string? PersianName { get; set; }
    [MaxLength(50)] public string? EnglishName { get; set; }
    [MaxLength(50)] public string NationalID { get; set; } = "";
    [MaxLength(50)] public string? PostalCode { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>
    /// اتصال واحد این خدمات‌دهنده به سامانه مودیان (هر خدمات‌دهنده حداکثر یک رکورد اتصال دارد).
    /// </summary>
    public ICollection<MoadianProviderConnectionProfile> Connections { get; set; } = new List<MoadianProviderConnectionProfile>();
}

/// <summary>
/// Non-secret metadata for a provider's single Moadian connection. The database stores
/// only a server-side path to a protected PKCS#8 PEM file kept per provider under
/// wwwroot/uploads/moadian/{service-provider folder}; PEM bytes are never stored here.
/// </summary>
public class MoadianProviderConnectionProfile
{
    public int Id { get; set; }
    public int ServiceProviderId { get; set; }
    public MoadianServiceProviderProfile? ServiceProvider { get; set; }
    [MaxLength(2048)] public string WebServiceAddress { get; set; } = "";
    [MaxLength(50)] public string TaxMemoryID { get; set; } = "";
    [MaxLength(2048)] public string PrivateKeyPath { get; set; } = "";
    public bool IsDeleted { get; set; }
    /// <summary>نتیجهٔ آخرین تست اتصال/احراز هویت — فقط خلاصهٔ غیرحساس، بدون کلید/مسیر/توکن.</summary>
    public DateTime? LastConnectionTestAt { get; set; }
    public bool? LastConnectionTestSucceeded { get; set; }
    [MaxLength(500)] public string? LastConnectionTestMessage { get; set; }
}

/// <summary>Buyer/customer data scoped to a Moadian service provider.</summary>
public class MoadianCustomerProfile
{
    public int Id { get; set; }
    public int ServiceProviderId { get; set; }
    public MoadianServiceProviderProfile? ServiceProvider { get; set; }
    [MaxLength(50)] public string? Type { get; set; }
    [MaxLength(50)] public string? Name { get; set; }
    [MaxLength(50)] public string? NationalID { get; set; }
    [MaxLength(50)] public string? EconomicNumber { get; set; }
    [MaxLength(50)] public string? Phone { get; set; }
    [MaxLength(50)] public string? PostalCode { get; set; }
    public string? Address { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>Provider-scoped Moadian goods/service master-data record.</summary>
public class MoadianGoodsOrServiceProfile
{
    public int Id { get; set; }
    public int ServiceProviderId { get; set; }
    public MoadianServiceProviderProfile? ServiceProvider { get; set; }
    [MaxLength(50)] public string? Name { get; set; }
    [MaxLength(50)] public string? UniqueIdentifier { get; set; }
    [MaxLength(50)] public string UnitOfMeasurement { get; set; } = "";
    public byte ValueAddedPercentage { get; set; }
    public decimal Price { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>Operator-maintained unit lookup; official codes are not seeded or guessed.</summary>
public class MoadianUnitOfMeasurement
{
    public int Id { get; set; }
    [MaxLength(50)] public string Code { get; set; } = "";
    [MaxLength(50)] public string Name { get; set; } = "";
    public bool IsDeleted { get; set; }
}
