namespace AikoBot.Data;

/// <summary>Represents a single stored chat message.</summary>
public sealed class ChatMessageRecord
{
    public long Id { get; set; }
    public long ChatId { get; set; }
    public long UserId { get; set; }
    /// <summary>"user", "assistant", or "summary"</summary>
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
