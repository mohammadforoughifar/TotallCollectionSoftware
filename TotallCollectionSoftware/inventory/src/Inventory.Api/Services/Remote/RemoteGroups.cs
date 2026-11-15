namespace Inventory.Api.Services.Remote;

public static class RemoteGroups
{
    public static string Device(string deviceId) => $"RemoteDevice_{deviceId}";
    public static string Session(string sessionId) => $"RemoteSession_{sessionId}";
}
