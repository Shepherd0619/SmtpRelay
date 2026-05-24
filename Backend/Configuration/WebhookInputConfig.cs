namespace SmtpRelay.App.Configuration;

public class WebhookInputConfig
{
    public bool Enabled { get; set; } = true;
    public string ApiKey { get; set; } = "";
}