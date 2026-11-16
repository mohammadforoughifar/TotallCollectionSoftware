using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Inventory.Api.Services.ItAssets;

/// <summary>Bounded, scoped identifiers and content fingerprints for idempotent requests.</summary>
public static class WorkOrderRequestIdentity
{
    public static string Key(string operation, int companyId, int userId, int orderId, string clientId) =>
        Hash($"{operation}|{companyId}|{userId}|{orderId}|{clientId.Trim()}");

    public static string Fingerprint<T>(string operation, T value) =>
        Hash(operation + "|" + JsonSerializer.Serialize(value));

    public static bool SamePayload(string? stored, string? incoming) =>
        stored is not null && incoming is not null && string.Equals(stored, incoming, StringComparison.Ordinal);

    public static bool ReplyIsUnchanged(DateTime? repliedAt, bool? recordedDone, string? recordedText,
        bool requestedDone, string? requestedText) => repliedAt is not null && recordedDone == requestedDone &&
        string.Equals(recordedText ?? "", requestedText?.Trim() ?? "", StringComparison.Ordinal);

    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
