using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

// ============================================================
//  تنظیمات ساختار شماره نامه (اندیکاتور)
//  ------------------------------------------------------------
//  شماره‌ی هر نامه از چند «جزء» ساخته می‌شود و ترتیب اجزا، جداکننده،
//  تعداد ارقام سریال، پیشوند/پسوند و قاعده‌ی ریست شمارنده در این جدول
//  تنظیم می‌شود. تنظیمات به‌ازای هر نوع نامه (داخلی/صادره/وارده) جداست.
// ============================================================

/// <summary>تنظیمات ساختار شماره نامه — یک رکورد به ازای هر نوع نامه</summary>
public class LetterNumberSetting
{
    public int Id { get; set; }

    /// <summary>نوع نامه (در نسخه فعلی 1=داخلی)</summary>
    public int SourceType { get; set; } = 1;

    /// <summary>
    /// ترتیب اجزای شماره — لیستِ کدهای جزء با کاما، به همان ترتیبی که در شماره می‌آید.
    /// کدهای معتبر: prefix | year | month | day | dept | company | user | serial | suffix | text1 | text2
    /// مثال: "prefix,year,serial"  →  «ف‌آ/1404/125»
    /// </summary>
    [MaxLength(300)]
    public string PartsOrder { get; set; } = "year,serial";

    /// <summary>جداکننده‌ی اجزا (مثلاً «/» یا «-»)</summary>
    [MaxLength(5)]
    public string Separator { get; set; } = "/";

    /// <summary>متن ثابت پیشوند (جزء prefix)</summary>
    [MaxLength(30)]
    public string? Prefix { get; set; }

    /// <summary>متن ثابت پسوند (جزء suffix)</summary>
    [MaxLength(30)]
    public string? Suffix { get; set; }

    /// <summary>متن ثابت دلخواه ۱ (جزء text1)</summary>
    [MaxLength(30)]
    public string? Text1 { get; set; }

    /// <summary>متن ثابت دلخواه ۲ (جزء text2)</summary>
    [MaxLength(30)]
    public string? Text2 { get; set; }

    /// <summary>تعداد ارقام سال: 2 (مثلاً ۰۴) یا 4 (مثلاً ۱۴۰۴)</summary>
    public int YearDigits { get; set; } = 4;

    /// <summary>تعداد ارقام شماره سریال (با صفر ابتدایی پر می‌شود). 0 = بدون صفر اضافه</summary>
    public int SerialDigits { get; set; } = 0;

    /// <summary>شماره‌ی شروع شمارنده در هر دوره (حداقل ۱)</summary>
    public int StartNumber { get; set; } = 1;

    /// <summary>گام افزایش شمارنده</summary>
    public int Step { get; set; } = 1;

    /// <summary>قاعده‌ی صفر شدن شمارنده: Yearly | Monthly | Never</summary>
    [MaxLength(20)]
    public string ResetPolicy { get; set; } = "Yearly";

    /// <summary>ارقام فارسی در شماره‌ی ذخیره‌شده استفاده شود؟ (پیش‌فرض: خیر — لاتین ذخیره می‌شود)</summary>
    public bool UsePersianDigits { get; set; }

    /// <summary>کد واحد/دپارتمان پیش‌فرض (جزء dept) — وقتی کاربر دپارتمان ندارد</summary>
    [MaxLength(30)]
    public string? DefaultDeptCode { get; set; }

    /// <summary>کد شرکت پیش‌فرض (جزء company)</summary>
    [MaxLength(100)]
    public string? DefaultCompanyCode { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
