using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;

namespace Inventory.Api.Services.Finance;

/// <summary>Reject a mismatched expense, lifecycle or bank voucher instead of guessing a balance.</summary>
public static class ShopExpenseChequeIntegrity
{
    public static void Validate(ShopExpenseCheque c)
    {
        if (c.Expense is null || c.CompanyId != c.Expense.CompanyId || c.Amount != c.Expense.Amount || c.Amount <= 0 ||
            c.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(c.Number) || string.IsNullOrWhiteSpace(c.Recipient) ||
            c.DueDate == default || c.HandedAt == default || c.HandedAt.Date < c.Expense.Date.Date ||
            c.Kind is not (ExpenseChequeKind.Issued or ExpenseChequeKind.Received) ||
            c.State is not (ExpenseChequeState.Outstanding or ExpenseChequeState.Cleared or ExpenseChequeState.Returned) ||
            (c.State == ExpenseChequeState.Outstanding && (c.StatusOperationId.HasValue || c.SettledAt.HasValue || c.StatusAt.HasValue)) ||
            (c.State != ExpenseChequeState.Outstanding && (!c.StatusOperationId.HasValue || c.StatusOperationId == Guid.Empty ||
                !c.SettledAt.HasValue || !c.StatusAt.HasValue || c.SettledAt.Value.Date < c.HandedAt.Date)))
            throw new InvalidOperationException("سابقه چک هزینه با سند آن مطابقت ندارد.");
        if (c.Kind == ExpenseChequeKind.Received)
        {
            if (c.SourceChequeId is not > 0 || c.SourceTransactionId is not > 0 || c.IssuingAccountId.HasValue || c.SettlementVoucherId.HasValue)
                throw new InvalidOperationException("مبدأ یا گردش بانکی چک خرج‌شده معتبر نیست.");
            return;
        }
        if (c.SourceChequeId.HasValue || c.SourceTransactionId.HasValue || c.IssuingAccountId is not > 0)
            throw new InvalidOperationException("حساب صادرکننده چک هزینه معتبر نیست.");
        if (c.State != ExpenseChequeState.Cleared)
        {
            if (c.SettlementVoucherId.HasValue)
                throw new InvalidOperationException("چک پاس‌نشده نباید گردش بانکی داشته باشد.");
            return;
        }
        var voucher = c.SettlementVoucher;
        if (voucher is null || voucher.Id != c.SettlementVoucherId || voucher.CompanyId != c.CompanyId ||
            voucher.Kind != TreasuryKind.Payment || voucher.Status != TreasuryStatus.Confirmed || voucher.Date.Date != c.SettledAt!.Value.Date ||
            voucher.TotalAmount != c.Amount || voucher.ChequeAmount != c.Amount || voucher.CashAmount != 0 ||
            voucher.Lines.Count != 1 || voucher.Lines[0].TrsAccountId != c.IssuingAccountId ||
            voucher.Lines[0].Amount != c.Amount || voucher.Lines[0].Method != PayMethod.Cheque || voucher.Lines[0].ChequeId.HasValue ||
            voucher.RefNumber != c.Number || voucher.Lines[0].RefNumber != c.Number)
            throw new InvalidOperationException("پاس چک هزینه با گردش بانک مطابقت ندارد.");
    }
}
