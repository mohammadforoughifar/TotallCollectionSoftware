using Inventory.Api.Services;
using Inventory.Shared;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>اسناد خرید و فروش.</summary>
[Route("api/orders")]
[Inventory.Api.Infrastructure.ModulePermission("Orders")]
public class OrdersController : ApiControllerBase
{
    private readonly IInventoryService _service;

    public OrdersController(IInventoryService service) => _service = service;

    /// <summary>فهرست اسناد با فیلتر نوع/تاریخ/طرف حساب/انبار.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<Order>>> GetAll(
        [FromQuery] TransactionType type, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int? partyId, [FromQuery] int? warehouseId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] int? skip = null, [FromQuery] int? take = null,
        [FromQuery] bool? hasChequeIncome = null)
        => Ok(await _service.GetOrdersAsync(type, from, to, partyId, warehouseId, Paging.ToPage(skip, take, page), Paging.ToPageSize(take, pageSize), hasChequeIncome));

    /// <summary>پیشنهاد قیمت بر اساس آخرین معامله کالا.</summary>
    [HttpGet("suggest-price")]
    public async Task<ActionResult<decimal>> SuggestPrice([FromQuery] int productId, [FromQuery] TransactionType type)
        => Ok(await _service.SuggestPriceAsync(productId, type));

    /// <summary>آخرین فاکتور خریدی که شامل این کالاست.</summary>
    [HttpGet("last-purchase")]
    public async Task<ActionResult<Order>> LastPurchase([FromQuery] int productId)
    {
        var order = await _service.GetLastPurchaseAsync(productId);
        return order is null
            ? NotFound(new { message = "برای این کالا سند خریدی ثبت نشده است." })
            : Ok(order);
    }

    /// <summary>دریافت یک سند.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Order>> GetById(int id)
    {
        var order = await _service.GetOrderAsync(id);
        return order is null ? NotFound(new { message = "سند یافت نشد." }) : Ok(order);
    }

    /// <summary>ثبت سند خرید/فروش جدید.</summary>
    [HttpPost]
    public async Task<ActionResult<Order>> Create([FromBody] OrderCommand cmd)
    {
        var error = TrackingError(cmd, cmd.Type, null);
        return error is null ? Ok(await _service.CreateOrderAsync(cmd, User.Identity?.Name)) : BadRequest(new { message = error });
    }

    /// <summary>ویرایش سند خرید/فروش.</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<Order>> Update(int id, [FromBody] OrderCommand cmd)
    {
        var existing = await _service.GetOrderAsync(id);
        if (existing is null) return NotFound(new { message = "سند یافت نشد." });
        var error = TrackingError(cmd, existing.Type, existing);
        return error is null ? Ok(await _service.UpdateOrderAsync(id, cmd)) : BadRequest(new { message = error });
    }

    private static string? TrackingError(OrderCommand cmd, TransactionType type, Order? existing)
    {
        // Old clients may omit the new property; retain an existing transfer reference.
        var reference = cmd.CashTrackingNumber ?? (existing?.CashType == CashType.CardTransfer ? existing.CashTrackingNumber : null);
        if (type == TransactionType.Sale && cmd.CashType == CashType.CardTransfer &&
            (cmd.PaymentMethod == PaymentMethod.Cash || cmd.CashAmount > 0) &&
            string.IsNullOrWhiteSpace(reference))
            return "شماره پیگیری پرداخت کارت به کارت را وارد کنید.";
        return reference?.Trim().Length > 100 ? "شماره پیگیری حداکثر ۱۰۰ نویسه است." : null;
    }

    /// <summary>پیش‌نمایش آزمایشی تعدیل سررسید بدون تغییر داده.</summary>
    [HttpGet("cheques/{chequeId:int}/reschedule-preview")]
    [Inventory.Api.Infrastructure.RequirePermission("Orders", "Update")]
    public async Task<ActionResult<SaleChequeReschedulePreview>> PreviewReschedule(int chequeId,
        [FromQuery] DateTime expectedDueDate, [FromQuery] DateTime newDueDate)
        => Ok(await _service.PreviewSaleChequeRescheduleAsync(chequeId, expectedDueDate, newDueDate));

    /// <summary>ثبت آزمایشی تغییر سررسید و سند تعدیل با تأیید پیش‌نمایش.</summary>
    [HttpPut("cheques/{chequeId:int}/reschedule")]
    public async Task<ActionResult<SaleChequeReschedulePreview>> Reschedule(int chequeId, [FromBody] SaleChequeRescheduleCommand cmd)
        => Ok(await _service.RescheduleSaleChequeAsync(chequeId, cmd, User.Identity?.Name));

    /// <summary>وصول آزمایشی چک فروش به حساب بانکی انتخاب‌شده.</summary>
    [HttpPut("cheques/{chequeId:int}/receipt")]
    public async Task<IActionResult> ReceiveCheque(int chequeId, [FromBody] SaleChequeReceiptCommand cmd)
    {
        await _service.ReceiveSaleChequeAsync(chequeId, cmd, User.Identity?.Name);
        return Ok(new { ok = true });
    }

    /// <summary>لغو وصول آزمایشی با سند معکوس.</summary>
    [HttpPut("cheques/{chequeId:int}/receipt-reversal")]
    public async Task<IActionResult> ReverseChequeReceipt(int chequeId, [FromBody] SaleChequeReceiptReversalCommand cmd)
    {
        await _service.ReverseSaleChequeReceiptAsync(chequeId, cmd, User.Identity?.Name);
        return Ok(new { ok = true });
    }

    /// <summary>تاریخچه وضعیت چک مشمول تاخیر تادیه.</summary>
    [HttpGet("cheques/{chequeId:int}/status-history")]
    public async Task<ActionResult<List<SaleChequeStatusHistoryItem>>> ChequeStatusHistory(int chequeId)
        => Ok(await _service.GetSaleChequeStatusHistoryAsync(chequeId));

    /// <summary>تغییر وضعیت چک آزمایشی بدون ایجاد سود جدید.</summary>
    [HttpPut("cheques/{chequeId:int}/status")]
    public async Task<IActionResult> ChangeChequeStatus(int chequeId, [FromBody] SaleChequeStatusCommand cmd)
    {
        await _service.ChangeSaleChequeStatusAsync(chequeId, cmd, User.Identity?.Name);
        return Ok(new { ok = true });
    }

    /// <summary>پیش‌نمایش برگشت کالای فاکتور دارای تاخیر تادیه چک بدون تغییر داده.</summary>
    [HttpPost("{id:int}/cheque-return-preview")]
    [Inventory.Api.Infrastructure.RequirePermission("Orders", "Update")]
    public async Task<ActionResult<SaleChequeReturnPreviewDto>> PreviewChequeReturn(int id, [FromBody] SaleChequeReturnPreviewRequest req)
        => Ok(await _service.PreviewSaleChequeReturnAsync(id, req));

    /// <summary>ثبت اتمیک برگشت کالای فاکتور دارای تاخیر تادیه چک همراه با تصمیم مشتری درباره چک.</summary>
    [HttpPost("{id:int}/cheque-return")]
    [Inventory.Api.Infrastructure.RequirePermission("Orders", "Update")]
    public async Task<ActionResult<SaleChequeReturnSummaryDto>> ReturnChequeOrder(int id, [FromBody] SaleChequeReturnCommand cmd)
        => Ok(await _service.ReturnSaleChequeOrderAsync(id, cmd, User.Identity?.Name));

    /// <summary>تعویض صریح چک وصول‌نشده با حفظ چک قبلی در تاریخچه.</summary>
    [HttpPut("cheques/{chequeId:int}/replace")]
    [Inventory.Api.Infrastructure.RequirePermission("Orders", "Update")]
    public async Task<IActionResult> ReplaceCheque(int chequeId, [FromBody] SaleChequeReplaceCommand cmd)
    {
        await _service.ReplaceSaleChequeAsync(chequeId, cmd, User.Identity?.Name);
        return Ok(new { ok = true });
    }

    /// <summary>پرداخت وجه بستانکاری مشتری از حساب صندوق/بانک شرکت.</summary>
    [HttpPost("{id:int}/credit-refund")]
    [Inventory.Api.Infrastructure.RequirePermission("Orders", "Update")]
    public async Task<IActionResult> RefundCredit(int id, [FromBody] SaleChequeCreditRefundCommand cmd)
    {
        await _service.RefundSaleChequeCreditAsync(id, cmd, User.Identity?.Name);
        return Ok(new { ok = true });
    }

    /// <summary>مصرف بستانکاری مشتری برای تسویه فاکتور فروش دیگر همان مشتری.</summary>
    [HttpPost("{id:int}/credit-allocation")]
    [Inventory.Api.Infrastructure.RequirePermission("Orders", "Update")]
    public async Task<IActionResult> AllocateCredit(int id, [FromBody] SaleChequeCreditAllocationCommand cmd)
    {
        await _service.ApplySaleChequeCreditAsync(id, cmd, User.Identity?.Name);
        return Ok(new { ok = true });
    }

    /// <summary>دریافت قسط فاکتور دارای تاخیر تادیه چک به حساب منتخب شرکت.</summary>
    [HttpPut("installments/{installmentId:int}/receipt")]
    [Inventory.Api.Infrastructure.RequirePermission("Orders", "Update")]
    public async Task<IActionResult> ReceiveInstallment(int installmentId, [FromBody] SaleChequeInstallmentReceiptCommand cmd)
    {
        await _service.ReceiveSaleChequeInstallmentAsync(installmentId, cmd, User.Identity?.Name);
        return Ok(new { ok = true });
    }

    /// <summary>لغو دریافت قسط فاکتور دارای تاخیر تادیه چک با سند معکوس.</summary>
    [HttpPut("installments/{installmentId:int}/receipt-reversal")]
    [Inventory.Api.Infrastructure.RequirePermission("Orders", "Update")]
    public async Task<IActionResult> ReverseInstallmentReceipt(int installmentId, [FromBody] SaleChequeInstallmentReversalCommand cmd)
    {
        await _service.ReverseSaleChequeInstallmentReceiptAsync(installmentId, cmd, User.Identity?.Name);
        return Ok(new { ok = true });
    }

    /// <summary>ابطال اتمیک فاکتور دارای تاخیر تادیه چک با سند معکوس کامل.</summary>
    [HttpPost("{id:int}/cheque-cancel")]
    [Inventory.Api.Infrastructure.RequirePermission("Orders", "Delete")]
    public async Task<IActionResult> CancelChequeOrder(int id, [FromBody] SaleChequeCancelCommand cmd)
    {
        await _service.CancelSaleChequeOrderAsync(id, cmd, User.Identity?.Name);
        return Ok(new { ok = true });
    }

    /// <summary>ویرایش اتمیک فاکتور سوددار اولیه پیش از گردش‌های بعدی.</summary>
    [HttpPut("{id:int}/cheque-edit")]
    [Inventory.Api.Infrastructure.RequirePermission("Orders", "Update")]
    public async Task<ActionResult<Order>> EditChequeOrder(int id, [FromBody] OrderCommand cmd)
    {
        var existing = await _service.GetOrderAsync(id);
        if (existing is null) return NotFound(new { message = "سند یافت نشد." });
        var error = TrackingError(cmd, TransactionType.Sale, existing);
        return error is null ? Ok(await _service.EditSaleChequeOrderAsync(id, cmd, User.Identity?.Name)) : BadRequest(new { message = error });
    }

    /// <summary>حساب‌های فعال خزانه/بانک متعلق به شرکت فعال برای عملیات فروش و وصول.</summary>
    [HttpGet("active-treasury-accounts")]
    public async Task<ActionResult<List<TrsAccount>>> ActiveTreasuryAccounts([FromQuery] TreasuryAccountKind? kind = null)
        => Ok(await _service.GetActiveCompanyTreasuryAccountsAsync(kind));

    /// <summary>حذف سند و برگرداندن موجودی.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteOrderAsync(id);
        return Ok(new { ok = true });
    }
}
