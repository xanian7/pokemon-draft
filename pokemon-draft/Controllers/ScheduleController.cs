using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PokemonDraft.DTOs;
using PokemonDraft.Hubs;
using PokemonDraft.Services;

namespace PokemonDraft.Controllers;

[Route("api/leagues/{code}")]
public class ScheduleController(ILeagueService leagueService, IHubContext<DraftHub> hub)
    : LeagueBaseController(leagueService, hub)
{
    [HttpGet("schedule")]
    public IActionResult GetSchedule(string code)
    {
        var schedule = leagueService.GetSchedule(code);
        return schedule is null ? NotFound() : Ok(schedule);
    }

    [HttpGet("playoff-outlook")]
    public IActionResult GetPlayoffOutlook(string code)
    {
        var outlook = leagueService.GetPlayoffOutlook(code);
        return outlook is null ? NotFound() : Ok(outlook);
    }

    [HttpPost("schedule/{matchupId}/report")]
    public async Task<IActionResult> ReportMatchup(string code, int matchupId, ReportMatchupRequest req)
    {
        var (success, error) = leagueService.ReportMatchup(code, matchupId, req.PlayerId, req.Pin, req.Player1Wins, req.Player2Wins);
        if (!success) return BadRequest(error);
        await hub.Clients.Group(code.ToUpperInvariant()).SendAsync("ScheduleUpdate", leagueService.GetSchedule(code));
        return Ok();
    }

    [HttpPatch("schedule/{matchupId}/edit")]
    public async Task<IActionResult> EditMatchup(string code, int matchupId, EditMatchupRequest req)
    {
        var (success, error) = leagueService.EditMatchup(code, matchupId, req.AdminPin, req.Player1Wins, req.Player2Wins);
        if (!success) return BadRequest(error);
        await hub.Clients.Group(code.ToUpperInvariant()).SendAsync("ScheduleUpdate", leagueService.GetSchedule(code));
        return Ok();
    }
}
