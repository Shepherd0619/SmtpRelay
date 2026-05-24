using SmtpRelay.App.Services.Queue;
using MimeKit;
using SmtpRelay.App.Models;

namespace SmtpRelay.App.Services;

public interface IEmailProcessingService
{
    Task ProcessReceivedEmailAsync(byte[] mimeBytes, CancellationToken ct = default);
}