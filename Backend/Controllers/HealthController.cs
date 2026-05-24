using Microsoft.AspNetCore.Mvc;

namespace SmtpRelay.App.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly ILogger<HealthController> _logger;

    public HealthController(ILogger<HealthController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            status = "healthy",
            timestamp = DateTimeOffset.UtcNow,
            version = "1.0.0"
        });
    }

    [HttpGet("ready")]
    public IActionResult GetReady()
    {
        return Ok(new { status = "ready" });
    }
}