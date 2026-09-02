using System.ComponentModel.DataAnnotations;

namespace Inventory.Shared.Dtos;

// ============================================================
//  ماژول اتوماسیون اداری — فاز دوم: نامه صادره (Outgoing)
//  DTO ها مشابه نامه داخلی اما با گیرنده بیرونی + وضعیت صدور
//  پوشه‌بندی تمیز و خوانا — هر DTO با کامنت فارسی
// ============================================================

/// <summary>ثبت/ویرایش نامه صادره — معادل AddInnerLetterDto اما با گیرنده بیرونی</summary>
public class AddOutgoingLetterDto
{
    public int LetterId { get; set; }

    [Required(ErrorMessage = "عنوان نامه الزامی است")]
    public string Title { get; set; } = "";
    public string? Text { get; set; }

    /// <summary>عادی / محرمانه / سری</summary>
    public string Mahramanegi { get; set; } = "عادی";

    /// <summary>عادی / فوری / آنی</summary>
    public string Foriat { get; set; } = "عادی";

    // ==================== گیرنده بیرونی — الزامی برای صادره ====================

    [Required(ErrorMessage = "نام سازمان مقصد الزامی است")]
    public string ReceiverOrganization { get; set; } = "";

    public string? ReceiverName { get; set; }
    public string? ReceiverTitle { get; set; }
    public string? ReceiverAddress { get; set; }

    /// <summary>رونوشت‌ها — متن آزاد</summary>
    public string? CopyTo { get; set; }

    public string? ExternalRefNumber { get; set; }

    // ==================== گردش داخلی جهت تایید قبل از صدور ====================

    /// <summary>گیرندگان اصلی داخلی (جهت تایید/اطلاع قبل از صدور)</summary>
    public List<int> ReciversGirande { get; set; } = new();

    /// <summary>گیرندگان ارجاع (جهت اقدام)</summary>
    public List<int> ReciversErja { get; set; } = new();

    /// <summary>گیرندگان هامش (رونوشت داخلی)</summary>
    public List<int> ReciversHamesh { get; set; } = new();

    public List<int> GroupsGirande { get; set; } = new();
    public List<int> GroupsErja { get; set; } = new();
    public List<int> GroupsHamesh { get; set; } = new();

    /// <summary>نامه‌های مرتبط: عطف (2) / پیرو (1)</summary>
    public List<RelatedLetterDto> RelatedLetters { get; set; } = new();

    /// <summary>شناسه پیش‌نویس مبدأ — بعد از ارسال حذف می‌شود (اختیاری)</summary>
    public int? FromPishnevisId { get; set; }

    // ==================== امضا کنندگان — انتخاب بر اساس دسترسی ====================

    /// <summary>کاربران امضا کننده — باید دسترسی OutgoingLetters.Sign داشته باشند</summary>
    public List<int> SignerUserIds { get; set; } = new();

    /// <summary>گروه‌های امضا کننده — اعضای گروه باید دسترسی Sign داشته باشند</summary>
    public List<int> SignerGroupIds { get; set; } = new();
}

/// <summary>ویرایش نامه صادره — فقط قبل از خوانده‌شدن توسط گیرندگان داخلی یا قبل از صدور نهایی</summary>
public class EditOutgoingLetterDto
{
    [Required(ErrorMessage = "عنوان نامه الزامی است")]
    public string Title { get; set; } = "";
    public string? Text { get; set; }
    public string Mahramanegi { get; set; } = "عادی";
    public string Foriat { get; set; } = "عادی";

    [Required(ErrorMessage = "نام سازمان مقصد الزامی است")]
    public string ReceiverOrganization { get; set; } = "";
    public string? ReceiverName { get; set; }
    public string? ReceiverTitle { get; set; }
    public string? ReceiverAddress { get; set; }
    public string? CopyTo { get; set; }
    public string? ExternalRefNumber { get; set; }

    // گیرندگان داخلی — قابل ویرایش تا قبل از خوانده‌شدن
    public List<int> ReciversGirande { get; set; } = new();
    public List<int> GroupsGirande { get; set; } = new();
    public List<int> ReciversErja { get; set; } = new();
    public List<int> GroupsErja { get; set; } = new();
    public List<int> ReciversHamesh { get; set; } = new();
    public List<int> GroupsHamesh { get; set; } = new();

    public List<RelatedLetterDto> RelatedLetters { get; set; } = new();

    public List<int> SignerUserIds { get; set; } = new();
    public List<int> SignerGroupIds { get; set; } = new();
}

/// <summary>امضا کننده نامه صادره</summary>
public class OutgoingSignerDto
{
    public int Id { get; set; }
    public int SourceId { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int? SematId { get; set; }
    public string? SematTitle { get; set; }
    public int Order { get; set; }
    public bool IsSigned { get; set; }
    public DateTime? DateSigned { get; set; }
    public string? SignNote { get; set; }
}

/// <summary>درخواست امضا</summary>
public class SignOutgoingLetterDto
{
    public string? SignNote { get; set; }
}

/// <summary>سطر لیست کارتابل نامه صادره</summary>
public class OutgoingLetterListItemDto
{
    public int LetterId { get; set; }
    public int? ErjaId { get; set; }
    public string LetterNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public string Sender { get; set; } = "";
    public int SenderUserId { get; set; }

    // گیرنده بیرونی
    public string ReceiverOrganization { get; set; } = "";
    public string? ReceiverName { get; set; }

    public DateTime Date { get; set; }
    public string Mahramanegi { get; set; } = "عادی";
    public string Foriat { get; set; } = "عادی";
    public string? ErjaType { get; set; }
    public string? MatnErja { get; set; }
    public DateTime? MohlatPasokh { get; set; }
    public bool IsNeshan { get; set; }
    public bool HasAttachment { get; set; }
    public bool IsRead { get; set; }
    public int ReciverCount { get; set; }
    public int TypeTaeed { get; set; }
    public bool HasAnswer { get; set; }
    public int Status { get; set; }
    public string StatusTitle => Status switch
    {
        0 => "پیش‌نویس",
        1 => "در گردش",
        2 => "تایید شده",
        3 => "صادر شده",
        _ => "نامشخص"
    };

    // امضا
    public string? SadereNumber { get; set; }
    public DateTime? DateSadere { get; set; }
    public bool IsSigner { get; set; }
    public bool IsSigned { get; set; }
    public bool CanSign { get; set; }
    public int SignersTotal { get; set; }
    public int SignersSigned { get; set; }
}

/// <summary>جزئیات کامل نامه صادره</summary>
public class OutgoingLetterDetailDto
{
    public int LetterId { get; set; }
    public string LetterNumber { get; set; } = "";
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string? Text { get; set; }
    public string Mahramanegi { get; set; } = "عادی";
    public string Foriat { get; set; } = "عادی";
    public DateTime DateSabt { get; set; }
    public int SenderUserId { get; set; }
    public string SenderName { get; set; } = "";

    // گیرنده بیرونی
    public string ReceiverOrganization { get; set; } = "";
    public string? ReceiverName { get; set; }
    public string? ReceiverTitle { get; set; }
    public string? ReceiverAddress { get; set; }
    public string? CopyTo { get; set; }
    public string? ExternalRefNumber { get; set; }
    public int Status { get; set; }

    // شماره صادره رسمی — بعد از امضا
    public string? SadereNumber { get; set; }
    public DateTime? DateSadere { get; set; }

    public List<LetterReciverDto> ReciversGirande { get; set; } = new();
    public List<LetterReciverDto> ReciversErja { get; set; } = new();
    public List<LetterReciverDto> ReciversHamesh { get; set; } = new();
    public List<RelatedLetterDto> RelatedLetters { get; set; } = new();
    public List<OutgoingSignerDto> Signers { get; set; } = new();

    public ErjaDto? MyErja { get; set; }
    public OutgoingSignerDto? MySigner { get; set; }
    public bool IsMine { get; set; }
    public bool CanEdit { get; set; }
    public bool CanSign { get; set; }
    public bool IsSigner { get; set; }
}

/// <summary>پیش‌نویس نامه صادره</summary>
public class OutgoingPishnevisDto
{
    public int PishnevisId { get; set; }

    [Required(ErrorMessage = "عنوان الزامی است")]
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public string? ReceiverOrganization { get; set; }
    public string? ReceiverName { get; set; }
    public string? ReceiverTitle { get; set; }
    public bool IsNeshan { get; set; }
}

/// <summary>آمار کارتابل نامه صادره</summary>
public class OutgoingLetterCartableStatsDto
{
    public int InboxUnread { get; set; }
    public int InboxTotal { get; set; }
    public int SentTotal { get; set; }
    public int PishnevisTotal { get; set; }
    public int DeadlineSoon { get; set; }
    public int DraftTotal { get; set; }
}

/// <summary>آیتم انتخاب نامه برای عطف/پیرو در صادره — شامل هر دو نوع داخلی و صادره</summary>
public class OutgoingLetterPickDto
{
    public int LetterId { get; set; }
    public string LetterNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime Date { get; set; }
    public bool IsSent { get; set; }
    public int SourceType { get; set; }
    public string SourceTypeTitle => SourceType == 2 ? "صادره" : "داخلی";
}
