using Microsoft.AspNetCore.Mvc;
using SmtpRelay.App.Services.Queue;

namespace SmtpRelay.App.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MetricsController : ControllerBase
{
    private readonly IQueueService _queue;

    public MetricsController(IQueueService queue)
    {
        _queue = queue;
    }

    [HttpGet]
    public async Task<IActionResult> GetMetrics()
    {
        var depth = await _queue.GetQueueDepthAsync();

        return Ok(new
        {
            queueDepth = depth,
            uptime = Environment.TickCount64,
            timestamp = DateTimeOffset.UtcNow
        });
    }
}