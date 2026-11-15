namespace Inventory.Shared;

/// <summary>
/// مبالغ سند حسابداری فاکتور فروش دارای درآمد چک، مستقل از شناسهٔ حساب‌ها.
/// مبلغ کالا بدون VAT است؛ مالیات کالا و درآمد چک هر کدام بستانکاری مستقل دارند.
/// این محاسبه ثبت وصول یا اصلاح وضعیت چک را انجام نمی‌دهد.
/// </summary>
public readonly record struct SaleChequeVoucherAmounts(
    decimal CustomerDebit, decimal GoodsCredit, decimal VatCredit, decimal ChequeIncomeCredit)
{
    private const decimal MaxLedgerAmount = 9999999999999999.99m; // decimal(18,2).
    public decimal TotalCredit => GoodsCredit + VatCredit + ChequeIncomeCredit;

    public static SaleChequeVoucherAmounts Create(decimal goodsAfterDiscount, decimal goodsVat, decimal chequeIncome)
    {
        static bool Valid(decimal value) => value >= 0 && value <= MaxLedgerAmount && decimal.Round(value, 2) == value;
        if (!Valid(goodsAfterDiscount)) throw new ArgumentOutOfRangeException(nameof(goodsAfterDiscount));
        if (!Valid(goodsVat)) throw new ArgumentOutOfRangeException(nameof(goodsVat));
        if (!Valid(chequeIncome)) throw new ArgumentOutOfRangeException(nameof(chequeIncome));
        var total = goodsAfterDiscount + goodsVat + chequeIncome;
        if (!Valid(total)) throw new ArgumentOutOfRangeException(nameof(goodsAfterDiscount), "مبلغ کل از محدودهٔ دفتر حسابداری بیشتر است.");
        return new SaleChequeVoucherAmounts(total, goodsAfterDiscount, goodsVat, chequeIncome);
    }
}
