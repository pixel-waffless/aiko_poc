# Aiko Bot (POC)

A Telegram bot built with .NET 10 that talks to an OpenAI-compatible LLM provider and delivers voice responses synthesized with ElevenLabs.

## Features

- Listens for Telegram messages using long-polling
- Forwards each message to a chat-completions compatible LLM endpoint
- Runs a second LLM pass with an enhancement prompt to prepare expressive speech text
- Streams the enhanced speech text to ElevenLabs and sends the resulting audio back to the user in Telegram
- In groups, stores incoming messages and generates a rolling chat summary on `/summary` or after a message threshold
- **Conversation memory** — per-chat/per-user message history stored in SQLite; the relevant context is sent to the LLM provider on every message
- **Persistent personality + memory** — a system prompt is loaded from a `.txt` file, and long conversations are compacted into summaries to stay inside the model context window
- **Options validation** — startup fails fast if required Telegram, LLM-provider, system-prompt, and ElevenLabs settings are missing or invalid
- **Structured logging** via Serilog — console + daily rolling file sinks, enriched with environment name, machine name, process ID, and thread ID

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A Telegram Bot Token (from [@BotFather](https://t.me/BotFather))
- An API key for your OpenAI-compatible LLM provider
- An ElevenLabs API Key plus a valid voice and model ID

## Getting Started

1. Copy `src/AikoBot/appsettings.example.json` to `src/AikoBot/appsettings.json` and fill in your credentials:

```json
{
  "BotConfiguration": {
    "BotToken": "YOUR_TELEGRAM_BOT_TOKEN",
    "LlmApiKey": "YOUR_LLM_API_KEY",
    "LlmApiBase": "https://your-llm-provider.example/v1",
    "LlmModel": "your-model-id",
    "SystemPromptFilePath": "system-prompt.txt",
    "EnhancingPromptFilePath": "enhancing-prompt.txt",
    "ContextWindowTokens": 120000,
    "SummaryTriggerTokens": 90000,
    "RecentContextTokens": 24000,
    "ResponseReserveTokens": 12000,
    "TextFirstResponseThresholdChars": 500,
    "GroupSummaryMessageThreshold": 200,
    "ElevenLabsApiKey": "YOUR_ELEVENLABS_API_KEY",
    "ElevenLabsModelId": "eleven_3",
    "ElevenLabsVoiceId": "YOUR_ELEVENLABS_VOICE_ID",
    "DatabasePath": "aiko-bot.db"
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    }
  }
}
```

2. Run the bot:

```bash
cd src/AikoBot
dotnet run
```

> **Note:** `appsettings.json` and the SQLite database (`*.db`) are excluded from source control. Never commit your API keys.

## Docker Deployment

Build the image from the repository root:

```bash
docker build -t aiko-bot .
```

Run it with environment variables and mounted data/log directories:

```bash
docker run -d \
  --name aiko-bot \
  -e BotConfiguration__BotToken="YOUR_TELEGRAM_BOT_TOKEN" \
  -e BotConfiguration__LlmApiKey="YOUR_LLM_API_KEY" \
  -e BotConfiguration__LlmApiBase="https://your-llm-provider.example/v1" \
  -e BotConfiguration__LlmModel="your-model-id" \
  -e BotConfiguration__ElevenLabsApiKey="YOUR_ELEVENLABS_API_KEY" \
  -e BotConfiguration__ElevenLabsModelId="eleven_3" \
  -e BotConfiguration__ElevenLabsVoiceId="YOUR_ELEVENLABS_VOICE_ID" \
  -v "$(pwd)/data:/app/data" \
  -v "$(pwd)/logs:/app/logs" \
  aiko-bot
```

The image already includes `system-prompt.txt` and `enhancing-prompt.txt`, and defaults to:
- `BotConfiguration__DatabasePath=/app/data/aiko-bot.db`
- `BotConfiguration__SystemPromptFilePath=/app/system-prompt.txt`
- `BotConfiguration__EnhancingPromptFilePath=/app/enhancing-prompt.txt`
- `BotConfiguration__LogFilePath=/app/logs/aiko-bot-.log`

If you want custom prompt files in production, mount them and override those paths with environment variables.

## Configuration

| Key | Description |
|-----|-------------|
| `BotConfiguration:BotToken` | Your Telegram bot token (required, min 20 chars) |
| `BotConfiguration:LlmApiKey` | API key for your OpenAI-compatible LLM provider (required, min 20 chars) |
| `BotConfiguration:LlmApiBase` | Base URL for the LLM endpoint (required, must be a valid URL) |
| `BotConfiguration:LlmModel` | Model ID used for chat completions (required) |
| `BotConfiguration:SystemPromptFilePath` | Path to the `.txt` file that defines the bot's persistent personality and behavior (required, must exist) |
| `BotConfiguration:SystemPromptOverrideUserId` | The only Telegram user ID allowed to override the system prompt with private-chat commands (default: `0`, disabled) |
| `BotConfiguration:EnhancingPromptFilePath` | Path to the `.txt` file used to enhance clean responses before speech synthesis (required, must exist) |
| `BotConfiguration:LogFilePath` | File path used by Serilog for rolling file logs (default: `logs/aiko-bot-.log`) |
| `BotConfiguration:ContextWindowTokens` | Maximum context budget to respect when building prompts (default: `120000`) |
| `BotConfiguration:SummaryTriggerTokens` | Approximate history size that triggers conversation summarization (default: `90000`) |
| `BotConfiguration:RecentContextTokens` | Budget reserved for the most recent unsummarized turns after compaction (default: `24000`) |
| `BotConfiguration:ResponseReserveTokens` | Budget reserved for the model's next response so prompts stay under the context limit (default: `12000`) |
| `BotConfiguration:TextFirstResponseThresholdChars` | If the clean response is this long or longer, send text first and audio after it (default: `500`) |
| `BotConfiguration:GroupSummaryMessageThreshold` | In group chats, automatically generate and post a summary after this many stored messages (default: `200`) |
| `BotConfiguration:ElevenLabsApiKey` | Your ElevenLabs API key (required, min 20 chars) |
| `BotConfiguration:ElevenLabsModelId` | ElevenLabs TTS model ID to use for speech generation (required) |
| `BotConfiguration:ElevenLabsVoiceId` | ElevenLabs voice ID used to synthesize Telegram replies (required) |
| `BotConfiguration:DatabasePath` | Path to the SQLite database file (default: `aiko-bot.db`) |

All settings can also be provided as environment variables (e.g. `BotConfiguration__BotToken`).

## Logging

Logs are written to:
- **Console** — human-readable with timestamp, level, and source context
- **`logs/aiko-bot-<date>.log`** — daily rolling file (kept for 7 days), enriched with environment, machine, process, and thread metadata

## Project Structure

```
src/
└── AikoBot/
    ├── Program.cs              # Host setup, Serilog config, DI registration
    ├── BotConfiguration.cs     # Configuration POCO with data-annotation validation
    ├── BotService.cs           # Background service that runs the bot
    ├── ConversationMemoryService.cs # Builds context with system prompt + summarized memory
    ├── EnhancingPromptProvider.cs # Loads the speech-enhancement prompt from disk
    ├── GroupSummaryService.cs   # Summarizes accumulated group-chat messages and compacts history
    ├── enhancing-prompt.txt    # Prompt used to prepare expressive speech-friendly text
    ├── SpeechEnhancementService.cs # Runs the second LLM pass for speech enhancement
    ├── SpeechSynthesisService.cs # ElevenLabs text-to-speech integration
    ├── SystemPromptProvider.cs # Loads and validates the system prompt from disk
    ├── UpdateHandler.cs        # Handles Telegram updates, calls the LLM provider, and sends voice replies
    ├── system-prompt.txt       # Default personality prompt loaded at startup
    └── Data/
        ├── ChatMessageRecord.cs      # Model for a stored message
        ├── ConversationDbContext.cs  # SQLite connection + schema bootstrap
        └── ConversationRepository.cs # Read/write conversation history
```

## GitHub Actions

The repository includes [`.github/workflows/publish-image.yml`](/Users/alxrd/RiderProjects/aiko-bot/.github/workflows/publish-image.yml:1), which runs on pushes to `main` and:
- builds the Docker image
- publishes it to `ghcr.io`

The workflow uses the built-in `GITHUB_TOKEN`, so you do not need extra registry secrets as long as GitHub Packages is enabled for the repository.

## Private Admin Commands

In a direct chat with the bot, the configured `SystemPromptOverrideUserId` can use:
- `/setsystemprompt <new prompt>`
- `/resetsystemprompt`

The override is applied in memory and affects the active prompt used by the bot until it is reset or the process restarts.
