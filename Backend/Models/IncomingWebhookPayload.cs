namespace SmtpRelay.App.Models;

public class IncomingWebhookPayload
{
    public string From { get; set; } = "";
    public List<string> To { get; set; } = new();
    public List<string>? Cc { get; set; }
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string? BodyHtml { get; set; }
    public List<WebhookAttachment>? Attachments { get; set; }
}

public class WebhookAttachment
{
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public string ContentBase64 { get; set; } = "";
}