using Microsoft.AspNetCore.SignalR;
using PokemonDraftApi.Services;

namespace PokemonDraftApi.Hubs;

public class DraftHub(LeagueService leagueService) : Hub
{
    /// <summary>Called by clients to subscribe to a league's real-time updates.</summary>
    public async Task JoinLeagueGroup(string leagueCode)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, leagueCode.ToUpperInvariant());
        var league = leagueService.GetLeague(leagueCode);
        if (league is not null)
            await Clients.Caller.SendAsync("LeagueState", leagueService.ToResponse(league));
    }

    public async Task LeaveLeagueGroup(string leagueCode)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, leagueCode.ToUpperInvariant());
    }
}
