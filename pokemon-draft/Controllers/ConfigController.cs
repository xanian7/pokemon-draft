using Microsoft.AspNetCore.Mvc;

namespace PokemonDraft.Controllers;

[ApiController]
[Route("api/config")]
public class ConfigController(IConfiguration config) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            googleClientId = config["Google:ClientId"] ?? string.Empty,
        });
    }
}
