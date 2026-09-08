using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;
public class Expense
{
    public int Id { get; set; }
    [MaxLength(30)] public string Number { get; set; } = "";
    public int CategoryId { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public CashType PayType { get; set; } = CashType.Cash;
    [MaxLength(150)] public string? Payee { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}