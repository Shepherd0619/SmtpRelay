using System.Buffers;
using SmtpRelay.App.Services;

namespace SmtpRelay.App.SmtpServer;

public class RelayMessageStore : SmtpServerStorage.MessageStore
{
    private readonly IEmailProcessingService _processingService;
    private readonly ILogger<RelayMessageStore> _logger;

    public RelayMessageStore(
        IEmailProcessingService processingService,
        ILogger<RelayMessageStore> logger)
    {
        _processingService = processingService;
        _logger = logger;
    }

    public override Task<SmtpServerProtocol.SmtpResponse> SaveAsync(
        SmtpServerApi.ISessionContext context,
        SmtpServerApi.IMessageTransaction transaction,
        ReadOnlySequence<byte> buffer,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = buffer.ToArray();

            _ = Task.Run(async () =>
            {
                try
                {
                    await _processingService.ProcessReceivedEmailAsync(bytes, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process received email in background");
                }
            }, cancellationToken).ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _logger.LogError(t.Exception, "Background email processing faulted");
            }, TaskContinuationOptions.OnlyOnFaulted);

            _logger.LogInformation(
                "Email accepted from {Sender}. Size: {Size} bytes. Recipients: {Recipients}",
                transaction.From,
                bytes.Length,
                string.Join(", ", transaction.To.Select(m => m.ToString())));

            return Task.FromResult(new SmtpServerProtocol.SmtpResponse(
                SmtpServerProtocol.SmtpReplyCode.Ok, "OK"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to accept email");
            return Task.FromResult(new SmtpServerProtocol.SmtpResponse(
                SmtpServerProtocol.SmtpReplyCode.TransactionFailed, ex.Message));
        }
    }
}