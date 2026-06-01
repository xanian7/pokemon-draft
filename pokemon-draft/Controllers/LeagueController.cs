using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PokemonDraft.DTOs;
using PokemonDraft.Hubs;
using PokemonDraft.Services;

namespace PokemonDraft.Controllers;

[Route("api/leagues")]
public class LeagueController(ILeagueService leagueService, IHubContext<DraftHub> hub)
    : LeagueBaseController(leagueService, hub)
{
    [HttpPost]
    public IActionResult CreateLeague(CreateLeagueRequest req)
    {
        var reqWithUser = req with { UserId = GetRequestUserId() };
        var (result, error) = leagueService.CreateLeague(reqWithUser);
        return result is null ? BadRequest(error) : Ok(result);
    }

    [HttpGet("{code}")]
    public IActionResult GetLeague(string code)
    {
        var response = leagueService.GetLeagueResponse(code);
        return response is null ? NotFound() : Ok(response);
    }

    [HttpPatch("{code}/config")]
    public async Task<IActionResult> UpdateConfig(string code, UpdateLeagueConfigRequest req)
    {
        var (success, error) = leagueService.UpdateConfig(code, req);
        if (!success) return error is null ? NotFound() : BadRequest(error);
        await BroadcastLeague(code);
        return Ok();
    }

    [HttpPut("{code}/points")]
    public async Task<IActionResult> SetPointValues(string code, SetPointValuesRequest req)
    {
        var (success, error) = leagueService.SetPointValues(code, req.Values);
        if (!success) return error is null ? NotFound() : BadRequest(error);
        await BroadcastLeague(code);
        return Ok();
    }
}
