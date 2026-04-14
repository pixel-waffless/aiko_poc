using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AikoBot;

public interface ISystemPromptProvider
{
    string Prompt { get; }
    void SetOverride(string prompt);
    void ClearOverride();
    bool HasOverride { get; }
}

public sealed class SystemPromptProvider : ISystemPromptProvider
{
    private readonly string _basePrompt;
    private readonly object _sync = new();
    private string? _overridePrompt;

    public string Prompt
    {
        get
        {
            lock (_sync)
            {
                return _overridePrompt ?? _basePrompt;
            }
        }
    }

    public bool HasOverride
    {
        get
        {
            lock (_sync)
            {
                return !string.IsNullOrWhiteSpace(_overridePrompt);
            }
        }
    }

    public SystemPromptProvider(IOptions<BotConfiguration> config, IHostEnvironment environment)
    {
        var configuredPath = config.Value.SystemPromptFilePath;
        var fullPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredPath));

        _basePrompt = File.ReadAllText(fullPath).Trim();

        if (string.IsNullOrWhiteSpace(_basePrompt))
        {
            throw new InvalidOperationException("System prompt file is empty.");
        }
    }

    public void SetOverride(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("Override prompt must not be empty.", nameof(prompt));
        }

        lock (_sync)
        {
            _overridePrompt = prompt.Trim();
        }
    }

    public void ClearOverride()
    {
        lock (_sync)
        {
            _overridePrompt = null;
        }
    }
}
