namespace Inventory.Shared;

/// <summary>Defaults only for unambiguous built-in units; custom units require a decision.</summary>
public static class ShopQuantityRules
{
    public static bool? DefaultAllowsFraction(string? name) => name?.Trim() switch
    {
        "عدد" or "بسته" or "کارتن" or "جفت" or "حلقه" or "دستگاه" or "شاخه" or "رول" => false,
        "کیلوگرم" or "گرم" or "لیتر" or "متر" or "سانتی‌متر" or "مترمربع" or "تن" => true,
        _ => null
    };

    public static bool IsWhole(decimal quantity) => quantity == decimal.Truncate(quantity);
}
