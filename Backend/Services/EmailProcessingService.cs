using MimeKit;
using SmtpRelay.App.Models;
using SmtpRelay.App.Services.Queue;

namespace SmtpRelay.App.Services;

public class EmailProcessingService : IEmailProcessingService
{
    private readonly IQueueService _queue;
    private readonly ILogger<EmailProcessingService> _logger;

    public EmailProcessingService(IQueueService queue, ILogger<EmailProcessingService> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    public async Task ProcessReceivedEmailAsync(byte[] mimeBytes, CancellationToken ct = default)
    {
        try
        {
            using var memoryStream = new MemoryStream(mimeBytes);
            var mimeMessage = await MimeMessage.LoadAsync(memoryStream, ct);

            var relayMessage = new RelayMessage
            {
                Id = Guid.NewGuid(),
                ReceivedAt = DateTimeOffset.UtcNow,
                From = mimeMessage.From?.FirstOrDefault()?.ToString() ?? "",
                To = mimeMessage.To?.Mailboxes?.Select(m => m.Address).ToList() ?? new(),
                Cc = mimeMessage.Cc?.Mailboxes?.Select(m => m.Address).ToList() ?? new(),
                Subject = mimeMessage.Subject ?? "(no subject)",
                MessageId = mimeMessage.MessageId ?? "",
                MimeContent = mimeBytes,
                Status = MessageStatus.Pending
            };

            await _queue.EnqueueAsync(relayMessage, ct);

            _logger.LogInformation(
                "Email processed: Id={Id}, From={From}, To={To}, Subject={Subject}",
                relayMessage.Id, relayMessage.From, string.Join(",", relayMessage.To), relayMessage.Subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse received email ({Size} bytes)", mimeBytes.Length);
            throw;
        }
    }
}