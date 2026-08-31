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
}