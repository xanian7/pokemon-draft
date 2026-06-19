using Microsoft.AspNetCore.Mvc;
using PokemonDraft.Services;

namespace PokemonDraft.Controllers;

[ApiController]
[Route("api/pokemon")]
public class PokemonController(IPokemonService pokemonService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            return Ok(await pokemonService.GetAllPokemon());
        }
        catch (HttpRequestException)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                "Pokemon data is temporarily unavailable.");
        }
        catch (InvalidOperationException)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                "Pokemon data is temporarily unavailable.");
        }
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var pokemon = await pokemonService.GetPokemonById(id);
        return pokemon is null ? NotFound() : Ok(pokemon);
    }

    [HttpGet("{id:int}/detail")]
    public async Task<IActionResult> GetDetail(int id)
    {
        try
        {
            var detail = await pokemonService.GetPokemonDetail(id);
            return detail is null ? NotFound() : Ok(detail);
        }
        catch (HttpRequestException)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                "Pokemon detail data is temporarily unavailable.");
        }
    }
}
