using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>Current shop-only rule; the default in a company without a row is disabled.</summary>
public class ShopPricingPolicy : ICompanyOwned
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public bool MinimumProfitEnabled { get; set; }
    public decimal MinimumMarkupPercent { get; set; }
    public DateTime UpdatedAt { get; set; }
    [MaxLength(120)] public string? UpdatedBy { get; set; }
}

/// <summary>Append-only dated selling price. No attempt is made to reconstruct older prices.</summary>
public class ShopSalePrice : ICompanyOwned
{
    public long Id { get; set; }
    public int? CompanyId { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public DateTime EffectiveDate { get; set; }
    public decimal Price { get; set; }
    public decimal? PreviousPrice { get; set; }
    [MaxLength(24)] public string Source { get; set; } = "";
    public int? PurchaseTransactionId { get; set; }
    public Guid? OperationId { get; set; }
    [MaxLength(64)] public string? PreviewHash { get; set; }
    public DateTime CreatedAt { get; set; }
    [MaxLength(120)] public string? CreatedBy { get; set; }
}
