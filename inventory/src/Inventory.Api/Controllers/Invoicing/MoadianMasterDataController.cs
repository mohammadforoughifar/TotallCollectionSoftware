using System.Net;
using Inventory.Api.Controllers;
using Inventory.Api.Data;
using Inventory.Api.Services.Invoicing;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers.Invoicing;

/// <summary>
/// Provider-scoped Moadian master-data CRUD and protected private-key management.
/// Connection testing is temporarily disabled; invoice intake, queue, and sender routes are untouched.
/// </summary>
[Route("api/moadian/master-data")]
public sealed class MoadianMasterDataController : RbacControllerBase
{
    private readonly IMoadianMasterDataService _service;
    private readonly IMoadianProviderPrivateKeyService _privateKeys;
    private readonly IMoadianConnectionTestService _connectionTest;

    public MoadianMasterDataController(
        AppDbContext db,
        IMoadianMasterDataService service,
        IMoadianProviderPrivateKeyService privateKeys,
        IMoadianConnectionTestService connectionTest) : base(db)
        => (_service, _privateKeys, _connectionTest) = (service, privateKeys, connectionTest);

    [HttpGet("providers")]
    public async Task<ActionResult<List<MoadianServiceProviderProfileDto>>> GetProviders()
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Read", "MasterData", "InvoiceNew", "FiscalYears", "ProviderInvoices") is ObjectResult forbidden) return forbidden;
        return Ok(await _service.GetProvidersAsync());
    }

    [HttpPost("providers")]
    public async Task<ActionResult<MoadianServiceProviderProfileDto>> CreateProvider([FromBody] MoadianServiceProviderProfileDto dto)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Create", "MasterData") is ObjectResult forbidden) return forbidden;
        return await WriteAsync(() => _service.CreateProviderAsync(dto));
    }

    [HttpPut("providers/{id:int}")]
    public async Task<ActionResult<MoadianServiceProviderProfileDto>> UpdateProvider(int id, [FromBody] MoadianServiceProviderProfileDto dto)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Update", "MasterData") is ObjectResult forbidden) return forbidden;
        return await WriteAsync(() => _service.UpdateProviderAsync(id, dto));
    }

    [HttpDelete("providers/{id:int}")]
    public async Task<IActionResult> DeleteProvider(int id)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Delete", "MasterData") is ObjectResult forbidden) return forbidden;
        return await WriteAsync(() => _service.SetProviderDeletedAsync(id, true));
    }

    [HttpPost("providers/{id:int}/restore")]
    public async Task<IActionResult> RestoreProvider(int id)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Update", "MasterData") is ObjectResult forbidden) return forbidden;
        return await WriteAsync(() => _service.SetProviderDeletedAsync(id, false));
    }

    // هر خدمات‌دهنده فقط یک اتصال دارد؛ مدیریت اتصال (تعریف/ویرایش/حذف/غیرفعال‌سازی/کلید خصوصی) فقط برای Admin.
    [HttpGet("connections")]
    public async Task<ActionResult<List<MoadianProviderConnectionProfileDto>>> GetConnections([FromQuery] int? serviceProviderId)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Read", "MasterData", "InvoiceNew", "FiscalYears", "ProviderInvoices") is ObjectResult forbidden) return forbidden;
        return Ok(await _service.GetConnectionsAsync(serviceProviderId));
    }

    [HttpPost("connections")]
    public async Task<ActionResult<MoadianProviderConnectionProfileDto>> CreateConnection([FromBody] MoadianProviderConnectionProfileDto dto)
    {
        if (await ForbiddenUnlessAdminAsync() is ObjectResult forbidden) return forbidden;
        return await WriteAsync(() => _service.CreateConnectionAsync(dto));
    }

    [HttpPut("connections/{id:int}")]
    public async Task<ActionResult<MoadianProviderConnectionProfileDto>> UpdateConnection(int id, [FromBody] MoadianProviderConnectionProfileDto dto)
    {
        if (await ForbiddenUnlessAdminAsync() is ObjectResult forbidden) return forbidden;
        return await WriteAsync(() => _service.UpdateConnectionAsync(id, dto));
    }

    [HttpDelete("connections/{id:int}")]
    public async Task<IActionResult> DeleteConnection(int id)
    {
        if (await ForbiddenUnlessAdminAsync() is ObjectResult forbidden) return forbidden;
        return await WriteAsync(() => _service.SetConnectionDeletedAsync(id, true));
    }

    [HttpPost("connections/{id:int}/restore")]
    public async Task<IActionResult> RestoreConnection(int id)
    {
        if (await ForbiddenUnlessAdminAsync() is ObjectResult forbidden) return forbidden;
        return await WriteAsync(() => _service.SetConnectionDeletedAsync(id, false));
    }

    [HttpPost("connections/{id:int}/private-key")]
    [RequestSizeLimit(80L * 1024L)]
    [RequestFormLimits(MultipartBodyLengthLimit = 80L * 1024L)]
    public async Task<IActionResult> UploadPrivateKey(
        int id,
        [FromForm] IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (await ForbiddenUnlessAdminAsync() is { } forbidden) return forbidden;
        Response.Headers.CacheControl = "no-store";
        var remoteAddress = HttpContext.Connection.RemoteIpAddress;
        if (!Request.IsHttps && (remoteAddress is null || !IPAddress.IsLoopback(remoteAddress)))
            return BadRequest(new { message = "برای بارگذاری کلید خصوصی، HTTPS لازم است (HTTP فقط از loopback مجاز است)." });
        if (file is null)
            return BadRequest(new { message = "یک فایل کلید خصوصی PKCS#8 با پسوند .key انتخاب کنید." });
        try
        {
            return Ok(await _privateKeys.UploadAsync(id, file, cancellationToken));
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (DbUpdateException) { return Conflict(new { message = "ثبت مسیر امن کلید در پایگاه‌داده ناموفق بود." }); }
    }

    [HttpPost("connections/{id:int}/test-connection")]
    public async Task<IActionResult> TestConnection(int id, CancellationToken cancellationToken)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Read", "MasterData", "InvoiceNew", "FiscalYears", "ProviderInvoices") is { } forbidden) return forbidden;
        Response.Headers.CacheControl = "no-store";
        try
        {
            return Ok(await _connectionTest.TestAsync(id, cancellationToken));
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (OperationCanceledException) { return StatusCode(499, new { message = "تست اتصال پیش از پایان لغو شد." }); }
    }

    [HttpDelete("connections/{id:int}/private-key")]
    public async Task<IActionResult> RemovePrivateKey(int id, CancellationToken cancellationToken)
    {
        if (await ForbiddenUnlessAdminAsync() is { } forbidden) return forbidden;
        Response.Headers.CacheControl = "no-store";
        try
        {
            return Ok(await _privateKeys.RemoveAsync(id, cancellationToken));
        }
        catch (InvalidOperationException ex) { return NotFound(new { message = ex.Message }); }
        catch (DbUpdateException) { return Conflict(new { message = "حذف ارجاع کلید از پایگاه‌داده ناموفق بود." }); }
    }

    /// <summary>
    /// حذف قطعی اتصال: ابتدا فایل کلید خصوصی و ارجاع آن پاک می‌شود و سپس سطر اتصال از پایگاه‌داده حذف می‌گردد.
    /// حذف فایل (در صورت نبودن/قفل بودن) مانع حذف رکورد نمی‌شود.
    /// </summary>
    [HttpDelete("connections/{id:int}/permanent")]
    public async Task<IActionResult> DeleteConnectionPermanently(int id, CancellationToken cancellationToken)
    {
        if (await ForbiddenUnlessAdminAsync() is ObjectResult forbidden) return forbidden;

        var connection = await Db.MoadianProviderConnections.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (connection is null) return NotFound(new { message = "اتصال خدمات‌دهنده یافت نشد." });

        if (!string.IsNullOrWhiteSpace(connection.PrivateKeyPath))
        {
            try { await _privateKeys.RemoveAsync(id, cancellationToken); }
            catch (InvalidOperationException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return await WriteAsync(() => _service.DeleteConnectionAsync(id));
    }

    [HttpGet("customers")]
    public async Task<ActionResult<List<MoadianCustomerProfileDto>>> GetCustomers([FromQuery] int? serviceProviderId)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Read", "MasterData", "InvoiceNew", "FiscalYears", "ProviderInvoices") is ObjectResult forbidden) return forbidden;
        return Ok(await _service.GetCustomersAsync(serviceProviderId));
    }

    [HttpPost("customers")]
    public async Task<ActionResult<MoadianCustomerProfileDto>> CreateCustomer([FromBody] MoadianCustomerProfileDto dto)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Create", "MasterData") is ObjectResult forbidden) return forbidden;
        return await WriteAsync(() => _service.CreateCustomerAsync(dto));
    }

    [HttpPut("customers/{id:int}")]
    public async Task<ActionResult<MoadianCustomerProfileDto>> UpdateCustomer(int id, [FromBody] MoadianCustomerProfileDto dto)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Update", "MasterData") is ObjectResult forbidden) return forbidden;
        return await WriteAsync(() => _service.UpdateCustomerAsync(id, dto));
    }

    [HttpDelete("customers/{id:int}")]
    public async Task<IActionResult> DeleteCustomer(int id)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Delete", "MasterData") is ObjectResult forbidden) return forbidden;
        return await WriteAsync(() => _service.SetCustomerDeletedAsync(id, true));
    }

    [HttpPost("customers/{id:int}/restore")]
    public async Task<IActionResult> RestoreCustomer(int id)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Update", "MasterData") is ObjectResult forbidden) return forbidden;
        return await WriteAsync(() => _service.SetCustomerDeletedAsync(id, false));
    }

    [HttpGet("goods-services")]
    public async Task<ActionResult<List<MoadianGoodsOrServiceProfileDto>>> GetGoodsOrServices([FromQuery] int? serviceProviderId)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Read", "MasterData", "InvoiceNew", "FiscalYears", "ProviderInvoices") is ObjectResult forbidden) return forbidden;
        return Ok(await _service.GetGoodsOrServicesAsync(serviceProviderId));
    }

    [HttpPost("goods-services")]
    public async Task<ActionResult<MoadianGoodsOrServiceProfileDto>> CreateGoodsOrService([FromBody] MoadianGoodsOrServiceProfileDto dto)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Create", "MasterData") is ObjectResult forbidden) return forbidden;
        return await WriteAsync(() => _service.CreateGoodsOrServiceAsync(dto));
    }

    [HttpPut("goods-services/{id:int}")]
    public async Task<ActionResult<MoadianGoodsOrServiceProfileDto>> UpdateGoodsOrService(int id, [FromBody] MoadianGoodsOrServiceProfileDto dto)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Update", "MasterData") is ObjectResult forbidden) return forbidden;
        return await WriteAsync(() => _service.UpdateGoodsOrServiceAsync(id, dto));
    }

    [HttpDelete("goods-services/{id:int}")]
    public async Task<IActionResult> DeleteGoodsOrService(int id)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Delete", "MasterData") is ObjectResult forbidden) return forbidden;
        return await WriteAsync(() => _service.SetGoodsOrServiceDeletedAsync(id, true));
    }

    [HttpPost("goods-services/{id:int}/restore")]
    public async Task<IActionResult> RestoreGoodsOrService(int id)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Update", "MasterData") is ObjectResult forbidden) return forbidden;
        return await WriteAsync(() => _service.SetGoodsOrServiceDeletedAsync(id, false));
    }

    /// <summary>ورود گروهی «کالا و خدمت» از فایل اکسل (xlsx).</summary>
    [HttpPost("goods-services/import")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<ExcelImportResult>> ImportGoodsOrServices(IFormFile? file)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Create", "MasterData") is ObjectResult forbidden) return forbidden;
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "فایل اکسل انتخاب نشده است." });
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "فقط فایل با فرمت xlsx پشتیبانی می‌شود." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;

        try
        {
            return Ok(await _service.ImportGoodsOrServicesAsync(ms));
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (DbUpdateException) { return Conflict(new { message = "ثبت داده‌های اکسل در پایگاه‌داده ناموفق بود." }); }
    }

    /// <summary>دانلود فایل اکسل نمونهٔ «کالا و خدمت» (سطرهای نمونه + راهنما + فهرست واحدها).</summary>
    [HttpGet("goods-services/import/template")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadGoodsOrServicesTemplate()
    {
        var bytes = await _service.BuildGoodsOrServicesTemplateAsync();
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileDownloadName: "Moadian-GoodsOrServices-Template.xlsx");
    }

    [HttpGet("units")]
    public async Task<ActionResult<List<MoadianUnitOfMeasurementDto>>> GetUnits()
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Read", "MasterData", "InvoiceNew", "FiscalYears", "ProviderInvoices") is ObjectResult forbidden) return forbidden;
        return Ok(await _service.GetUnitsAsync());
    }

    [HttpPost("units")]
    public async Task<ActionResult<MoadianUnitOfMeasurementDto>> CreateUnit([FromBody] MoadianUnitOfMeasurementDto dto)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Create", "MasterData") is ObjectResult forbidden) return forbidden;
        return await WriteAsync(() => _service.CreateUnitAsync(dto));
    }

    [HttpPut("units/{id:int}")]
    public async Task<ActionResult<MoadianUnitOfMeasurementDto>> UpdateUnit(int id, [FromBody] MoadianUnitOfMeasurementDto dto)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Update", "MasterData") is ObjectResult forbidden) return forbidden;
        return await WriteAsync(() => _service.UpdateUnitAsync(id, dto));
    }

    [HttpDelete("units/{id:int}")]
    public async Task<IActionResult> DeleteUnit(int id)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Delete", "MasterData") is ObjectResult forbidden) return forbidden;
        return await WriteAsync(() => _service.SetUnitDeletedAsync(id, true));
    }

    [HttpPost("units/{id:int}/restore")]
    public async Task<IActionResult> RestoreUnit(int id)
    {
        if (await ForbiddenUnlessFormAsync("Moadian", "Update", "MasterData") is ObjectResult forbidden) return forbidden;
        return await WriteAsync(() => _service.SetUnitDeletedAsync(id, false));
    }

    private async Task<ActionResult<T>> WriteAsync<T>(Func<Task<T>> operation)
    {
        try { return Ok(await operation()); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (DbUpdateException) { return Conflict(new { message = "تغییر با دادهٔ موجود تداخل دارد؛ فهرست را تازه کنید و دوباره تلاش کنید." }); }
    }

    private async Task<IActionResult> WriteAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return NoContent();
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (DbUpdateException) { return Conflict(new { message = "تغییر با دادهٔ موجود تداخل دارد؛ فهرست را تازه کنید و دوباره تلاش کنید." }); }
    }
}
