namespace Inventory.Client.Services;

/// <summary>قرارداد کلاینت HTTP پایه برای ارتباط با API.</summary>
public interface IApiClient
{
    Task<T> GetAsync<T>(string path);
    Task<T> PostAsync<T>(string path, object? body = null);
    Task<T> PutAsync<T>(string path, object? body = null);
    Task DeleteAsync(string path);

    /// <summary>حذف با خواندن بدنهٔ پاسخ (مثلاً برای دانستن اینکه حذف در انتظار تایید مدیر مانده یا نه).</summary>
    Task<T> DeleteAsync<T>(string path);

    /// <summary>ارسال فایل (multipart/form-data).</summary>
    Task<T> PostFileAsync<T>(string path, Stream fileStream, string fileName, string formFieldName = "file", string? contentType = null);

    /// <summary>ساخت آدرس کامل از مسیر نسبی API (برای لینک دانلود).</summary>
    string BuildUrl(string path);

    /// <summary>دریافت فایل باینری (PDF/Excel) به‌همراه نام پیشنهادی سرور.</summary>
    Task<(byte[] Data, string FileName, string ContentType)> GetFileAsync(string path);
}
