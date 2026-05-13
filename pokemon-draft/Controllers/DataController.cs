using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PokemonDraft.DTOs;
using PokemonDraft.Hubs;
using PokemonDraft.Models;
using PokemonDraft.Services;

namespace PokemonDraft.Controllers;

[ApiController]
[Route("api")]
public class DataController(ILeagueService leagueService, IHubContext<DraftHub> hub) : ControllerBase
{
    private async Task BroadcastLeague(string code)
    {
        var response = leagueService.GetLeagueResponse(code);
        if (response is not null)
            await hub.Clients.Group(code.ToUpperInvariant()).SendAsync("LeagueState", response);
    }

    // Auth

    [HttpPost("auth/join")]
    public IActionResult Join(JoinRequest req)
    {
        var result = leagueService.ValidatePin(req.LeagueCode, req.Pin);
        return result is null ? Unauthorized() : Ok(result);
    }

    // Leagues

    [HttpPost("leagues")]
    public IActionResult CreateLeague(CreateLeagueRequest req)
    {
        var (result, error) = leagueService.CreateLeague(req);
        return result is null ? BadRequest(error) : Ok(result);
    }

    [HttpGet("leagues/{code}")]
    public IActionResult GetLeague(string code)
    {
        var response = leagueService.GetLeagueResponse(code);
        return response is null ? NotFound() : Ok(response);
    }

    [HttpPatch("leagues/{code}/config")]
    public async Task<IActionResult> UpdateConfig(string code, UpdateLeagueConfigRequest req)
    {
        var (success, error) = leagueService.UpdateConfig(code, req);
        if (!success) return error is null ? NotFound() : BadRequest(error);
        await BroadcastLeague(code);
        return Ok();
    }

    // Players

    [HttpPost("leagues/{code}/players/register")]
    public async Task<IActionResult> RegisterPlayer(string code, RegisterPlayerRequest req)
    {
        var (result, error) = leagueService.RegisterPlayer(code, req);
        if (result is null) return BadRequest(error);
        await BroadcastLeague(code);
        return Ok(result);
    }

    [HttpPost("leagues/{code}/players")]
    public async Task<IActionResult> AddPlayer(string code, AddPlayerRequest req)
    {
        var (result, error) = leagueService.AddPlayer(code, req);
        if (result is null) return error is null ? NotFound() : BadRequest(error);
        await BroadcastLeague(code);
        return Ok(result);
    }

    [HttpDelete("leagues/{code}/players/{playerId}")]
    public async Task<IActionResult> RemovePlayer(string code, string playerId)
    {
        var (success, error) = leagueService.RemovePlayer(code, playerId);
        if (!success) return error is null ? NotFound() : BadRequest(error);
        await BroadcastLeague(code);
        return Ok();
    }

    [HttpPost("leagues/{code}/players/move")]
    public async Task<IActionResult> MovePlayer(string code, MovePlayerRequest req)
    {
        var (success, error) = leagueService.MovePlayer(code, req.FromIndex, req.ToIndex);
        if (!success) return error is null ? NotFound() : BadRequest(error);
        await BroadcastLeague(code);
        return Ok();
    }

    [HttpPatch("leagues/{code}/players/{playerId}/profile")]
    public async Task<IActionResult> UpdatePlayerProfile(string code, string playerId, UpdatePlayerProfileRequest req)
    {
        var (success, error) = leagueService.UpdatePlayerProfile(code, playerId, req.Pin, req.TeamName, req.TeamImageUrl);
        if (!success) return BadRequest(error);
        await BroadcastLeague(code);
        return Ok();
    }

    // Point values

    [HttpPut("leagues/{code}/points")]
    public async Task<IActionResult> SetPointValues(string code, SetPointValuesRequest req)
    {
        var (success, error) = leagueService.SetPointValues(code, req.Values);
        if (!success) return error is null ? NotFound() : BadRequest(error);
        await BroadcastLeague(code);
        return Ok();
    }

    // Draft

    [HttpPost("leagues/{code}/draft/start")]
    public async Task<IActionResult> StartDraft(string code)
    {
        var (success, error) = leagueService.StartDraft(code);
        if (!success) return error is null ? NotFound() : BadRequest(error);
        await BroadcastLeague(code);
        return Ok();
    }

    [HttpPost("leagues/{code}/draft/reset")]
    public async Task<IActionResult> ResetDraft(string code)
    {
        var (success, error) = leagueService.ResetDraft(code);
        if (!success) return error is null ? NotFound() : BadRequest(error);
        await BroadcastLeague(code);
        return Ok();
    }

    [HttpPost("leagues/{code}/draft/pick")]
    public async Task<IActionResult> MakePick(string code, MakePickRequest req)
    {
        var (success, error, draftCompleted) = leagueService.MakePick(code, req.PlayerId, req.Pin, req.PokemonId);
        if (!success) return BadRequest(error);
        if (draftCompleted) leagueService.GenerateSchedule(code);
        await BroadcastLeague(code);
        return Ok();
    }

    // Roster

    [HttpPost("leagues/{code}/roster/drop")]
    public async Task<IActionResult> DropPokemon(string code, RosterChangeRequest req)
    {
        var (success, error) = leagueService.DropPokemon(code, req.PlayerId, req.Pin, req.PokemonId);
        if (!success) return BadRequest(error);
        await BroadcastLeague(code);
        return Ok();
    }

    [HttpPost("leagues/{code}/roster/add")]
    public async Task<IActionResult> AddPokemon(string code, RosterChangeRequest req)
    {
        var (success, error) = leagueService.AddPokemon(code, req.PlayerId, req.Pin, req.PokemonId);
        if (!success) return BadRequest(error);
        await BroadcastLeague(code);
        return Ok();
    }

    // Trades

    [HttpGet("leagues/{code}/trades")]
    public IActionResult GetTrades(string code) => Ok(leagueService.GetTrades(code));

    [HttpPost("leagues/{code}/trades")]
    public async Task<IActionResult> ProposeTrade(string code, ProposeTradeRequest req)
    {
        var (trade, error) = leagueService.ProposeTrade(code, req.InitiatorPlayerId, req.InitiatorPin, req.TargetPlayerId, req.OfferingPokemonIds, req.RequestingPokemonIds);
        if (trade is null) return BadRequest(error);
        await BroadcastLeague(code);
        return Ok(trade);
    }

    [HttpPost("leagues/{code}/trades/{tradeId}/accept")]
    public async Task<IActionResult> AcceptTrade(string code, int tradeId, RespondTradeRequest req)
    {
        var (success, error) = leagueService.RespondToTrade(code, tradeId, req.PlayerId, req.Pin, TradeStatus.Accepted);
        if (!success) return BadRequest(error);
        await BroadcastLeague(code);
        return Ok();
    }

    [HttpPost("leagues/{code}/trades/{tradeId}/reject")]
    public async Task<IActionResult> RejectTrade(string code, int tradeId, RespondTradeRequest req)
    {
        var (success, error) = leagueService.RespondToTrade(code, tradeId, req.PlayerId, req.Pin, TradeStatus.Rejected);
        if (!success) return BadRequest(error);
        await BroadcastLeague(code);
        return Ok();
    }

    [HttpPost("leagues/{code}/trades/{tradeId}/cancel")]
    public async Task<IActionResult> CancelTrade(string code, int tradeId, RespondTradeRequest req)
    {
        var (success, error) = leagueService.RespondToTrade(code, tradeId, req.PlayerId, req.Pin, TradeStatus.Cancelled);
        if (!success) return BadRequest(error);
        await BroadcastLeague(code);
        return Ok();
    }

    // Schedule

    [HttpGet("leagues/{code}/schedule")]
    public IActionResult GetSchedule(string code)
    {
        var schedule = leagueService.GetSchedule(code);
        return schedule is null ? NotFound() : Ok(schedule);
    }

    [HttpPost("leagues/{code}/schedule/{matchupId}/report")]
    public async Task<IActionResult> ReportMatchup(string code, int matchupId, ReportMatchupRequest req)
    {
        var (success, error) = leagueService.ReportMatchup(code, matchupId, req.PlayerId, req.Pin, req.Player1Wins, req.Player2Wins);
        if (!success) return BadRequest(error);
        await hub.Clients.Group(code.ToUpperInvariant()).SendAsync("ScheduleUpdate", leagueService.GetSchedule(code));
        return Ok();
    }

    [HttpPatch("leagues/{code}/schedule/{matchupId}/edit")]
    public async Task<IActionResult> EditMatchup(string code, int matchupId, EditMatchupRequest req)
    {
        var (success, error) = leagueService.EditMatchup(code, matchupId, req.AdminPin, req.Player1Wins, req.Player2Wins);
        if (!success) return BadRequest(error);
        await hub.Clients.Group(code.ToUpperInvariant()).SendAsync("ScheduleUpdate", leagueService.GetSchedule(code));
        return Ok();
    }
}
