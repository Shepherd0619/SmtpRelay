namespace SmtpRelay.App.Models;

public class ChannelDeliveryResult
{
    public string ChannelName { get; set; } = "";
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset AttemptedAt { get; set; } = DateTimeOffset.UtcNow;
}