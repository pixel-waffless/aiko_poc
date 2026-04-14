using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace AikoBot;

public interface ISpeechEnhancementService
{
    Task<string> EnhanceAsync(string cleanResponse, CancellationToken cancellationToken);
}

public sealed class SpeechEnhancementService : ISpeechEnhancementService
{
    private readonly ChatClient _chatClient;
    private readonly IEnhancingPromptProvider _enhancingPromptProvider;
    private readonly ILogger<SpeechEnhancementService> _logger;

    public SpeechEnhancementService(
        ChatClient chatClient,
        IEnhancingPromptProvider enhancingPromptProvider,
        ILogger<SpeechEnhancementService> logger)
    {
        _chatClient = chatClient;
        _enhancingPromptProvider = enhancingPromptProvider;
        _logger = logger;
    }

    public async Task<string> EnhanceAsync(string cleanResponse, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Enhancing clean response for speech generation.");

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(_enhancingPromptProvider.Prompt),
            new UserChatMessage(cleanResponse)
        };

        var response = await _chatClient.CompleteChatAsync(
            messages,
            cancellationToken: cancellationToken);

        var enhancedText = response.Value.Content.Count > 0
            ? response.Value.Content[0].Text
            : string.Empty;

        if (string.IsNullOrWhiteSpace(enhancedText))
        {
            throw new InvalidOperationException("Speech enhancement returned empty content.");
        }

        return enhancedText.Trim();
    }
}
