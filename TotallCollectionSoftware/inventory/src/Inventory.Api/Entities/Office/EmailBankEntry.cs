using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Inventory.Api.Data;

// ============================================================
//  بانک ایمیل مخاطبین دبیرخانه — Oto_TBL_EmailBank
//  • آدرس‌هایی که در ارسال نامه صادره استفاده/انتخاب می‌شوند این‌جا ذخیره می‌شوند
//    تا کاربر دبیرخانه مجبور نباشد هر بار آدرس را از ایمیل‌ها پیدا کند.
//  • Email یکتا است (مقایسهٔ بدون حساسیت به حروف بزرگ/کوچک)؛ تلاش برای ثبت
//    آدرس تکراری ردیف جدید نمی‌سازد و پیام «قبلاً ثبت شده» برمی‌گرداند.
//  • Organization مبنای پیشنهاد اول در فرم است: آدرس‌های همان سازمان گیرنده
//    بالای فهرست پیشنهادها می‌آیند.
// ============================================================

[Table("Oto_TBL_EmailBank")]
public class OtoEmailBankEntry
{
    [Key]
    [Column("Id")]
    public int Id { get; set; }

    /// <summary>آدرس ایمیل — یکتا (بدون حساسیت به حروف بزرگ/کوچک)</summary>
    [Column("Email")]
    [MaxLength(250)]
    public string Email { get; set; } = "";

    /// <summary>سازمان/شرکت مقصد</summary>
    [Column("Organization")]
    [MaxLength(250)]
    public string? Organization { get; set; }

    /// <summary>نام شخص مخاطب</summary>
    [Column("PersonName")]
    [MaxLength(250)]
    public string? PersonName { get; set; }

    /// <summary>توضیح آزاد</summary>
    [Column("Note")]
    [MaxLength(500)]
    public string? Note { get; set; }

    [Column("IsActive")]
    public bool IsActive { get; set; } = true;

    [Column("CreatedAt")]
    public DateTime CreatedAt { get; set; } = Services.Core.TehranClock.Now();

    /// <summary>آخرین بار که این آدرس در ارسال استفاده شد</summary>
    [Column("LastUsedAt")]
    public DateTime? LastUsedAt { get; set; }

    /// <summary>شمار استفاده در ارسال‌ها — پیشنهادهای پرکاربرد بالاتر می‌آیند</summary>
    [Column("UseCount")]
    public int UseCount { get; set; }

    /// <summary>کاربری که ردیف را ساخت/آخرین بار استفاده کرد</summary>
    [Column("CreatedByUserId")]
    public int? CreatedByUserId { get; set; }

    [Column("CreatedByName")]
    [MaxLength(150)]
    public string? CreatedByName { get; set; }
}
