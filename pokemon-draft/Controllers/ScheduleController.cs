using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PokemonDraft.DTOs;
using PokemonDraft.Hubs;
using PokemonDraft.Services;

namespace PokemonDraft.Controllers;

[Route("api/leagues/{code}")]
public class ScheduleController(
    ILeagueService leagueService,
    IReplayAnalysisService replayAnalysisService,
    IHubContext<DraftHub> hub)
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

    [HttpGet("replay-stats")]
    public async Task<IActionResult> GetReplayStats(string code, CancellationToken cancellationToken)
    {
        await replayAnalysisService.AnalyzeMissingAsync(code, cancellationToken);
        var stats = await replayAnalysisService.GetStatsAsync(code, cancellationToken);
        return stats is null ? NotFound() : Ok(stats);
    }

    [HttpPost("schedule")]
    public async Task<IActionResult> CreateScheduleMatchup(string code, CreateScheduleMatchupRequest req)
    {
        var (success, error) = LeagueService.CreateScheduleMatchup(
            code, req.AdminPin, req.Week, req.Player1Id, req.Player2Id);
        if (!success) return error is null ? NotFound() : BadRequest(error);
        await BroadcastSchedule(code);
        return Ok();
    }

    [HttpPatch("schedule/{matchupId}/matchup")]
    public async Task<IActionResult> UpdateScheduleMatchup(string code, int matchupId, UpdateScheduleMatchupRequest req)
    {
        var (success, error) = LeagueService.UpdateScheduleMatchup(
            code, matchupId, req.AdminPin, req.Week, req.Player1Id, req.Player2Id, req.ForceScoredChange);
        if (!success) return error is null ? NotFound() : BadRequest(error);
        await BroadcastSchedule(code);
        return Ok();
    }

    [HttpDelete("schedule/{matchupId}")]
    public async Task<IActionResult> DeleteScheduleMatchup(string code, int matchupId, DeleteScheduleMatchupRequest? req)
    {
        var (success, error) = LeagueService.DeleteScheduleMatchup(
            code, matchupId, req?.AdminPin ?? string.Empty, req?.ForceScoredChange ?? false);
        if (!success) return error is null ? NotFound() : BadRequest(error);
        await BroadcastSchedule(code);
        return Ok();
    }

    [HttpPost("schedule/{matchupId}/report")]
    public async Task<IActionResult> ReportMatchup(string code, int matchupId, ReportMatchupRequest req)
    {
        var (success, error) = LeagueService.ReportMatchup(code, matchupId, req.PlayerId, req.Pin, req.Player1Wins, req.Player2Wins, req.ReplayUrl, req.ReplayUrls);
        if (!success) return BadRequest(error);
        await replayAnalysisService.AnalyzeMatchupAsync(code, matchupId, HttpContext.RequestAborted);
        await Hub.Clients.Group(code.ToUpperInvariant()).SendAsync("ScheduleUpdate", LeagueService.GetSchedule(code));
        return Ok();
    }

    [HttpPatch("schedule/{matchupId}/edit")]
    public async Task<IActionResult> EditMatchup(string code, int matchupId, EditMatchupRequest req)
    {
        var (success, error) = LeagueService.EditMatchup(code, matchupId, req.AdminPin, req.Player1Wins, req.Player2Wins, req.ReplayUrl, req.ReplayUrls);
        if (!success) return BadRequest(error);
        await replayAnalysisService.AnalyzeMatchupAsync(code, matchupId, HttpContext.RequestAborted);
        await Hub.Clients.Group(code.ToUpperInvariant()).SendAsync("ScheduleUpdate", LeagueService.GetSchedule(code));
        return Ok();
    }

    private async Task BroadcastSchedule(string code)
    {
        await BroadcastLeague(code);
        await Hub.Clients.Group(code.ToUpperInvariant()).SendAsync("ScheduleUpdate", LeagueService.GetSchedule(code));
    }
}
