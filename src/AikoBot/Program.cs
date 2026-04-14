using AikoBot;
using AikoBot.Data;
using ElevenLabs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using Serilog;
using Serilog.Events;
using Telegram.Bot;
using System.ClientModel;

var bootstrapConfiguration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

var bootstrapLogFilePath = bootstrapConfiguration["BotConfiguration:LogFilePath"] ?? "logs/aiko-bot-.log";
EnsureParentDirectoryExists(bootstrapLogFilePath);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithMachineName()
    .Enrich.WithProcessId()
    .Enrich.WithThreadId()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: bootstrapLogFilePath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate:
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] " +
            "[Env:{EnvironmentName}] [Machine:{MachineName}] [PID:{ProcessId}] [TID:{ThreadId}] " +
            "{Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting AikoBot host");

    var host = Host.CreateDefaultBuilder(args)
        .UseSerilog((context, services, loggerConfig) =>
        {
            var logFilePath = context.Configuration["BotConfiguration:LogFilePath"] ?? "logs/aiko-bot-.log";
            EnsureParentDirectoryExists(logFilePath);

            loggerConfig
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithEnvironmentName()
                .Enrich.WithMachineName()
                .Enrich.WithProcessId()
                .Enrich.WithThreadId()
                .WriteTo.Console(outputTemplate:
                    "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    path: logFilePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate:
                        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] " +
                        "[Env:{EnvironmentName}] [Machine:{MachineName}] [PID:{ProcessId}] [TID:{ThreadId}] " +
                        "{Message:lj}{NewLine}{Exception}");
        })
        .ConfigureAppConfiguration(config =>
        {
            config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
            config.AddEnvironmentVariables();
        })
        .ConfigureServices((context, services) =>
        {
            services
                .AddOptions<BotConfiguration>()
                .Bind(context.Configuration.GetSection("BotConfiguration"))
                .ValidateDataAnnotations()
                .Validate(
                    cfg => cfg.SummaryTriggerTokens < cfg.ContextWindowTokens,
                    "BotConfiguration:SummaryTriggerTokens must be lower than BotConfiguration:ContextWindowTokens.")
                .Validate(
                    cfg => cfg.RecentContextTokens + cfg.ResponseReserveTokens < cfg.ContextWindowTokens,
                    "BotConfiguration:RecentContextTokens + BotConfiguration:ResponseReserveTokens must be lower than BotConfiguration:ContextWindowTokens.")
                .Validate<IHostEnvironment>(
                    (cfg, environment) =>
                    {
                        var promptPath = Path.IsPathRooted(cfg.SystemPromptFilePath)
                            ? cfg.SystemPromptFilePath
                            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, cfg.SystemPromptFilePath));
                        return File.Exists(promptPath);
                    },
                    "BotConfiguration:SystemPromptFilePath must point to an existing text file.")
                .Validate<IHostEnvironment>(
                    (cfg, environment) =>
                    {
                        var promptPath = Path.IsPathRooted(cfg.EnhancingPromptFilePath)
                            ? cfg.EnhancingPromptFilePath
                            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, cfg.EnhancingPromptFilePath));
                        return File.Exists(promptPath);
                    },
                    "BotConfiguration:EnhancingPromptFilePath must point to an existing text file.")
                .ValidateOnStart();

            services.AddSingleton<ITelegramBotClient>(sp =>
            {
                var cfg = sp.GetRequiredService<IOptions<BotConfiguration>>().Value;
                return new TelegramBotClient(cfg.BotToken);
            });

            services.AddSingleton(sp =>
            {
                var cfg = sp.GetRequiredService<IOptions<BotConfiguration>>().Value;
                var credential = new ApiKeyCredential(cfg.LlmApiKey);
                var options = new OpenAIClientOptions
                {
                    Endpoint = new Uri(cfg.LlmApiBase)
                };

                return new OpenAIClient(credential, options);
            });

            services.AddSingleton<ChatClient>(sp =>
            {
                var cfg = sp.GetRequiredService<IOptions<BotConfiguration>>().Value;
                var llmClient = sp.GetRequiredService<OpenAIClient>();
                return llmClient.GetChatClient(cfg.LlmModel);
            });

            services.AddSingleton<IElevenLabsClient>(sp =>
            {
                var cfg = sp.GetRequiredService<IOptions<BotConfiguration>>().Value;
                return new ElevenLabsClient(cfg.ElevenLabsApiKey);
            });

            services.AddSingleton<ConversationDbContext>();
            services.AddSingleton<ConversationRepository>();
            services.AddSingleton<IBotIdentityStore, BotIdentityStore>();
            services.AddSingleton<ISystemPromptProvider, SystemPromptProvider>();
            services.AddSingleton<IEnhancingPromptProvider, EnhancingPromptProvider>();
            services.AddSingleton<IConversationMemoryService, ConversationMemoryService>();
            services.AddSingleton<IGroupSummaryService, GroupSummaryService>();
            services.AddSingleton<ISpeechEnhancementService, SpeechEnhancementService>();
            services.AddSingleton<ISpeechSynthesisService, SpeechSynthesisService>();
            services.AddSingleton<UpdateHandler>();
            services.AddHostedService<BotService>();
        })
        .Build();

    await host.RunAsync();
}
catch (Exception ex) when (ex is not OperationCanceledException && ex is not HostAbortedException)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

static void EnsureParentDirectoryExists(string filePath)
{
    var fullPath = Path.GetFullPath(filePath);
    var directory = Path.GetDirectoryName(fullPath);

    if (!string.IsNullOrWhiteSpace(directory))
    {
        Directory.CreateDirectory(directory);
    }
}
