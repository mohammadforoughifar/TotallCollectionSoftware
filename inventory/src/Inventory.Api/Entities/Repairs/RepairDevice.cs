using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>مشخصات یکی از دستگاه‌های ثبت‌شده در یک پذیرش تعمیر.</summary>
public class RepairDevice
{
    public int Id { get; set; }
    public int RepairOrderId { get; set; }

    [MaxLength(100)]
    public string DeviceType { get; set; } = "";

    [MaxLength(200)]
    public string? DeviceModel { get; set; }

    [MaxLength(100)]
    public string? SerialNumber { get; set; }

    [MaxLength(1000)]
    public string? ProblemDescription { get; set; }

    [MaxLength(500)]
    public string? Accessories { get; set; }

    /// <summary>مبلغ برآوردی همین دستگاه.</summary>
    public decimal QuotedPrice { get; set; }
}
