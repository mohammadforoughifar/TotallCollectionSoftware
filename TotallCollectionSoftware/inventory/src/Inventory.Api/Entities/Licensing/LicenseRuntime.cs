using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// وضعیت زمان‌دارِ «خودِ این نصب» — نه لایسنس، نه کسب‌وکار.
///
/// <para>
/// اولین مصرف‌کننده: زمان «اولین استارت این بیلد». توکن‌های بدون <c>sid</c> (توکن‌هایی
/// که نسخهٔ قبلی صادر کرده) فقط تا پنجرهٔ کوتاهی بعد از استقرار بیلد تازه پذیرفته
/// می‌شوند؛ پس این زمان باید جایی <b>مقاوم</b> ذخیره شود، نه در حافظه که با ری‌استارت
/// صفر می‌شود (وگرنه پنجره تا ابد باز می‌ماند) و نه در فایل کنار برنامه که با انتشار
/// بعدی پاک می‌شود.
/// </para>
///
/// <para>
/// جدول کلید/مقدار است تا برای وضعیت‌های کوچک بعدی (مثلاً آخرین اجرای پاک‌سازی) هم
/// استفاده شود، بدون مهاجرت تازه.
/// </para>
/// </summary>
public class LicenseRuntime
{
    /// <summary>کلید ثابت وضعیت (مثل <c>build.firstStart</c>).</summary>
    [MaxLength(64)]
    public string Key { get; set; } = "";

    /// <summary>مقدار متنی؛ برای زمان‌ها از <see cref="DateTime"/> با فرمت ISO استفاده می‌شود.</summary>
    [MaxLength(400)]
    public string? Value { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
