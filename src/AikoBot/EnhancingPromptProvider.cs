using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AikoBot;

public interface IEnhancingPromptProvider
{
    string Prompt { get; }
}

public sealed class EnhancingPromptProvider : IEnhancingPromptProvider
{
    public string Prompt { get; }

    public EnhancingPromptProvider(IOptions<BotConfiguration> config, IHostEnvironment environment)
    {
        var configuredPath = config.Value.EnhancingPromptFilePath;
        var fullPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredPath));

        Prompt = File.ReadAllText(fullPath).Trim();

        if (string.IsNullOrWhiteSpace(Prompt))
        {
            throw new InvalidOperationException("Enhancing prompt file is empty.");
        }
    }
}
