using SmtpRelay.App.Configuration;
using Microsoft.Extensions.Options;

namespace SmtpRelay.App.SmtpServer;

public class RelayMailboxFilter : SmtpServerStorage.IMailboxFilter
{
    private readonly IOptions<SmtpRelayConfig> _config;
    private readonly ILogger<RelayMailboxFilter> _logger;

    public RelayMailboxFilter(IOptions<SmtpRelayConfig> config, ILogger<RelayMailboxFilter> logger)
    {
        _config = config;
        _logger = logger;
    }

    public Task<bool> CanAcceptFromAsync(
        SmtpServerApi.ISessionContext context,
        SmtpServerMail.IMailbox from,
        int size,
        CancellationToken ct)
    {
        var maxSize = _config.Value.SmtpServer.MaxMessageSizeMB * 1024 * 1024;
        if (size > maxSize)
        {
            _logger.LogWarning("Message too large ({Size} bytes) from {From}, max is {Max}",
                size, from, maxSize);
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    public Task<bool> CanDeliverToAsync(
        SmtpServerApi.ISessionContext context,
        SmtpServerMail.IMailbox to,
        SmtpServerMail.IMailbox from,
        CancellationToken ct)
    {
        var allowedDomains = _config.Value.SmtpServer.AllowedRecipientDomains;

        if (allowedDomains == "*")
        {
            return Task.FromResult(true);
        }

        var domains = allowedDomains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var recipientDomain = to.Host?.ToLowerInvariant() ?? "";

        if (domains.Any(d => string.Equals(d.Trim(), recipientDomain, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(true);
        }

        _logger.LogWarning("Recipient domain {Domain} not allowed", recipientDomain);
        return Task.FromResult(false);
    }
}