using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class DiscordController(IDiscordService discordService) : ControllerBase
{
    private readonly IDiscordService _discordService = discordService;

    public record SendMessageRequest(string Content);

    [HttpPost("send")]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest req, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(req.Content)) return BadRequest("Message cannot be empty.");

        try
        {
            await _discordService.SendMessageAsync(req.Content, cancellationToken);
            return Ok("Message sent successfully.");
        }
        catch (OperationCanceledException)
        {
            return StatusCode(503, "Message sending was canceled.");
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"An error occurred while sending the message: {ex.Message}");
        }
    }
}