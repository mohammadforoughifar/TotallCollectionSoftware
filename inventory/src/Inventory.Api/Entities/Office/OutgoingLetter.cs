using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Inventory.Api.Data;

// ============================================================
//  ماژول اتوماسیون اداری — فاز دوم: نامه صادره (Outgoing)
//  ساختار مشابه نامه داخلی اما با گیرنده بیرونی + وضعیت صدور
//  SourceType = 2 در LetterSource
//  پوشه‌بندی تمیز: هر موجودیت در فایل اختصاصی خودش
// ============================================================

/// <summary>
/// نامه صادره — شناسه = همان شناسه LetterSource (کلید مشترک)
/// شماره‌گذاری اندیکاتور بر اساس سال شمسی (هر سال از ۱ شروع می‌شود)
/// مانند نامه داخلی اما با اطلاعات گیرنده بیرونی
/// </summary>
public class OutgoingLetter
{
    /// <summary>کلید مشترک با LetterSource — دستی مقداردهی می‌شود</summary>
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }

    /// <summary>شماره اندیکاتور کامل نامه — مثل «1404/ص-12» یا «1404/12»</summary>
    [MaxLength(60)] public string? LetterNumber { get; set; }

    /// <summary>شماره ترتیبی داخل سال شمسی (هر سال از ۱ شروع می‌شود)</summary>
    public int Number { get; set; }

    /// <summary>کاربر ثبت‌کننده (فرستنده/صادرکننده)</summary>
    public int CreatorUserId { get; set; }

    /// <summary>سمت ثبت‌کننده — بعد از پیاده‌سازی چارت سازمانی فعال می‌شود</summary>
    public int? CreatorSematId { get; set; }

    [MaxLength(300)] public string Title { get; set; } = "";
    public string? Text { get; set; }

    public DateTime DateSabt { get; set; } = DateTime.Now;

    /// <summary>محرمانگی: عادی / محرمانه / سری</summary>
    [MaxLength(20)] public string Mahramanegi { get; set; } = "عادی";

    /// <summary>فوریت: عادی / فوری / آنی</summary>
    [MaxLength(20)] public string Foriat { get; set; } = "عادی";

    // ==================== گیرنده بیرونی (اطلاعات صادره) ====================

    /// <summary>سازمان/شرکت مقصد — الزامی برای نامه صادره</summary>
    [MaxLength(250)] public string ReceiverOrganization { get; set; } = "";

    /// <summary>نام شخص گیرنده در سازمان مقصد (اختیاری)</summary>
    [MaxLength(250)] public string? ReceiverName { get; set; }

    /// <summary>سمت گیرنده (اختیاری)</summary>
    [MaxLength(250)] public string? ReceiverTitle { get; set; }

    /// <summary>آدرس/توضیحات مقصد (اختیاری)</summary>
    [MaxLength(500)] public string? ReceiverAddress { get; set; }

    /// <summary>رونوشت‌ها — متن آزاد (مثلاً: «واحد مالی - جهت اطلاع»)</summary>
    [MaxLength(1000)] public string? CopyTo { get; set; }

    /// <summary>شماره نامه طرف مقابل در صورت وجود (برای مکاتبات دوطرفه)</summary>
    [MaxLength(100)] public string? ExternalRefNumber { get; set; }

    /// <summary>شماره صادره رسمی — بعد از امضا مقدار می‌گیرد (درخواست کارفرما: SadereNumber)</summary>
    [MaxLength(60)] public string? SadereNumber { get; set; }

    /// <summary>تاریخ صدور رسمی — زمان امضای نهایی</summary>
    public DateTime? DateSadere { get; set; }

    /// <summary>وضعیت صدور: 0=پیش‌نویس داخلی، 1=در گردش تایید، 2=تایید شده، 3=صادر شده</summary>
    public int Status { get; set; } = 0;

    public bool IsDelete { get; set; }

    [ForeignKey(nameof(Id))] public LetterSource Source { get; set; } = null!;
    public User? Creator { get; set; }

    public ICollection<OutgoingLetterSigner> Signers { get; set; } = new List<OutgoingLetterSigner>();
}

/// <summary>
/// امضا کنندگان نامه صادره — هر نامه صادره می‌تواند چند امضا کننده داشته باشد
/// با توجه به دسترسی افراد (RBAC: OutgoingLetters.Sign) انتخاب می‌شوند
/// وقتی امضا شد SadereNumber مقدار می‌گیرد
/// </summary>
public class OutgoingLetterSigner
{
    public int Id { get; set; }

    /// <summary>کلید نامه (LetterSource)</summary>
    public int SourceId { get; set; }

    public int UserId { get; set; }

    /// <summary>سمت — فاز چارت سازمانی</summary>
    public int? SematId { get; set; }

    /// <summary>ترتیب امضا (1=اولین امضا کننده)</summary>
    public int Order { get; set; } = 1;

    public bool IsSigned { get; set; }
    public DateTime? DateSigned { get; set; }

    /// <summary>توضیح/پاراف امضا کننده (اختیاری)</summary>
    [MaxLength(1000)] public string? SignNote { get; set; }

    public bool IsDelete { get; set; }

    [ForeignKey(nameof(SourceId))] public LetterSource Source { get; set; } = null!;
    public User? User { get; set; }
}

/// <summary>پیش‌نویس نامه صادره — مشابه PishnevisLetter اما با فیلدهای صادره</summary>
public class OutgoingPishnevisLetter
{
    public int PishnevisId { get; set; }

    [MaxLength(300)] public string Title { get; set; } = "";
    public string Text { get; set; } = "";

    [MaxLength(250)] public string? ReceiverOrganization { get; set; }
    [MaxLength(250)] public string? ReceiverName { get; set; }
    [MaxLength(250)] public string? ReceiverTitle { get; set; }

    public int UserId { get; set; }
    public int? SematId { get; set; }

    public bool IsNeshan { get; set; }
    public bool IsDelete { get; set; }

    public User? User { get; set; }
}
