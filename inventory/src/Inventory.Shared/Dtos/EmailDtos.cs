using System.ComponentModel.DataAnnotations;

namespace Inventory.Shared.Dtos;

// ============================================================
//  ایمیل سازمانی (پست الکترونیک) — حساب‌ها، صندوق، ارسال، بایگانی
//  جداول مرتبط: Oto_TBL_Email / Oto_TBL_Sent / Oto_TBL_Inbox /
//               Oto_TBl_EmailFolder / Oto_TBL_EmailAttachments
// ============================================================

/// <summary>حساب ایمیل کاربر — هر کاربر می‌تواند چند حساب داشته باشد</summary>
public class EmailAccountDto
{
    public int EmailId { get; set; }
    public string EmailAddress { get; set; } = "";
    public string Smtp { get; set; } = "";
    public string Imap { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public bool SmtpSsl { get; set; } = true;
    public int ImapPort { get; set; } = 993;
    public bool ImapSsl { get; set; } = true;
    public string EmailType { get; set; } = "Gmail";
    public bool IsActive { get; set; } = true;

    /// <summary>true = ایمیل رسمی دبیرخانه — جدا از ایمیل شخصی</summary>
    public bool IsDabirkhane { get; set; }

    public string? DisplayName { get; set; }
    public DateTime? LastSync { get; set; }

    /// <summary>مالک حساب</summary>
    public int UserId { get; set; }
    public string UserName { get; set; } = "";

    /// <summary>آیا این حساب متعلق به کاربر جاری است؟</summary>
    public bool IsMine { get; set; }
}

/// <summary>ذخیره/ویرایش حساب ایمیل</summary>
public class SaveEmailAccountDto
{
    public int EmailId { get; set; }

    [Required(ErrorMessage = "آدرس ایمیل الزامی است")]
    [EmailAddress(ErrorMessage = "آدرس ایمیل معتبر نیست")]
    public string EmailAddress { get; set; } = "";

    [Required(ErrorMessage = "رمز عبور (App Password) الزامی است")]
    public string Password { get; set; } = "";

    /// <summary>Gmail / Yahoo / Outlook / Webmail / Custom</summary>
    public string EmailType { get; set; } = "Gmail";

    public string Smtp { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public bool SmtpSsl { get; set; } = true;
    public string Imap { get; set; } = "";
    public int ImapPort { get; set; } = 993;
    public bool ImapSsl { get; set; } = true;
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>ثبت به‌عنوان ایمیل رسمی دبیرخانه — فقط دارندگان دسترسی دبیرخانه صادره</summary>
    public bool IsDabirkhane { get; set; }
}

/// <summary>سطر لیست ایمیل (دریافتی یا ارسالی)</summary>
public class EmailMessageListItemDto
{
    /// <summary>Inbox_Id یا Sent_Id</summary>
    public int Id { get; set; }

    /// <summary>Inbox یا Sent</summary>
    public string Box { get; set; } = "Inbox";

    public int EmailId { get; set; }
    public string AccountAddress { get; set; } = "";

    /// <summary>دریافتی: فرستنده | ارسالی: گیرنده</summary>
    public string FromOrTo { get; set; } = "";
    public string Subject { get; set; } = "";
    public DateTime Date { get; set; }
    public bool IsRead { get; set; }
    public bool IsNeshan { get; set; }
    public bool IsAttachment { get; set; }
    public int IsInFolder { get; set; }
    public string? FolderTitle { get; set; }
}

/// <summary>جزئیات کامل یک ایمیل</summary>
public class EmailMessageDetailDto
{
    public int Id { get; set; }
    public string Box { get; set; } = "Inbox";
    public int EmailId { get; set; }
    public string AccountAddress { get; set; } = "";
    public string FromOrTo { get; set; } = "";
    public string Subject { get; set; } = "";
    public DateTime Date { get; set; }
    public bool IsRead { get; set; }
    public bool IsNeshan { get; set; }
    public string Body { get; set; } = "";
    public int IsInFolder { get; set; }
    public List<EmailAttachmentDto> Attachments { get; set; } = new();
}

/// <summary>پیوست ایمیل</summary>
public class EmailAttachmentDto
{
    public int AttachmentId { get; set; }
    public string RealName { get; set; } = "";
    public long Size { get; set; }
}

/// <summary>ارسال ایمیل جدید</summary>
public class EmailComposeDto
{
    /// <summary>حساب فرستنده</summary>
    [Required(ErrorMessage = "حساب فرستنده الزامی است")]
    public int EmailAccountId { get; set; }

    /// <summary>گیرنده(ها) با کاما جدا شوند</summary>
    [Required(ErrorMessage = "گیرنده الزامی است")]
    public string To { get; set; } = "";

    /// <summary>گیرندگان رونوشت (اختیاری)</summary>
    public string? Cc { get; set; }

    [Required(ErrorMessage = "موضوع الزامی است")]
    public string Subject { get; set; } = "";

    /// <summary>متن HTML</summary>
    public string Body { get; set; } = "";
}

/// <summary>پوشه بایگانی ایمیل (Oto_TBl_EmailFolder با IsFolder=true)</summary>
public class EmailFolderDto
{
    public int EmailFolderId { get; set; }
    public string Title { get; set; } = "";
    public int ParentId { get; set; }

    /// <summary>0=دریافتی، 1=ارسالی</summary>
    public int TypeEmail { get; set; }
}

/// <summary>ایجاد/ویرایش پوشه بایگانی</summary>
public class SaveEmailFolderDto
{
    public int EmailFolderId { get; set; }
    [Required(ErrorMessage = "عنوان پوشه الزامی است")]
    public string Title { get; set; } = "";
    public int ParentId { get; set; }

    /// <summary>0=دریافتی، 1=ارسالی</summary>
    public int TypeEmail { get; set; }
}

/// <summary>نتیجه تست اتصال حساب ایمیل</summary>
public class EmailTestResultDto
{
    public bool SmtpOk { get; set; }
    public bool ImapOk { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>نتیجه همگام‌سازی صندوق</summary>
public class EmailSyncResultDto
{
    public int NewCount { get; set; }
    public int TotalCount { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>بایگانی/خروج ایمیل در پوشه — معادل ستون IsInFolder</summary>
public class EmailArchiveDto
{
    /// <summary>Inbox یا Sent</summary>
    public string Box { get; set; } = "Inbox";

    /// <summary>شناسه پیام</summary>
    public int Id { get; set; }

    /// <summary>شناسه پوشه — 0 یعنی خروج از بایگانی</summary>
    public int FolderId { get; set; }
}

/// <summary>پیش‌تنظیم سرورهای سرویس‌دهنده‌های معروف — Gmail، Yahoo، Outlook، وب‌میل و سفارشی</summary>
public static class OtoEmailPresets
{
    public sealed record Preset(string Type, string Smtp, int SmtpPort, bool SmtpSsl, string Imap, int ImapPort, bool ImapSsl, string Hint);

    public static readonly Preset[] All =
    {
        new("Gmail", "smtp.gmail.com", 587, true, "imap.gmail.com", 993, true,
            "برای Gmail باید «رمز برنامه» (App Password) از Google Account بسازید — رمز اصلی کار نمی‌کند."),
        new("Yahoo", "smtp.mail.yahoo.com", 465, true, "imap.mail.yahoo.com", 993, true,
            "برای Yahoo هم «App Password» لازم است (Account Security ← Generate app password)."),
        new("Outlook", "smtp.office365.com", 587, true, "outlook.office365.com", 993, true,
            "برای Outlook/Office365 احراز هویت اولیه (Basic Auth) باید در مستاجر فعال باشد."),
        new("Webmail", "", 587, true, "", 993, true,
            "وب‌میل هاست/سازمان — معمولاً mail.دامنه-شما با پورت 587 برای ارسال و 993 برای دریافت است. مقادیر را از هاست خود بگیرید."),
        new("Custom", "", 587, true, "", 993, true, "تنظیمات دستی سرور SMTP و IMAP.")
    };

    public static Preset? Find(string? type) =>
        All.FirstOrDefault(p => string.Equals(p.Type, type, StringComparison.OrdinalIgnoreCase));

    /// <summary>عنوان فارسی نوع حساب</summary>
    public static string Title(string? type) => type switch
    {
        "Gmail" => "جیمیل",
        "Yahoo" => "یاهو",
        "Outlook" => "اوت‌لوک / Office365",
        "Webmail" => "وب‌میل (هاست/سازمان)",
        _ => "سفارشی"
    };
}
