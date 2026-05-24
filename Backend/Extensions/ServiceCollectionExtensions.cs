using SmtpRelay.App.Configuration;
using SmtpRelay.App.Services;
using SmtpRelay.App.Services.Output;
using SmtpRelay.App.Services.Queue;
using SmtpRelay.App.SmtpServer;

namespace SmtpRelay.App.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSmtpRelay(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind typed configuration
        services.Configure<SmtpRelayConfig>(configuration.GetSection(SmtpRelayConfig.SectionName));

        // Queue
        services.AddSingleton<IQueueService, SqliteQueueService>();
        services.AddHostedService<QueueProcessorService>();

        // Email processing
        services.AddSingleton<IEmailProcessingService, EmailProcessingService>();

        // Output handlers
        services.AddSingleton<IOutputHandler, GraphApiOutputHandler>();
        services.AddSingleton<IOutputHandler, SmtpOutputHandler>();
        services.AddSingleton<IOutputHandler, WebhookOutputHandler>();

        // SMTP Server components
        services.AddSingleton<SmtpServerStorage.IMailboxFilter, RelayMailboxFilter>();
        services.AddSingleton<SmtpServerStorage.IMessageStore, RelayMessageStore>();

        // HTTP client for webhook output
        services.AddHttpClient("WebhookOutput", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "SmtpRelay/1.0");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Conditional SMTP server registration
        var smtpConfig = configuration
            .GetSection($"{SmtpRelayConfig.SectionName}:SmtpServer")
            .Get<SmtpServerConfig>() ?? new SmtpServerConfig();

        if (smtpConfig.Enabled)
        {
            services.AddHostedService<SmtpServerHostedService>();
        }

        return services;
    }
}