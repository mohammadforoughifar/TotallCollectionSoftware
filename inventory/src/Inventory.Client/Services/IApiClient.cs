namespace Inventory.Client.Services;

/// <summary>قرارداد کلاینت HTTP پایه برای ارتباط با API.</summary>
public interface IApiClient
{
    Task<T> GetAsync<T>(string path);
    Task<T> PostAsync<T>(string path, object? body = null);
    Task<T> PutAsync<T>(string path, object? body = null);
    Task DeleteAsync(string path);

    /// <summary>ارسال فایل (multipart/form-data).</summary>
    Task<T> PostFileAsync<T>(string path, Stream fileStream, string fileName, string formFieldName = "file");

    /// <summary>ساخت آدرس کامل از مسیر نسبی API (برای لینک دانلود).</summary>
    string BuildUrl(string path);
}
