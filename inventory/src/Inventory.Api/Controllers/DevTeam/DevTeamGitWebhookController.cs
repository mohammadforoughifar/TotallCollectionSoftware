using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inventory.Api.Services.DevTeam;
using Inventory.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Inventory.Api.Controllers.DevTeam;

/// <summary>
/// وب‌هوک گیت برای میز کار توسعه.
/// GitHub: POST /api/dev-team/hooks/github  + هدر X-Hub-Signature-256 یا X-DevTeam-Token
/// GitLab: POST /api/dev-team/hooks/gitlab  + هدر X-Gitlab-Token یا X-DevTeam-Token
///
/// در پیام کامیت بنویسید:
///   DT-1405-0001   یا  #DT-1405-0001  یا  task:12
///   mod:Office     (اختیاری — کلید ماژول)
/// </summary>
[ApiController]
[Route("api/dev-team/hooks")]
[AllowAnonymous]
public class DevTeamGitWebhookController : ControllerBase
{
    private readonly IDevTeamService _svc;
    private readonly IConfiguration _config;
    private readonly ILogger<DevTeamGitWebhookController> _log;

    public DevTeamGitWebhookController(IDevTeamService svc, IConfiguration config, ILogger<DevTeamGitWebhookController> log)
    {
        _svc = svc;
        _config = config;
        _log = log;
    }

    private string? Secret =>
        _config["DevTeam:GitWebhookSecret"]
        ?? _config["DevTeam__GitWebhookSecret"]
        ?? Environment.GetEnvironmentVariable("DEVTEAM_GIT_WEBHOOK_SECRET");

    private bool IsEnabled =>
        string.Equals(_config["DevTeam:GitWebhookEnabled"] ?? "true", "true", StringComparison.OrdinalIgnoreCase);

    private IActionResult? Guard(string? gitlabToken, string? hubSignature256, string rawBody)
    {
        if (!IsEnabled)
            return StatusCode(503, new { message = "وب‌هوک DevTeam غیرفعال است." });

        var secret = Secret;
        if (string.IsNullOrWhiteSpace(secret))
            return StatusCode(503, new { message = "DevTeam:GitWebhookSecret در تنظیمات تعریف نشده است." });

        // 1) توکن ساده مشترک
        if (Request.Headers.TryGetValue("X-DevTeam-Token", out var simple) &&
            FixedEqual(simple.ToString(), secret))
            return null;

        // 2) GitLab token
        if (!string.IsNullOrEmpty(gitlabToken) && FixedEqual(gitlabToken, secret))
            return null;

        // 3) GitHub HMAC SHA-256: sha256=hex
        if (!string.IsNullOrEmpty(hubSignature256) && hubSignature256.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            var their = hubSignature256["sha256=".Length..].Trim();
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody ?? ""));
            var mine = Convert.ToHexString(hash).ToLowerInvariant();
            if (FixedEqual(mine, their.ToLowerInvariant()))
                return null;
        }

        _log.LogWarning("DevTeam webhook: unauthorized from {IP}", HttpContext.Connection.RemoteIpAddress);
        return Unauthorized(new { message = "توکن/امضای وب‌هوک نامعتبر است." });
    }

    private static bool FixedEqual(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a ?? "");
        var bb = Encoding.UTF8.GetBytes(b ?? "");
        if (ba.Length != bb.Length) return false;
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    [HttpPost("github")]
    public async Task<IActionResult> GitHub()
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var raw = await reader.ReadToEndAsync();

        var sig = Request.Headers.TryGetValue("X-Hub-Signature-256", out var s) ? s.ToString() : null;
        var deny = Guard(null, sig, raw);
        if (deny != null) return deny;

        var ghEvent = Request.Headers.TryGetValue("X-GitHub-Event", out var ev) ? ev.ToString() : "push";
        if (string.Equals(ghEvent, "ping", StringComparison.OrdinalIgnoreCase))
            return Ok(new DtGitWebhookResultDto { Ok = true, Provider = "GitHub", Messages = { "pong" } });
        if (!string.Equals(ghEvent, "push", StringComparison.OrdinalIgnoreCase))
            return Ok(new DtGitWebhookResultDto { Ok = true, Provider = "GitHub", Messages = { $"رویداد {ghEvent} نادیده گرفته شد." } });

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        var result = await _svc.ProcessGitHubPushAsync(doc.RootElement);
        return Ok(result);
    }

    [HttpPost("gitlab")]
    public async Task<IActionResult> GitLab()
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var raw = await reader.ReadToEndAsync();

        var token = Request.Headers.TryGetValue("X-Gitlab-Token", out var t) ? t.ToString() : null;
        var deny = Guard(token, null, raw);
        if (deny != null) return deny;

        if (Request.Headers.TryGetValue("X-Gitlab-Event", out var ev) &&
            !string.IsNullOrEmpty(ev.ToString()) &&
            !ev.ToString().Contains("Push", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ev.ToString(), "Push Hook", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new DtGitWebhookResultDto { Ok = true, Provider = "GitLab", Messages = { $"رویداد {ev} نادیده گرفته شد." } });
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        var result = await _svc.ProcessGitLabPushAsync(doc.RootElement);
        return Ok(result);
    }

    /// <summary>تست سلامت وب‌هوک (نیاز به توکن).</summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        var hasSecret = !string.IsNullOrWhiteSpace(Secret);
        return Ok(new
        {
            enabled = IsEnabled,
            secretConfigured = hasSecret,
            endpoints = new[]
            {
                "POST /api/dev-team/hooks/github",
                "POST /api/dev-team/hooks/gitlab"
            },
            commitHints = new[]
            {
                "DT-1405-0001 or #DT-1405-0001 or task:12",
                "mod:Office (optional module key)"
            }
        });
    }
}
