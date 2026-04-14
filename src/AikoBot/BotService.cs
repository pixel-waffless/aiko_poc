using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace AikoBot;

public sealed class BotService : BackgroundService
{
    private readonly ITelegramBotClient _botClient;
    private readonly IBotIdentityStore _botIdentityStore;
    private readonly UpdateHandler _updateHandler;
    private readonly ILogger<BotService> _logger;

    public BotService(
        ITelegramBotClient botClient,
        IBotIdentityStore botIdentityStore,
        UpdateHandler updateHandler,
        ILogger<BotService> logger)
    {
        _botClient = botClient;
        _botIdentityStore = botIdentityStore;
        _updateHandler = updateHandler;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var botInfo = await _botClient.GetMe(stoppingToken);
        _botIdentityStore.UserId = botInfo.Id;
        _botIdentityStore.Username = botInfo.Username;
        _logger.LogInformation("Bot @{Username} is starting...", botInfo.Username);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message]
        };

        await _botClient.ReceiveAsync(
            updateHandler: _updateHandler.HandleUpdateAsync,
            errorHandler: _updateHandler.HandleErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken);

        _logger.LogInformation("Bot has stopped.");
    }
}
