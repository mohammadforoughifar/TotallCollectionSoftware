namespace InventoryRemoteAgent;

public sealed record CapturedFrame(byte[] Jpeg, int Width, int Height);

public interface IRemotePlatform
{
    Task<bool> RequestConsentAsync(string technicianName, int timeoutSeconds, CancellationToken ct);
    CapturedFrame CaptureFrame(int maxWidth, int jpegQuality);
    void InjectInput(string inputJson);
}
