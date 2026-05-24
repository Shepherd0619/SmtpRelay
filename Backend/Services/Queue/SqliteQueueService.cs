using System.Text.Json;
using Microsoft.Data.Sqlite;
using SmtpRelay.App.Configuration;
using SmtpRelay.App.Models;
using Microsoft.Extensions.Options;

namespace SmtpRelay.App.Services.Queue;

public class SqliteQueueService : IQueueService, IDisposable
{
    private readonly string _connectionString;
    private readonly IOptions<QueueConfig> _config;
    private readonly ILogger<SqliteQueueService> _logger;
    private bool _initialized;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public SqliteQueueService(IOptions<SmtpRelayConfig> config, ILogger<SqliteQueueService> logger)
    {
        _config = Options.Create(config.Value.Queue);
        _logger = logger;

        var dbPath = config.Value.Queue.DatabasePath;
        if (!Path.IsPathRooted(dbPath))
        {
            dbPath = Path.Combine(AppContext.BaseDirectory, dbPath);
        }

        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = $"Data Source={dbPath}";
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            CREATE TABLE IF NOT EXISTS EmailQueue (
                Id TEXT PRIMARY KEY,
                ReceivedAt TEXT NOT NULL,
                Status TEXT NOT NULL DEFAULT 'Pending',
                RetryCount INTEGER NOT NULL DEFAULT 0,
                LastError TEXT,
                FromAddress TEXT NOT NULL,
                ToAddresses TEXT NOT NULL,
                CcAddresses TEXT,
                Subject TEXT NOT NULL,
                MessageId TEXT,
                MimeContent BLOB NOT NULL,
                ChannelResults TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_EmailQueue_Status ON EmailQueue(Status);
            CREATE INDEX IF NOT EXISTS IX_EmailQueue_ReceivedAt ON EmailQueue(ReceivedAt);
            """;

        await using var cmd = new SqliteCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);

        _initialized = true;
        _logger.LogInformation("Queue database initialized at {Path}", _connectionString);
    }

    public async Task EnqueueAsync(RelayMessage message, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            INSERT INTO EmailQueue (Id, ReceivedAt, Status, RetryCount, FromAddress, ToAddresses,
                CcAddresses, Subject, MessageId, MimeContent, ChannelResults, CreatedAt, UpdatedAt)
            VALUES (@Id, @ReceivedAt, @Status, @RetryCount, @FromAddress, @ToAddresses,
                @CcAddresses, @Subject, @MessageId, @MimeContent, @ChannelResults, @CreatedAt, @UpdatedAt)
            """;

        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", message.Id.ToString());
        cmd.Parameters.AddWithValue("@ReceivedAt", message.ReceivedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@Status", message.Status.ToString());
        cmd.Parameters.AddWithValue("@RetryCount", message.RetryCount);
        cmd.Parameters.AddWithValue("@FromAddress", message.From);
        cmd.Parameters.AddWithValue("@ToAddresses", JsonSerializer.Serialize(message.To, JsonOptions));
        cmd.Parameters.AddWithValue("@CcAddresses", JsonSerializer.Serialize(message.Cc, JsonOptions));
        cmd.Parameters.AddWithValue("@Subject", message.Subject);
        cmd.Parameters.AddWithValue("@MessageId", message.MessageId ?? "");
        cmd.Parameters.AddWithValue("@MimeContent", message.MimeContent);
        cmd.Parameters.AddWithValue("@ChannelResults", JsonSerializer.Serialize(message.ChannelResults, JsonOptions));
        cmd.Parameters.AddWithValue("@CreatedAt", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@UpdatedAt", DateTimeOffset.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogDebug("Message {Id} enqueued", message.Id);
    }

    public async Task<IReadOnlyList<RelayMessage>> GetPendingMessagesAsync(int limit, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var maxRetry = _config.Value.MaxRetryCount;

        const string sql = """
            SELECT * FROM EmailQueue
            WHERE Status = 'Pending' OR (Status = 'Failed' AND RetryCount < @MaxRetry)
            ORDER BY ReceivedAt ASC
            LIMIT @Limit
            """;

        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@MaxRetry", maxRetry);
        cmd.Parameters.AddWithValue("@Limit", limit);

        var messages = new List<RelayMessage>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            messages.Add(MapMessage(reader));
        }

        // Mark them as Processing
        foreach (var msg in messages)
        {
            await UpdateStatusAsync(conn, msg.Id, MessageStatus.Processing, null, ct);
        }

        return messages;
    }

    public async Task MarkDeliveredAsync(Guid messageId, Dictionary<string, ChannelDeliveryResult> results, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var allSuccess = results.Values.All(r => r.Success);
        var status = allSuccess ? MessageStatus.Delivered : MessageStatus.PartiallyDelivered;

        const string sql = """
            UPDATE EmailQueue
            SET Status = @Status, ChannelResults = @Results, UpdatedAt = @UpdatedAt
            WHERE Id = @Id
            """;

        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Status", status.ToString());
        cmd.Parameters.AddWithValue("@Results", JsonSerializer.Serialize(results, JsonOptions));
        cmd.Parameters.AddWithValue("@UpdatedAt", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@Id", messageId.ToString());

        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Message {Id} delivered with status {Status}", messageId, status);
    }

    public async Task MarkFailedAsync(Guid messageId, string error, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            UPDATE EmailQueue
            SET Status = 'Failed', RetryCount = RetryCount + 1, LastError = @Error, UpdatedAt = @UpdatedAt
            WHERE Id = @Id
            """;

        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Error", error);
        cmd.Parameters.AddWithValue("@UpdatedAt", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@Id", messageId.ToString());

        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogWarning("Message {Id} marked as failed: {Error}", messageId, error);
    }

    public async Task MarkDeadLetteredAsync(Guid messageId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            UPDATE EmailQueue
            SET Status = 'DeadLettered', UpdatedAt = @UpdatedAt
            WHERE Id = @Id
            """;

        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@UpdatedAt", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@Id", messageId.ToString());

        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogWarning("Message {Id} dead-lettered", messageId);
    }

    public async Task<int> GetQueueDepthAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = "SELECT COUNT(*) FROM EmailQueue WHERE Status IN ('Pending', 'Processing', 'Failed')";

        await using var cmd = new SqliteCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<IReadOnlyList<RelayMessage>> GetMessagesAsync(int skip, int take, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            SELECT * FROM EmailQueue
            ORDER BY ReceivedAt DESC
            LIMIT @Take OFFSET @Skip
            """;

        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Take", take);
        cmd.Parameters.AddWithValue("@Skip", skip);

        var messages = new List<RelayMessage>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            messages.Add(MapMessage(reader));
        }

        return messages;
    }

    public async Task CleanupOldMessagesAsync(int retentionDays, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays).ToString("O");

        const string sql = """
            DELETE FROM EmailQueue
            WHERE Status IN ('Delivered', 'DeadLettered', 'PartiallyDelivered')
            AND ReceivedAt < @Cutoff
            """;

        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Cutoff", cutoff);

        var deleted = await cmd.ExecuteNonQueryAsync(ct);
        if (deleted > 0)
        {
            _logger.LogInformation("Cleanup removed {Count} old messages", deleted);
        }
    }

    private async Task UpdateStatusAsync(SqliteConnection conn, Guid messageId, MessageStatus status, string? error, CancellationToken ct)
    {
        const string sql = """
            UPDATE EmailQueue
            SET Status = @Status, LastError = COALESCE(@Error, LastError), UpdatedAt = @UpdatedAt
            WHERE Id = @Id
            """;

        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Status", status.ToString());
        cmd.Parameters.AddWithValue("@Error", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@UpdatedAt", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@Id", messageId.ToString());

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static RelayMessage MapMessage(SqliteDataReader reader)
    {
        var toJson = reader.GetString(reader.GetOrdinal("ToAddresses"));
        var ccJson = reader.GetString(reader.GetOrdinal("CcAddresses"));
        var resultsJson = reader.IsDBNull(reader.GetOrdinal("ChannelResults"))
            ? null
            : reader.GetString(reader.GetOrdinal("ChannelResults"));

        return new RelayMessage
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("Id"))),
            ReceivedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("ReceivedAt"))),
            Status = Enum.Parse<MessageStatus>(reader.GetString(reader.GetOrdinal("Status"))),
            RetryCount = reader.GetInt32(reader.GetOrdinal("RetryCount")),
            LastError = reader.IsDBNull(reader.GetOrdinal("LastError")) ? null : reader.GetString(reader.GetOrdinal("LastError")),
            From = reader.GetString(reader.GetOrdinal("FromAddress")),
            To = JsonSerializer.Deserialize<List<string>>(toJson, JsonOptions) ?? new(),
            Cc = JsonSerializer.Deserialize<List<string>>(ccJson, JsonOptions) ?? new(),
            Subject = reader.GetString(reader.GetOrdinal("Subject")),
            MessageId = reader.GetString(reader.GetOrdinal("MessageId")),
            MimeContent = GetBlobBytes(reader, "MimeContent"),
            ChannelResults = resultsJson != null
                ? JsonSerializer.Deserialize<Dictionary<string, ChannelDeliveryResult>>(resultsJson, JsonOptions) ?? new()
                : new()
        };
    }

    private static byte[] GetBlobBytes(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal)) return Array.Empty<byte>();

        using var stream = reader.GetStream(ordinal);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
    }
}