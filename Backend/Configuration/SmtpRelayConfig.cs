namespace SmtpRelay.App.Configuration;

public class SmtpRelayConfig
{
    public const string SectionName = "SmtpRelay";

    public SmtpServerConfig SmtpServer { get; set; } = new();
    public GraphApiConfig GraphApi { get; set; } = new();
    public SmtpOutputConfig SmtpOutput { get; set; } = new();
    public WebhookOutputConfig WebhookOutput { get; set; } = new();
    public WebhookInputConfig WebhookInput { get; set; } = new();
    public QueueConfig Queue { get; set; } = new();
}