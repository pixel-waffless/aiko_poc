using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AikoBot.Data;

/// <summary>
/// Provides read/write access to the conversation history stored in SQLite.
/// Each method opens its own short-lived connection so concurrent calls
/// from multiple Telegram updates are safe.
/// </summary>
public sealed class ConversationRepository
{
    private readonly ConversationDbContext _db;

    public ConversationRepository(ConversationDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Returns all messages for a specific chat + user, ordered oldest first.
    /// </summary>
    public IReadOnlyList<ChatMessageRecord> GetHistory(long chatId, long userId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, ChatId, UserId, Role, Content, CreatedAt
            FROM ChatMessages
            WHERE ChatId = $chatId AND UserId = $userId
            ORDER BY Id ASC;
            """;
        cmd.Parameters.AddWithValue("$chatId", chatId);
        cmd.Parameters.AddWithValue("$userId", userId);

        using var reader = cmd.ExecuteReader();
        var results = new List<ChatMessageRecord>();

        while (reader.Read())
        {
            results.Add(new ChatMessageRecord
            {
                Id = reader.GetInt64(0),
                ChatId = reader.GetInt64(1),
                UserId = reader.GetInt64(2),
                Role = reader.GetString(3),
                Content = reader.GetString(4),
                CreatedAt = DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture)
            });
        }

        return results;
    }

    /// <summary>
    /// Returns all messages for a chat, ordered oldest first.
    /// Useful for group-level summarization across multiple users.
    /// </summary>
    public IReadOnlyList<ChatMessageRecord> GetChatHistory(long chatId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, ChatId, UserId, Role, Content, CreatedAt
            FROM ChatMessages
            WHERE ChatId = $chatId
            ORDER BY Id ASC;
            """;
        cmd.Parameters.AddWithValue("$chatId", chatId);

        using var reader = cmd.ExecuteReader();
        var results = new List<ChatMessageRecord>();

        while (reader.Read())
        {
            results.Add(new ChatMessageRecord
            {
                Id = reader.GetInt64(0),
                ChatId = reader.GetInt64(1),
                UserId = reader.GetInt64(2),
                Role = reader.GetString(3),
                Content = reader.GetString(4),
                CreatedAt = DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture)
            });
        }

        return results;
    }

    /// <summary>Appends a new message to the conversation history.</summary>
    public void AddMessage(long chatId, long userId, string role, string content)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ChatMessages (ChatId, UserId, Role, Content)
            VALUES ($chatId, $userId, $role, $content);
            """;
        cmd.Parameters.AddWithValue("$chatId", chatId);
        cmd.Parameters.AddWithValue("$userId", userId);
        cmd.Parameters.AddWithValue("$role", role);
        cmd.Parameters.AddWithValue("$content", content);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Clears all messages for a specific chat + user.</summary>
    public void ClearHistory(long chatId, long userId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ChatMessages WHERE ChatId = $chatId AND UserId = $userId;";
        cmd.Parameters.AddWithValue("$chatId", chatId);
        cmd.Parameters.AddWithValue("$userId", userId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Clears all messages for a specific chat, regardless of user.</summary>
    public void ClearChatHistory(long chatId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ChatMessages WHERE ChatId = $chatId;";
        cmd.Parameters.AddWithValue("$chatId", chatId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Replaces the full persisted history for a chat + user with the provided messages.
    /// Useful when older turns have been compacted into a single summary entry.
    /// </summary>
    public void ReplaceHistory(long chatId, long userId, IReadOnlyList<(string Role, string Content)> messages)
    {
        using var conn = _db.CreateConnection();
        using var transaction = conn.BeginTransaction();

        using (var deleteCmd = conn.CreateCommand())
        {
            deleteCmd.Transaction = transaction;
            deleteCmd.CommandText = "DELETE FROM ChatMessages WHERE ChatId = $chatId AND UserId = $userId;";
            deleteCmd.Parameters.AddWithValue("$chatId", chatId);
            deleteCmd.Parameters.AddWithValue("$userId", userId);
            deleteCmd.ExecuteNonQuery();
        }

        foreach (var message in messages)
        {
            using var insertCmd = conn.CreateCommand();
            insertCmd.Transaction = transaction;
            insertCmd.CommandText = """
                INSERT INTO ChatMessages (ChatId, UserId, Role, Content)
                VALUES ($chatId, $userId, $role, $content);
                """;
            insertCmd.Parameters.AddWithValue("$chatId", chatId);
            insertCmd.Parameters.AddWithValue("$userId", userId);
            insertCmd.Parameters.AddWithValue("$role", message.Role);
            insertCmd.Parameters.AddWithValue("$content", message.Content);
            insertCmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// Replaces the full persisted history for a chat with the provided messages.
    /// Useful after generating a group summary so the compacted summary becomes the new context.
    /// </summary>
    public void ReplaceChatHistory(long chatId, IReadOnlyList<(long UserId, string Role, string Content)> messages)
    {
        using var conn = _db.CreateConnection();
        using var transaction = conn.BeginTransaction();

        using (var deleteCmd = conn.CreateCommand())
        {
            deleteCmd.Transaction = transaction;
            deleteCmd.CommandText = "DELETE FROM ChatMessages WHERE ChatId = $chatId;";
            deleteCmd.Parameters.AddWithValue("$chatId", chatId);
            deleteCmd.ExecuteNonQuery();
        }

        foreach (var message in messages)
        {
            using var insertCmd = conn.CreateCommand();
            insertCmd.Transaction = transaction;
            insertCmd.CommandText = """
                INSERT INTO ChatMessages (ChatId, UserId, Role, Content)
                VALUES ($chatId, $userId, $role, $content);
                """;
            insertCmd.Parameters.AddWithValue("$chatId", chatId);
            insertCmd.Parameters.AddWithValue("$userId", message.UserId);
            insertCmd.Parameters.AddWithValue("$role", message.Role);
            insertCmd.Parameters.AddWithValue("$content", message.Content);
            insertCmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }
}
