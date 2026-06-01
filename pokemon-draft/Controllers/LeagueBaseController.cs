using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PokemonDraft.Hubs;
using PokemonDraft.Services;

namespace PokemonDraft.Controllers;

/// <summary>Shared helpers for controllers that interact with league state.</summary>
[ApiController]
public abstract class LeagueBaseController : ControllerBase
{
    private readonly ILeagueService _leagueService;
    private readonly IHubContext<DraftHub> _hub;

    protected LeagueBaseController(ILeagueService leagueService, IHubContext<DraftHub> hub)
    {
        _leagueService = leagueService;
        _hub = hub;
    }

    protected ILeagueService LeagueService => _leagueService;
    protected IHubContext<DraftHub> Hub => _hub;

    /// <summary>Returns the authenticated Google user's ID, or null for PIN-only requests.</summary>
    protected Guid? GetRequestUserId()
    {
        var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return sub is not null && Guid.TryParse(sub, out var id) ? id : null;
    }

    /// <summary>Pushes the current league state to all connected clients in the league's SignalR group.</summary>
    protected async Task BroadcastLeague(string code)
    {
        var state = _leagueService.GetLeagueResponse(code);
        if (state is not null)
            await _hub.Clients.Group(code.ToUpperInvariant()).SendAsync("LeagueState", state);
    }
}
