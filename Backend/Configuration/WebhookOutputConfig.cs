namespace SmtpRelay.App.Configuration;

public class WebhookOutputConfig
{
    public bool Enabled { get; set; } = false;
    public List<WebhookTarget> Targets { get; set; } = new();
}

public class WebhookTarget
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public Dictionary<string, string> Headers { get; set; } = new();
}