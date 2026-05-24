using SmtpRelay.App.Models;

namespace SmtpRelay.App.Services.Output;

public interface IOutputHandler
{
    string ChannelName { get; }
    bool IsEnabled { get; }
    Task<ChannelDeliveryResult> DeliverAsync(RelayMessage message, CancellationToken ct = default);
}