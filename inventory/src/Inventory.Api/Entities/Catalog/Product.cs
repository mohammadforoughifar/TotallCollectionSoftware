using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;
public class Product
{
    public int Id { get; set; }
    [MaxLength(50)] public string Code { get; set; } = "";
    [MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(50)] public string Unit { get; set; } = "عدد";
    [MaxLength(100)] public string? Category { get; set; }
    [MaxLength(100)] public string? Barcode { get; set; }
    public decimal SalePrice { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal ReorderPoint { get; set; }
    public decimal MaxStock { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsService { get; set; } = false;
    public int? WarehouseId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
