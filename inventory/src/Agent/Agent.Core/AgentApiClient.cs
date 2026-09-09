using System.Text;
using System.Text.Json;

namespace Agent.Core;

/// <summary>
/// کلاینت ارتباط ایجنت با سرور شناسنامه سیستم (api/SystemInfo).
/// ارسال مشخصات سخت‌افزار + دریافت و گزارش نتیجه‌ی دستورهای از راه دور.
/// </summary>
public class AgentApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _api;
    private static readonly JsonSerializerOptions CamelJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    /// <param name="apiBaseUrl">آدرس سرور API — مثال: http://192.168.1.10:5100</param>
    /// <param name="timeoutSeconds">مهلت هر درخواست (ثانیه)</param>
    public AgentApiClient(string apiBaseUrl, int timeoutSeconds = 15)
    {
        _api = (apiBaseUrl ?? "http://localhost:5100").Trim().TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
    }

    public string ApiBaseUrl => _api;

    /// <summary>ارسال مشخصات سخت‌افزار به سرور (upsert در سمت سرور).</summary>
    public async Task<SendResult> SendInfoAsync(SystemInfoData info, CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(info, CamelJson);
            var resp = await _http.PostAsync(_api + "/api/SystemInfo",
                new StringContent(json, Encoding.UTF8, "application/json"), ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return new SendResult
            {
                Success = resp.IsSuccessStatusCode,
                StatusCode = (int)resp.StatusCode,
                ResponseBody = body
            };
        }
        catch (Exception ex)
        {
            return new SendResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>دریافت دستورهای از راه دور در انتظار این سیستم (Reboot | Shutdown | Lock).</summary>
    public async Task<List<AgentCommand>> GetPendingCommandsAsync(string agentId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync($"{_api}/api/SystemInfo/agent-commands?agentId={Uri.EscapeDataString(agentId)}", ct);
            if (!resp.IsSuccessStatusCode) return new List<AgentCommand>();
            var cmds = JsonSerializer.Deserialize<List<AgentCommand>>(await resp.Content.ReadAsStringAsync(ct), CaseInsensitive);
            return cmds ?? new List<AgentCommand>();
        }
        catch
        {
            return new List<AgentCommand>();
        }
    }

    /// <summary>گزارش نتیجه‌ی اجرای یک دستور از راه دور به سرور.</summary>
    public async Task ReportCommandResultAsync(int commandId, bool ok, string message, CancellationToken ct = default)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { ok, message });
            await _http.PostAsync($"{_api}/api/SystemInfo/commands/{commandId}/result",
                new StringContent(payload, Encoding.UTF8, "application/json"), ct);
        }
        catch { }
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>اجرای دستورهای از راه دور روی سیستم (فقط ویندوز).</summary>
public static class RemoteCommandExecutor
{
    /// <summary>اجرای یک دستور: Reboot | Shutdown | Lock — خروجی: (موفق؟، پیام).</summary>
    public static (bool Ok, string Message) Execute(string action)
    {
        if (!OperatingSystem.IsWindows())
            return (false, "فقط روی ویندوز قابل اجرا است.");

        return action switch
        {
            "Reboot" => RunCmd("shutdown", "/r /t 5 /c \"ری‌استارت از سامانه انبار\""),
            "Shutdown" => RunCmd("shutdown", "/s /t 5 /c \"خاموش‌شدن از سامانه انبار\""),
            "Lock" => RunCmd("rundll32.exe", "user32.dll,LockWorkStation"),
            _ => (false, "عملیات نامعتبر")
        };
    }

    private static (bool, string) RunCmd(string exe, string args)
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(exe, args) { UseShellExecute = false });
            return (true, $"دستور ارسال شد: {exe}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
