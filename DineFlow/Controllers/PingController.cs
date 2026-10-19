using Microsoft.AspNetCore.Mvc;

namespace DineFlow.Controllers;

[ApiController]
[Route("v1/public/ping")]
public class PingController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { service = "DineFlow", time = DateTimeOffset.UtcNow });
}
