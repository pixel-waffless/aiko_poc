using ElevenLabs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AikoBot;

public interface ISpeechSynthesisService
{
    Task<Stream> SynthesizeAsync(string text, CancellationToken cancellationToken);
}

public sealed class SpeechSynthesisService : ISpeechSynthesisService
{
    private readonly IElevenLabsClient _elevenLabsClient;
    private readonly BotConfiguration _config;
    private readonly ILogger<SpeechSynthesisService> _logger;

    public SpeechSynthesisService(
        IElevenLabsClient elevenLabsClient,
        IOptions<BotConfiguration> config,
        ILogger<SpeechSynthesisService> logger)
    {
        _elevenLabsClient = elevenLabsClient;
        _config = config.Value;
        _logger = logger;
    }

    public Task<Stream> SynthesizeAsync(string text, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Generating speech with ElevenLabs using VoiceId={VoiceId} and ModelId={ModelId}",
            _config.ElevenLabsVoiceId,
            _config.ElevenLabsModelId);

        return _elevenLabsClient.TextToSpeech.CreateTextToSpeechByVoiceIdStreamAsync(
            voiceId: _config.ElevenLabsVoiceId,
            text: text,
            modelId: _config.ElevenLabsModelId,
            outputFormat: TextToSpeechStreamOutputFormat.Mp32205032,
            cancellationToken: cancellationToken);
    }
}
