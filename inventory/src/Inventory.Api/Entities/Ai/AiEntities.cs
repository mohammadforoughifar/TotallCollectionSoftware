using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

// =====================================================================
// انتیتی‌های «هوش مصنوعی فروغ آریا» — دستیار مرکزی + نامه‌ها + منشی کارمندی
// جدول‌ها با AiSchemaV1.EnsureAsync خودکار ساخته می‌شوند (SQL Server و SQLite).
// =====================================================================

/// <summary>یک رشته گفتگوی کاربر با دستیار (در وب، بله یا پیام‌رسان داخلی).</summary>
public class AiConversation
{
    public int Id { get; set; }

    public int UserId { get; set; }

    /// <summary>کانال گفتگو: web | bale | messenger</summary>
    [MaxLength(20)]
    public string Channel { get; set; } = "web";

    [MaxLength(200)]
    public string Title { get; set; } = "گفتگوی جدید";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastMessageAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsArchived { get; set; }
}

/// <summary>یک پیام داخل گفتگو (کاربر یا دستیار).</summary>
public class AiMessage
{
    public int Id { get; set; }

    public int ConversationId { get; set; }

    /// <summary>user | assistant</summary>
    [MaxLength(20)]
    public string Role { get; set; } = "user";

    public string Content { get; set; } = "";

    /// <summary>نام ابزارهایی که برای این پاسخ استفاده شد (با کاما) — برای شفافیت و دیباگ.</summary>
    [MaxLength(500)]
    public string? ToolsUsed { get; set; }

    /// <summary>آیا پاسخ در حالت آفلاین (بدون مدل زبانی) تولید شده؟</summary>
    public bool UsedFallback { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>مقاله راهنمای سیستم برای جستجوی معنایی دستیار (راهنمای هوشمند — قابلیت ۵).</summary>
public class AiKnowledgeDoc
{
    public int Id { get; set; }

    /// <summary>دسته: مثلا «فروش»، «مرخصی»، «نامه‌ها».</summary>
    [MaxLength(100)]
    public string Category { get; set; } = "";

    [MaxLength(200)]
    public string Title { get; set; } = "";

    public string Content { get; set; } = "";

    /// <summary>لینک صفحه مرتبط داخل نرم‌افزار (مثلا /fa-att/my-leaves) — می‌تواند خالی باشد.</summary>
    [MaxLength(300)]
    public string? Link { get; set; }

    /// <summary>کلید یکتا برای سید مجدد بدون تکرار (مثلا guide:leave-request).</summary>
    [MaxLength(100)]
    public string DocKey { get; set; } = "";

    /// <summary>بردار امبدینگ به‌صورت JSON (برای جستجوی معنایی) — تنبل و خودکار پر می‌شود.</summary>
    public string? EmbeddingJson { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
