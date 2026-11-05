using System.Data;
using Inventory.Api.Data;
using Inventory.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inventory.Api.Services.Invoicing;

public sealed record SaleChequeReplacementInput(
    Guid OperationId,
    DateTime OperationDate,
    string Number,
    decimal Amount,
    DateTime DueDate,
    string? BankName,
    string? AccountInfo,
    string? OwnerName,
    string? Note,
    string? Reason,
    DateTime? ConfirmedCreditDueDate);

/// <summary>
/// تعویض صریح چک وصول‌نشده با حفظ کامل چک قبلی در تاریخچه؛
/// بدون تغییر خودکار و بدون ترکیب پنهان با تعدیل سررسید.
/// </summary>
public static class SaleChequeReplacementService
{
    private const decimal MaxMoney = 9999999999999999.99m;

    public static async Task<SaleChequeReplacement> ReplaceAsync(
        AppDbContext db, int oldChequeId, SaleChequeReplacementInput input, string? user)
    {
        var tx = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("تعویض چک باید در تراکنش حسابداری انجام شود.");
        try
        {
            if (tx.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                throw new InvalidOperationException("تراکنش تعویض چک باید سطح Serializable داشته باشد.");
            return await ReplaceInTransactionAsync(db, oldChequeId, input, user);
        }
        catch
        {
            try { await tx.RollbackAsync(); }
            finally { db.ChangeTracker.Clear(); }
            throw;
        }
    }

    private static async Task<SaleChequeReplacement> ReplaceInTransactionAsync(
        AppDbContext db, int oldChequeId, SaleChequeReplacementInput input, string? user)
    {
        ArgumentNullException.ThrowIfNull(input);
        var number = input.Number?.Trim() ?? "";
        var reason = string.IsNullOrWhiteSpace(input.Reason) ? $"تعویض با چک شماره {number}" : input.Reason.Trim();
        if (input.OperationId == Guid.Empty || input.OperationDate == default || input.DueDate == default ||
            string.IsNullOrWhiteSpace(number) || number.Length > 60 || reason.Length > 200 || user?.Length > 120)
            throw new InvalidOperationException("مشخصات چک جایگزین یا شناسه عملیات معتبر نیست.");
        if (input.Amount <= 0 || input.Amount > MaxMoney || decimal.Round(input.Amount, 2) != input.Amount)
            throw new InvalidOperationException("مبلغ چک جایگزین باید مثبت و با حداکثر دو رقم اعشار باشد.");

        var opDate = input.OperationDate.Date;
        var dueDate = input.DueDate.Date;

        var prior = await db.SaleChequeReplacements.Include(r => r.NewCheque)
            .FirstOrDefaultAsync(r => r.OperationId == input.OperationId);
        if (prior is not null)
        {
            if (prior.OldChequeId != oldChequeId || prior.Date.Date != opDate ||
                prior.NewAmount != input.Amount || prior.NewCheque.Number != number ||
                prior.NewCheque.DueDate.Date != dueDate)
                throw new InvalidOperationException("شناسه عملیات قبلاً برای تعویض چک دیگری استفاده شده است.");
            return prior;
        }

        var oldCheque = await db.Cheques.FirstOrDefaultAsync(c => c.Id == oldChequeId)
            ?? throw new InvalidOperationException("چک مورد نظر یافت نشد.");
        if (oldCheque.IsCleared || oldCheque.Status is not (ChequeStatus.InHand or ChequeStatus.Bounced) ||
            await db.SaleChequeReceipts.AnyAsync(r => r.ChequeId == oldCheque.Id && r.ReversalDate == null))
            throw new InvalidOperationException("فقط چک وصول‌نشده نزد صندوق یا برگشتی قابل تعویض است.");
        if (dueDate != oldCheque.DueDate.Date)
            throw new InvalidOperationException("سررسید چک جایگزین باید برابر سررسید فعلی چک قبلی باشد؛ در صورت تغییر سررسید، پس از تعویض از گزینه تغییر سررسید استفاده کنید.");

        var sale = await db.Transactions.Include(t => t.Cheques).Include(t => t.Installments)
            .FirstOrDefaultAsync(t => t.Id == oldCheque.TransactionId)
            ?? throw new InvalidOperationException("فاکتور فروش یافت نشد.");
        var charge = await db.SaleChequeCharges.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TransactionId == sale.Id)
            ?? throw new InvalidOperationException("ردیف تاخیر تادیه چک یافت نشد.");
        if (sale.Type != TransactionType.Sale || sale.PartyId is not > 0 || opDate < sale.Date.Date ||
            oldCheque.CompanyId != sale.CompanyId || charge.CompanyId != sale.CompanyId)
            throw new InvalidOperationException("فاکتور فروش یا تاریخ تعویض چک معتبر نیست.");

        if (await db.SaleChequeCancellations.AnyAsync(c => c.SaleTransactionId == sale.Id))
            throw new InvalidOperationException("فاکتور ابطال شده است و امکان تعویض چک ندارد.");
        var remainingPayable = Math.Max(0m, await SaleChequeSettlementLedger.SignedChargedSaleBalanceAsync(db, sale, charge));
        var otherUncollected = sale.Cheques
            .Where(c => c.Id != oldCheque.Id && !c.IsCleared && c.Status is ChequeStatus.InHand or ChequeStatus.Bounced or ChequeStatus.InCollection)
            .Sum(c => c.Amount);
        var unpaidInstallments = sale.Installments.Where(i => !i.IsPaid).Sum(i => i.Amount);
        var totalScheduledAfter = otherUncollected + unpaidInstallments + input.Amount;
        if (totalScheduledAfter > remainingPayable)
            throw new InvalidOperationException("جمع چک جایگزین و سایر تعهدات باز نمی‌تواند از مانده قابل پرداخت فاکتور بیشتر باشد.");
        if (totalScheduledAfter < remainingPayable && !sale.DueDate.HasValue && !input.ConfirmedCreditDueDate.HasValue)
            throw new InvalidOperationException("مبلغ چک جایگزین کمتر از مانده فاکتور است؛ سررسید مانده نسیه را مشخص کنید.");
        if (input.ConfirmedCreditDueDate.HasValue)
        {
            if (input.ConfirmedCreditDueDate.Value.Date < sale.Date.Date)
                throw new InvalidOperationException("سررسید مانده نسیه نمی‌تواند پیش از تاریخ فاکتور باشد.");
            sale.DueDate = input.ConfirmedCreditDueDate.Value.Date;
            if (sale.PaymentMethod == PaymentMethod.Cheque)
                sale.PaymentMethod = PaymentMethod.Mixed;
        }

        var oldStatus = oldCheque.Status;
        oldCheque.Status = ChequeStatus.Cancelled;
        db.SaleChequeStatusEvents.Add(new SaleChequeStatusEvent
        {
            CompanyId = oldCheque.CompanyId,
            OperationId = input.OperationId,
            ChequeId = oldCheque.Id,
            PreviousStatus = oldStatus,
            NewStatus = ChequeStatus.Cancelled,
            CreatedAt = DateTime.Now,
            CreatedBy = user,
            Reason = reason
        });

        var newCheque = new Cheque
        {
            CompanyId = sale.CompanyId,
            TransactionId = sale.Id,
            Number = number,
            BankName = string.IsNullOrWhiteSpace(input.BankName) ? oldCheque.BankName : input.BankName.Trim(),
            AccountInfo = string.IsNullOrWhiteSpace(input.AccountInfo) ? oldCheque.AccountInfo : input.AccountInfo.Trim(),
            OwnerName = string.IsNullOrWhiteSpace(input.OwnerName) ? oldCheque.OwnerName : input.OwnerName.Trim(),
            Amount = input.Amount,
            DueDate = dueDate,
            OriginalDueDate = dueDate,
            Status = ChequeStatus.InHand,
            IsCleared = false,
            Note = string.IsNullOrWhiteSpace(input.Note) ? $"جایگزین چک {oldCheque.Number}" : input.Note.Trim()
        };
        db.Cheques.Add(newCheque);

        var replacement = new SaleChequeReplacement
        {
            CompanyId = sale.CompanyId,
            OperationId = input.OperationId,
            TransactionId = sale.Id,
            SaleChequeChargeId = charge.Id,
            OldCheque = oldCheque,
            NewCheque = newCheque,
            Date = opDate,
            OldAmount = oldCheque.Amount,
            NewAmount = input.Amount,
            Reason = reason,
            CreatedBy = user,
            CreatedAt = DateTime.Now
        };
        db.SaleChequeReplacements.Add(replacement);
        await db.SaveChangesAsync();
        return replacement;
    }
}
