namespace Inventory.Shared;

/// <summary>
/// عنوان‌های فارسی انواع پرداخت صورتحساب (setm). مقادیر عددی همان enum
/// <see cref="MoadianPayType"/> است و با قرارداد سامانهٔ مودیان یکی است.
/// </summary>
public static class MoadianPayTypes
{
    public static IReadOnlyList<MoadianPayType> All { get; } = new[]
    {
        MoadianPayType.Cash,
        MoadianPayType.Credit,
        MoadianPayType.Electronic,
        MoadianPayType.Facility,
        MoadianPayType.Offset
    };

    public static string Title(MoadianPayType? payType) => payType switch
    {
        MoadianPayType.Cash => "نقدی",
        MoadianPayType.Credit => "نسیه",
        MoadianPayType.Electronic => "الکترونیکی",
        MoadianPayType.Facility => "تسهیلات",
        MoadianPayType.Offset => "تهاتر",
        _ => "—"
    };

    public static string TitleWithCode(MoadianPayType payType) => $"{(int)payType} — {Title(payType)}";
}
