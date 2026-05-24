namespace SmtpRelay.App.Models;

public class RelayMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public string From { get; set; } = "";
    public List<string> To { get; set; } = new();
    public List<string> Cc { get; set; } = new();
    public string Subject { get; set; } = "";
    public string MessageId { get; set; } = "";
    public byte[] MimeContent { get; set; } = Array.Empty<byte>();
    public MessageStatus Status { get; set; } = MessageStatus.Pending;
    public int RetryCount { get; set; } = 0;
    public string? LastError { get; set; }
    public Dictionary<string, ChannelDeliveryResult> ChannelResults { get; set; } = new();
}

public enum MessageStatus
{
    Pending,
    Processing,
    Delivered,
    PartiallyDelivered,
    Failed,
    DeadLettered
}