using System.Text;
using System.Text.Json;
using SmtpRelay.App.Configuration;
using SmtpRelay.App.Models;
using Microsoft.Extensions.Options;
using MimeKit;

namespace SmtpRelay.App.Services.Output;

public class WebhookOutputHandler : IOutputHandler
{
    private readonly IOptions<SmtpRelayConfig> _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookOutputHandler> _logger;

    public string ChannelName => "WebhookOutput";
    public bool IsEnabled => _config.Value.WebhookOutput.Enabled;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public WebhookOutputHandler(
        IOptions<SmtpRelayConfig> config,
        IHttpClientFactory httpClientFactory,
        ILogger<WebhookOutputHandler> logger)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ChannelDeliveryResult> DeliverAsync(RelayMessage message, CancellationToken ct = default)
    {
        var webhookConfig = _config.Value.WebhookOutput;
        var allSuccess = true;
        var errors = new List<string>();

        foreach (var target in webhookConfig.Targets)
        {
            try
            {
                var payload = await BuildWebhookPayload(message);
                var json = JsonSerializer.Serialize(payload, JsonOptions);

                var client = _httpClientFactory.CreateClient("WebhookOutput");
                var request = new HttpRequestMessage(HttpMethod.Post, target.Url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                foreach (var header in target.Headers)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                var response = await client.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Webhook {Name} delivered successfully (HTTP {Status})",
                        target.Name, (int)response.StatusCode);
                }
                else
                {
                    allSuccess = false;
                    var err = $"HTTP {(int)response.StatusCode}: {body[..Math.Min(body.Length, 500)]}";
                    errors.Add($"{target.Name}: {err}");
                    _logger.LogWarning("Webhook {Name} returned {StatusCode}: {Body}",
                        target.Name, (int)response.StatusCode, body[..Math.Min(body.Length, 200)]);
                }
            }
            catch (Exception ex)
            {
                allSuccess = false;
                errors.Add($"{target.Name}: {ex.Message}");
                _logger.LogError(ex, "Webhook {Name} delivery failed for message {Id}", target.Name, message.Id);
            }
        }

        return new ChannelDeliveryResult
        {
            ChannelName = ChannelName,
            Success = webhookConfig.Targets.Count > 0 && allSuccess,
            ErrorMessage = errors.Count > 0 ? string.Join("; ", errors) : null,
            AttemptedAt = DateTimeOffset.UtcNow
        };
    }

    private static async Task<object> BuildWebhookPayload(RelayMessage message)
    {
        var mimeStream = new MemoryStream(message.MimeContent);
        var mimeMessage = await MimeMessage.LoadAsync(mimeStream);

        var headers = new Dictionary<string, string>();
        foreach (var header in mimeMessage.Headers)
        {
            headers[header.Field] = header.Value;
        }

        var attachments = new List<object>();
        foreach (var attachment in mimeMessage.Attachments)
        {
            if (attachment is MimePart { Content: not null } part)
            {
                using var ms = new MemoryStream();
                await part.Content.DecodeToAsync(ms);
                attachments.Add(new
                {
                    fileName = part.FileName,
                    contentType = part.ContentType.MimeType,
                    contentBase64 = Convert.ToBase64String(ms.ToArray()),
                    contentId = part.ContentId
                });
            }
        }

        return new
        {
            messageId = message.Id,
            originalMessageId = message.MessageId,
            from = message.From,
            to = message.To,
            cc = message.Cc,
            subject = message.Subject,
            date = message.ReceivedAt,
            headers,
            body = mimeMessage.TextBody ?? "",
            bodyHtml = mimeMessage.HtmlBody ?? "",
            attachments,
            rawMime = Convert.ToBase64String(message.MimeContent)
        };
    }
}