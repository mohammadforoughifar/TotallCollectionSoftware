using System.ComponentModel.DataAnnotations;

namespace Inventory.Shared.Dtos;

// ============================================================
//  ماژول نامه صادره (نامه به خارج از سازمان) — فاز دوم
//  شماره‌گذاری مستقل، ساختار مجزا، اما کلید مرجع مشترک با LetterSource
// ============================================================

/// <summary>ایجاد نامه صادره جدید</summary>
public class AddLetterSadereDto
{
    /// <summary>فوریت: 1=عادی، 2=فوری، 3=آنی</summary>
    [Range(1, 3)] public int Foriat { get; set; } = 1;

    /// <summary>محرمانگی: 1=عادی، 2=محرمانه، 3=سری</summary>
    [Range(1, 3)] public int Mahramangi { get; set; } = 1;

    /// <summary>سمت ایجادکننده — فاز چارت سازمانی (اختیاری)</summary>
    public int? CreatorSematId { get; set; }

    [Required(ErrorMessage = "عنوان نامه الزامی است")]
    [MaxLength(300)]
    public string Title { get; set; } = "";

    public string? Text { get; set; }

    /// <summary>تاریخ ارسال (اختیاری — پیش‌فرض هم‌اکنون)</summary>
    public DateTime? DateErsal { get; set; }

    /// <summary>آیا بلافاصله ارسال شود؟</summary>
    public bool IsSent { get; set; }

    /// <summary>مرجع ارسال‌کننده (دپارتمان/واحد)</summary>
    [Range(1, int.MaxValue, ErrorMessage = "مرجع ارسال الزامی است")]
    public int MarjeErsalId { get; set; }

    /// <summary>شماره ثبت در مقصد</summary>
    public int? NumberSabtMaghsad { get; set; }

    /// <summary>گیرنده اصلی در خارج از سازمان</summary>
    [MaxLength(200)]
    public string? GirandeAsli { get; set; }

    /// <summary>نام شخص ارسال‌کننده/حامل</summary>
    [MaxLength(200)]
    public string? TransferName { get; set; }
}

/// <summary>ویرایش نامه صادره</summary>
public class EditLetterSadereDto
{
    [Range(1, 3)] public int Foriat { get; set; } = 1;
    [Range(1, 3)] public int Mahramangi { get; set; } = 1;

    [MaxLength(300)]
    [Required(ErrorMessage = "عنوان الزامی است")]
    public string? Title { get; set; }

    public string? Text { get; set; }
    public DateTime? DateErsal { get; set; }
    public int? NumberSabtMaghsad { get; set; }

    [MaxLength(200)]
    public string? GirandeAsli { get; set; }

    [MaxLength(200)]
    public string? TransferName { get; set; }
}

/// <summary>نمایش نامه صادره در لیست</summary>
public class LetterSadereListItemDto
{
    public int Id { get; set; }
    public string LetterNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public string? GirandeAsli { get; set; }
    public int Foriat { get; set; }
    public int Mahramangi { get; set; }
    public bool IsSent { get; set; }
    public DateTime? DateErsal { get; set; }
    public DateTime DateSabt { get; set; }
    public string CreatorName { get; set; } = "";
    public bool IsArchived { get; set; }
}

/// <summary>جزئیات کامل نامه صادره</summary>
public class LetterSadereDetailDto
{
    public int Id { get; set; }
    public string LetterNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Text { get; set; }
    public int Foriat { get; set; }
    public int Mahramangi { get; set; }
    public DateTime? DateErsal { get; set; }
    public bool IsSent { get; set; }
    public int MarjeErsalId { get; set; }
    public string? MarjeErsalName { get; set; }
    public int? NumberSabtMaghsad { get; set; }
    public string? GirandeAsli { get; set; }
    public string? TransferName { get; set; }
    public bool IsArchived { get; set; }
    public DateTime DateSabt { get; set; }
    public string CreatorName { get; set; } = "";
    public int CreatorUserId { get; set; }
}