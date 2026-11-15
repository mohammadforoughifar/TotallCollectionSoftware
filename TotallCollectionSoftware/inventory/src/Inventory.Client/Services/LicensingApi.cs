using System.Text.Json;

namespace Inventory.Client.Services;

/// <summary>یک رخداد لایسنس (برای جدول «آخرین هشدارها»).</summary>
public sealed record LicenseEventDto(long Id, DateTime At, string Kind, string Severity,
    string Message, int? UserId, string? Serial);

/// <summary>یک رزرو «در انتظار» (برای تسویهٔ دستی).</summary>
public sealed record PendingReservationDto(long Id, string Module, string Metric, string PeriodKey,
    int Amount, string? EntityType, string? EntityId, string State, DateTime ReservedAt, int AgeMinutes);

/// <summary>نتیجهٔ آپلود یک فایل لایسنس.</summary>
public sealed record ActivationResultDto(string? Serial, bool IsAddOn, string? Message, string? Status);

/// <summary>فهرست رخدادها از <c>/api/license/events</c>.</summary>
public sealed record LicenseEventListDto(int Count, List<LicenseEventDto>? Items);

/// <summary>فهرست رزروها از <c>/api/license/reservations</c>.</summary>
public sealed record ReservationListDto(int Count, List<PendingReservationDto>? Items);

/// <summary>
/// کلاینت صفحهٔ لایسنس.
///
/// <para>
/// چرا یک سرویس جدا؟ چون صفحه نباید بداند آدرس‌ها و شکل JSON چیست؛ و اگر روزی
/// مسیری عوض شد، تنها همین فایل تغییر می‌کند.
/// </para>
/// </summary>
public interface ILicensingApi
{
    Task<LicenseStatusSummary> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<List<LicenseEventDto>> GetWarningEventsAsync(int take = 50, CancellationToken cancellationToken = default);
    Task<List<PendingReservationDto>> GetPendingReservationsAsync(CancellationToken cancellationToken = default);
    Task<(byte[] Data, string FileName)> DownloadRequestFileAsync(CancellationToken cancellationToken = default);
    Task<List<(string FileName, bool Ok, string Message, ActivationResultDto? Result)>> UploadFilesAsync(
        IEnumerable<(string FileName, Stream Content)> files, CancellationToken cancellationToken = default);
    Task SettleAsync(long reservationId, bool confirm, string? note, CancellationToken cancellationToken = default);
}

public sealed class LicensingApi : ILicensingApi
{
    private readonly IApiClient _api;

    public LicensingApi(IApiClient api) => _api = api;

    public async Task<LicenseStatusSummary> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var element = await _api.GetAsync<JsonElement>("api/license/status", cancellationToken);
        return LicenseStatusSummary.Parse(element.GetRawText());
    }

    public async Task<List<LicenseEventDto>> GetWarningEventsAsync(int take = 50,
        CancellationToken cancellationToken = default)
    {
        var response = await _api.GetAsync<LicenseEventListDto>(
            $"api/license/events?take={take}&severity=Warning", cancellationToken);
        return response.Items ?? new List<LicenseEventDto>();
    }

    public async Task<List<PendingReservationDto>> GetPendingReservationsAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await _api.GetAsync<ReservationListDto>(
            "api/license/reservations?state=Pending&take=50", cancellationToken);
        return response.Items ?? new List<PendingReservationDto>();
    }

    public Task<(byte[] Data, string FileName)> DownloadRequestFileAsync(CancellationToken cancellationToken = default)
        => DownloadAsync(cancellationToken);

    private async Task<(byte[] Data, string FileName)> DownloadAsync(CancellationToken cancellationToken)
    {
        var file = await _api.GetFileAsync("api/license/request-file", cancellationToken);
        return (file.Data, string.IsNullOrWhiteSpace(file.FileName) ? "license-request.json" : file.FileName);
    }

    /// <summary>
    /// آپلود چندفایلی: هر فایل جدا ارسال می‌شود و نتیجهٔ هر کدام جدا برگردانده می‌شود.
    ///
    /// <para>
    /// چرا جدا؟ چون بستهٔ مشتری می‌تواند شامل سند پایه و چند Add-on باشد؛ اگر یکی خراب
    /// بود، بقیه باید اعمال شوند و مدیر باید ببیند دقیقاً کدام رد شد و چرا.
    /// </para>
    /// </summary>
    public async Task<List<(string FileName, bool Ok, string Message, ActivationResultDto? Result)>> UploadFilesAsync(
        IEnumerable<(string FileName, Stream Content)> files, CancellationToken cancellationToken = default)
    {
        var results = new List<(string, bool, string, ActivationResultDto?)>();

        foreach (var (fileName, content) in files)
        {
            try
            {
                var result = await _api.PostFileAsync<ActivationResultDto>(
                    "api/license/activate", content, fileName, "file");
                results.Add((fileName, true, result?.Message ?? "اعمال شد.", result));
            }
            catch (Exception ex)
            {
                results.Add((fileName, false, ex.Message, null));
            }
            finally
            {
                await content.DisposeAsync();
            }
        }

        return results;
    }

    public Task SettleAsync(long reservationId, bool confirm, string? note,
        CancellationToken cancellationToken = default)
        => _api.PostAsync<object>($"api/license/reservations/{reservationId}/settle",
            new { action = confirm ? "Confirm" : "Refund", note });
}
