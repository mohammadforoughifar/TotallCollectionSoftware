namespace Inventory.Client.Services;

// =====================================================================
// سرویس کلاینت «هوش مصنوعی فروغ آریا» — تنها نقطه ساخت مسیرهای API دستیار
// =====================================================================

public class AiHealth
{
    public bool Enabled { get; set; }
    public bool ModelReachable { get; set; }
    public string ChatModel { get; set; } = "";
    public string? Error { get; set; }
}

public class AiChatResult
{
    public int ConversationId { get; set; }
    public string Reply { get; set; } = "";
    public bool UsedFallback { get; set; }
    public List<string> ToolsUsed { get; set; } = new();
}

public class AiConversation
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Channel { get; set; } = "web";
    public DateTime LastMessageAtUtc { get; set; }
    public List<AiChatMessage> Messages { get; set; } = new();
}

public class AiChatMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
    public bool UsedFallback { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class AiLetterDraft
{
    public string Subject { get; set; } = "";
    public string BodyHtml { get; set; } = "";
    public bool UsedFallback { get; set; }
}

public class AiLetterSummary
{
    public int LetterId { get; set; }
    public string Summary { get; set; } = "";
    public string CurrentStatus { get; set; } = "";
    public bool UsedFallback { get; set; }
}

public class AiReferralSuggestion
{
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public string? Department { get; set; }
    public string Reason { get; set; } = "";
}

public class AiReferralResponse
{
    public int LetterId { get; set; }
    public List<AiReferralSuggestion> Suggestions { get; set; } = new();
    public bool UsedFallback { get; set; }
}

public class AiLetterTask
{
    public string Action { get; set; } = "";
    public string? AssigneeHint { get; set; }
    public string? DeadlineText { get; set; }
    public DateTime? DeadlineDate { get; set; }
}

public class AiLetterTasks
{
    public int LetterId { get; set; }
    public List<AiLetterTask> Tasks { get; set; } = new();
    public bool UsedFallback { get; set; }
}

public class AiLetterCategory
{
    public int LetterId { get; set; }
    public string Category { get; set; } = "";
    public int Priority { get; set; } = 3;
    public List<string> Tags { get; set; } = new();
    public bool UsedFallback { get; set; }
}

public class AiMinutesItem
{
    public string Text { get; set; } = "";
    public string Kind { get; set; } = "مصوبه";
    public string? Responsible { get; set; }
    public string? DeadlineText { get; set; }
}

public class AiMinutesDraft
{
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<AiMinutesItem> Items { get; set; } = new();
    public bool UsedFallback { get; set; }
}

public class AiAssistantService
{
    private readonly IApiClient _api;
    public AiAssistantService(IApiClient api) => _api = api;

    public Task<AiHealth> GetHealthAsync()
        => _api.GetAsync<AiHealth>("api/ai/health");

    public Task<AiChatResult> ChatAsync(int? conversationId, string message)
        => _api.PostAsync<AiChatResult>("api/ai/chat", new { conversationId, message });

    public Task<List<AiConversation>> GetConversationsAsync()
        => _api.GetAsync<List<AiConversation>>("api/ai/conversations");

    public Task<AiConversation> GetConversationAsync(int id)
        => _api.GetAsync<AiConversation>($"api/ai/conversations/{id}");

    public Task DeleteConversationAsync(int id)
        => _api.DeleteAsync($"api/ai/conversations/{id}");

    public Task<AiLetterDraft> DraftLetterAsync(string topic, string tone, string kind, string? toName, string? extra)
        => _api.PostAsync<AiLetterDraft>("api/ai/letters/draft",
            new { topic, tone, kind, toName, extraContext = extra });

    public Task<AiLetterSummary> SummarizeLetterAsync(int letterId)
        => _api.PostAsync<AiLetterSummary>($"api/ai/letters/{letterId}/summarize", new { });

    public Task<AiReferralResponse> SuggestReferralAsync(int letterId)
        => _api.PostAsync<AiReferralResponse>($"api/ai/letters/{letterId}/suggest-referral", new { });

    public Task<AiLetterTasks> ExtractTasksAsync(int letterId)
        => _api.PostAsync<AiLetterTasks>($"api/ai/letters/{letterId}/extract-tasks", new { });

    public Task<AiLetterCategory> CategorizeLetterAsync(int letterId)
        => _api.PostAsync<AiLetterCategory>($"api/ai/letters/{letterId}/categorize", new { });

    public Task<AiMinutesDraft> DraftMinutesAsync(string rawText, string? title)
        => _api.PostAsync<AiMinutesDraft>("api/ai/minutes/draft", new { rawText, title });
}
