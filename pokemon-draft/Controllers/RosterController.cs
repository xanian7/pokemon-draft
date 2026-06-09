using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PokemonDraft.DTOs;
using PokemonDraft.Hubs;
using PokemonDraft.Services;

namespace PokemonDraft.Controllers;

[Route("api/leagues/{code}/roster")]
public class RosterController(ILeagueService leagueService, IHubContext<DraftHub> hub)
    : LeagueBaseController(leagueService, hub)
{
    [HttpGet("transactions")]
    public IActionResult GetTransactions(string code) =>
        Ok(LeagueService.GetRosterTransactions(code));

    [HttpPost("transaction")]
    public async Task<IActionResult> ApplyTransaction(string code, RosterTransactionRequest req)
    {
        var (success, error) = LeagueService.ApplyRosterTransaction(
            code, req.PlayerId, req.Pin, req.AddPokemonId, req.DropPokemonId);
        if (!success) return BadRequest(error);
        await BroadcastLeague(code);
        return Ok();
    }

    [HttpPost("drop")]
    public async Task<IActionResult> DropPokemon(string code, RosterChangeRequest req)
    {
        var (success, error) = LeagueService.DropPokemon(code, req.PlayerId, req.Pin, req.PokemonId);
        if (!success) return BadRequest(error);
        await BroadcastLeague(code);
        return Ok();
    }

    [HttpPost("add")]
    public async Task<IActionResult> AddPokemon(string code, RosterChangeRequest req)
    {
        var (success, error) = LeagueService.AddPokemon(code, req.PlayerId, req.Pin, req.PokemonId);
        if (!success) return BadRequest(error);
        await BroadcastLeague(code);
        return Ok();
    }
}
