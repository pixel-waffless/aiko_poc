using AikoBot;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace AikoBot.Data;

/// <summary>
/// Singleton factory for SQLite connections. Holds the connection string,
/// ensures the schema and WAL mode are configured once on startup, and
/// vends short-lived connections to callers (each caller is responsible
/// for disposing the returned connection).
/// </summary>
public sealed class ConversationDbContext
{
    private readonly string _connectionString;

    public ConversationDbContext(IOptions<BotConfiguration> config)
    {
        EnsureParentDirectoryExists(config.Value.DatabasePath);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = config.Value.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        EnsureSchema();
    }

    /// <summary>
    /// Opens and returns a new <see cref="SqliteConnection"/>.
    /// The caller must dispose it (use <c>using var conn = …</c>).
    /// </summary>
    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void EnsureSchema()
    {
        using var connection = CreateConnection();

        using var walCmd = connection.CreateCommand();
        walCmd.CommandText = "PRAGMA journal_mode=WAL;";
        walCmd.ExecuteNonQuery();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS ChatMessages (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                ChatId      INTEGER NOT NULL,
                UserId      INTEGER NOT NULL,
                Role        TEXT    NOT NULL,
                Content     TEXT    NOT NULL,
                CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now'))
            );
            CREATE INDEX IF NOT EXISTS IX_ChatMessages_ChatId_UserId
                ON ChatMessages (ChatId, UserId);
            """;
        cmd.ExecuteNonQuery();
    }

    private static void EnsureParentDirectoryExists(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
