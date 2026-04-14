using AikoBot.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace AikoBot;

public interface IConversationMemoryService
{
    Task<IReadOnlyList<ChatMessage>> BuildChatMessagesAsync(long chatId, long userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatMessage>> BuildChatMessagesForChatAsync(long chatId, CancellationToken cancellationToken);
}

public sealed class ConversationMemoryService : IConversationMemoryService
{
    private const string SummaryInstruction = """
        You condense prior conversation into durable memory for a Telegram AI companion.
        Preserve stable user preferences, relevant facts, commitments, unresolved tasks, tone cues,
        and any important context needed for future replies.
        Do not include filler or repetitive chit-chat.
        Write concise bullet-like prose in plain text.
        """;

    private readonly ChatClient _chatClient;
    private readonly ConversationRepository _conversations;
    private readonly BotConfiguration _config;
    private readonly ISystemPromptProvider _systemPromptProvider;
    private readonly ILogger<ConversationMemoryService> _logger;

    public ConversationMemoryService(
        ChatClient chatClient,
        ConversationRepository conversations,
        IOptions<BotConfiguration> config,
        ISystemPromptProvider systemPromptProvider,
        ILogger<ConversationMemoryService> logger)
    {
        _chatClient = chatClient;
        _conversations = conversations;
        _config = config.Value;
        _systemPromptProvider = systemPromptProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ChatMessage>> BuildChatMessagesAsync(long chatId, long userId, CancellationToken cancellationToken)
    {
        var history = _conversations.GetHistory(chatId, userId);
        var compactedHistory = await EnsureHistoryWithinBudgetAsync(chatId, userId, history, cancellationToken);
        return BuildChatMessages(compactedHistory);
    }

    public Task<IReadOnlyList<ChatMessage>> BuildChatMessagesForChatAsync(long chatId, CancellationToken cancellationToken)
    {
        var history = _conversations.GetChatHistory(chatId);
        var trimmedHistory = TrimHistoryToContextWindow(history);
        return Task.FromResult(BuildChatMessages(trimmedHistory));
    }

    private async Task<IReadOnlyList<ChatMessageRecord>> EnsureHistoryWithinBudgetAsync(
        long chatId,
        long userId,
        IReadOnlyList<ChatMessageRecord> history,
        CancellationToken cancellationToken)
    {
        if (EstimateTokens(history) <= _config.SummaryTriggerTokens)
        {
            return history;
        }

        _logger.LogInformation(
            "Conversation history exceeded summary threshold for ChatId={ChatId}, UserId={UserId}. Compacting memory.",
            chatId, userId);

        var summaryRecord = history.LastOrDefault(m => m.Role == "summary");
        var nonSummaryHistory = history.Where(m => m.Role != "summary").ToList();
        var recentMessages = TakeTailWithinTokenBudget(nonSummaryHistory, _config.RecentContextTokens, EstimateTokens);
        var messagesToSummarize = nonSummaryHistory.Take(nonSummaryHistory.Count - recentMessages.Count).ToList();

        if (messagesToSummarize.Count == 0)
        {
            return TrimHistoryToContextWindow(history);
        }

        try
        {
            var summary = await SummarizeAsync(summaryRecord?.Content, messagesToSummarize, cancellationToken);
            var replacementHistory = new List<(string Role, string Content)>
            {
                ("summary", summary)
            };

            replacementHistory.AddRange(recentMessages.Select(m => (m.Role, m.Content)));
            _conversations.ReplaceHistory(chatId, userId, replacementHistory);
            return _conversations.GetHistory(chatId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to summarize history for ChatId={ChatId}, UserId={UserId}. Falling back to context trimming.",
                chatId, userId);

            return TrimHistoryToContextWindow(history);
        }
    }

    private IReadOnlyList<ChatMessage> BuildChatMessages(IReadOnlyList<ChatMessageRecord> history)
    {
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(_systemPromptProvider.Prompt)
        };

        var summaryRecord = history.LastOrDefault(m => m.Role == "summary");
        if (summaryRecord is not null)
        {
            messages.Add(new SystemChatMessage($"Conversation memory summary:\n{summaryRecord.Content}"));
        }

        foreach (var message in history.Where(m => m.Role != "summary"))
        {
            messages.Add(message.Role == "assistant"
                ? new AssistantChatMessage(message.Content)
                : new UserChatMessage(message.Content));
        }

        return messages;
    }

    private async Task<string> SummarizeAsync(
        string? existingSummary,
        IReadOnlyList<ChatMessageRecord> messagesToSummarize,
        CancellationToken cancellationToken)
    {
        var transcript = string.Join(
            "\n",
            messagesToSummarize.Select(m => $"[{m.Role}] {m.Content}"));

        var prompt = existingSummary is { Length: > 0 }
            ? $"Existing summary:\n{existingSummary}\n\nNew conversation turns to merge:\n{transcript}"
            : $"Conversation turns to summarize:\n{transcript}";

        var summaryMessages = new List<ChatMessage>
        {
            new SystemChatMessage(SummaryInstruction),
            new UserChatMessage(prompt)
        };

        var summaryResponse = await _chatClient.CompleteChatAsync(
            summaryMessages,
            cancellationToken: cancellationToken);

        var summaryText = summaryResponse.Value.Content.Count > 0
            ? summaryResponse.Value.Content[0].Text
            : string.Empty;

        if (string.IsNullOrWhiteSpace(summaryText))
        {
            throw new InvalidOperationException("Summary generation returned empty content.");
        }

        return summaryText.Trim();
    }

    private IReadOnlyList<ChatMessageRecord> TrimHistoryToContextWindow(IReadOnlyList<ChatMessageRecord> history)
    {
        var summaryRecord = history.LastOrDefault(m => m.Role == "summary");
        var nonSummaryHistory = history.Where(m => m.Role != "summary").ToList();
        var budget = Math.Max(1000, _config.ContextWindowTokens - _config.ResponseReserveTokens - EstimateTokens(_systemPromptProvider.Prompt));

        if (summaryRecord is not null)
        {
            budget -= EstimateTokens(summaryRecord.Content);
        }

        var keptTail = TakeTailWithinTokenBudget(nonSummaryHistory, budget, EstimateTokens);
        var trimmed = new List<ChatMessageRecord>();

        if (summaryRecord is not null)
        {
            trimmed.Add(summaryRecord);
        }

        trimmed.AddRange(keptTail);
        return trimmed;
    }

    private static List<T> TakeTailWithinTokenBudget<T>(IReadOnlyList<T> items, int tokenBudget, Func<T, int> tokenEstimator)
    {
        var selected = new List<T>();
        var usedTokens = 0;

        for (var index = items.Count - 1; index >= 0; index--)
        {
            var item = items[index];
            var itemTokens = tokenEstimator(item);

            if (selected.Count > 0 && usedTokens + itemTokens > tokenBudget)
            {
                break;
            }

            selected.Insert(0, item);
            usedTokens += itemTokens;
        }

        return selected;
    }

    private static int EstimateTokens(IReadOnlyList<ChatMessageRecord> messages)
        => messages.Sum(EstimateTokens);

    private static int EstimateTokens(ChatMessageRecord message)
        => EstimateTokens($"{message.Role}:{message.Content}");

    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        // Rough heuristic for keeping the request comfortably under the model window
        // without pulling in an external tokenizer dependency.
        return Math.Max(1, (int)Math.Ceiling(text.Length / 4.0));
    }
}
