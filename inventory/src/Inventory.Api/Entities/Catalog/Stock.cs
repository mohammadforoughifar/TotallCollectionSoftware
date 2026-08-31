using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;

/// <summary>موجودی فعلی هر کالا در هر انبار</summary>
public class Stock
{
    public int Id { get; set; }
    public int WarehouseId { get; set; }
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>قیمت میانگین موزون</summary>
    public decimal AvgCost { get; set; }
}