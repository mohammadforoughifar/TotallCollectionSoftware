using Inventory.Shared.Dtos;

namespace Inventory.Api.Services;

/// <summary>سرویس نامه صادره — منطق مشابه InnerLetter اما برای ارسال به خارج از سازمان</summary>
public interface ILetterSadereService
{
    /// <summary>ایجاد نامه صادره جدید + ثبت خودکار در LetterSource (SourceType=2)</summary>
    Task<ApiResult<int>> CreateAsync(AddLetterSadereDto dto, int creatorUserId, string creatorName);

    /// <summary>ویرایش نامه صادره (فقط قبل از اولین ارسال)</summary>
    Task<ApiResult> EditAsync(int id, EditLetterSadereDto dto, int userId, bool isAdmin);

    /// <summary>حذف منطقی نامه صادره</summary>
    Task<ApiResult> DeleteAsync(int id, int userId, bool isAdmin);

    /// <summary>دریافت لیست نامه‌های صادره</summary>
    Task<List<LetterSadereListItemDto>> GetListAsync(string? search, bool? archived);

    /// <summary>دریافت جزئیات کامل نامه صادره</summary>
    Task<LetterSadereDetailDto?> GetDetailAsync(int id);
}

/// <summary>نتیجه ساده API</summary>
public class ApiResult
{
    public bool Success { get; set; }
    public int StatusCode { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>نتیجه API به همراه داده</summary>
public class ApiResult<T> : ApiResult
{
    public T? Data { get; set; }
}