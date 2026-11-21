using System.Security.Claims;
using Inventory.Api.Services.Backup;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers.Backup;

[ApiController]
[Route("api/backup/session")]
[Authorize]
public sealed class BackupSessionController : ControllerBase
{
    private readonly IBackupSessionService _sessions;
    public BackupSessionController(IBackupSessionService sessions) => _sessions = sessions;

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] BackupSessionHeartbeatDto dto)
    {
        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
        var name = User.FindFirstValue(ClaimTypes.Name) ?? "";
        await _sessions.HeartbeatAsync(dto.SessionId, userId, name, Request.Headers.UserAgent.ToString());
        return Ok(new { ok = true });
    }

    [HttpPost("close")]
    public async Task<IActionResult> Close([FromBody] BackupSessionHeartbeatDto dto)
    {
        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
        await _sessions.CloseAsync(dto.SessionId, userId, "browser");
        return Ok(new { ok = true });
    }
}
