using Microsoft.AspNetCore.Mvc;
using RealtimeApi.Services;

namespace RealtimeApi.Controllers;

[Route("api/[controller]")]
[ApiController]
public class TcpServerController : ControllerBase
{
    private readonly ILogger<TcpServerController> _logger;
    private readonly ITcpServerManager _tcpServer;

    public TcpServerController(ILogger<TcpServerController> logger, ITcpServerManager tcpServer)
    {
        _logger = logger;
        _tcpServer = tcpServer;
    }

    [HttpPost("broadcast")]
    public IActionResult Broadcast([FromBody] string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return BadRequest("Message cannot be empty.");
        }
        _logger.LogInformation("Broadcasting message: {Message}", message);

        _tcpServer.BroadcastMessage($"{message}{Environment.NewLine}");
        return Ok("Message broadcasted successfully.");
    }
}
