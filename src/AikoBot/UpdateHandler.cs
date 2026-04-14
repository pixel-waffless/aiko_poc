using System.ClientModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using AikoBot.Data;
using ElevenLabs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace AikoBot;

public sealed class UpdateHandler
{
    private const double EnhancedTextGrowthMultiplier = 1.5;
    private const int EnhancedTextGrowthAllowanceChars = 200;
    private const string VoiceFailureNotice = "I couldn't generate the voice version, so here's the text only.";

    private readonly ChatClient _chatClient;
    private readonly BotConfiguration _config;
    private readonly IBotIdentityStore _botIdentityStore;
    private readonly ISystemPromptProvider _systemPromptProvider;
    private readonly ConversationRepository _conversations;
    private readonly IConversationMemoryService _conversationMemoryService;
    private readonly IGroupSummaryService _groupSummaryService;
    private readonly ISpeechEnhancementService _speechEnhancementService;
    private readonly ISpeechSynthesisService _speechSynthesisService;
    private readonly ILogger<UpdateHandler> _logger;
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _chatLocks = new();

    public UpdateHandler(
        ChatClient chatClient,
        IOptions<BotConfiguration> config,
        IBotIdentityStore botIdentityStore,
        ISystemPromptProvider systemPromptProvider,
        ConversationRepository conversations,
        IConversationMemoryService conversationMemoryService,
        IGroupSummaryService groupSummaryService,
        ISpeechEnhancementService speechEnhancementService,
        ISpeechSynthesisService speechSynthesisService,
        ILogger<UpdateHandler> logger)
    {
        _chatClient = chatClient;
        _config = config.Value;
        _botIdentityStore = botIdentityStore;
        _systemPromptProvider = systemPromptProvider;
        _conversations = conversations;
        _conversationMemoryService = conversationMemoryService;
        _groupSummaryService = groupSummaryService;
        _speechEnhancementService = speechEnhancementService;
        _speechSynthesisService = speechSynthesisService;
        _logger = logger;
    }

    public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Type != UpdateType.Message || update.Message?.Text is null)
            return;

        var message = update.Message;
        var chatId = message.Chat.Id;
        var userId = message.From?.Id ?? 0L;
        var userText = message.Text;
        var isGroupChat = message.Chat.Type is ChatType.Group or ChatType.Supergroup;
        var isPrivateChat = message.Chat.Type == ChatType.Private;
        var replyParameters = new ReplyParameters { MessageId = message.MessageId };

        _logger.LogInformation(
            "Received message from UserId={UserId} in ChatId={ChatId}: {Text}",
            userId, chatId, userText);

        try
        {
            if (isPrivateChat && await TryHandleSystemPromptOverrideCommandAsync(botClient, chatId, userId, userText, replyParameters, cancellationToken))
            {
                return;
            }

            if (isGroupChat)
            {
                await HandleGroupMessageAsync(botClient, message, userId, userText, replyParameters, cancellationToken);
                return;
            }

            var chatMessages = await BuildPrivateChatMessagesAsync(chatId, userId, userText, cancellationToken);
            await SendConversationalReplyAsync(botClient, chatId, userId, chatMessages, persistAssistantReply: text => _conversations.AddMessage(chatId, userId, "assistant", text), replyParameters, cancellationToken);
        }
        catch (ClientResultException ex)
        {
            _logger.LogError(
                ex,
                "LLM provider request failed for UserId={UserId} in ChatId={ChatId}. Status={Status}",
                userId, chatId, ex.Status);

            await botClient.SendMessage(
                chatId,
                "Sorry, the AI provider returned an error. Please try again.",
                replyParameters: replyParameters,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while processing message from UserId={UserId} in ChatId={ChatId}", userId, chatId);

            await botClient.SendMessage(
                chatId,
                "Sorry, something went wrong. Please try again.",
                replyParameters: replyParameters,
                cancellationToken: cancellationToken);
        }
    }

    public Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Telegram polling error");
        return Task.CompletedTask;
    }

    private async Task HandleGroupMessageAsync(
        ITelegramBotClient botClient,
        Message message,
        long userId,
        string userText,
        ReplyParameters replyParameters,
        CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;

        if (string.Equals(userText.Trim(), "/summary", StringComparison.OrdinalIgnoreCase))
        {
            var summary = await _groupSummaryService.SummarizeChatAsync(chatId, cancellationToken);

            await botClient.SendMessage(
                chatId,
                string.IsNullOrWhiteSpace(summary)
                    ? "There are no group messages to summarize yet."
                    : summary,
                replyParameters: replyParameters,
                cancellationToken: cancellationToken);

            return;
        }

        var chatLock = _chatLocks.GetOrAdd(chatId, _ => new SemaphoreSlim(1, 1));
        IReadOnlyList<ChatMessage>? chatMessages = null;
        var shouldReply = false;
        var shouldAutoSummary = false;
        var lockAcquired = false;
        var waitStarted = Stopwatch.StartNew();

        await chatLock.WaitAsync(cancellationToken);
        lockAcquired = true;
        waitStarted.Stop();

        if (waitStarted.Elapsed > TimeSpan.FromSeconds(1))
        {
            _logger.LogWarning(
                "Waited {ElapsedMs}ms for chat lock in ChatId={ChatId}",
                waitStarted.ElapsedMilliseconds,
                chatId);
        }

        try
        {
            var senderName = message.From?.Username is { Length: > 0 } username
                ? $"@{username}"
                : string.Join(
                    " ",
                    new[] { message.From?.FirstName, message.From?.LastName }
                        .Where(part => !string.IsNullOrWhiteSpace(part)));

            var storedText = string.IsNullOrWhiteSpace(senderName)
                ? userText
                : $"{senderName}: {userText}";

            _conversations.AddMessage(chatId, userId, "user", storedText);

            shouldReply = ShouldReplyInGroup(message, userText);
            if (shouldReply)
            {
                chatMessages = await _conversationMemoryService.BuildChatMessagesForChatAsync(chatId, cancellationToken);
            }

            shouldAutoSummary = _groupSummaryService.CountPendingMessages(chatId) >= _config.GroupSummaryMessageThreshold;
        }
        finally
        {
            if (lockAcquired)
            {
                chatLock.Release();
            }
        }

        if (shouldReply && chatMessages is not null)
        {
            await SendConversationalReplyAsync(
                botClient,
                chatId,
                userId,
                chatMessages,
                persistAssistantReply: text => _conversations.AddMessage(chatId, 0L, "assistant", text),
                replyParameters,
                cancellationToken);
        }

        if (!shouldAutoSummary)
        {
            return;
        }

        var autoSummary = await _groupSummaryService.SummarizeChatAsync(chatId, cancellationToken);

        if (string.IsNullOrWhiteSpace(autoSummary))
        {
            return;
        }

        await botClient.SendMessage(
            chatId,
            autoSummary,
            replyParameters: replyParameters,
            cancellationToken: cancellationToken);
    }

    private async Task<IReadOnlyList<ChatMessage>> BuildPrivateChatMessagesAsync(
        long chatId,
        long userId,
        string userText,
        CancellationToken cancellationToken)
    {
        var chatLock = _chatLocks.GetOrAdd(chatId, _ => new SemaphoreSlim(1, 1));

        var lockAcquired = false;
        var waitStarted = Stopwatch.StartNew();

        await chatLock.WaitAsync(cancellationToken);
        lockAcquired = true;
        waitStarted.Stop();

        if (waitStarted.Elapsed > TimeSpan.FromSeconds(1))
        {
            _logger.LogWarning(
                "Waited {ElapsedMs}ms for chat lock while preparing private context in ChatId={ChatId}",
                waitStarted.ElapsedMilliseconds,
                chatId);
        }

        try
        {
            _conversations.AddMessage(chatId, userId, "user", userText);
            return await _conversationMemoryService.BuildChatMessagesAsync(chatId, userId, cancellationToken);
        }
        finally
        {
            if (lockAcquired)
            {
                chatLock.Release();
            }
        }
    }

    private async Task SendConversationalReplyAsync(
        ITelegramBotClient botClient,
        long chatId,
        long userId,
        IReadOnlyList<ChatMessage> chatMessages,
        Action<string> persistAssistantReply,
        ReplyParameters replyParameters,
        CancellationToken cancellationToken)
    {
        var response = await _chatClient.CompleteChatAsync(
            chatMessages,
            cancellationToken: cancellationToken);

        var content = response.Value.Content;
        var replyText = content.Count > 0
            ? content[0].Text
            : "I'm sorry, I couldn't generate a response.";

        _logger.LogInformation(
            "Generated response for UserId={UserId} in ChatId={ChatId}: {ReplyText}",
            userId, chatId, replyText);

        persistAssistantReply(replyText);

        var sentCleanTextFirst = false;

        if (replyText.Length >= _config.TextFirstResponseThresholdChars)
        {
            await botClient.SendMessage(
                chatId,
                replyText,
                replyParameters: replyParameters,
                cancellationToken: cancellationToken);

            sentCleanTextFirst = true;
        }

        try
        {
            var enhancedReplyText = await EnhanceForVoiceWithRetryAsync(replyText, cancellationToken);

            if (enhancedReplyText is null)
            {
                if (!sentCleanTextFirst)
                {
                    await botClient.SendMessage(
                        chatId,
                        replyText,
                        replyParameters: replyParameters,
                        cancellationToken: cancellationToken);
                }

                await botClient.SendMessage(
                    chatId,
                    VoiceFailureNotice,
                    replyParameters: replyParameters,
                    cancellationToken: cancellationToken);

                return;
            }

            await botClient.SendChatAction(
                chatId,
                ChatAction.UploadVoice,
                cancellationToken: cancellationToken);

            await using var voiceStream = await _speechSynthesisService.SynthesizeAsync(enhancedReplyText, cancellationToken);

            await botClient.SendVoice(
                chatId,
                InputFile.FromStream(voiceStream, "reply.ogg"),
                replyParameters: replyParameters,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Sent voice response for UserId={UserId} in ChatId={ChatId}",
                userId, chatId);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(
                ex,
                "Enhanced voice pipeline failed for UserId={UserId} in ChatId={ChatId}. Falling back to clean text response.",
                userId, chatId);

            if (!sentCleanTextFirst)
            {
                await botClient.SendMessage(
                    chatId,
                    replyText,
                    replyParameters: replyParameters,
                    cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Voice delivery failed for UserId={UserId} in ChatId={ChatId}. Falling back to clean text response.",
                userId, chatId);

            if (!sentCleanTextFirst)
            {
                await botClient.SendMessage(
                    chatId,
                    replyText,
                    replyParameters: replyParameters,
                    cancellationToken: cancellationToken);
            }
        }
    }

    private async Task<string?> EnhanceForVoiceWithRetryAsync(
        string cleanReplyText,
        CancellationToken cancellationToken)
    {
        var enhancedReplyText = await _speechEnhancementService.EnhanceAsync(cleanReplyText, cancellationToken);
        if (!IsEnhancedTextTooLarge(cleanReplyText, enhancedReplyText))
        {
            return enhancedReplyText;
        }

        _logger.LogWarning(
            "Enhanced speech text grew too much on first attempt. Retrying voice enhancement.");

        enhancedReplyText = await _speechEnhancementService.EnhanceAsync(cleanReplyText, cancellationToken);
        if (!IsEnhancedTextTooLarge(cleanReplyText, enhancedReplyText))
        {
            return enhancedReplyText;
        }

        _logger.LogWarning(
            "Enhanced speech text still too large after retry. Skipping voice generation.");

        return null;
    }

    private static bool IsEnhancedTextTooLarge(string cleanReplyText, string enhancedReplyText)
    {
        var cleanLength = Math.Max(1, cleanReplyText.Length);
        var maxAllowedLength = Math.Max(
            cleanLength + EnhancedTextGrowthAllowanceChars,
            (int)Math.Ceiling(cleanLength * EnhancedTextGrowthMultiplier));

        return enhancedReplyText.Length > maxAllowedLength;
    }

    private bool ShouldReplyInGroup(Message message, string userText)
    {
        var botUsername = _botIdentityStore.Username;
        var botUserId = _botIdentityStore.UserId;

        if (botUserId is not null &&
            message.ReplyToMessage?.From?.Id == botUserId)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(botUsername))
        {
            return false;
        }

        var mentionToken = $"@{botUsername}";

        if (userText.Contains(mentionToken, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return message.Entities?.Any(entity =>
            entity.Type == MessageEntityType.Mention &&
            entity.Offset >= 0 &&
            entity.Offset + entity.Length <= userText.Length &&
            string.Equals(
                userText.Substring(entity.Offset, entity.Length),
                mentionToken,
                StringComparison.OrdinalIgnoreCase)) == true;
    }

    private async Task<bool> TryHandleSystemPromptOverrideCommandAsync(
        ITelegramBotClient botClient,
        long chatId,
        long userId,
        string userText,
        ReplyParameters replyParameters,
        CancellationToken cancellationToken)
    {
        if (!userText.StartsWith("/setsystemprompt", StringComparison.OrdinalIgnoreCase) &&
            !userText.StartsWith("/resetsystemprompt", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_config.SystemPromptOverrideUserId <= 0 || userId != _config.SystemPromptOverrideUserId)
        {
            await botClient.SendMessage(
                chatId,
                "You are not allowed to override the system prompt.",
                replyParameters: replyParameters,
                cancellationToken: cancellationToken);

            return true;
        }

        if (userText.StartsWith("/resetsystemprompt", StringComparison.OrdinalIgnoreCase))
        {
            _systemPromptProvider.ClearOverride();

            await botClient.SendMessage(
                chatId,
                "System prompt override cleared. The bot is using the file-based prompt again.",
                replyParameters: replyParameters,
                cancellationToken: cancellationToken);

            return true;
        }

        var overridePrompt = userText["/setsystemprompt".Length..].Trim();
        if (string.IsNullOrWhiteSpace(overridePrompt))
        {
            await botClient.SendMessage(
                chatId,
                "Usage: /setsystemprompt <new prompt>",
                replyParameters: replyParameters,
                cancellationToken: cancellationToken);

            return true;
        }

        _systemPromptProvider.SetOverride(overridePrompt);

        await botClient.SendMessage(
            chatId,
            "System prompt override applied.",
            replyParameters: replyParameters,
            cancellationToken: cancellationToken);

        return true;
    }
}
