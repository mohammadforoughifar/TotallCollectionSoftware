using System.Text.Json;

namespace InventoryRemoteAgent;

public sealed class RemoteAgentConfig
{
    public string ServerUrl { get; set; } = "";
    public string DeviceKey { get; set; } = "";
    public string? DeviceName { get; set; }
    public bool Insecure { get; set; }

    public static RemoteAgentConfig Load(string path)
    {
        if (!File.Exists(path)) return new RemoteAgentConfig();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<RemoteAgentConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new RemoteAgentConfig();
        }
        catch { return new RemoteAgentConfig(); }
    }

    public static void WriteSample(string path)
    {
        var sample = new RemoteAgentConfig
        {
            ServerUrl = "https://central.example.com",
            DeviceKey = "rmd_REPLACE_WITH_DEVICE_KEY",
            DeviceName = "",
            Insecure = false
        };
        File.WriteAllText(path, JsonSerializer.Serialize(sample, new JsonSerializerOptions { WriteIndented = true }));
    }
}
