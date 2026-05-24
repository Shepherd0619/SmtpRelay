using SmtpRelay.App.Configuration;
using Microsoft.Extensions.Options;

namespace SmtpRelay.App.SmtpServer;

public class SmtpServerHostedService : BackgroundService
{
    private readonly SmtpServerApi.SmtpServer _smtpServer;
    private readonly SmtpServerConfig _config;
    private readonly ILogger<SmtpServerHostedService> _logger;

    public SmtpServerHostedService(
        IServiceProvider serviceProvider,
        IOptions<SmtpRelayConfig> config,
        ILogger<SmtpServerHostedService> logger)
    {
        _config = config.Value.SmtpServer;
        _logger = logger;

        var options = new SmtpServerApi.SmtpServerOptionsBuilder()
            .ServerName(_config.HostName)
            .Port(_config.Port)
            .MaxMessageSize(_config.MaxMessageSizeMB * 1024 * 1024)
            .Build();

        _smtpServer = new SmtpServerApi.SmtpServer(options, serviceProvider);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SMTP server starting on port {Port} (hostname: {HostName})",
            _config.Port, _config.HostName);

        try
        {
            await _smtpServer.StartAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("SMTP server cancellation requested");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP server error");
        }

        _logger.LogInformation("SMTP server stopped");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping SMTP server...");
        _smtpServer.Shutdown();
        await base.StopAsync(cancellationToken);
    }
}