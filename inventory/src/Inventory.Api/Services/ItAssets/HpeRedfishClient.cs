using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Inventory.Api.Services.ItAssets;

/// <summary>اعتبارنامه‌ای که فقط در حافظه و هنگام فراخوانی Redfish استفاده می‌شود.</summary>
public sealed record RedfishCredentials(string Username, string Password);

/// <summary>خلاصهٔ غیرمحرمانهٔ خوانده‌شده از iLO/Redfish.</summary>
public sealed class RedfishSnapshot
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string Health { get; init; } = "Unknown";
    public string? PowerState { get; init; }
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? SerialNumber { get; init; }
    public string? AssetTag { get; init; }
    public string? BiosVersion { get; init; }
    public string? FirmwareVersion { get; init; }
    public string? IloVersion { get; init; }
}

/// <summary>
/// کلاینت فقط-خواندنی Redfish برای HPE iLO. درخواست‌ها فقط روی HTTPS، بدون دنبال‌کردن redirect
/// و فقط به همان origin ثبت‌شده ارسال می‌شوند تا اعتبارنامه به مقصد دیگری نرود.
/// </summary>
public sealed class HpeRedfishClient
{
    private const string ClientName = "HpeRedfish";
    private readonly IHttpClientFactory _clients;

    public HpeRedfishClient(IHttpClientFactory clients) => _clients = clients;

    public async Task<RedfishSnapshot> GetSnapshotAsync(string address, int port, RedfishCredentials credentials, CancellationToken ct = default)
    {
        try
        {
            var baseUri = BuildBaseUri(address, port);
            using var root = await GetJsonAsync(baseUri, new Uri(baseUri, "redfish/v1/"), credentials, ct);
            var systemsLink = GetOdataLink(root.RootElement, "Systems", baseUri)
                ?? throw new InvalidOperationException("منبع Systems در پاسخ Redfish پیدا نشد.");
            using var systems = await GetJsonAsync(baseUri, systemsLink, credentials, ct);
            var systemLink = GetFirstMemberLink(systems.RootElement, baseUri)
                ?? throw new InvalidOperationException("هیچ System قابل مدیریتی در Redfish پیدا نشد.");
            using var system = await GetJsonAsync(baseUri, systemLink, credentials, ct);

            string? firmware = null;
            var managersLink = GetOdataLink(root.RootElement, "Managers", baseUri);
            if (managersLink is not null)
            {
                try
                {
                    using var managers = await GetJsonAsync(baseUri, managersLink, credentials, ct);
                    var managerLink = GetFirstMemberLink(managers.RootElement, baseUri);
                    if (managerLink is not null)
                    {
                        using var manager = await GetJsonAsync(baseUri, managerLink, credentials, ct);
                        firmware = ReadString(manager.RootElement, "FirmwareVersion");
                    }
                }
                // نبودن Managers نباید Health/System را از دسترس خارج کند.
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException)
                {
                    firmware = null;
                }
            }

            var systemNode = system.RootElement;
            return new RedfishSnapshot
            {
                Success = true,
                Health = ReadNestedString(systemNode, "Status", "HealthRollup")
                         ?? ReadNestedString(systemNode, "Status", "Health")
                         ?? "Unknown",
                PowerState = ReadString(systemNode, "PowerState"),
                Manufacturer = ReadString(systemNode, "Manufacturer"),
                Model = ReadString(systemNode, "Model"),
                SerialNumber = ReadString(systemNode, "SerialNumber"),
                AssetTag = ReadString(systemNode, "AssetTag"),
                BiosVersion = ReadString(systemNode, "BiosVersion"),
                FirmwareVersion = firmware,
                // FirmwareVersion منیجر iLO، نسخهٔ واقعی iLO است؛ RedfishVersion فقط نسخهٔ پروتکل است.
                IloVersion = firmware
            };
        }
        catch (OperationCanceledException)
        {
            return new RedfishSnapshot { Error = "عملیات به‌دلیل پایان مهلت یا لغو درخواست متوقف شد." };
        }
        catch (HttpRequestException ex)
        {
            return new RedfishSnapshot { Error = ToSafeError(ex) };
        }
        catch (JsonException)
        {
            return new RedfishSnapshot { Error = "پاسخ iLO معتبر نیست یا Redfish در این آدرس فعال نیست." };
        }
        catch (Exception ex) when (ex is UriFormatException or InvalidOperationException)
        {
            return new RedfishSnapshot { Error = ex.Message };
        }
    }

    private async Task<JsonDocument> GetJsonAsync(Uri origin, Uri endpoint, RedfishCredentials credentials, CancellationToken ct)
    {
        EnsureSameOrigin(origin, endpoint);
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("OData-Version", "4.0");
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.Username}:{credentials.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        var client = _clients.CreateClient(ClientName);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            var friendly = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "نام کاربری یا رمز iLO معتبر نیست، یا این حساب دسترسی Redfish ندارد.",
                HttpStatusCode.NotFound => "Redfish روی iLO پیدا نشد.",
                _ => $"iLO پاسخ HTTP {status} برگرداند."
            };
            throw new HttpRequestException(friendly, null, response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private static Uri BuildBaseUri(string address, int port)
    {
        if (string.IsNullOrWhiteSpace(address)) throw new InvalidOperationException("آدرس iLO وارد نشده است.");
        if (port is < 1 or > 65535) throw new InvalidOperationException("پورت iLO نامعتبر است.");

        var text = address.Trim();
        if (!text.Contains("://", StringComparison.Ordinal)) text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed) || parsed.Host.Length == 0)
            throw new InvalidOperationException("آدرس iLO معتبر نیست.");
        if (!string.IsNullOrEmpty(parsed.UserInfo))
            throw new InvalidOperationException("آدرس iLO نباید شامل نام کاربری یا رمز باشد.");
        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("برای امنیت، اتصال iLO فقط با HTTPS مجاز است.");

        var builder = new UriBuilder(parsed)
        {
            Scheme = Uri.UriSchemeHttps,
            Port = port,
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri;
    }

    private static void EnsureSameOrigin(Uri origin, Uri target)
    {
        if (!string.Equals(origin.Scheme, target.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(origin.Host, target.Host, StringComparison.OrdinalIgnoreCase)
            || origin.Port != target.Port)
            throw new InvalidOperationException("لینک Redfish به مقصدی خارج از iLO ثبت‌شده اشاره می‌کند و اجرا نشد.");
    }

    private static Uri? GetOdataLink(JsonElement source, string name, Uri origin)
    {
        if (!source.TryGetProperty(name, out var node)) return null;
        return ReadLink(node, origin);
    }

    private static Uri? GetFirstMemberLink(JsonElement collection, Uri origin)
    {
        if (!collection.TryGetProperty("Members", out var members) || members.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in members.EnumerateArray())
        {
            var link = ReadLink(item, origin);
            if (link is not null) return link;
        }
        return null;
    }

    private static Uri? ReadLink(JsonElement node, Uri origin)
    {
        var raw = node.ValueKind == JsonValueKind.String ? node.GetString()
            : ReadString(node, "@odata.id");
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var uri = new Uri(origin, raw);
        EnsureSameOrigin(origin, uri);
        return uri;
    }

    private static string? ReadString(JsonElement node, string property) =>
        node.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadNestedString(JsonElement node, string objectProperty, string valueProperty) =>
        node.TryGetProperty(objectProperty, out var child) && child.ValueKind == JsonValueKind.Object
            ? ReadString(child, valueProperty)
            : null;

    private static string ToSafeError(HttpRequestException ex)
    {
        if (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return "نام کاربری یا رمز iLO معتبر نیست، یا این حساب دسترسی Redfish ندارد.";
        if (ex.StatusCode == HttpStatusCode.NotFound) return "Redfish روی iLO پیدا نشد.";
        return "ارتباط امن با iLO برقرار نشد. آدرس، پورت، شبکه، گواهی TLS و فعال بودن Redfish را بررسی کنید.";
    }
}
