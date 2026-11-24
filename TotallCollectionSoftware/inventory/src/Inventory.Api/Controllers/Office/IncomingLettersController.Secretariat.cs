using Inventory.Api.Services;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

public partial class IncomingLettersController
{
    // The unified secretariat is gated by OutgoingLetters.Dabirkhane in the menu.
    // Retain the existing incoming-secretariat/admin permission as well. A generic
    // IncomingLetters.Read permission never grants access to this shared archive.
    private async Task<bool> HasSecretariatAccessAsync() =>
        await HasDabirkhaneAsync() || await HasAsync("OutgoingLetters", "Dabirkhane");

    private async Task<IActionResult> SecretariatResultAsync(Func<Task<object>> action)
    {
        if (!await HasSecretariatAccessAsync())
            return StatusCode(403, new { message = "شما به دبیرخانه نامه وارده دسترسی ندارید." });
        try { return Ok(await action()); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet("dabirkhane")]
    public Task<IActionResult> SecretariatLetters([FromServices] IIncomingSecretariatArchiveService archive,
        [FromQuery] bool archived = false, [FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 15) =>
        SecretariatResultAsync(async () => await archive.GetIncomingSecretariatAsync(archived, search, page, pageSize));

    [HttpGet("dabirkhane/bayegani/tree")]
    public Task<IActionResult> SecretariatTree([FromServices] IIncomingSecretariatArchiveService archive,
        [FromQuery] int skip = 0, [FromQuery] int? take = null) =>
        SecretariatResultAsync(async () => await Paging.ResultAsync(archive.GetIncomingSecretariatTreeAsync, skip, take));

    [HttpPost("dabirkhane/bayegani/main-category")]
    public Task<IActionResult> AddSecretariatMainCategory([FromServices] IIncomingSecretariatArchiveService archive,
        [FromBody] SaveBayeganiFolderDto dto) =>
        SecretariatResultAsync(async () => await archive.AddIncomingSecretariatFolderAsync(MyUserId, dto, rootOnly: true));

    [HttpPost("dabirkhane/bayegani/sub-category")]
    public Task<IActionResult> AddSecretariatSubCategory([FromServices] IIncomingSecretariatArchiveService archive,
        [FromBody] SaveBayeganiFolderDto dto) =>
        SecretariatResultAsync(async () => await archive.AddIncomingSecretariatFolderAsync(MyUserId, dto, rootOnly: false));

    [HttpPut("dabirkhane/bayegani/folder/{id:int}")]
    public Task<IActionResult> EditSecretariatFolder([FromServices] IIncomingSecretariatArchiveService archive,
        int id, [FromBody] SaveBayeganiFolderDto dto) =>
        SecretariatResultAsync(async () => await archive.EditIncomingSecretariatFolderAsync(id, dto));

    [HttpPost("dabirkhane/bayegani/folder/{id:int}/move")]
    public Task<IActionResult> MoveSecretariatFolder([FromServices] IIncomingSecretariatArchiveService archive,
        int id, [FromQuery] int newParentId) =>
        SecretariatResultAsync(async () => await archive.MoveIncomingSecretariatFolderAsync(id, newParentId));

    [HttpDelete("dabirkhane/bayegani/{id:int}")]
    public Task<IActionResult> DeleteSecretariatNode([FromServices] IIncomingSecretariatArchiveService archive, int id) =>
        SecretariatResultAsync(async () =>
        {
            await archive.DeleteIncomingSecretariatNodeAsync(id);
            return new { message = "از بایگانی مشترک دبیرخانه حذف شد." };
        });

    [HttpPost("dabirkhane/bayegani/letters")]
    public Task<IActionResult> ArchiveSecretariatLetters([FromServices] IIncomingSecretariatArchiveService archive,
        [FromBody] ArchiveSecretariatLettersDto dto) =>
        SecretariatResultAsync(async () =>
        {
            await archive.ArchiveIncomingSecretariatLettersAsync(MyUserId, dto);
            return new { message = "نامه‌ها به بایگانی مشترک دبیرخانه اضافه شدند." };
        });

    [HttpDelete("dabirkhane/bayegani/letter/{letterId:int}")]
    public Task<IActionResult> UnarchiveSecretariatLetter([FromServices] IIncomingSecretariatArchiveService archive, int letterId) =>
        SecretariatResultAsync(async () =>
        {
            await archive.UnarchiveIncomingSecretariatLetterAsync(letterId);
            return new { message = "نامه از بایگانی مشترک دبیرخانه خارج شد." };
        });

    [HttpPost("dabirkhane/bayegani/move-letter")]
    public Task<IActionResult> MoveSecretariatLetter([FromServices] IIncomingSecretariatArchiveService archive,
        [FromBody] MoveArchivedLetterDto dto) =>
        SecretariatResultAsync(async () => await archive.MoveIncomingSecretariatLetterAsync(dto));
}
