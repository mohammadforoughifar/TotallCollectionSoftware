using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Data;

/// <summary>پذیرش طرح تعویض؛ دستگاه صرفاً شرح است و به انبار وارد نمی‌شود.</summary>
public class ShopTradeIn : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public Guid OperationId { get; set; }
    [MaxLength(30)] public string Number { get; set; } = "";
    public int PartyId { get; set; }
    public Party Party { get; set; } = null!;
    [MaxLength(100)] public string DeviceType { get; set; } = "";
    [MaxLength(200)] public string? DeviceModel { get; set; }
    [MaxLength(100)] public string? SerialNumber { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
    public decimal Value { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? FinalizedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    [MaxLength(120)] public string? CreatedBy { get; set; }
    [MaxLength(120)] public string? FinalizedBy { get; set; }
}

/// <summary>مصرف یا پرداخت اعتبار طرح تعویض؛ دفتر مستقل فروشگاه و بدون فاکتور صوری.</summary>
public class ShopTradeInUse : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public Guid OperationId { get; set; }
    public int TradeInId { get; set; }
    public ShopTradeIn TradeIn { get; set; } = null!;
    public TradeInUseKind Kind { get; set; }
    public int? TargetSaleId { get; set; }
    public Transaction? TargetSale { get; set; }
    public int? TrsAccountId { get; set; }
    public TrsAccount? TrsAccount { get; set; }
    public int? TrsVoucherId { get; set; }
    public TrsVoucher? TrsVoucher { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    [MaxLength(200)] public string? Note { get; set; }
    [MaxLength(120)] public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
