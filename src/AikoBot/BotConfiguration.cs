using System.ComponentModel.DataAnnotations;

namespace AikoBot;

public sealed class BotConfiguration
{
    [Required(ErrorMessage = "BotConfiguration:BotToken is required.")]
    [MinLength(20, ErrorMessage = "BotConfiguration:BotToken appears invalid (too short).")]
    public string BotToken { get; set; } = string.Empty;

    [Required(ErrorMessage = "BotConfiguration:LlmApiKey is required.")]
    [MinLength(20, ErrorMessage = "BotConfiguration:LlmApiKey appears invalid (too short).")]
    public string LlmApiKey { get; set; } = string.Empty;

    [Required(ErrorMessage = "BotConfiguration:LlmApiBase is required.")]
    [Url(ErrorMessage = "BotConfiguration:LlmApiBase appears invalid (not a URL).")]
    public string LlmApiBase { get; set; } = string.Empty;

    [Required(ErrorMessage = "BotConfiguration:LlmModel is required.")]
    [MinLength(1, ErrorMessage = "BotConfiguration:LlmModel must not be empty.")]
    public string LlmModel { get; set; } = string.Empty;

    [Required(ErrorMessage = "BotConfiguration:SystemPromptFilePath is required.")]
    [MinLength(1, ErrorMessage = "BotConfiguration:SystemPromptFilePath must not be empty.")]
    public string SystemPromptFilePath { get; set; } = string.Empty;

    public long SystemPromptOverrideUserId { get; set; }

    [Required(ErrorMessage = "BotConfiguration:EnhancingPromptFilePath is required.")]
    [MinLength(1, ErrorMessage = "BotConfiguration:EnhancingPromptFilePath must not be empty.")]
    public string EnhancingPromptFilePath { get; set; } = string.Empty;

    [Required(ErrorMessage = "BotConfiguration:LogFilePath is required.")]
    [MinLength(1, ErrorMessage = "BotConfiguration:LogFilePath must not be empty.")]
    public string LogFilePath { get; set; } = "logs/aiko-bot-.log";

    [Range(1000, 120000, ErrorMessage = "BotConfiguration:ContextWindowTokens must be between 1000 and 120000.")]
    public int ContextWindowTokens { get; set; } = 120000;

    [Range(500, 110000, ErrorMessage = "BotConfiguration:SummaryTriggerTokens must be between 500 and 110000.")]
    public int SummaryTriggerTokens { get; set; } = 90000;

    [Range(500, 60000, ErrorMessage = "BotConfiguration:RecentContextTokens must be between 500 and 60000.")]
    public int RecentContextTokens { get; set; } = 24000;

    [Range(500, 60000, ErrorMessage = "BotConfiguration:ResponseReserveTokens must be between 500 and 60000.")]
    public int ResponseReserveTokens { get; set; } = 12000;

    [Range(100, 10000, ErrorMessage = "BotConfiguration:TextFirstResponseThresholdChars must be between 100 and 10000.")]
    public int TextFirstResponseThresholdChars { get; set; } = 500;

    [Range(10, 5000, ErrorMessage = "BotConfiguration:GroupSummaryMessageThreshold must be between 10 and 5000.")]
    public int GroupSummaryMessageThreshold { get; set; } = 200;

    [Required(ErrorMessage = "BotConfiguration:ElevenLabsApiKey is required.")]
    [MinLength(20, ErrorMessage = "BotConfiguration:ElevenLabsApiKey appears invalid (too short).")]
    public string ElevenLabsApiKey { get; set; } = string.Empty;

    [Required(ErrorMessage = "BotConfiguration:ElevenLabsModelId is required.")]
    [MinLength(1, ErrorMessage = "BotConfiguration:ElevenLabsModelId must not be empty.")]
    public string ElevenLabsModelId { get; set; } = string.Empty;

    [Required(ErrorMessage = "BotConfiguration:ElevenLabsVoiceId is required.")]
    [MinLength(1, ErrorMessage = "BotConfiguration:ElevenLabsVoiceId must not be empty.")]
    public string ElevenLabsVoiceId { get; set; } = string.Empty;

    [Required(ErrorMessage = "BotConfiguration:DatabasePath is required.")]
    [MinLength(1, ErrorMessage = "BotConfiguration:DatabasePath must not be empty.")]
    public string DatabasePath { get; set; } = "aiko-bot.db";
}
