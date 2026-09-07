using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;

public class Warehouse
{
    public int Id { get; set; }

    [MaxLength(150)]
    public string Name { get; set; } = "";

    [MaxLength(250)]
    public string? Address { get; set; }

    [MaxLength(50)]
    public string? Phone { get; set; }

    public string? Note { get; set; }
    public bool IsActive { get; set; } = true;

    // ---------- ماژول انبارداری ----------
    /// <summary>کد انبار (اختیاری، یکتا)</summary>
    [MaxLength(30)] public string? Code { get; set; }

    /// <summary>ماهیت انبار (اصلی، فرعی، امانی، ضایعات، …)</summary>
    public WarehouseKind Kind { get; set; } = WarehouseKind.Main;

    /// <summary>نام انباردار</summary>
    [MaxLength(120)] public string? KeeperName { get; set; }

    /// <summary>انبار پیش‌فرض سیستم</summary>
    public bool IsDefault { get; set; }

    /// <summary>اجازه منفی شدن موجودی در این انبار</summary>
    public bool AllowNegative { get; set; }
}