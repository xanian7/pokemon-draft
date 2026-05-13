using Microsoft.AspNetCore.Mvc;
using PokemonDraft.Services;

namespace PokemonDraft.Controllers;

[ApiController]
[Route("api/pokemon")]
public class PokemonController(IPokemonService pokemonService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await pokemonService.GetAllPokemon());

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var pokemon = await pokemonService.GetPokemonById(id);
        return pokemon is null ? NotFound() : Ok(pokemon);
    }
}
