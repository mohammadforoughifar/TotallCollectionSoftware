using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;

public class TransactionLine
{
    public int Id { get; set; }
    public int TransactionId { get; set; }
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }

    [MaxLength(300)]
    public string? Description { get; set; }
}