namespace Inventory.Shared;

/// <summary>
/// مبالغ اصلاح حسابداری فروش دارای تاخیر تادیه چک، بدون ثبت سند یا تسویهٔ چک.
/// ماندهٔ بستانکار طرف حساب به معنی طلب مشتری است، نه پرداخت نقدی خودکار.
/// </summary>
public readonly record struct SaleChequeCorrectionAmounts(
    decimal PartyDebit, decimal PartyCredit, decimal GoodsDebit, decimal VatDebit,
    decimal IncomeDebit, decimal IncomeCredit)
{
    private const decimal MaxAmount = 9999999999999999.99m; // decimal(18,2)
    public decimal TotalDebit => PartyDebit + GoodsDebit + VatDebit + IncomeDebit;
    public decimal TotalCredit => PartyCredit + IncomeCredit;

    private static void Validate(decimal value, string name)
    {
        if (value < 0 || value > MaxAmount || decimal.Round(value, 2) != value)
            throw new ArgumentOutOfRangeException(name, "مبلغ باید غیرمنفی و در محدوده حسابداری با حداکثر دو رقم اعشار باشد.");
    }

    /// <summary>
    /// اصلاح سررسید: اختلاف مثبت بدهکار مشتری و بستانکار درآمد است؛ اختلاف منفی برعکس.
    /// اگر کاهش از درآمد قبلی بیشتر باشد، حساب مشتری می‌تواند بستانکار و حساب درآمد خالص بدهکار شود.
    /// این صرفاً آرتیکل متوازن است؛ هیچ چکی را وصول، برگشت یا تسویه نمی‌کند.
    /// </summary>
    public static SaleChequeCorrectionAmounts ForDueDate(decimal adjustment)
    {
        if (adjustment < -MaxAmount || adjustment > MaxAmount || decimal.Round(adjustment, 2) != adjustment)
            throw new ArgumentOutOfRangeException(nameof(adjustment));
        return adjustment >= 0
            ? new(adjustment, 0, 0, 0, 0, adjustment)
            : new(0, -adjustment, 0, 0, -adjustment, 0);
    }

    /// <summary>
    /// برگشت کالا: فروش، مالیات کالا و درآمد چک جداگانه بدهکار و مشتری به جمع آنها بستانکار می‌شود.
    /// وصول قبلی در ماندهٔ موجود طرف حساب لحاظ شده است و نباید به‌عنوان پرداخت مجدد ثبت شود.
    /// </summary>
    public static SaleChequeCorrectionAmounts ForReturn(decimal goodsBeforeVat, decimal goodsVat, decimal returnedIncome)
    {
        Validate(goodsBeforeVat, nameof(goodsBeforeVat));
        Validate(goodsVat, nameof(goodsVat));
        Validate(returnedIncome, nameof(returnedIncome));
        var total = goodsBeforeVat + goodsVat + returnedIncome;
        Validate(total, nameof(goodsBeforeVat));
        return new(0, total, goodsBeforeVat, goodsVat, returnedIncome, 0);
    }
}
