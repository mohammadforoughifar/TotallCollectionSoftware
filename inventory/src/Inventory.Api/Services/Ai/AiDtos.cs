namespace Inventory.Api.Services.Ai;

// =====================================================================
// DTO های API دستیار — قرارداد بین کنترلر، سرویس‌ها و کلاینت Blazor
// =====================================================================

public class AiHealthDto
{
    public bool Enabled { get; set; }
    public bool ModelReachable { get; set; }
    public string ChatModel { get; set; } = "";
    public string? Error { get; set; }
}

public class AiChatRequest
{
    public int? ConversationId { get; set; }
    public string Message { get; set; } = "";
}

public class AiChatResponse
{
    public int ConversationId { get; set; }
    public string Reply { get; set; } = "";
    public bool UsedFallback { get; set; }
    public List<string> ToolsUsed { get; set; } = new();
}

public class AiConversationDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Channel { get; set; } = "web";
    public DateTime LastMessageAtUtc { get; set; }
    public List<AiMessageDto> Messages { get; set; } = new();
}

public class AiMessageDto
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
    public bool UsedFallback { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

// ---------------- هوش نامه‌ها ----------------

public class AiLetterDraftRequest
{
    /// <summary>موضوع یا شرح کوتاه خواسته (مثلا «درخواست خرید ۵ لپ‌تاپ برای واحد مالی»).</summary>
    public string Topic { get; set; } = "";
    /// <summary>لحن: رسمی | دوستانه | دستوری</summary>
    public string Tone { get; set; } = "رسمی";
    /// <summary>نوع: داخلی | صادره | جوابیه | درخواست | ابلاغ</summary>
    public string Kind { get; set; } = "داخلی";
    /// <summary>نام گیرنده (اختیاری — برای خطاب دقیق‌تر).</summary>
    public string? ToName { get; set; }
    /// <summary>جزئیات اضافه (اختیاری).</summary>
    public string? ExtraContext { get; set; }
}

public class AiLetterDraftDto
{
    public string Subject { get; set; } = "";
    public string BodyHtml { get; set; } = "";
    public bool UsedFallback { get; set; }
}

public class AiLetterSummaryDto
{
    public int LetterId { get; set; }
    public string Summary { get; set; } = "";
    /// <summary>وضعیت فعلی گردش: دست کیست / آخرین اقدام.</summary>
    public string CurrentStatus { get; set; } = "";
    public bool UsedFallback { get; set; }
}

public class AiReferralSuggestionDto
{
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public string? Department { get; set; }
    public string Reason { get; set; } = "";
}

public class AiReferralSuggestResponse
{
    public int LetterId { get; set; }
    public List<AiReferralSuggestionDto> Suggestions { get; set; } = new();
    public bool UsedFallback { get; set; }
}

public class AiLetterTaskDto
{
    public string Action { get; set; } = "";
    public string? AssigneeHint { get; set; }
    public string? DeadlineText { get; set; }
    public DateTime? DeadlineDate { get; set; }
}

public class AiLetterTasksResponse
{
    public int LetterId { get; set; }
    public List<AiLetterTaskDto> Tasks { get; set; } = new();
    public bool UsedFallback { get; set; }
}

public class AiLetterCategoryDto
{
    public int LetterId { get; set; }
    public string Category { get; set; } = "";
    /// <summary>اولویت ۱ (کم) تا ۵ (خیلی فوری).</summary>
    public int Priority { get; set; } = 3;
    public List<string> Tags { get; set; } = new();
    public bool UsedFallback { get; set; }
}

public class AiMinutesDraftRequest
{
    /// <summary>متن خام / شرح جلسه (می‌تواند رونوشت ویس هم باشد).</summary>
    public string RawText { get; set; } = "";
    /// <summary>عنوان جلسه (اختیاری).</summary>
    public string? Title { get; set; }
}

public class AiMinutesItemDto
{
    public string Text { get; set; } = "";
    /// <summary>نوع: مصوبه | اقدام | اطلاع</summary>
    public string Kind { get; set; } = "مصوبه";
    public string? Responsible { get; set; }
    public string? DeadlineText { get; set; }
}

public class AiMinutesDraftDto
{
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<AiMinutesItemDto> Items { get; set; } = new();
    public bool UsedFallback { get; set; }
}
