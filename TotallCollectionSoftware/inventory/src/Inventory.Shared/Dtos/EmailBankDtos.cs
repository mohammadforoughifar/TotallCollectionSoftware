using System.ComponentModel.DataAnnotations;

namespace Inventory.Shared.Dtos;

// ============================================================
//  بانک ایمیل مخاطبین (دبیرخانه)
//  هدف: کاربر دبیرخانه برای هر ارسال، آدرس ایمیل مقصد را از ایمیل شخصی
//  یا حافظهٔ خودش پیدا نکند؛ آدرس‌هایی که یک‌بار استفاده/انتخاب شده‌اند
//  در این بانک می‌مانند و در فرم «دبیرخانه یکپارچه» به‌صورت پیشنهاد می‌آیند.
// ============================================================

/// <summary>یک ردیف بانک ایمیل مخاطبین</summary>
public class EmailBankEntryDto
{
    public int Id { get; set; }

    /// <summary>آدرس ایمیل (کلید یکتای بانک)</summary>
    public string Email { get; set; } = "";

    /// <summary>سازمان/شرکت مقصد — مبنای پیشنهاد اول در فرم نامه صادره</summary>
    public string? Organization { get; set; }

    /// <summary>نام شخص مخاطب (اختیاری)</summary>
    public string? PersonName { get; set; }

    /// <summary>توضیح آزاد</summary>
    public string? Note { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }

    /// <summary>چند بار در ارسال‌ها استفاده شده — مرتب‌سازی پیشنهادها با این عدد</summary>
    public int UseCount { get; set; }

    /// <summary>آخرین کاربری که این آدرس را استفاده/ثبت کرده (نام نمایشی)</summary>
    public string? CreatedBy { get; set; }
}

/// <summary>ثبت/ویرایش دستی یک ردیف بانک ایمیل</summary>
public class SaveEmailBankEntryDto
{
    public int Id { get; set; }

    [Required(ErrorMessage = "آدرس ایمیل الزامی است")]
    [EmailAddress(ErrorMessage = "آدرس ایمیل معتبر نیست")]
    public string Email { get; set; } = "";

    public string? Organization { get; set; }
    public string? PersonName { get; set; }
    public string? Note { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// نتیجهٔ «افزودن/به‌خاطر‌سپاری» یک آدرس.
/// Duplicate = true یعنی آدرس از قبل در بانک بود و ردیف تکراری ساخته نشد
/// (رابط کاربری باید پیام «قبلاً ثبت شده» بدهد).
/// </summary>
public class EmailBankSaveResultDto
{
    public int Id { get; set; }
    public bool Created { get; set; }
    public bool Duplicate { get; set; }
    public string Message { get; set; } = "";

    public EmailBankEntryDto? Entry { get; set; }
}

/// <summary>نتیجهٔ جست‌وجوی پیشنهادها در فرم ارسال</summary>
public class EmailBankSuggestDto
{
    /// <summary>آدرس‌های همان سازمان گیرنده (اولویت اول)</summary>
    public List<EmailBankEntryDto> OrganizationMatches { get; set; } = new();

    /// <summary>سایر آدرس‌های بانک که با عبارت جست‌وجو می‌خوانند</summary>
    public List<EmailBankEntryDto> OtherMatches { get; set; } = new();
}
