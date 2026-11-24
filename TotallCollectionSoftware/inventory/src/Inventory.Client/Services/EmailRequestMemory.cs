using System.Text.Json;
using Inventory.Shared.Dtos;
using Microsoft.JSInterop;

namespace Inventory.Client.Services;

/// <summary>
/// Session-only recovery hint, scoped to the signed-in login and form. Never stores a body,
/// attachment, address or credential. The authorized server ledger remains authoritative.
/// This is not an SMTP queue and never initiates a send.
/// </summary>
public static class EmailRequestMemory
{
    private static string Key(int userId, string scope) => $"email-delivery:v1:{userId}:{scope}";
    public static async Task<EmailDeliveryResultDto?> RestoreAsync(IJSRuntime js, int userId, string scope)
    {
        try
        {
            var json = await js.InvokeAsync<string?>("sessionStorage.getItem", Key(userId, scope));
            var result = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<EmailDeliveryResultDto>(json);
            return result?.RequestId != Guid.Empty && result != null && Enum.IsDefined(result.Status) ? result : null;
        }
        catch (Exception) { return null; } // Browsers can disable storage; server history is still available.
    }
    public static async Task<bool> RememberAsync(IJSRuntime js, int userId, string scope, EmailDeliveryResultDto result)
    {
        try
        {
            if (result.ResultPersisted && (result.SmtpAccepted || result.Status == EmailDeliveryStatus.Failed))
                await js.InvokeVoidAsync("sessionStorage.removeItem", Key(userId, scope));
            else
                await js.InvokeVoidAsync("sessionStorage.setItem", Key(userId, scope), JsonSerializer.Serialize(result));
            return true;
        }
        catch (Exception) { return false; }
    }
    public static async Task ForgetAsync(IJSRuntime js, int userId, string scope)
    {
        try { await js.InvokeVoidAsync("sessionStorage.removeItem", Key(userId, scope)); }
        catch (Exception) { }
    }
}
