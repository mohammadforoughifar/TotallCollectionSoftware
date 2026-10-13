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
    /// <summary>فقط دادهٔ ساخت‌یافتهٔ غیرمحرمانه؛ پاسخ خام Redfish نگه‌داری نمی‌شود.</summary>
    public IReadOnlyList<RedfishHardwareComponent> Components { get; init; } = Array.Empty<RedfishHardwareComponent>();
    public IReadOnlyList<RedfishHardwareEvent> Events { get; init; } = Array.Empty<RedfishHardwareEvent>();
}

public sealed class RedfishHardwareComponent
{
    public string Type { get; init; } = "";
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string Health { get; init; } = "Unknown";
    public string? State { get; init; }
    public string? Reading { get; init; }
    public string? Model { get; init; }
    public string? SerialNumber { get; init; }
    public string? Message { get; init; }
}

public sealed class RedfishHardwareEvent
{
    public string Key { get; init; } = "";
    public string Severity { get; init; } = "Unknown";
    public string Message { get; init; } = "";
    public string? Origin { get; init; }
    public DateTime? OccurredAt { get; init; }
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

    /// <param name="includeHardwareDetails">خواندن محدود قطعات/رخدادها؛ برای پایش و دریافت Inventory استفاده شود، نه تست سریع اتصال.</param>
    public async Task<RedfishSnapshot> GetSnapshotAsync(string address, int port, RedfishCredentials credentials, CancellationToken ct = default,
        bool includeHardwareDetails = false)
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
            IReadOnlyList<RedfishHardwareComponent> components = Array.Empty<RedfishHardwareComponent>();
            IReadOnlyList<RedfishHardwareEvent> events = Array.Empty<RedfishHardwareEvent>();
            if (includeHardwareDetails)
            {
                // جزئیات اختیاری‌اند: نبودن یک resource در نسل‌های مختلف iLO نباید Health اصلی را ناموفق کند.
                try { components = await ReadHardwareComponentsAsync(baseUri, root.RootElement, systemNode, credentials, ct); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException) { }
                try { events = await ReadRecentEventsAsync(baseUri, root.RootElement, credentials, ct); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException) { }
            }

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
                IloVersion = firmware,
                Components = components,
                Events = events
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

    private async Task<IReadOnlyList<RedfishHardwareComponent>> ReadHardwareComponentsAsync(Uri origin, JsonElement root,
        JsonElement system, RedfishCredentials credentials, CancellationToken ct)
    {
        var components = new List<RedfishHardwareComponent>();

        // هر resource اختیاری جداگانه محافظت می‌شود؛ نبودن Thermal نباید Memory/Storage را حذف کند.
        try
        {
            // Chassis منابع Power و Thermal را در Redfish استاندارد نگه‌داری می‌کند.
            var chassisCollection = GetOdataLink(root, "Chassis", origin);
            if (chassisCollection is not null)
            {
                using var chassisMembers = await GetJsonAsync(origin, chassisCollection, credentials, ct);
                var chassisLink = GetFirstMemberLink(chassisMembers.RootElement, origin);
                if (chassisLink is not null)
                {
                    using var chassis = await GetJsonAsync(origin, chassisLink, credentials, ct);
                    AddComponent(components, "Chassis", chassis.RootElement);
                    await ReadPowerAndThermalAsync(origin, chassis.RootElement, credentials, components, ct);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException) { }

        // سقف منابع مانع فشار غیرقابل‌پیش‌بینی روی کنترلر مدیریت می‌شود.
        try { await ReadSystemResourceComponentsAsync(origin, system, "Memory", "Memory", 16, credentials, components, ct); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException) { }
        try { await ReadSystemResourceComponentsAsync(origin, system, "Processors", "Processor", 8, credentials, components, ct); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException) { }
        try { await ReadStorageComponentsAsync(origin, system, credentials, components, ct); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException) { }
        return components.Take(128).ToList();
    }

    private async Task ReadPowerAndThermalAsync(Uri origin, JsonElement chassis, RedfishCredentials credentials,
        List<RedfishHardwareComponent> components, CancellationToken ct)
    {
        var powerLink = GetOdataLink(chassis, "Power", origin);
        if (powerLink is not null)
        {
            using var power = await GetJsonAsync(origin, powerLink, credentials, ct);
            if (power.RootElement.TryGetProperty("PowerSupplies", out var supplies) && supplies.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in supplies.EnumerateArray().Take(16))
                    AddComponent(components, "PowerSupply", item, ReadNumber(item, "LastPowerOutputWatts", "W")
                        ?? ReadNumber(item, "PowerCapacityWatts", "W"));
            }
        }

        var thermalLink = GetOdataLink(chassis, "Thermal", origin);
        if (thermalLink is null) return;
        using var thermal = await GetJsonAsync(origin, thermalLink, credentials, ct);
        if (thermal.RootElement.TryGetProperty("Fans", out var fans) && fans.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in fans.EnumerateArray().Take(32))
                AddComponent(components, "Fan", item, ReadNumber(item, "Reading", ReadString(item, "ReadingUnits") ?? ""));
        }
        if (thermal.RootElement.TryGetProperty("Temperatures", out var temperatures) && temperatures.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in temperatures.EnumerateArray().Take(32))
                AddComponent(components, "Temperature", item, ReadNumber(item, "ReadingCelsius", "°C")
                    ?? ReadNumber(item, "Reading", ReadString(item, "ReadingUnits") ?? ""));
        }
    }

    private async Task ReadSystemResourceComponentsAsync(Uri origin, JsonElement system, string property, string type,
        int max, RedfishCredentials credentials, List<RedfishHardwareComponent> components, CancellationToken ct)
    {
        var collectionLink = GetOdataLink(system, property, origin);
        if (collectionLink is null) return;
        using var collection = await GetJsonAsync(origin, collectionLink, credentials, ct);
        foreach (var link in GetMemberLinks(collection.RootElement, origin, max))
        {
            using var resource = await GetJsonAsync(origin, link, credentials, ct);
            var reading = type == "Memory" ? ReadNumber(resource.RootElement, "CapacityMiB", "MiB")
                : ReadNumber(resource.RootElement, "TotalCores", "cores");
            AddComponent(components, type, resource.RootElement, reading);
        }
    }

    private async Task ReadStorageComponentsAsync(Uri origin, JsonElement system, RedfishCredentials credentials,
        List<RedfishHardwareComponent> components, CancellationToken ct)
    {
        var storageCollection = GetOdataLink(system, "Storage", origin);
        if (storageCollection is null) return;
        using var storageMembers = await GetJsonAsync(origin, storageCollection, credentials, ct);
        var remainingDrives = 16;
        foreach (var storageLink in GetMemberLinks(storageMembers.RootElement, origin, 4))
        {
            if (remainingDrives <= 0) break;
            using var storage = await GetJsonAsync(origin, storageLink, credentials, ct);
            AddComponent(components, "Storage", storage.RootElement);
            var driveLinks = GetPropertyLinks(storage.RootElement, "Drives", origin, remainingDrives);
            remainingDrives -= driveLinks.Count;
            foreach (var driveLink in driveLinks)
            {
                using var drive = await GetJsonAsync(origin, driveLink, credentials, ct);
                AddComponent(components, "Disk", drive.RootElement, ReadBytes(drive.RootElement, "CapacityBytes"));
            }
        }
    }

    private async Task<IReadOnlyList<RedfishHardwareEvent>> ReadRecentEventsAsync(Uri origin, JsonElement root,
        RedfishCredentials credentials, CancellationToken ct)
    {
        var managerCollection = GetOdataLink(root, "Managers", origin);
        if (managerCollection is null) return Array.Empty<RedfishHardwareEvent>();
        using var managers = await GetJsonAsync(origin, managerCollection, credentials, ct);
        var managerLink = GetFirstMemberLink(managers.RootElement, origin);
        if (managerLink is null) return Array.Empty<RedfishHardwareEvent>();
        using var manager = await GetJsonAsync(origin, managerLink, credentials, ct);
        var managerKey = ReadString(manager.RootElement, "Id") ?? "manager";
        var logsLink = GetOdataLink(manager.RootElement, "LogServices", origin);
        if (logsLink is null) return Array.Empty<RedfishHardwareEvent>();
        using var logs = await GetJsonAsync(origin, logsLink, credentials, ct);
        var logLink = GetFirstMemberLink(logs.RootElement, origin);
        if (logLink is null) return Array.Empty<RedfishHardwareEvent>();
        using var log = await GetJsonAsync(origin, logLink, credentials, ct);
        var entriesLink = GetOdataLink(log.RootElement, "Entries", origin);
        if (entriesLink is null) return Array.Empty<RedfishHardwareEvent>();
        var entriesTop = new UriBuilder(entriesLink) { Query = "$top=15" }.Uri;
        using var entries = await GetJsonAsync(origin, entriesTop, credentials, ct);

        var result = new List<RedfishHardwareEvent>();
        if (!entries.RootElement.TryGetProperty("Members", out var members) || members.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var member in members.EnumerateArray().Take(15))
        {
            if (!string.IsNullOrWhiteSpace(ReadString(member, "Message")))
                AddEvent(result, managerKey, member);
            else if (ReadLink(member, origin) is { } entryLink)
            {
                using var entry = await GetJsonAsync(origin, entryLink, credentials, ct);
                AddEvent(result, managerKey, entry.RootElement);
            }
        }
        return result;
    }

    private static void AddComponent(List<RedfishHardwareComponent> items, string type, JsonElement source, string? reading = null)
    {
        var name = ReadString(source, "Name") ?? ReadString(source, "MemberId") ?? ReadString(source, "Id") ?? type;
        var key = ReadString(source, "Id") ?? ReadString(source, "MemberId") ?? name;
        var health = ReadNestedString(source, "Status", "HealthRollup")
            ?? ReadNestedString(source, "Status", "Health") ?? "Unknown";
        items.Add(new RedfishHardwareComponent
        {
            Type = type, Key = Limit(key, 200) ?? type, Name = Limit(name, 200) ?? type,
            Health = Limit(health, 20) ?? "Unknown", State = Limit(ReadNestedString(source, "Status", "State"), 40),
            Reading = Limit(reading, 100), Model = Limit(ReadString(source, "Model") ?? ReadString(source, "PartNumber"), 150),
            SerialNumber = Limit(ReadString(source, "SerialNumber"), 100),
            Message = Limit(ReadString(source, "Status") ?? ReadString(source, "StatusMessage"), 500)
        });
    }

    private static void AddEvent(List<RedfishHardwareEvent> items, string managerKey, JsonElement source)
    {
        var message = Limit(ReadString(source, "Message"), 2000);
        if (string.IsNullOrWhiteSpace(message)) return;
        var id = ReadString(source, "Id") ?? $"{ReadString(source, "Created")}|{message}";
        DateTime? occurred = DateTime.TryParse(ReadString(source, "Created"), out var parsed) ? parsed : null;
        items.Add(new RedfishHardwareEvent
        {
            Key = Limit($"{managerKey}:{id}", 200) ?? managerKey,
            Severity = Limit(ReadString(source, "Severity") ?? ReadString(source, "EventType"), 30) ?? "Unknown",
            Message = message, Origin = Limit(ReadNestedString(source, "OriginOfCondition", "@odata.id"), 500), OccurredAt = occurred
        });
    }

    private static List<Uri> GetMemberLinks(JsonElement collection, Uri origin, int max)
    {
        if (!collection.TryGetProperty("Members", out var members) || members.ValueKind != JsonValueKind.Array) return new();
        return members.EnumerateArray().Select(item => ReadLink(item, origin)).Where(link => link is not null)
            .Select(link => link!).Take(max).ToList();
    }

    private static List<Uri> GetPropertyLinks(JsonElement source, string property, Uri origin, int max)
    {
        if (!source.TryGetProperty(property, out var values)) return new();
        if (values.ValueKind != JsonValueKind.Array) return ReadLink(values, origin) is { } link ? new List<Uri> { link } : new();
        return values.EnumerateArray().Select(item => ReadLink(item, origin)).Where(link => link is not null)
            .Select(link => link!).Take(max).ToList();
    }

    private static string? ReadNumber(JsonElement source, string property, string unit)
    {
        if (!source.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number) return null;
        return $"{value.GetRawText()} {unit}".Trim();
    }

    private static string? ReadBytes(JsonElement source, string property)
    {
        if (!source.TryGetProperty(property, out var value) || !value.TryGetInt64(out var bytes) || bytes <= 0) return null;
        return $"{Math.Round(bytes / 1073741824d, 1)} GiB";
    }

    private static string? Limit(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length <= max ? value : value[..max];
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
