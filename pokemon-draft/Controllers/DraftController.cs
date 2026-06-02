using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PokemonDraft.DTOs;
using PokemonDraft.Hubs;
using PokemonDraft.Services;

namespace PokemonDraft.Controllers;

[Route("api/leagues/{code}/draft")]
public class DraftController(ILeagueService leagueService, IHubContext<DraftHub> hub, IDiscordService discord)
    : LeagueBaseController(leagueService, hub)
{
    [HttpPost("start")]
    public async Task<IActionResult> StartDraft(string code)
    {
        var (success, error) = LeagueService.StartDraft(code);
        if (!success) return error is null ? NotFound() : BadRequest(error);
        await BroadcastLeague(code);

        var state = LeagueService.GetLeagueResponse(code);
        if (state?.Draft.CurrentPickerId is not null)
        {
            var picker = state.Players.FirstOrDefault(p => p.Id == state.Draft.CurrentPickerId);
            var mention = picker?.DiscordId is not null ? $"<@{picker.DiscordId}>" : picker?.Name ?? "First player";
            await discord.SendMessageAsync($"The draft has started! {mention} is on the clock.");
        }

        return Ok();
    }

    [HttpPost("reset")]
    public async Task<IActionResult> ResetDraft(string code)
    {
        var (success, error) = LeagueService.ResetDraft(code);
        if (!success) return error is null ? NotFound() : BadRequest(error);
        await BroadcastLeague(code);
        return Ok();
    }

    [HttpPost("pick")]
    public async Task<IActionResult> MakePick(string code, MakePickRequest req)
    {
        var (success, error, draftCompleted) = LeagueService.MakePick(code, req.PlayerId, req.Pin, req.PokemonId);
        if (!success) return BadRequest(error);
        if (draftCompleted) LeagueService.GenerateSchedule(code);

        var state = LeagueService.GetLeagueResponse(code);
        if (state?.Draft.CurrentPickerId is not null)
        {
            var picker = state.Players.FirstOrDefault(p => p.Id == state.Draft.CurrentPickerId);
            var mention = picker?.DiscordId is not null ? $"<@{picker.DiscordId}>" : picker?.Name ?? "Next player";
            await discord.SendMessageAsync($"{mention} is on the clock!");
        }

        await BroadcastLeague(code);
        return Ok();
    }
}
