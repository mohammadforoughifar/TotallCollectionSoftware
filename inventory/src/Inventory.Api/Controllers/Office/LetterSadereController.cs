using Inventory.Api.Data;
using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Controllers;

/// <summary>
/// اتوماسیون اداری — نامه صادره (نامه به خارج از سازمان)
/// ماژول دسترسی: InnerLetters (هم‌گروه نامه داخلی)
/// </summary>
[Route("api/letters/sadere")]
public class LetterSadereController : RbacControllerBase
{
    private const string Module = "InnerLetters";

    private readonly ILetterSadereService _sadere;

    public LetterSadereController(AppDbContext db, ILetterSadereService sadere)
        : base(db)
    {
        _sadere = sadere;
    }

    private async Task<bool> IsAdminAsync() => await HasAsync(Module, "Delete");
    private async Task<string> MyDisplayNameAsync()
    {
        var u = await Db.Users.AsNoTracking()
            .Where(x => x.Id == MyUserId)
            .Select(x => new { x.FirstName, x.LastName, x.Username })
            .FirstOrDefaultAsync();
        if (u == null) return MyUsername;
        var full = $"{u.FirstName} {u.LastName}".Trim();
        return string.IsNullOrWhiteSpace(full) ? u.Username : full;
    }

    // ==================== ایجاد ====================

    /// <summary>ایجاد نامه صادره جدید</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AddLetterSadereDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        var result = await _sadere.CreateAsync(dto, MyUserId, await MyDisplayNameAsync());
        return StatusCode(result.StatusCode, result);
    }

    // ==================== ویرایش ====================

    /// <summary>ویرایش نامه صادره</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Edit(int id, [FromBody] EditLetterSadereDto dto)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;
        var result = await _sadere.EditAsync(id, dto, MyUserId, await IsAdminAsync());
        return StatusCode(result.StatusCode, result);
    }

    // ==================== حذف ====================

    /// <summary>حذف منطقی نامه صادره</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Delete") is { } forbid) return forbid;
        var result = await _sadere.DeleteAsync(id, MyUserId, await IsAdminAsync());
        return StatusCode(result.StatusCode, result);
    }

    // ==================== لیست ====================

    /// <summary>لیست نامه‌های صادره</summary>
    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] string? search, [FromQuery] bool? archived)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        return Ok(await _sadere.GetListAsync(search, archived));
    }

    // ==================== جزئیات ====================

    /// <summary>جزئیات کامل نامه صادره</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetDetail(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Read") is { } forbid) return forbid;
        var detail = await _sadere.GetDetailAsync(id);
        if (detail == null) return NotFound(new { message = "نامه صادره یافت نشد." });
        return Ok(detail);
    }

    // ==================== بایگانی/خروج از بایگانی ====================

    /// <summary>تغییر وضعیت بایگانی نامه صادره</summary>
    [HttpPatch("{id:int}/archive")]
    public async Task<IActionResult> ToggleArchive(int id, [FromBody] bool archive)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;

        var letter = await Db.Letter_Saderes.FirstOrDefaultAsync(s => s.SadereLetterId == id && !s.IsDeleted);
        if (letter == null)
            return NotFound(new { message = "نامه صادره یافت نشد." });

        if (letter.CreatorUserId != MyUserId && !await IsAdminAsync())
            return Forbid();

        letter.IsArchived = archive;
        letter.UpdatedAt = DateTime.Now;
        await Db.SaveChangesAsync();

        return Ok(new { message = archive ? "نامه بایگانی شد." : "نامه از بایگانی خارج شد." });
    }

    /// <summary>علامت‌گذاری به عنوان ارسال شده</summary>
    [HttpPatch("{id:int}/mark-sent")]
    public async Task<IActionResult> MarkAsSent(int id)
    {
        if (await ForbiddenUnlessAsync(Module, "Create") is { } forbid) return forbid;

        var letter = await Db.Letter_Saderes.FirstOrDefaultAsync(s => s.SadereLetterId == id && !s.IsDeleted);
        if (letter == null)
            return NotFound(new { message = "نامه صادره یافت نشد." });

        if (letter.CreatorUserId != MyUserId && !await IsAdminAsync())
            return Forbid();

        letter.IsSent = true;
        letter.DateErsal ??= DateTime.Now;
        letter.UpdatedAt = DateTime.Now;
        await Db.SaveChangesAsync();

        return Ok(new { message = "نامه به عنوان ارسال‌شده علامت خورد." });
    }
}