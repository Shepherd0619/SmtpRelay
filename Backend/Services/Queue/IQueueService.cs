using SmtpRelay.App.Models;

namespace SmtpRelay.App.Services.Queue;

public interface IQueueService
{
    Task InitializeAsync(CancellationToken ct = default);
    Task EnqueueAsync(RelayMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<RelayMessage>> GetPendingMessagesAsync(int limit, CancellationToken ct = default);
    Task MarkDeliveredAsync(Guid messageId, Dictionary<string, ChannelDeliveryResult> results, CancellationToken ct = default);
    Task MarkFailedAsync(Guid messageId, string error, CancellationToken ct = default);
    Task MarkDeadLetteredAsync(Guid messageId, CancellationToken ct = default);
    Task<int> GetQueueDepthAsync(CancellationToken ct = default);
    Task<IReadOnlyList<RelayMessage>> GetMessagesAsync(int skip, int take, CancellationToken ct = default);
    Task CleanupOldMessagesAsync(int retentionDays, CancellationToken ct = default);
}