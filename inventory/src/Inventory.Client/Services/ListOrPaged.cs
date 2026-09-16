using System.Text.Json;

namespace Inventory.Client.Services;

/// <summary>
/// خواندن «فهرست» از API وقتی شکل پاسخ ممکن است آرایهٔ ساده یا PagedResult باشد.
/// بعضی endpointهای نامه‌ها در سمت سرور به PagedResult (با صفحه‌بندی) تغییر کرده‌اند؛
/// بدون این لایهٔ سازگاری، دی‌سریالایز آرایه/آبجکت باعث خطا و خالی‌شدن فهرست در کلاینت می‌شود.
/// </summary>
public static class ListOrPaged
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public static async Task<List<T>> GetAsync<T>(IApiClient api, string path)
    {
        var el = await api.GetAsync<JsonElement>(path);
        return Normalize<T>(el);
    }

    /// <summary>آرایه → همان آرایه؛ آبجکت دارای items → items؛ در غیر این صورت فهرست خالی.</summary>
    public static List<T> Normalize<T>(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Array:
                return el.Deserialize<List<T>>(Opts) ?? new List<T>();

            case JsonValueKind.Object:
                if (el.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                    return items.Deserialize<List<T>>(Opts) ?? new List<T>();
                return new List<T>();

            default:
                return new List<T>();
        }
    }
}
