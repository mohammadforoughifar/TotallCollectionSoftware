using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;

/// <summary>چک دریافتی بابت سند فروش</summary>
public class Cheque
{
    public int Id { get; set; }
    public int TransactionId { get; set; }

    /// <summary>شماره چک / صیاد</summary>
    [MaxLength(50)]
    public string Number { get; set; } = "";

    /// <summary>نام بانک</summary>
    [MaxLength(100)]
    public string? BankName { get; set; }

    /// <summary>شماره حساب / شعبه</summary>
    [MaxLength(100)]
    public string? AccountInfo { get; set; }

    /// <summary>صاحب چک</summary>
    [MaxLength(150)]
    public string? OwnerName { get; set; }

    public decimal Amount { get; set; }

    /// <summary>تاریخ سررسید چک</summary>
    public DateTime DueDate { get; set; }

    /// <summary>پاس شده؟</summary>
    public bool IsCleared { get; set; }

    /// <summary>تاریخ پاس شدن</summary>
    public DateTime? ClearedAt { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}