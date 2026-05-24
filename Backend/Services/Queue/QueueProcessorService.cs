using SmtpRelay.App.Configuration;
using SmtpRelay.App.Models;
using SmtpRelay.App.Services.Output;
using SmtpRelay.App.Services.Queue;
using Microsoft.Extensions.Options;

namespace SmtpRelay.App.Services.Queue;

public class QueueProcessorService : BackgroundService
{
    private readonly IQueueService _queue;
    private readonly IEnumerable<IOutputHandler> _handlers;
    private readonly IOptions<QueueConfig> _config;
    private readonly ILogger<QueueProcessorService> _logger;
    private readonly SemaphoreSlim _concurrencySemaphore;

    public QueueProcessorService(
        IQueueService queue,
        IEnumerable<IOutputHandler> handlers,
        IOptions<SmtpRelayConfig> config,
        ILogger<QueueProcessorService> logger)
    {
        _queue = queue;
        _handlers = handlers;
        _config = Options.Create(config.Value.Queue);
        _logger = logger;
        _concurrencySemaphore = new SemaphoreSlim(config.Value.Queue.MaxConcurrentProcessing);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Queue processor started (interval: {Interval}s, max concurrent: {Max})",
            _config.Value.ProcessingIntervalSeconds, _config.Value.MaxConcurrentProcessing);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in queue processor loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(_config.Value.ProcessingIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("Queue processor stopped");
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        var enabledHandlers = _handlers.Where(h => h.IsEnabled).ToList();

        if (enabledHandlers.Count == 0)
        {
            return;
        }

        var messages = await _queue.GetPendingMessagesAsync(5, ct);

        if (messages.Count == 0)
        {
            return;
        }

        _logger.LogDebug("Processing batch of {Count} messages", messages.Count);

        var tasks = messages.Select(msg => ProcessMessageAsync(msg, enabledHandlers, ct));
        await Task.WhenAll(tasks);
    }

    private async Task ProcessMessageAsync(RelayMessage message, List<IOutputHandler> handlers, CancellationToken ct)
    {
        await _concurrencySemaphore.WaitAsync(ct);

        try
        {
            _logger.LogInformation("Delivering message {Id} via {Count} channels", message.Id, handlers.Count);

            var tasks = handlers.Select(h => DeliverWithRetryAsync(h, message, ct)).ToArray();
            var results = await Task.WhenAll(tasks);

            var resultsDict = new Dictionary<string, ChannelDeliveryResult>();
            var allSuccess = true;

            for (int i = 0; i < handlers.Count; i++)
            {
                resultsDict[handlers[i].ChannelName] = results[i];
                if (!results[i].Success) allSuccess = false;
            }

            if (allSuccess)
            {
                await _queue.MarkDeliveredAsync(message.Id, resultsDict, ct);
            }
            else if (message.RetryCount >= _config.Value.MaxRetryCount)
            {
                _logger.LogWarning("Message {Id} exceeded max retries, dead-lettering", message.Id);
                await _queue.MarkDeadLetteredAsync(message.Id, ct);
            }
            else
            {
                var errors = results.Where(r => !r.Success)
                    .Select(r => $"{r.ChannelName}: {r.ErrorMessage}");
                await _queue.MarkFailedAsync(message.Id, string.Join("; ", errors), ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process message {Id}", message.Id);
            await _queue.MarkFailedAsync(message.Id, ex.Message, ct);
        }
        finally
        {
            _concurrencySemaphore.Release();
        }
    }

    private async Task<ChannelDeliveryResult> DeliverWithRetryAsync(
        IOutputHandler handler, RelayMessage message, CancellationToken ct)
    {
        var attempt = 0;
        const int maxAttempts = 3;

        while (attempt < maxAttempts)
        {
            attempt++;
            var result = await handler.DeliverAsync(message, ct);

            if (result.Success)
            {
                return result;
            }

            if (attempt < maxAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.LogWarning(
                    "Channel {Channel} attempt {Attempt}/{Max} failed for message {Id}, retrying in {Delay}s",
                    handler.ChannelName, attempt, maxAttempts, message.Id, delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }
            else
            {
                _logger.LogError(
                    "Channel {Channel} exhausted {Max} attempts for message {Id}: {Error}",
                    handler.ChannelName, maxAttempts, message.Id, result.ErrorMessage);
                return result;
            }
        }

        return new ChannelDeliveryResult
        {
            ChannelName = handler.ChannelName,
            Success = false,
            ErrorMessage = "Retry exhausted",
            AttemptedAt = DateTimeOffset.UtcNow
        };
    }

    public override void Dispose()
    {
        _concurrencySemaphore.Dispose();
        base.Dispose();
    }
}