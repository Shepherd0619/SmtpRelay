using System.Text;
using Microsoft.AspNetCore.Mvc;
using MimeKit;
using SmtpRelay.App.Configuration;
using SmtpRelay.App.Models;
using SmtpRelay.App.Services;
using Microsoft.Extensions.Options;

namespace SmtpRelay.App.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WebhookController : ControllerBase
{
    private readonly IEmailProcessingService _processingService;
    private readonly IOptions<WebhookInputConfig> _config;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        IEmailProcessingService processingService,
        IOptions<SmtpRelayConfig> config,
        ILogger<WebhookController> logger)
    {
        _processingService = processingService;
        _config = Options.Create(config.Value.WebhookInput);
        _logger = logger;
    }

    [HttpPost("email")]
    public async Task<IActionResult> ReceiveEmail(
        [FromBody] IncomingWebhookPayload payload)
    {
        if (!_config.Value.Enabled)
        {
            return StatusCode(503, new { error = "Webhook input is disabled" });
        }

        if (!string.IsNullOrEmpty(_config.Value.ApiKey))
        {
            var authHeader = Request.Headers.Authorization.ToString();
            if (string.IsNullOrEmpty(authHeader) || authHeader != $"Bearer {_config.Value.ApiKey}")
            {
                return Unauthorized(new { error = "Invalid or missing API key" });
            }
        }

        try
        {
            var mimeBytes = BuildMimeMessage(payload);

            await _processingService.ProcessReceivedEmailAsync(mimeBytes);

            return Accepted(new { status = "accepted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process incoming webhook email");
            return BadRequest(new { error = ex.Message });
        }
    }

    private static byte[] BuildMimeMessage(IncomingWebhookPayload payload)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(payload.From));
        message.To.AddRange(payload.To.Select(MailboxAddress.Parse));

        if (payload.Cc?.Count > 0)
        {
            message.Cc.AddRange(payload.Cc.Select(MailboxAddress.Parse));
        }

        message.Subject = payload.Subject;

        var builder = new BodyBuilder();

        if (!string.IsNullOrEmpty(payload.BodyHtml))
        {
            builder.HtmlBody = payload.BodyHtml;
            builder.TextBody = payload.Body;
        }
        else
        {
            builder.TextBody = payload.Body;
        }

        if (payload.Attachments != null)
        {
            foreach (var att in payload.Attachments)
            {
                var bytes = Convert.FromBase64String(att.ContentBase64);
                builder.Attachments.Add(att.FileName, bytes, ContentType.Parse(att.ContentType));
            }
        }

        message.Body = builder.ToMessageBody();

        using var ms = new MemoryStream();
        message.WriteTo(ms);
        return ms.ToArray();
    }
}