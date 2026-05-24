using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using SmtpRelay.App.Configuration;
using SmtpRelay.App.Models;
using Microsoft.Extensions.Options;

namespace SmtpRelay.App.Services.Output;

public class SmtpOutputHandler : IOutputHandler
{
    private readonly IOptions<SmtpRelayConfig> _config;
    private readonly ILogger<SmtpOutputHandler> _logger;

    public string ChannelName => "SmtpOutput";
    public bool IsEnabled => _config.Value.SmtpOutput.Enabled;

    public SmtpOutputHandler(IOptions<SmtpRelayConfig> config, ILogger<SmtpOutputHandler> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<ChannelDeliveryResult> DeliverAsync(RelayMessage message, CancellationToken ct = default)
    {
        var smtpConfig = _config.Value.SmtpOutput;

        try
        {
            using var mimeStream = new MemoryStream(message.MimeContent);
            var mimeMessage = await MimeMessage.LoadAsync(mimeStream, ct);

            if (!string.IsNullOrEmpty(smtpConfig.FromAddress))
            {
                mimeMessage.From.Clear();
                mimeMessage.From.Add(MailboxAddress.Parse(smtpConfig.FromAddress));
            }

            using var client = new SmtpClient();

            var secureOption = smtpConfig.UseSsl
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

            await client.ConnectAsync(smtpConfig.Host, smtpConfig.Port, secureOption, ct);

            if (!string.IsNullOrEmpty(smtpConfig.Username))
            {
                await client.AuthenticateAsync(smtpConfig.Username, smtpConfig.Password, ct);
            }

            await client.SendAsync(FormatOptions.Default, mimeMessage, ct);
            await client.DisconnectAsync(true, ct);

            _logger.LogInformation("SMTP relay succeeded for message {Id} via {Host}:{Port}",
                message.Id, smtpConfig.Host, smtpConfig.Port);

            return new ChannelDeliveryResult
            {
                ChannelName = ChannelName,
                Success = true,
                AttemptedAt = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP relay failed for message {Id}", message.Id);
            return new ChannelDeliveryResult
            {
                ChannelName = ChannelName,
                Success = false,
                ErrorMessage = ex.Message,
                AttemptedAt = DateTimeOffset.UtcNow
            };
        }
    }
}