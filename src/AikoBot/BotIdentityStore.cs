namespace AikoBot;

public interface IBotIdentityStore
{
    string? Username { get; set; }
    long? UserId { get; set; }
}

public sealed class BotIdentityStore : IBotIdentityStore
{
    public string? Username { get; set; }
    public long? UserId { get; set; }
}
