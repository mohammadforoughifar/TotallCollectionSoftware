using Inventory.Api.Data;
using Inventory.Api.Services.Office.Email;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers.Office;

/// <summary>
/// بانک ایمیل مخاطبین دبیرخانه — ماژول مجوز مستقل: <c>EmailBank</c>
/// (در «تنظیمات ← نقش‌ها و دسترسی‌ها ← اتوماسیون اداری توکا» قابل تخصیص است)
/// • <c>EmailBank.Read</c>   : دیدن بانک و انتخاب آدرس در فرم «ارسال به کدوم ایمیل؟»
/// • <c>EmailBank.Use</c>    : ثبت آدرس‌های انتخاب/استفاده‌شده در بانک
/// • <c>EmailBank.Manage</c> : مدیریت کامل بانک (فهرست، افزودن/ویرایش، حذف، ادغام)
/// سازگاری عقب‌رو: مجوزهای قبلی (<c>OutgoingLetters.Dabirkhane</c> برای خواندن/ثبت و
/// <c>Email.Delete</c> برای مدیریت) هم پذیرفته می‌شوند تا نقش‌های موجود نشکنند.
/// </summary>
[Route("api/email-bank")]
public class EmailBankController : RbacControllerBase
{
    private const string Module = "EmailBank";
    private readonly IEmailBankService _bank;

    public EmailBankController(AppDbContext db, IEmailBankService bank) : base(db) => _bank = bank;

    /// <summary>دیدن بانک و پیشنهادها</summary>
    private async Task<bool> CanReadAsync() =>
        await HasAsync(Module, "Read") || await HasAsync(Module, "Manage")
        || await HasAsync("OutgoingLetters", "Dabirkhane")      // سازگاری عقب‌رو
        || await HasAsync("Email", "Delete");                   // مدیر ایمیل

    /// <summary>ثبت/به‌خاطر‌سپاری آدرس در جریان ارسال</summary>
    private async Task<bool> CanUseAsync() =>
        await HasAsync(Module, "Use") || await HasAsync(Module, "Manage") || await CanReadAsync();

    /// <summary>مدیریت کامل بانک (صفحهٔ تنظیمات)</summary>
    private async Task<bool> CanManageAsync() =>
        await HasAsync(Module, "Manage") || await HasAsync("Email", "Delete");

    private async Task<IActionResult?> ForbidReadAsync()
        => await CanReadAsync() ? null : StatusCode(403, new { message = "شما مجوز دیدن بانک ایمیل دبیرخانه را ندارید (EmailBank.Read)." });

    private async Task<IActionResult?> ForbidWriteAsync()
        => await CanUseAsync() ? null : StatusCode(403, new { message = "شما مجوز ثبت آدرس در بانک ایمیل را ندارید (EmailBank.Use)." });

    private async Task<IActionResult?> ForbidManageAsync()
        => await CanManageAsync() ? null : StatusCode(403, new { message = "شما مجوز مدیریت بانک ایمیل را ندارید (EmailBank.Manage)." });

    // ==================== پیشنهاد در فرم ارسال ====================

    /// <summary>
    /// پیشنهاد آدرس برای فرم «دبیرخانه یکپارچه».
    /// organization = سازمان گیرندهٔ نامه ⇒ آدرس‌های همان سازمان اول برمی‌گردند.
    /// </summary>
    [HttpGet("suggest")]
    public async Task<IActionResult> Suggest([FromQuery] string? term, [FromQuery] string? organization,
        [FromQuery] int take = 12)
    {
        if (await ForbidReadAsync() is { } forbid) return forbid;
        return Ok(await _bank.SuggestAsync(term, organization, take));
    }

    /// <summary>آیا این آدرس از قبل در بانک هست؟ (پیام تکراری پیش از ذخیره)</summary>
    [HttpGet("find")]
    public async Task<IActionResult> Find([FromQuery] string email)
    {
        if (await ForbidReadAsync() is { } forbid) return forbid;
        var entry = await _bank.FindAsync(email);
        return Ok(new { found = entry is not null, entry });
    }

    /// <summary>
    /// به‌خاطر‌سپاری آدرس انتخاب‌شده در بانک.
    /// تکراری ⇒ <c>duplicate = true</c> و پیام «قبلاً ثبت شده» (ردیف دوم ساخته نمی‌شود).
    /// </summary>
    [HttpPost("remember")]
    public async Task<IActionResult> Remember([FromBody] RememberEmailBankDto dto)
    {
        if (await ForbidWriteAsync() is { } forbid) return forbid;
        return Ok(await _bank.RememberAsync(dto.Email, dto.Organization, dto.PersonName, MyUserId, MyUsername));
    }

    // ==================== مدیریت بانک ====================

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] bool? isActive,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        if (await ForbidManageAsync() is { } forbid) return forbid;
        return Ok(await _bank.ListAsync(search, isActive, page, pageSize));
    }

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SaveEmailBankEntryDto dto)
    {
        if (await ForbidManageAsync() is { } forbid) return forbid;
        return Ok(await _bank.SaveAsync(dto, MyUserId, MyUsername));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbidManageAsync() is { } forbid) return forbid;
        await _bank.DeleteAsync(id);
        return Ok(new { message = "ردیف بانک ایمیل حذف شد." });
    }

    /// <summary>ادغام دو ردیف تکراری در یک آدرس</summary>
    [HttpPost("merge")]
    public async Task<IActionResult> Merge([FromBody] MergeEmailBankDto dto)
    {
        if (await ForbidManageAsync() is { } forbid) return forbid;
        var ok = await _bank.MergeAsync(dto.KeepId, dto.RemoveId);
        return ok
            ? Ok(new { message = "ردیف‌ها ادغام شدند." })
            : BadRequest(new { message = "ادغام انجام نشد — دو ردیف معتبر و متفاوت انتخاب کنید." });
    }
}

/// <summary>درخواست به‌خاطر‌سپاری آدرس از فرم ارسال</summary>
public class RememberEmailBankDto
{
    public string Email { get; set; } = "";
    public string? Organization { get; set; }
    public string? PersonName { get; set; }
}

/// <summary>درخواست ادغام دو ردیف تکراری بانک</summary>
public class MergeEmailBankDto
{
    public int KeepId { get; set; }
    public int RemoveId { get; set; }
}
