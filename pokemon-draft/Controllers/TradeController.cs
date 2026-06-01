using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PokemonDraft.DTOs;
using PokemonDraft.Hubs;
using PokemonDraft.Models;
using PokemonDraft.Services;

namespace PokemonDraft.Controllers;

[Route("api/leagues/{code}/trades")]
public class TradeController(ILeagueService leagueService, IHubContext<DraftHub> hub)
    : LeagueBaseController(leagueService, hub)
{
    [HttpGet]
    public IActionResult GetTrades(string code) => Ok(leagueService.GetTrades(code));

    [HttpPost]
    public async Task<IActionResult> ProposeTrade(string code, ProposeTradeRequest req)
    {
        var (trade, error) = leagueService.ProposeTrade(
            code, req.InitiatorPlayerId, req.InitiatorPin,
            req.TargetPlayerId, req.OfferingPokemonIds, req.RequestingPokemonIds);
        if (trade is null) return BadRequest(error);
        await BroadcastLeague(code);
        return Ok(trade);
    }

    [HttpPost("{tradeId}/accept")]
    public async Task<IActionResult> AcceptTrade(string code, int tradeId, RespondTradeRequest req)
    {
        var (success, error) = leagueService.RespondToTrade(code, tradeId, req.PlayerId, req.Pin, TradeStatus.Accepted);
        if (!success) return BadRequest(error);
        await BroadcastLeague(code);
        return Ok();
    }

    [HttpPost("{tradeId}/reject")]
    public async Task<IActionResult> RejectTrade(string code, int tradeId, RespondTradeRequest req)
    {
        var (success, error) = leagueService.RespondToTrade(code, tradeId, req.PlayerId, req.Pin, TradeStatus.Rejected);
        if (!success) return BadRequest(error);
        await BroadcastLeague(code);
        return Ok();
    }

    [HttpPost("{tradeId}/cancel")]
    public async Task<IActionResult> CancelTrade(string code, int tradeId, RespondTradeRequest req)
    {
        var (success, error) = leagueService.RespondToTrade(code, tradeId, req.PlayerId, req.Pin, TradeStatus.Cancelled);
        if (!success) return BadRequest(error);
        await BroadcastLeague(code);
        return Ok();
    }
}
