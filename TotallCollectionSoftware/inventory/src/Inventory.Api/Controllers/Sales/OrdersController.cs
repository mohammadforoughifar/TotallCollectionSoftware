using Inventory.Api.Services;
using Inventory.Api.Services.Core;
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
    private readonly IEffectivePermissions _permissions;

    public OrdersController(IInventoryService service, IEffectivePermissions permissions)
    {
        _service = service;
        _permissions = permissions;
    }

    // Profit and its underlying cost must not be exposed by the order API merely
    // because the caller can read or manage sales. Always use current DB permissions.
    private async Task<bool> CanSeeProfitAsync() => User.Identity?.IsAuthenticated == true &&
        (await _permissions.IsLegacyAdminAsync(User) || (await _permissions.GetAsync(User)).Contains("Orders.ViewProfit"));

    private static Order RemoveSaleProfit(Order order)
    {
        if (order.Type is not (TransactionType.Sale or TransactionType.SaleReturn)) return order;
        order.TotalProfit = null;
        order.NetProfit = null;
        order.CommissionAmount = null;
        foreach (var line in order.Lines)
        {
            line.UnitCost = null;
            line.Profit = null;
            line.Commission = null;
        }
        return order;
    }

    private async Task<bool> MayReturnSaleProfitAsync(TransactionType type) =>
        type is not (TransactionType.Sale or TransactionType.SaleReturn) || await CanSeeProfitAsync();

    private async Task<Order> ProtectSaleProfitAsync(Order order) =>
        await MayReturnSaleProfitAsync(order.Type) ? order : RemoveSaleProfit(order);

    /// <summary>فهرست اسناد با فیلتر نوع/تاریخ/طرف حساب/انبار.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<Order>>> GetAll(
        [FromQuery] TransactionType type, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int? partyId, [FromQuery] int? warehouseId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] int? skip = null, [FromQuery] int? take = null,
        [FromQuery] bool? hasChequeIncome = null)
    {
        var result = await _service.GetOrdersAsync(type, from, to, partyId, warehouseId,
            Paging.ToPage(skip, take, page), Paging.ToPageSize(take, pageSize), hasChequeIncome);
        if (result.Items.Any(o => o.Type is TransactionType.Sale or TransactionType.SaleReturn) && !await CanSeeProfitAsync())
            foreach (var order in result.Items) RemoveSaleProfit(order);
        return Ok(result);
    }

    /// <summary>مجوز مؤثر فعلی برای نمایش سود، مستقل از claim قدیمی داخل توکن.</summary>
    [HttpGet("profit-access")]
    public async Task<ActionResult<bool>> ProfitAccess() => Ok(await CanSeeProfitAsync());

    /// <summary>موجودی تعدادی انبار برای انتخاب کالا در فاکتور، بدون افشای بهای تمام‌شده.</summary>
    [HttpGet("warehouse-stock")]
    public async Task<ActionResult<PagedResult<InvoiceStockItem>>> WarehouseStock(
        [FromQuery] int warehouseId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10000)
    {
        if (warehouseId <= 0) return BadRequest(new { message = "انبار معتبر انتخاب کنید." });
        var result = await _service.GetStockAsync(warehouseId, null, false, Math.Max(1, page), Math.Clamp(pageSize, 1, 10000));
        return Ok(new PagedResult<InvoiceStockItem>
        {
            Items = result.Items.Select(s => new InvoiceStockItem
            {
                WarehouseId = s.WarehouseId, ProductId = s.ProductId, Quantity = s.Quantity
            }).ToList(),
            TotalCount = result.TotalCount, Page = result.Page, PageSize = result.PageSize
        });
    }

    /// <summary>داده‌های محدود مورد نیاز ثبت فاکتور؛ مشاهده سود فروش همچنان مجوز مستقل دارد.</summary>
    [HttpGet("invoice-products")]
    public async Task<ActionResult<PagedResult<Product>>> InvoiceProducts([FromQuery] TransactionType type, [FromQuery] int page = 1, [FromQuery] int pageSize = 10000)
    {
        if (type is not (TransactionType.Sale or TransactionType.Purchase)) return BadRequest();
        var result = await _service.GetProductsAsync(null, false, Math.Max(1, page), Math.Clamp(pageSize, 1, 10000));
        var showCost = type == TransactionType.Purchase || await CanSeeProfitAsync();
        return Ok(new PagedResult<Product>
        {
            Items = result.Items.Select(p => new Product
            {
                Id = p.Id, Code = p.Code, Name = p.Name, Unit = p.Unit, IsActive = p.IsActive,
                IsService = p.IsService, WarehouseId = p.WarehouseId, SalePrice = p.SalePrice,
                InvoiceVatValue = p.InvoiceVatValue, PurchasePrice = showCost ? p.PurchasePrice : 0,
                AvgCost = showCost ? p.AvgCost : 0
            }).ToList(),
            TotalCount = result.TotalCount, Page = result.Page, PageSize = result.PageSize
        });
    }

    [HttpGet("invoice-warehouses")]
    public async Task<ActionResult<List<LookupItem>>> InvoiceWarehouses([FromQuery] TransactionType type, [FromQuery] bool activeOnly = false)
    {
        if (type is not (TransactionType.Sale or TransactionType.Purchase)) return BadRequest();
        var warehouses = await _service.GetWarehousesAsync();
        return Ok(warehouses.Where(w => !activeOnly || w.IsActive).Select(w => new LookupItem { Id = w.Id, Name = w.Name }).ToList());
    }

    [HttpGet("invoice-parties")]
    public async Task<ActionResult<List<Party>>> InvoiceParties([FromQuery] TransactionType type)
    {
        if (type is not (TransactionType.Sale or TransactionType.Purchase)) return BadRequest();
        return Ok(await _service.GetInvoicePartiesAsync(type == TransactionType.Sale ? PartyType.Customer : PartyType.Supplier));
    }

    /// <summary>پیشنهاد قیمت بر اساس آخرین معامله کالا.</summary>
    [HttpGet("suggest-price")]
    public async Task<ActionResult<decimal>> SuggestPrice([FromQuery] int productId, [FromQuery] TransactionType type)
        => Ok(await _service.SuggestPriceAsync(productId, type));

    /// <summary>آخرین فاکتور خریدی که شامل این کالاست.</summary>
    [HttpGet("last-purchase")]
    public async Task<ActionResult<Order>> LastPurchase([FromQuery] int productId)
    {
        if (!await CanSeeProfitAsync()) return StatusCode(403, new { message = "مشاهده قیمت خرید به مجوز مستقل مشاهده سود نیاز دارد." });
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
        return order is null ? NotFound(new { message = "سند یافت نشد." }) : Ok(await ProtectSaleProfitAsync(order));
    }

    /// <summary>ثبت سند خرید/فروش جدید.</summary>
    [HttpPost]
    public async Task<ActionResult<Order>> Create([FromBody] OrderCommand cmd)
    {
        var error = TrackingError(cmd, cmd.Type, null);
        if (error is not null) return BadRequest(new { message = error });
        // Check before the mutation: a permission-query failure after commit must
        // never look like a failed save and encourage a duplicate invoice retry.
        var revealProfit = await MayReturnSaleProfitAsync(cmd.Type);
        var saved = await _service.CreateOrderAsync(cmd, User.Identity?.Name);
        return Ok(revealProfit ? saved : RemoveSaleProfit(saved));
    }

    /// <summary>ویرایش سند خرید/فروش.</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<Order>> Update(int id, [FromBody] OrderCommand cmd)
    {
        var existing = await _service.GetOrderAsync(id);
        if (existing is null) return NotFound(new { message = "سند یافت نشد." });
        var error = TrackingError(cmd, existing.Type, existing);
        if (error is not null) return BadRequest(new { message = error });
        var revealProfit = await MayReturnSaleProfitAsync(existing.Type);
        var saved = await _service.UpdateOrderAsync(id, cmd);
        return Ok(revealProfit ? saved : RemoveSaleProfit(saved));
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

    /// <summary>پیش‌نمایش تعدیل سررسید بدون تغییر داده.</summary>
    [HttpGet("cheques/{chequeId:int}/reschedule-preview")]
    [Inventory.Api.Infrastructure.RequirePermission("Orders", "Update")]
    public async Task<ActionResult<SaleChequeReschedulePreview>> PreviewReschedule(int chequeId,
        [FromQuery] DateTime expectedDueDate, [FromQuery] DateTime newDueDate)
        => Ok(await _service.PreviewSaleChequeRescheduleAsync(chequeId, expectedDueDate, newDueDate));

    /// <summary>ثبت تغییر سررسید و تعدیل با تأیید پیش‌نمایش.</summary>
    [HttpPut("cheques/{chequeId:int}/reschedule")]
    public async Task<ActionResult<SaleChequeReschedulePreview>> Reschedule(int chequeId, [FromBody] SaleChequeRescheduleCommand cmd)
        => Ok(await _service.RescheduleSaleChequeAsync(chequeId, cmd, User.Identity?.Name));

    /// <summary>وصول چک فروش به حساب بانکی انتخاب‌شده.</summary>
    [HttpPut("cheques/{chequeId:int}/receipt")]
    public async Task<IActionResult> ReceiveCheque(int chequeId, [FromBody] SaleChequeReceiptCommand cmd)
    {
        await _service.ReceiveSaleChequeAsync(chequeId, cmd, User.Identity?.Name);
        return Ok(new { ok = true });
    }

    /// <summary>لغو وصول چک.</summary>
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

    /// <summary>تغییر وضعیت چک بدون ایجاد سود جدید.</summary>
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

    /// <summary>لغو دریافت قسط فاکتور دارای تاخیر تادیه چک با حفظ تاریخچه.</summary>
    [HttpPut("installments/{installmentId:int}/receipt-reversal")]
    [Inventory.Api.Infrastructure.RequirePermission("Orders", "Update")]
    public async Task<IActionResult> ReverseInstallmentReceipt(int installmentId, [FromBody] SaleChequeInstallmentReversalCommand cmd)
    {
        await _service.ReverseSaleChequeInstallmentReceiptAsync(installmentId, cmd, User.Identity?.Name);
        return Ok(new { ok = true });
    }

    /// <summary>ابطال اتمیک فاکتور دارای تاخیر تادیه چک با حفظ تاریخچه.</summary>
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
        if (error is not null) return BadRequest(new { message = error });
        var revealProfit = await CanSeeProfitAsync();
        var saved = await _service.EditSaleChequeOrderAsync(id, cmd, User.Identity?.Name);
        return Ok(revealProfit ? saved : RemoveSaleProfit(saved));
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
