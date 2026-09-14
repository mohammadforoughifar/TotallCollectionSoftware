using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
using WebPush;
using System.Security.Cryptography;

namespace Inventory.Api.Services;

public interface IPushService
{
    Task SaveSubscriptionAsync(int userId, string endpoint, string p256dh, string auth, string? userAgent);
    Task RemoveSubscriptionAsync(int userId, string endpoint);
    Task SendToUserAsync(int userId, string title, string? body, string? link);
    Task SendToUsersAsync(IEnumerable<int> userIds, string title, string? body, string? link);
    string VapidPublicKey { get; }
    bool IsConfigured { get; }
}

public static class PushEndpointPolicy
{
    public static bool ValidEndpoint(string endpoint)
    {
        if (endpoint.Length > 500 || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0) return false;
        return (uri.Host == "fcm.googleapis.com" && (uri.AbsolutePath.StartsWith("/fcm/send/") || uri.AbsolutePath.StartsWith("/wp/")))
            || (uri.Host == "updates.push.services.mozilla.com" && uri.AbsolutePath.StartsWith("/wpush/"))
            || (uri.Host == "web.push.apple.com" && uri.AbsolutePath.StartsWith("/Q"));
    }
    public static byte[] Decode(string text) => Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/') + new string('=', (4 - text.Length % 4) % 4));
    public static bool ValidKeys(string publicKey, string auth)
    {
        try
        {
            var key = Decode(publicKey);
            if (key.Length != 65 || key[0] != 4 || Decode(auth).Length != 16) return false;
            using var ec = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = key[1..33], Y = key[33..65] } });
            return true;
        }
        catch { return false; }
    }
}

public sealed class PushSettings
{
    public string PublicKey { get; }
    public string PrivateKey { get; }
    public string Subject { get; }
    public bool IsConfigured { get; }
    public PushSettings(IConfiguration cfg)
    {
        PublicKey = Environment.GetEnvironmentVariable("VAPID_PUBLIC_KEY") ?? cfg["PushNotifications:VapidPublicKey"] ?? "";
        PrivateKey = Environment.GetEnvironmentVariable("VAPID_PRIVATE_KEY") ?? cfg["PushNotifications:VapidPrivateKey"] ?? "";
        Subject = Environment.GetEnvironmentVariable("VAPID_SUBJECT") ?? cfg["PushNotifications:VapidSubject"] ?? "";
        try
        {
            var pub = PushEndpointPolicy.Decode(PublicKey); var secret = PushEndpointPolicy.Decode(PrivateKey);
            if (pub.Length != 65 || pub[0] != 4 || secret.Length != 32 || !(Subject.StartsWith("mailto:") || Subject.StartsWith("https://"))) return;
            using var signing = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = secret });
            var derived = signing.ExportParameters(false);
            IsConfigured = derived.Q.X!.SequenceEqual(pub[1..33]) && derived.Q.Y!.SequenceEqual(pub[33..65]);
        }
        catch { IsConfigured = false; }
    }
}

public class PushService(AppDbContext db, PushSettings settings) : IPushService
{
    public string VapidPublicKey => settings.IsConfigured ? settings.PublicKey : "";
    public bool IsConfigured => settings.IsConfigured;
    public async Task SaveSubscriptionAsync(int userId, string endpoint, string p256dh, string auth, string? userAgent)
    {
        if (userId <= 0 || !PushEndpointPolicy.ValidEndpoint(endpoint) || !PushEndpointPolicy.ValidKeys(p256dh, auth)) throw new ArgumentException("اشتراک دستگاه معتبر نیست؛ از Chrome به‌روز و HTTPS استفاده کنید.");
        if (!IsConfigured) throw new InvalidOperationException("کلیدهای اعلان روی سرور تنظیم نشده یا معتبر نیستند.");
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var existing = await db.PushSubscriptions.SingleOrDefaultAsync(s => s.Endpoint == endpoint);
        if (existing == null) { existing = new Inventory.Api.Data.PushSubscription { Endpoint = endpoint }; db.PushSubscriptions.Add(existing); }
        existing.UserId = userId; existing.P256DH = p256dh; existing.Auth = auth;
        existing.UserAgent = userAgent is { Length: > 250 } ? userAgent[..250] : userAgent;
        existing.LastSeenAt = DateTime.Now;
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public async Task RemoveSubscriptionAsync(int userId, string endpoint)
        => await db.PushSubscriptions.Where(s => s.UserId == userId && s.Endpoint == endpoint).ExecuteDeleteAsync();
    public Task SendToUserAsync(int userId, string title, string? body, string? link) => SendToUsersAsync(new[] { userId }, title, body, link);
    public async Task SendToUsersAsync(IEnumerable<int> userIds, string title, string? body, string? link)
    {
        await PushQueue.StageAsync(db, userIds, title, body, link); await db.SaveChangesAsync();
    }
}

public interface IPushTransport { Task<int> SendAsync(Inventory.Api.Data.PushSubscription subscription, string payload, CancellationToken ct); }
public class WebPushTransport(PushSettings settings, IHttpClientFactory factory) : IPushTransport
{
    public async Task<int> SendAsync(Inventory.Api.Data.PushSubscription subscription, string payload, CancellationToken ct)
    {
        using var client = new WebPushClient(factory.CreateClient("WebPush"));
        using var json = System.Text.Json.JsonDocument.Parse(payload);
        var expires = json.RootElement.GetProperty("expiresAtUtc").GetDateTime();
        var ttl = Math.Clamp((int)(expires - DateTime.UtcNow).TotalSeconds, 0, 172800);
        try
        {
            await client.SendNotificationAsync(new WebPush.PushSubscription(subscription.Endpoint, subscription.P256DH, subscription.Auth), payload,
                new Dictionary<string, object> { ["vapidDetails"] = new VapidDetails(settings.Subject, settings.PublicKey, settings.PrivateKey), ["TTL"] = ttl, ["headers"] = new Dictionary<string, object> { ["Urgency"] = "high" } }, ct);
            return 201;
        }
        catch (WebPushException ex) { return (int)ex.StatusCode; }
    }
}
