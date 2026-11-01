using System.Globalization;
using System.Text.Json;
using KiNexus.Core.Inference;
using KiNexus.Core.Training;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace KiNexus.Core.Chats;

public interface IChatRepository
{
    Task InitializeAsync(CancellationToken ct);
    Task<ChatRecord> CreateChatAsync(string? kiId, bool thinkEnabled, CancellationToken ct);
    Task<ChatRecord?> GetChatAsync(long chatId, CancellationToken ct);
    Task<IReadOnlyList<ChatRecord>> ListChatsAsync(string? search, CancellationToken ct);
    Task UpdateChatAsync(ChatRecord chat, CancellationToken ct);
    Task DeleteChatAsync(long chatId, CancellationToken ct);
    Task<IReadOnlyList<MessageRecord>> GetMessagesAsync(long chatId, CancellationToken ct);
    Task<MessageRecord> AddMessageAsync(MessageRecord message, CancellationToken ct);
    Task SetRatingAsync(long messageId, int? rating, CancellationToken ct);

    /// <summary>Löscht diese Nachricht und alle späteren im selben Chat (für "neu erzeugen" und "bearbeiten").</summary>
    Task DeleteMessagesFromAsync(long chatId, long messageId, CancellationToken ct);

    /// <summary>Speichert eine Fassung des Systemprompts. Behalten werden die neuesten <paramref name="keep"/>.</summary>
    Task AddPromptVersionAsync(string kiId, string content, int keep, CancellationToken ct);

    /// <summary>Die gespeicherten Fassungen einer KI, die neueste zuerst.</summary>
    Task<IReadOnlyList<PromptVersion>> GetPromptVersionsAsync(string kiId, CancellationToken ct);

    /// <summary>Antworten mit Daumen hoch, je mit der Frage davor. Für den Datensatz im Training-Tab (Bauplan 8.6).</summary>
    Task<IReadOnlyList<TrainingExample>> GetLikedExamplesAsync(CancellationToken ct);
}

/// <summary>Eine gespeicherte Fassung eines Systemprompts (Tabelle system_prompt_versions).</summary>
public sealed record PromptVersion(long Id, string KiId, string Content, DateTimeOffset CreatedAt);

/// <summary>
/// Speichert Chats in data/chats.db (SQLite, eine Datei, kein Server nötig).
/// Die Datenbank hat eine Versionsnummer (PRAGMA user_version). Neue Versionen ändern sie mit Migrationen,
/// so gehen beim Update keine Chats verloren.
/// </summary>
public sealed class ChatRepository(string databasePath, ILogger<ChatRepository> logger) : IChatRepository
{
    /// <summary>
    /// Alle Migrationen der Reihe nach. Nie eine bestehende ändern, nur neue anhängen.
    /// Index 0 macht aus Version 0 die Version 1 usw.
    /// </summary>
    private static readonly string[] Migrations =
    [
        """
        CREATE TABLE chats (
            id            INTEGER PRIMARY KEY AUTOINCREMENT,
            title         TEXT    NOT NULL,
            ki_id         TEXT,
            created_at    TEXT    NOT NULL,
            updated_at    TEXT    NOT NULL,
            think_enabled INTEGER NOT NULL DEFAULT 0,
            voice_mode    TEXT
        );
        CREATE TABLE messages (
            id                INTEGER PRIMARY KEY AUTOINCREMENT,
            chat_id           INTEGER NOT NULL REFERENCES chats(id) ON DELETE CASCADE,
            role              TEXT    NOT NULL,
            content           TEXT    NOT NULL,
            thinking          TEXT    NOT NULL DEFAULT '',
            thinking_ms       INTEGER,
            ki_id             TEXT,
            engine            TEXT,
            attachment_paths  TEXT    NOT NULL DEFAULT '[]',
            prompt_tokens     INTEGER,
            completion_tokens INTEGER,
            tokens_per_second REAL,
            duration_ms       INTEGER,
            context_tokens    INTEGER,
            rating            INTEGER,
            created_at        TEXT    NOT NULL
        );
        CREATE INDEX ix_messages_chat ON messages(chat_id, id);
        CREATE TABLE system_prompt_versions (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            ki_id      TEXT NOT NULL,
            content    TEXT NOT NULL,
            created_at TEXT NOT NULL
        );
        CREATE INDEX ix_prompt_versions_ki ON system_prompt_versions(ki_id, id);
        """,
    ];

    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        ForeignKeys = true,
        Pooling = true,
    }.ToString();

    public static int LatestVersion => Migrations.Length;

    public async Task InitializeAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", ct).ConfigureAwait(false);

        var version = Convert.ToInt32(await ScalarAsync(connection, "PRAGMA user_version;", ct).ConfigureAwait(false), CultureInfo.InvariantCulture);
        if (version > LatestVersion)
        {
            throw new InvalidOperationException($"chats.db hat Version {version}, diese App kennt nur bis {LatestVersion}. Ist die App veraltet?");
        }

        for (var next = version; next < LatestVersion; next++)
        {
            await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            await ExecuteAsync(connection, Migrations[next], ct, (SqliteTransaction)transaction).ConfigureAwait(false);
            await ExecuteAsync(connection, $"PRAGMA user_version = {next + 1};", ct, (SqliteTransaction)transaction).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            logger.LogInformation("chats.db auf Version {Version} gebracht.", next + 1);
        }
    }

    public async Task<ChatRecord> CreateChatAsync(string? kiId, bool thinkEnabled, CancellationToken ct)
    {
        var now = DateTimeOffset.Now;
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO chats (title, ki_id, created_at, updated_at, think_enabled)
            VALUES ($title, $ki, $now, $now, $think);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$title", ChatRecord.DefaultTitle);
        command.Parameters.AddWithValue("$ki", (object?)kiId ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", Format(now));
        command.Parameters.AddWithValue("$think", thinkEnabled ? 1 : 0);
        var id = (long)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        return new ChatRecord(id, ChatRecord.DefaultTitle, kiId, now, now, thinkEnabled, null);
    }

    public async Task<ChatRecord?> GetChatAsync(long chatId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, title, ki_id, created_at, updated_at, think_enabled, voice_mode FROM chats WHERE id = $id;";
        command.Parameters.AddWithValue("$id", chatId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadChat(reader) : null;
    }

    /// <summary>Alle Chats, neueste zuerst. Mit Suche: Titel oder irgendeine Nachricht enthält den Text.</summary>
    public async Task<IReadOnlyList<ChatRecord>> ListChatsAsync(string? search, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, title, ki_id, created_at, updated_at, think_enabled, voice_mode FROM chats";
        if (!string.IsNullOrWhiteSpace(search))
        {
            command.CommandText += """
                 WHERE title LIKE $q ESCAPE '\'
                    OR EXISTS (SELECT 1 FROM messages m WHERE m.chat_id = chats.id AND m.content LIKE $q ESCAPE '\')
                """;
            command.Parameters.AddWithValue("$q", "%" + EscapeLike(search.Trim()) + "%");
        }

        command.CommandText += " ORDER BY updated_at DESC, id DESC;";
        var chats = new List<ChatRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            chats.Add(ReadChat(reader));
        }

        return chats;
    }

    public async Task UpdateChatAsync(ChatRecord chat, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE chats SET title = $title, ki_id = $ki, updated_at = $updated, think_enabled = $think, voice_mode = $voice
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", chat.Id);
        command.Parameters.AddWithValue("$title", chat.Title);
        command.Parameters.AddWithValue("$ki", (object?)chat.KiId ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated", Format(chat.UpdatedAt));
        command.Parameters.AddWithValue("$think", chat.ThinkEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$voice", (object?)chat.VoiceMode ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteChatAsync(long chatId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        // Die Nachrichten löscht SQLite mit (ON DELETE CASCADE).
        command.CommandText = "DELETE FROM chats WHERE id = $id;";
        command.Parameters.AddWithValue("$id", chatId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MessageRecord>> GetMessagesAsync(long chatId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, chat_id, role, content, thinking, thinking_ms, ki_id, engine, attachment_paths,
                   prompt_tokens, completion_tokens, tokens_per_second, duration_ms, context_tokens, rating, created_at
            FROM messages WHERE chat_id = $chat ORDER BY id;
            """;
        command.Parameters.AddWithValue("$chat", chatId);
        var messages = new List<MessageRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            messages.Add(ReadMessage(reader));
        }

        return messages;
    }

    public async Task<MessageRecord> AddMessageAsync(MessageRecord message, CancellationToken ct)
    {
        var createdAt = message.CreatedAt == default ? DateTimeOffset.Now : message.CreatedAt;
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO messages (chat_id, role, content, thinking, thinking_ms, ki_id, engine, attachment_paths,
                                  prompt_tokens, completion_tokens, tokens_per_second, duration_ms, context_tokens, rating, created_at)
            VALUES ($chat, $role, $content, $thinking, $thinkingMs, $ki, $engine, $attachments,
                    $prompt, $completion, $tps, $duration, $context, $rating, $created);
            SELECT last_insert_rowid();
            """;
        insert.Parameters.AddWithValue("$chat", message.ChatId);
        insert.Parameters.AddWithValue("$role", message.Role.ToString().ToLowerInvariant());
        insert.Parameters.AddWithValue("$content", message.Content);
        insert.Parameters.AddWithValue("$thinking", message.Thinking);
        insert.Parameters.AddWithValue("$thinkingMs", Db(message.ThinkingMs));
        insert.Parameters.AddWithValue("$ki", (object?)message.KiId ?? DBNull.Value);
        insert.Parameters.AddWithValue("$engine", (object?)message.Engine ?? DBNull.Value);
        insert.Parameters.AddWithValue("$attachments", JsonSerializer.Serialize(message.AttachmentPaths));
        insert.Parameters.AddWithValue("$prompt", Db(message.PromptTokens));
        insert.Parameters.AddWithValue("$completion", Db(message.CompletionTokens));
        insert.Parameters.AddWithValue("$tps", Db(message.TokensPerSecond));
        insert.Parameters.AddWithValue("$duration", Db(message.DurationMs));
        insert.Parameters.AddWithValue("$context", Db(message.ContextTokens));
        insert.Parameters.AddWithValue("$rating", Db(message.Rating));
        insert.Parameters.AddWithValue("$created", Format(createdAt));
        var id = (long)(await insert.ExecuteScalarAsync(ct).ConfigureAwait(false))!;

        // Der Chat rückt in der Liste nach oben.
        await using var touch = connection.CreateCommand();
        touch.Transaction = transaction;
        touch.CommandText = "UPDATE chats SET updated_at = $now WHERE id = $chat;";
        touch.Parameters.AddWithValue("$now", Format(createdAt));
        touch.Parameters.AddWithValue("$chat", message.ChatId);
        await touch.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return message with { Id = id, CreatedAt = createdAt };
    }

    public async Task SetRatingAsync(long messageId, int? rating, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE messages SET rating = $rating WHERE id = $id;";
        command.Parameters.AddWithValue("$rating", Db(rating));
        command.Parameters.AddWithValue("$id", messageId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteMessagesFromAsync(long chatId, long messageId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM messages WHERE chat_id = $chat AND id >= $id;";
        command.Parameters.AddWithValue("$chat", chatId);
        command.Parameters.AddWithValue("$id", messageId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task AddPromptVersionAsync(string kiId, string content, int keep, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO system_prompt_versions (ki_id, content, created_at) VALUES ($ki, $content, $created);";
            insert.Parameters.AddWithValue("$ki", kiId);
            insert.Parameters.AddWithValue("$content", content);
            insert.Parameters.AddWithValue("$created", Format(DateTimeOffset.Now));
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var trim = connection.CreateCommand())
        {
            trim.Transaction = transaction;
            trim.CommandText = """
                DELETE FROM system_prompt_versions
                WHERE ki_id = $ki AND id NOT IN (SELECT id FROM system_prompt_versions WHERE ki_id = $ki ORDER BY id DESC LIMIT $keep);
                """;
            trim.Parameters.AddWithValue("$ki", kiId);
            trim.Parameters.AddWithValue("$keep", keep);
            await trim.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PromptVersion>> GetPromptVersionsAsync(string kiId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, ki_id, content, created_at FROM system_prompt_versions WHERE ki_id = $ki ORDER BY id DESC;";
        command.Parameters.AddWithValue("$ki", kiId);
        var versions = new List<PromptVersion>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            versions.Add(new PromptVersion(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), Parse(reader.GetString(3))));
        }

        return versions;
    }

    public async Task<IReadOnlyList<TrainingExample>> GetLikedExamplesAsync(CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT (SELECT u.content FROM messages u
                    WHERE u.chat_id = a.chat_id AND u.id < a.id AND u.role = 'user'
                    ORDER BY u.id DESC LIMIT 1) AS question,
                   a.content
            FROM messages a
            WHERE a.role = 'assistant' AND a.rating = 1
            ORDER BY a.id;
            """;
        var examples = new List<TrainingExample>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (!reader.IsDBNull(0) && reader.GetString(0).Trim().Length > 0 && reader.GetString(1).Trim().Length > 0)
            {
                examples.Add(new TrainingExample(reader.GetString(0).Trim(), reader.GetString(1).Trim()));
            }
        }

        return examples;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken ct, SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }

    private static ChatRecord ReadChat(SqliteDataReader r) => new(
        r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
        Parse(r.GetString(3)), Parse(r.GetString(4)), r.GetInt64(5) != 0, r.IsDBNull(6) ? null : r.GetString(6));

    private static MessageRecord ReadMessage(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        ChatId = r.GetInt64(1),
        Role = Enum.Parse<ChatRole>(r.GetString(2), ignoreCase: true),
        Content = r.GetString(3),
        Thinking = r.GetString(4),
        ThinkingMs = r.IsDBNull(5) ? null : r.GetInt64(5),
        KiId = r.IsDBNull(6) ? null : r.GetString(6),
        Engine = r.IsDBNull(7) ? null : r.GetString(7),
        AttachmentPaths = JsonSerializer.Deserialize<List<string>>(r.GetString(8)) ?? [],
        PromptTokens = r.IsDBNull(9) ? null : r.GetInt32(9),
        CompletionTokens = r.IsDBNull(10) ? null : r.GetInt32(10),
        TokensPerSecond = r.IsDBNull(11) ? null : r.GetDouble(11),
        DurationMs = r.IsDBNull(12) ? null : r.GetInt64(12),
        ContextTokens = r.IsDBNull(13) ? null : r.GetInt32(13),
        Rating = r.IsDBNull(14) ? null : r.GetInt32(14),
        CreatedAt = Parse(r.GetString(15)),
    };

    private static object Db<T>(T? value) where T : struct => value is T v ? v : DBNull.Value;

    /// <summary>Gespeichert wird in UTC. So stimmt die Sortierung auch beim Wechsel auf Sommer- oder Winterzeit.</summary>
    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture).ToLocalTime();

    /// <summary>% und _ haben bei LIKE eine Sonderbedeutung. Mit \ davor gelten sie als normale Zeichen.</summary>
    private static string EscapeLike(string text) => text.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
