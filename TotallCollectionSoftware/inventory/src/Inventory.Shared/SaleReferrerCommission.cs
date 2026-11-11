namespace Inventory.Shared;

/// <summary>پورسانت معرف فقط از سود فروشگاه کسر می‌شود و جزء مبلغ مشتری نیست.</summary>
public static class SaleReferrerCommission
{
    /// <summary>کالا: درصد معرف از سود مثبت هر سطر، بدون ارزش افزوده.</summary>
    public static decimal Goods(decimal lineProfit, decimal percent) =>
        decimal.Round(Math.Max(0m, lineProfit) * percent / 100m, 2);

    /// <summary>خدمت: درصد معرف از مبلغ باقی‌مانده پس از تخفیف، بدون ارزش افزوده.</summary>
    public static decimal Service(decimal lineAfterDiscount, decimal percent) =>
        decimal.Round(Math.Max(0m, lineAfterDiscount) * percent / 100m, 2);
}
