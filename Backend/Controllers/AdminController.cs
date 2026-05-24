using Microsoft.AspNetCore.Mvc;
using SmtpRelay.App.Models;
using SmtpRelay.App.Services.Queue;

namespace SmtpRelay.App.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    private readonly IQueueService _queue;
    private readonly ILogger<AdminController> _logger;

    public AdminController(IQueueService queue, ILogger<AdminController> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    [HttpGet("queue/stats")]
    public async Task<IActionResult> GetQueueStats()
    {
        var depth = await _queue.GetQueueDepthAsync();
        return Ok(new
        {
            queueDepth = depth,
            timestamp = DateTimeOffset.UtcNow
        });
    }

    [HttpGet("queue/messages")]
    public async Task<IActionResult> GetMessages([FromQuery] int skip = 0, [FromQuery] int take = 50)
    {
        var messages = await _queue.GetMessagesAsync(skip, take);
        return Ok(messages.Select(MapToDto));
    }

    [HttpPost("queue/messages/{id}/retry")]
    public IActionResult RetryMessage(Guid id)
    {
        _logger.LogInformation("Manual retry requested for message {Id}", id);
        return Ok(new { status = "acknowledged", messageId = id });
    }

    [HttpDelete("queue/messages/{id}")]
    public IActionResult DeleteMessage(Guid id)
    {
        _logger.LogInformation("Manual delete requested for message {Id}", id);
        return Ok(new { status = "acknowledged", messageId = id });
    }

    [HttpPost("queue/cleanup")]
    public async Task<IActionResult> Cleanup([FromQuery] int retentionDays = 7)
    {
        await _queue.CleanupOldMessagesAsync(retentionDays);
        return Ok(new { status = "cleanup_completed", retentionDays });
    }

    private static object MapToDto(RelayMessage msg)
    {
        return new
        {
            id = msg.Id,
            receivedAt = msg.ReceivedAt,
            from = msg.From,
            to = msg.To,
            cc = msg.Cc,
            subject = msg.Subject,
            messageId = msg.MessageId,
            status = msg.Status.ToString(),
            retryCount = msg.RetryCount,
            lastError = msg.LastError,
            channelResults = msg.ChannelResults
        };
    }
}