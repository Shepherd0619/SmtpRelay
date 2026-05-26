using System.Text.RegularExpressions;
using SmtpRelay.App.Configuration;
using SmtpRelay.App.Models;
using Microsoft.Extensions.Options;
using Microsoft.Graph.Models.ODataErrors;
using MimeKit;

namespace SmtpRelay.App.Services.Output;

public class GraphApiOutputHandler : IOutputHandler
{
    private readonly IOptions<SmtpRelayConfig> _config;
    private readonly ILogger<GraphApiOutputHandler> _logger;

    private static readonly Regex FromRegex = new(@"<([^>]+)>", RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

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

            var fromEmail = ExtractEmailAddress(message.From);

            var graphMessage = BuildGraphMessage(mimeMessage, message, fromEmail);

            await SendViaGraphAsync(graphClient, graphConfig, fromEmail, graphMessage, ct);

            _logger.LogInformation("Graph API delivery succeeded for message {Id}", message.Id);

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

    private async Task SendViaGraphAsync(
        Microsoft.Graph.GraphServiceClient graphClient,
        GraphApiConfig graphConfig,
        string fromEmail,
        Microsoft.Graph.Models.Message graphMessage,
        CancellationToken ct)
    {
        try
        {
            await SendViaUserEndpointAsync(graphClient, graphConfig, fromEmail, graphMessage, ct);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode is 404)
        {
            throw new InvalidOperationException(
                $"Graph API cannot send as '{fromEmail}' — this is not a user or shared mailbox. " +
                "M365 groups and distribution lists are not supported by Graph API with application permissions. " +
                "Use the SMTP output channel instead.");
        }
    }

    private static async Task SendViaUserEndpointAsync(
        Microsoft.Graph.GraphServiceClient graphClient,
        GraphApiConfig graphConfig,
        string userId,
        Microsoft.Graph.Models.Message graphMessage,
        CancellationToken ct)
    {
        if (graphConfig.CreateDraftsBeforeSending)
        {
            var draft = await graphClient.Users[userId]
                .Messages
                .PostAsync(graphMessage, cancellationToken: ct);

            await graphClient.Users[userId]
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

            await graphClient.Users[userId]
                .SendMail
                .PostAsync(requestBody, cancellationToken: ct);
        }
    }

    private static Microsoft.Graph.Models.Message BuildGraphMessage(
        MimeMessage mimeMessage,
        RelayMessage message,
        string fromEmail)
    {
        var graphMessage = new Microsoft.Graph.Models.Message
        {
            Subject = mimeMessage.Subject ?? message.Subject,
            From = new Microsoft.Graph.Models.Recipient
            {
                EmailAddress = new Microsoft.Graph.Models.EmailAddress { Address = fromEmail }
            },
            ToRecipients = message.To.ConvertAll(t => new Microsoft.Graph.Models.Recipient
            {
                EmailAddress = new Microsoft.Graph.Models.EmailAddress { Address = t }
            })
        };

        if (message.Cc.Count > 0)
        {
            graphMessage.CcRecipients = message.Cc.ConvertAll(c => new Microsoft.Graph.Models.Recipient
            {
                EmailAddress = new Microsoft.Graph.Models.EmailAddress { Address = c }
            });
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

        AddAttachments(mimeMessage, graphMessage);

        return graphMessage;
    }

    private static void AddAttachments(MimeMessage mimeMessage, Microsoft.Graph.Models.Message graphMessage)
    {
        if (!mimeMessage.Attachments.Any() && !mimeMessage.BodyParts.Any())
            return;

        graphMessage.Attachments = [];

        foreach (var attachment in mimeMessage.Attachments)
        {
            if (attachment is MimePart { Content: not null } part)
            {
                using var attachStream = new MemoryStream();
                part.Content.DecodeTo(attachStream);

                graphMessage.Attachments.Add(new Microsoft.Graph.Models.FileAttachment
                {
                    Name = part.FileName ?? "unnamed",
                    ContentType = part.ContentType.MimeType,
                    ContentBytes = attachStream.ToArray(),
                    ContentId = part.ContentId
                });
            }
        }

        var attachmentContentIds = new HashSet<string>(
            mimeMessage.Attachments
                .OfType<MimePart>()
                .Select(a => a.ContentId)
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!)
        );

        foreach (var bodyPart in mimeMessage.BodyParts)
        {
            if (bodyPart is MimePart { Content: not null } part
                && !string.IsNullOrEmpty(part.ContentId)
                && !attachmentContentIds.Contains(part.ContentId))
            {
                using var attachStream = new MemoryStream();
                part.Content.DecodeTo(attachStream);

                graphMessage.Attachments.Add(new Microsoft.Graph.Models.FileAttachment
                {
                    Name = part.FileName ?? part.ContentId,
                    ContentType = part.ContentType.MimeType,
                    ContentBytes = attachStream.ToArray(),
                    ContentId = part.ContentId,
                    IsInline = true
                });
            }
        }
    }

    /// <summary>
    /// Extracts the bare email address from a string that may be in
    /// "Display Name" &lt;address@domain&gt; format.
    /// </summary>
    internal static string ExtractEmailAddress(string from)
    {
        var match = FromRegex.Match(from);
        return match.Success ? match.Groups[1].Value : from;
    }
}