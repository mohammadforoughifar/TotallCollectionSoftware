using Inventory.Api.Data;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Infrastructure;

/// <summary>دسترسی فروشگاهی را بدون تبدیل مجوز گزارش به مجوز نوشتن اعمال می‌کند.</summary>
public static class ShopPermissionAuthorizer
{
    private static bool Section(IReadOnlySet<string> permissions, string section, bool write)
        => (ShopSections.ByKey(section)?.Writable == true && permissions.Contains($"Shop.{section}.Manage")) ||
           (!write && permissions.Contains($"Shop.{section}.View"));

    public static bool OrderType(IReadOnlySet<string> permissions, TransactionType type, bool write)
    {
        if (!write && (permissions.Contains("Orders.Read") || permissions.Contains("Orders.View") || permissions.Contains("Orders.Access"))) return true;
        return type switch
        {
            TransactionType.Sale or TransactionType.SaleReturn => Section(permissions, "Sale", write) ||
                (!write && (Section(permissions, "OrderReports", false) || Section(permissions, "PartyStatements", false) || Section(permissions, "Dashboard", false))),
            TransactionType.Purchase => Section(permissions, "Purchase", write) ||
                (!write && (Section(permissions, "OrderReports", false) || Section(permissions, "PartyStatements", false) || Section(permissions, "Dashboard", false))),
            TransactionType.Initial or TransactionType.Adjustment => Section(permissions, "OpeningBalance", write),
            _ => false
        };
    }

    public static async Task<bool> AllowsAsync(AppDbContext db, ActionExecutingContext context,
        IReadOnlySet<string> permissions, string module, string action)
    {
        if (permissions.Contains($"{module}.{action}")) return true;
        if (!module.Equals("Orders", StringComparison.OrdinalIgnoreCase))
            return permissions.Any(p => ShopSections.AliasAllows($"{module}.{action}", p));
        // ViewProfit is never implied by a shop section.
        if (action.Equals("ViewProfit", StringComparison.OrdinalIgnoreCase)) return false;
        if (context.ActionDescriptor is not ControllerActionDescriptor descriptor) return false;
        var write = !ShopSections.IsViewLike(action);
        var name = descriptor.ActionName;
        if (descriptor.ControllerName == "Dashboard" && (name is "ClearCheque" or "BounceCheque" or "ResetCheque" or
            "PayInstallment" or "UnpayInstallment" or "GetShopCreditSummary" or "GetShopCheques" or "GetShopInstallments"))
            return Section(permissions, "Installments", write);
        if (descriptor.ControllerName != "Orders") return false;
        if (name is "ProfitAccess" or "WarehouseStock" or "ActiveTreasuryAccounts")
            return !write && (Section(permissions, "Sale", false) || Section(permissions, "Purchase", false) ||
                Section(permissions, "Installments", false) || Section(permissions, "OpeningBalance", false) ||
                Section(permissions, "OrderReports", false));
        if (name == "LastPurchase") return !write && Section(permissions, "Sale", false);
        if (name is "ReceiveCheque" or "ReverseChequeReceipt")
        {
            if (Section(permissions, "Installments", true)) return true;
            if (!context.ActionArguments.TryGetValue("chequeId", out var chequeArg) || chequeArg is not int chequeId || chequeId <= 0) return false;
            var invoiceType = await (from cheque in db.Cheques.AsNoTracking()
                                     join order in db.Transactions.AsNoTracking() on cheque.TransactionId equals order.Id
                                     where cheque.Id == chequeId
                                     select (TransactionType?)order.Type).FirstOrDefaultAsync();
            return invoiceType.HasValue && (invoiceType.Value is TransactionType.Sale or TransactionType.Purchase) && OrderType(permissions, invoiceType.Value, true);
        }
        if (name is "ReceiveInstallment" or "ReverseInstallmentReceipt")
        {
            if (Section(permissions, "Installments", true)) return true;
            if (!context.ActionArguments.TryGetValue("installmentId", out var installmentArg) || installmentArg is not int installmentId || installmentId <= 0) return false;
            var invoiceType = await (from installment in db.Installments.AsNoTracking()
                                     join order in db.Transactions.AsNoTracking() on installment.TransactionId equals order.Id
                                     where installment.Id == installmentId
                                     select (TransactionType?)order.Type).FirstOrDefaultAsync();
            return invoiceType.HasValue && (invoiceType.Value is TransactionType.Sale or TransactionType.Purchase) && OrderType(permissions, invoiceType.Value, true);
        }
        if (name is "PreviewReschedule" or "Reschedule" or "ChangeChequeStatus" or "ReplaceCheque")
            return Section(permissions, "Installments", true) || Section(permissions, "Sale", true);
        if (name == "ChequeStatusHistory") return !write && (Section(permissions, "Installments", false) || Section(permissions, "Sale", false));
        if (name is "PreviewChequeReturn" or "ReturnChequeOrder" or "RefundCredit" or "AllocateCredit" or "CancelChequeOrder" or "EditChequeOrder")
            return Section(permissions, "Sale", true);
        if (name is "GetAll" or "SuggestPrice" or "InvoiceProducts" or "InvoiceWarehouses" or "InvoiceParties")
            return context.ActionArguments.TryGetValue("type", out var typeArg) && typeArg is TransactionType requestedType && OrderType(permissions, requestedType, write);
        if (name == "Create")
            return context.ActionArguments.TryGetValue("cmd", out var createArg) && createArg is OrderCommand createCommand && OrderType(permissions, createCommand.Type, true);
        if (name is "GetById" or "Update" or "Delete" or "ReconcileCash")
        {
            if (!context.ActionArguments.TryGetValue("id", out var orderArg) || orderArg is not int orderId || orderId <= 0) return false;
            var existing = await db.Transactions.AsNoTracking().Where(t => t.Id == orderId).Select(t => (TransactionType?)t.Type).FirstOrDefaultAsync();
            if (!existing.HasValue || !OrderType(permissions, existing.Value, write)) return false;
            if (name == "Update" && context.ActionArguments.TryGetValue("cmd", out var commandValue) && commandValue is OrderCommand updateCommand)
                return updateCommand.Type == existing.Value && OrderType(permissions, updateCommand.Type, true);
            return true;
        }
        return false;
    }
}
