using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PokemonDraft.DTOs;
using PokemonDraft.Hubs;
using PokemonDraft.Services;

namespace PokemonDraft.Controllers;

[Route("api/leagues/{code}/players")]
public class PlayerController(ILeagueService leagueService, IHubContext<DraftHub> hub)
    : LeagueBaseController(leagueService, hub)
{
    [HttpPost("register")]
    public async Task<IActionResult> RegisterPlayer(string code, RegisterPlayerRequest req)
    {
        var reqWithUser = req with { UserId = GetRequestUserId() };
        var (result, error) = leagueService.RegisterPlayer(code, reqWithUser);
        if (result is null) return BadRequest(error);
        await BroadcastLeague(code);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> AddPlayer(string code, AddPlayerRequest req)
    {
        var (result, error) = leagueService.AddPlayer(code, req);
        if (result is null) return error is null ? NotFound() : BadRequest(error);
        await BroadcastLeague(code);
        return Ok(result);
    }

    [HttpDelete("{playerId}")]
    public async Task<IActionResult> RemovePlayer(string code, string playerId)
    {
        var (success, error) = leagueService.RemovePlayer(code, playerId);
        if (!success) return error is null ? NotFound() : BadRequest(error);
        await BroadcastLeague(code);
        return Ok();
    }

    [HttpPost("move")]
    public async Task<IActionResult> MovePlayer(string code, MovePlayerRequest req)
    {
        var (success, error) = leagueService.MovePlayer(code, req.FromIndex, req.ToIndex);
        if (!success) return error is null ? NotFound() : BadRequest(error);
        await BroadcastLeague(code);
        return Ok();
    }

    [HttpPatch("{playerId}/profile")]
    public async Task<IActionResult> UpdatePlayerProfile(string code, string playerId, UpdatePlayerProfileRequest req)
    {
        var (success, error) = leagueService.UpdatePlayerProfile(code, playerId, req.Pin, req.TeamName, req.TeamImageUrl);
        if (!success) return BadRequest(error);
        await BroadcastLeague(code);
        return Ok();
    }
}
