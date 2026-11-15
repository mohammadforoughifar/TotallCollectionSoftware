using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inventory.Client.Services;

/// <summary>
/// خلاصهٔ خوانا از پاسخ <c>GET /api/license/status</c>.
///
/// <para>
/// چرا جدا از کامپوننت؟ تا منطق «درصد مصرف/آستانهٔ هشدار/فقط‌خواندنی» یک‌جا و
/// <b>قابل تست بدون مرورگر</b> باشد. کامپوننت فقط نمایش می‌دهد.
/// </para>
/// </summary>
public sealed class LicenseStatusSummary
{
    public string Status { get; init; } = "Missing";
    public string? CustomerName { get; init; }
    public string? InstallationId { get; init; }
    public string? Deployment { get; init; }
    public DateTime? UpdatesUntil { get; init; }
    public bool CanWrite { get; init; } = true;
    public bool RehostRecommended { get; init; }
    public string? ClockMessage { get; init; }
    public bool ClockBlocked { get; init; }
    public int SeatsGlobalMax { get; init; }
    public string? SeatMode { get; init; }
    public IReadOnlyList<ModuleRow> Modules { get; init; } = Array.Empty<ModuleRow>();
    public IReadOnlyList<LimitRow> Limits { get; init; } = Array.Empty<LimitRow>();
    public IReadOnlyList<IssueRow> Issues { get; init; } = Array.Empty<IssueRow>();
    public ReservationRow? Reservations { get; init; }

    /// <summary>یک ردیف ماژول با وضعیت و (اختیاری) تاریخ انقضا.</summary>
    public sealed record ModuleRow(string Code, string Title, string State, string StateTitle,
        bool AlwaysOn, DateTime? ExpiresAt);

    /// <summary>یک ردیف سهمیه: مصرف/سقف و درصد مصرف برای نوار پیشرفت.</summary>
    public sealed record LimitRow(string Module, string Metric, long Used, long Limit, int? Percent, bool AtRisk);

    public sealed record IssueRow(string Code, string Severity, string Message);

    public sealed record ReservationRow(int Pending, int Reserved, int Confirmed, int Refunded, bool Unavailable);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    /// <summary>پاسخ خام JSON را می‌خواند؛ خطای ساختار ⇒ استثنا (فراخوان باید نمایش دهد).</summary>
    public static LicenseStatusSummary Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string? Str(string name)
            => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        bool Bool(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

        int Int(string name)
        {
            if (!root.TryGetProperty(name, out var v)) return 0;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
            if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
            return 0;
        }

        // ---- ساعت ----
        string? clockMessage = null;
        var clockBlocked = false;
        if (root.TryGetProperty("clock", out var clock) && clock.ValueKind == JsonValueKind.Object)
        {
            if (clock.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                clockMessage = m.GetString();
            clockBlocked = clock.TryGetProperty("writesBlocked", out var wb) && wb.ValueKind == JsonValueKind.True;
        }

        // ---- صندلی ----
        var seatsGlobal = 0;
        string? seatMode = null;
        if (root.TryGetProperty("seats", out var seats) && seats.ValueKind == JsonValueKind.Object)
        {
            if (seats.TryGetProperty("globalMax", out var gm) && gm.ValueKind == JsonValueKind.Number
                && gm.TryGetInt32(out var gmValue)) seatsGlobal = gmValue;
            if (seats.TryGetProperty("mode", out var md) && md.ValueKind == JsonValueKind.String) seatMode = md.GetString();
        }

        // ---- ماژول‌ها ----
        var modules = new List<ModuleRow>();
        if (root.TryGetProperty("modules", out var modulesElement) && modulesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in modulesElement.EnumerateArray())
            {
                DateTime? expires = null;
                if (item.TryGetProperty("expiresAt", out var expiresElement)
                    && expiresElement.ValueKind == JsonValueKind.String
                    && DateTime.TryParse(expiresElement.GetString(), out var parsed))
                    expires = parsed;

                modules.Add(new ModuleRow(
                    item.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "",
                    item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                    item.TryGetProperty("state", out var s) ? s.GetString() ?? "" : "",
                    item.TryGetProperty("stateTitle", out var st) ? st.GetString() ?? "" : "",
                    item.TryGetProperty("alwaysOn", out var ao) && ao.ValueKind == JsonValueKind.True,
                    expires));
            }
        }

        // ---- سهمیه‌ها ----
        var limits = new List<LimitRow>();
        if (root.TryGetProperty("limits", out var limitsElement) && limitsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in limitsElement.EnumerateArray())
            {
                limits.Add(ReadLimit(item));
            }
        }

        // ---- موارد نیازمند اقدام ----
        var issues = new List<IssueRow>();
        if (root.TryGetProperty("issues", out var issuesElement) && issuesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in issuesElement.EnumerateArray())
            {
                issues.Add(new IssueRow(
                    item.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "",
                    item.TryGetProperty("severity", out var s) ? s.GetString() ?? "" : "",
                    item.TryGetProperty("message", out var m) ? m.GetString() ?? "" : ""));
            }
        }

        // ---- رزروها ----
        ReservationRow? reservations = null;
        if (root.TryGetProperty("reservations", out var res) && res.ValueKind == JsonValueKind.Object)
        {
            var unavailable = res.TryGetProperty("unavailable", out var un) && un.ValueKind == JsonValueKind.True;
            reservations = new ReservationRow(
                res.TryGetProperty("pending", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0,
                res.TryGetProperty("reserved", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt32() : 0,
                res.TryGetProperty("confirmed", out var cf) && cf.ValueKind == JsonValueKind.Number ? cf.GetInt32() : 0,
                res.TryGetProperty("refunded", out var rf) && rf.ValueKind == JsonValueKind.Number ? rf.GetInt32() : 0,
                unavailable);
        }

        return new LicenseStatusSummary
        {
            Status = Str("status") ?? "Missing",
            CustomerName = Str("customerName"),
            InstallationId = Str("installationId"),
            Deployment = Str("deployment"),
            CanWrite = !root.TryGetProperty("canWrite", out var cw) || cw.ValueKind != JsonValueKind.False,
            RehostRecommended = Bool("rehostRecommended"),
            ClockMessage = clockMessage,
            ClockBlocked = clockBlocked,
            SeatsGlobalMax = seatsGlobal,
            SeatMode = seatMode,
            Modules = modules,
            Limits = limits,
            Issues = issues,
            Reservations = reservations
        };
    }

    /// <summary>
    /// درصد مصرف و «در معرض خطر» بودن یک ردیف سهمیه.
    /// سقف نامحدود (۰ یا منفی) ⇒ درصد <c>null</c> (نوار پیشرفت پرشده نشان داده نمی‌شود).
    /// </summary>
    public static LimitRow ReadLimit(JsonElement item)
    {
        string Str(string name)
            => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        long Num(string name)
        {
            if (!item.TryGetProperty(name, out var v)) return 0;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
            if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s)) return s;
            return 0;
        }

        var used = Num("used");
        var limit = Num("limit");

        int? percent = null;
        if (limit > 0)
        {
            var raw = (int)Math.Round(used * 100.0 / limit);
            percent = raw < 0 ? 0 : (raw > 100 ? 100 : raw);
        }

        // آستانهٔ هشدار: ۸۵٪ و بیشتر. عدد ۱۰۰٪ «پرشده» است، نه «در خطر».
        var atRisk = percent is { } p && p >= 85;
        return new LimitRow(Str("module"), Str("metric"), used, limit, percent, atRisk);
    }

    /// <summary>آیا وضعیت، نیازمند اقدام مدیر است؟</summary>
    public bool NeedsAttention => Status != "Valid" || ClockBlocked || RehostRecommended
                                  || Issues.Any(i => i.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase))
                                  || Limits.Any(l => l.AtRisk);

    /// <summary>ماژول‌های غیرفعال — برای پنهان‌کردن منوها.</summary>
    public IReadOnlyCollection<string> DisabledModules
        => Modules.Where(m => m.State.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Code).ToList();

    /// <summary>عنوان فارسی وضعیت کلی برای نمایش.</summary>
    public string StatusTitle => Status switch
    {
        "Valid" => "فعال",
        "Invalid" => "نامعتبر",
        "Missing" => "بدون لایسنس",
        _ => Status
    };
}
