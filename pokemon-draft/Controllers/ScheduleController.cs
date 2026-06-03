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
        var schedule = LeagueService.GetSchedule(code);
        return schedule is null ? NotFound() : Ok(schedule);
    }

    [HttpGet("playoff-outlook")]
    public IActionResult GetPlayoffOutlook(string code)
    {
        var outlook = LeagueService.GetPlayoffOutlook(code);
        return outlook is null ? NotFound() : Ok(outlook);
    }

    [HttpPost("schedule/{matchupId}/report")]
    public async Task<IActionResult> ReportMatchup(string code, int matchupId, ReportMatchupRequest req)
    {
        var (success, error) = LeagueService.ReportMatchup(code, matchupId, req.PlayerId, req.Pin, req.Player1Wins, req.Player2Wins, req.ReplayUrl);
        if (!success) return BadRequest(error);
        await Hub.Clients.Group(code.ToUpperInvariant()).SendAsync("ScheduleUpdate", LeagueService.GetSchedule(code));
        return Ok();
    }

    [HttpPatch("schedule/{matchupId}/edit")]
    public async Task<IActionResult> EditMatchup(string code, int matchupId, EditMatchupRequest req)
    {
        var (success, error) = LeagueService.EditMatchup(code, matchupId, req.AdminPin, req.Player1Wins, req.Player2Wins, req.ReplayUrl);
        if (!success) return BadRequest(error);
        await Hub.Clients.Group(code.ToUpperInvariant()).SendAsync("ScheduleUpdate", LeagueService.GetSchedule(code));
        return Ok();
    }
}
