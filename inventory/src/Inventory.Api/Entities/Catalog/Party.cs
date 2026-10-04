using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;

public class Party
{
    public int Id { get; set; }
    public PartyType Type { get; set; }

    [MaxLength(200)]
    public string Name { get; set; } = "";

    [MaxLength(50)]
    public string? Phone { get; set; }

    [MaxLength(50)]
    public string? Mobile { get; set; }

    [MaxLength(250)]
    public string? Address { get; set; }

    /// <summary>شناسه مالیاتی خریدار (کد ملی/شناسه اقتصادی)، برای صورتحساب نوع اول</summary>
    [MaxLength(20)]
    public string? TaxId { get; set; }

    /// <summary>کد پستی خریدار (۱۰ رقم)</summary>
    [MaxLength(20)]
    public string? PostalCode { get; set; }

    public string? Note { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>معرف پیش‌فرض مشتری (اختیاری — null = بدون معرف)</summary>
    public int? ReferrerId { get; set; }
}