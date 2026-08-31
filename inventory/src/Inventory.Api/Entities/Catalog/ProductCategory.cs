using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;

/// <summary>گروه / دسته‌بندی کالا (درختی — با پشتیبانی از گروه والد)</summary>
public class ProductCategory
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = "";

    /// <summary>شناسه گروه والد (null = گروه ریشه)</summary>
    public int? ParentId { get; set; }

    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}