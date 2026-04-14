using AikoBot.Data;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace AikoBot;

public interface IGroupSummaryService
{
    Task<string?> SummarizeChatAsync(long chatId, CancellationToken cancellationToken);
    int CountPendingMessages(long chatId);
}

public sealed class GroupSummaryService : IGroupSummaryService
{
    private const long SummaryUserId = 0L;

    private const string GroupSummaryInstruction = """
        You summarize group chat conversations.
        Produce a concise, useful summary of the recent discussion, decisions, action items,
        open questions, notable opinions, and any important context worth carrying forward.
        Keep it easy to scan, grounded in what participants actually said, and avoid filler.
        """;

    private readonly ChatClient _chatClient;
    private readonly ConversationRepository _conversations;
    private readonly ILogger<GroupSummaryService> _logger;

    public GroupSummaryService(
        ChatClient chatClient,
        ConversationRepository conversations,
        ILogger<GroupSummaryService> logger)
    {
        _chatClient = chatClient;
        _conversations = conversations;
        _logger = logger;
    }

    public int CountPendingMessages(long chatId)
    {
        var history = _conversations.GetChatHistory(chatId);
        return history.Count(m => m.Role != "summary");
    }

    public async Task<string?> SummarizeChatAsync(long chatId, CancellationToken cancellationToken)
    {
        var history = _conversations.GetChatHistory(chatId);
        var existingSummary = history.LastOrDefault(m => m.Role == "summary");
        var pendingMessages = history.Where(m => m.Role != "summary").ToList();

        if (pendingMessages.Count == 0)
        {
            return existingSummary?.Content;
        }

        var transcript = string.Join(
            "\n",
            pendingMessages.Select(m => $"[user:{m.UserId}] {m.Content}"));

        var prompt = existingSummary is { Content.Length: > 0 }
            ? $"Existing group context:\n{existingSummary.Content}\n\nNew messages to summarize:\n{transcript}"
            : $"Messages to summarize:\n{transcript}";

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(GroupSummaryInstruction),
            new UserChatMessage(prompt)
        };

        var response = await _chatClient.CompleteChatAsync(
            messages,
            cancellationToken: cancellationToken);

        var summaryText = response.Value.Content.Count > 0
            ? response.Value.Content[0].Text?.Trim()
            : null;

        if (string.IsNullOrWhiteSpace(summaryText))
        {
            throw new InvalidOperationException("Group summary generation returned empty content.");
        }

        _conversations.ReplaceChatHistory(
            chatId,
            [(SummaryUserId, "summary", summaryText)]);

        _logger.LogInformation("Generated group summary for ChatId={ChatId}", chatId);
        return summaryText;
    }
}
