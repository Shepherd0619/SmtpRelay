using SmtpRelay.App.Configuration;
using SmtpRelay.App.Models;
using Microsoft.Extensions.Options;
using MimeKit;

namespace SmtpRelay.App.Services.Output;

public class GraphApiOutputHandler : IOutputHandler
{
    private readonly IOptions<SmtpRelayConfig> _config;
    private readonly ILogger<GraphApiOutputHandler> _logger;

    public string ChannelName => "GraphApi";
    public bool IsEnabled => _config.Value.GraphApi.Enabled;

    public GraphApiOutputHandler(IOptions<SmtpRelayConfig> config, ILogger<GraphApiOutputHandler> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<ChannelDeliveryResult> DeliverAsync(RelayMessage message, CancellationToken ct = default)
    {
        var graphConfig = _config.Value.GraphApi;

        try
        {
            if (string.IsNullOrEmpty(graphConfig.TenantId) ||
                string.IsNullOrEmpty(graphConfig.ClientId) ||
                string.IsNullOrEmpty(graphConfig.ClientSecret))
            {
                throw new InvalidOperationException("Graph API configuration is incomplete");
            }

            var mimeMessage = MimeMessage.Load(new MemoryStream(message.MimeContent));

            var credential = new Azure.Identity.ClientSecretCredential(
                graphConfig.TenantId, graphConfig.ClientId, graphConfig.ClientSecret);

            var graphClient = new Microsoft.Graph.GraphServiceClient(credential);

            var graphMessage = new Microsoft.Graph.Models.Message
            {
                Subject = mimeMessage.Subject ?? message.Subject,
                From = new Microsoft.Graph.Models.Recipient
                {
                    EmailAddress = new Microsoft.Graph.Models.EmailAddress { Address = message.From }
                },
                ToRecipients = message.To.Select(t => new Microsoft.Graph.Models.Recipient
                {
                    EmailAddress = new Microsoft.Graph.Models.EmailAddress { Address = t }
                }).ToList()
            };

            if (message.Cc.Count > 0)
            {
                graphMessage.CcRecipients = message.Cc.Select(c => new Microsoft.Graph.Models.Recipient
                {
                    EmailAddress = new Microsoft.Graph.Models.EmailAddress { Address = c }
                }).ToList();
            }

            var body = mimeMessage.HtmlBody ?? mimeMessage.TextBody;
            var contentType = !string.IsNullOrEmpty(mimeMessage.HtmlBody)
                ? Microsoft.Graph.Models.BodyType.Html
                : Microsoft.Graph.Models.BodyType.Text;

            graphMessage.Body = new Microsoft.Graph.Models.ItemBody
            {
                ContentType = contentType,
                Content = body
            };

            if (mimeMessage.Attachments.Any())
            {
                graphMessage.Attachments = new List<Microsoft.Graph.Models.Attachment>();

                foreach (var attachment in mimeMessage.Attachments)
                {
                    if (attachment is not MimePart { Content: not null } part)
                        continue;

                    using var attachStream = new MemoryStream();
                    await part.Content.DecodeToAsync(attachStream, ct);

                    graphMessage.Attachments.Add(new Microsoft.Graph.Models.FileAttachment
                    {
                        Name = part.FileName ?? "unnamed",
                        ContentType = part.ContentType.MimeType,
                        ContentBytes = attachStream.ToArray(),
                        ContentId = part.ContentId
                    });
                }
            }

            if (!string.IsNullOrEmpty(graphConfig.UserId))
            {
                if (graphConfig.CreateDraftsBeforeSending)
                {
                    var draft = await graphClient.Users[graphConfig.UserId]
                        .Messages
                        .PostAsync(graphMessage, cancellationToken: ct);

                    _logger.LogInformation("Draft created with ID {DraftId}", draft?.Id);

                    await graphClient.Users[graphConfig.UserId]
                        .Messages[draft!.Id]
                        .Send
                        .PostAsync(null, cancellationToken: ct);
                }
                else
                {
                    var requestBody = new Microsoft.Graph.Users.Item.SendMail.SendMailPostRequestBody
                    {
                        Message = graphMessage,
                        SaveToSentItems = graphConfig.SaveToSentItems
                    };

                    await graphClient.Users[graphConfig.UserId]
                        .SendMail
                        .PostAsync(requestBody, cancellationToken: ct);
                }

                _logger.LogInformation("Graph API delivery succeeded for message {Id}", message.Id);
            }
            else
            {
                _logger.LogWarning("Graph API UserId not configured, skipping delivery for message {Id}", message.Id);
            }

            return new ChannelDeliveryResult
            {
                ChannelName = ChannelName,
                Success = true,
                AttemptedAt = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Graph API delivery failed for message {Id}", message.Id);
            return new ChannelDeliveryResult
            {
                ChannelName = ChannelName,
                Success = false,
                ErrorMessage = ex.Message,
                AttemptedAt = DateTimeOffset.UtcNow
            };
        }
    }
}