using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PokemonDraftApi.Data;
using PokemonDraftApi.DTOs;
using PokemonDraftApi.Hubs;
using PokemonDraftApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<DraftDbContext>(opts =>
    opts.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<LeagueService>();
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .WithOrigins("http://localhost:5173", "http://localhost:4173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<DraftDbContext>().Database.EnsureCreated();

app.UseCors();
app.MapHub<DraftHub>("/hubs/draft");

// Redirect root to the Vite dev server (or built frontend) for convenience
app.MapGet("/", () => Results.Redirect("http://localhost:5173"));

async Task BroadcastLeague(IHubContext<DraftHub> hub, LeagueService svc, string code)
{
    var league = svc.GetLeague(code);
    if (league is not null)
        await hub.Clients.Group(code.ToUpperInvariant()).SendAsync("LeagueState", svc.ToResponse(league));
}

// Auth
app.MapPost("/api/auth/join", (JoinRequest req, LeagueService svc) =>
{
    var result = svc.ValidatePin(req.LeagueCode, req.Pin);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});

// Leagues
app.MapPost("/api/leagues", (CreateLeagueRequest req, LeagueService svc) =>
{
    if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.AdminPin) || string.IsNullOrWhiteSpace(req.CommissionerName))
        return Results.BadRequest("Name, commissioner name, and admin PIN are required.");

    var league = svc.CreateLeague(req.Name.Trim(), req.CommissionerName.Trim(), req.AdminPin);
    return Results.Ok(new { league.Code, league.Name });
});

app.MapGet("/api/leagues/{code}", (string code, LeagueService svc) =>
{
    var league = svc.GetLeague(code);
    return league is null ? Results.NotFound() : Results.Ok(svc.ToResponse(league));
});

app.MapPatch("/api/leagues/{code}/config", async (
    string code, UpdateLeagueConfigRequest req,
    LeagueService svc, IHubContext<DraftHub> hub) =>
{
    if (!svc.UpdateConfig(code, req))
        return Results.NotFound();
    await BroadcastLeague(hub, svc, code);
    return Results.Ok();
});

// Self-registration via invite link (no admin auth required)
app.MapPost("/api/leagues/{code}/players/register", async (
    string code, RegisterPlayerRequest req,
    LeagueService svc, IHubContext<DraftHub> hub) =>
{
    if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Pin))
        return Results.BadRequest("Name and PIN are required.");

    var (player, error) = svc.RegisterPlayer(code, req.Name.Trim(), req.Pin);
    if (player is null) return Results.BadRequest(error);

    await BroadcastLeague(hub, svc, code);
    return Results.Ok(new { player.Id, player.Name });
});

// Players (admin-managed)
app.MapPost("/api/leagues/{code}/players", async (
    string code, AddPlayerRequest req,
    LeagueService svc, IHubContext<DraftHub> hub) =>
{
    if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Pin))
        return Results.BadRequest("Name and PIN are required.");

    var player = svc.AddPlayer(code, req.Name.Trim(), req.Pin);
    if (player is null) return Results.NotFound();

    await BroadcastLeague(hub, svc, code);
    return Results.Ok(new { player.Id, player.Name });
});

app.MapDelete("/api/leagues/{code}/players/{playerId}", async (
    string code, string playerId,
    LeagueService svc, IHubContext<DraftHub> hub) =>
{
    if (!svc.RemovePlayer(code, playerId)) return Results.NotFound();
    await BroadcastLeague(hub, svc, code);
    return Results.Ok();
});

app.MapPost("/api/leagues/{code}/players/move", async (
    string code, MovePlayerRequest req,
    LeagueService svc, IHubContext<DraftHub> hub) =>
{
    if (!svc.MovePlayer(code, req.FromIndex, req.ToIndex)) return Results.BadRequest();
    await BroadcastLeague(hub, svc, code);
    return Results.Ok();
});

// Point values
app.MapPut("/api/leagues/{code}/points", async (
    string code, SetPointValuesRequest req,
    LeagueService svc, IHubContext<DraftHub> hub) =>
{
    if (!svc.SetPointValues(code, req.Values)) return Results.NotFound();
    await BroadcastLeague(hub, svc, code);
    return Results.Ok();
});

// Draft
app.MapPost("/api/leagues/{code}/draft/start", async (
    string code, LeagueService svc, IHubContext<DraftHub> hub) =>
{
    if (!svc.StartDraft(code))
        return Results.BadRequest("Could not start draft. Need at least 2 players.");
    await BroadcastLeague(hub, svc, code);
    return Results.Ok();
});

app.MapPost("/api/leagues/{code}/draft/reset", async (
    string code, LeagueService svc, IHubContext<DraftHub> hub) =>
{
    if (!svc.ResetDraft(code)) return Results.NotFound();
    await BroadcastLeague(hub, svc, code);
    return Results.Ok();
});

app.MapPost("/api/leagues/{code}/draft/pick", async (
    string code, MakePickRequest req,
    LeagueService svc, IHubContext<DraftHub> hub) =>
{
    var (success, error) = svc.MakePick(code, req.PlayerId, req.Pin, req.PokemonId);
    if (!success) return Results.BadRequest(error);
    await BroadcastLeague(hub, svc, code);
    return Results.Ok();
});

// Roster (add / drop post-draft)
app.MapPost("/api/leagues/{code}/roster/drop", async (
    string code, RosterChangeRequest req,
    LeagueService svc, IHubContext<DraftHub> hub) =>
{
    var (success, error) = svc.DropPokemon(code, req.PlayerId, req.Pin, req.PokemonId);
    if (!success) return Results.BadRequest(error);
    await BroadcastLeague(hub, svc, code);
    return Results.Ok();
});

app.MapPost("/api/leagues/{code}/roster/add", async (
    string code, RosterChangeRequest req,
    LeagueService svc, IHubContext<DraftHub> hub) =>
{
    var (success, error) = svc.AddPokemon(code, req.PlayerId, req.Pin, req.PokemonId);
    if (!success) return Results.BadRequest(error);
    await BroadcastLeague(hub, svc, code);
    return Results.Ok();
});

// Trades
app.MapGet("/api/leagues/{code}/trades", (string code, LeagueService svc) =>
    Results.Ok(svc.GetTrades(code)));

app.MapPost("/api/leagues/{code}/trades", async (
    string code, ProposeTradeRequest req,
    LeagueService svc, IHubContext<DraftHub> hub) =>
{
    var (trade, error) = svc.ProposeTrade(code, req.InitiatorPlayerId, req.InitiatorPin, req.TargetPlayerId, req.OfferingPokemonIds, req.RequestingPokemonIds);
    if (trade is null) return Results.BadRequest(error);
    await BroadcastLeague(hub, svc, code);
    return Results.Ok(trade);
});

app.MapPost("/api/leagues/{code}/trades/{tradeId}/accept", async (
    string code, int tradeId, RespondTradeRequest req,
    LeagueService svc, IHubContext<DraftHub> hub) =>
{
    var (success, error) = svc.RespondToTrade(code, tradeId, req.PlayerId, req.Pin, PokemonDraftApi.Models.TradeStatus.Accepted);
    if (!success) return Results.BadRequest(error);
    await BroadcastLeague(hub, svc, code);
    return Results.Ok();
});

app.MapPost("/api/leagues/{code}/trades/{tradeId}/reject", async (
    string code, int tradeId, RespondTradeRequest req,
    LeagueService svc, IHubContext<DraftHub> hub) =>
{
    var (success, error) = svc.RespondToTrade(code, tradeId, req.PlayerId, req.Pin, PokemonDraftApi.Models.TradeStatus.Rejected);
    if (!success) return Results.BadRequest(error);
    await BroadcastLeague(hub, svc, code);
    return Results.Ok();
});

app.MapPost("/api/leagues/{code}/trades/{tradeId}/cancel", async (
    string code, int tradeId, RespondTradeRequest req,
    LeagueService svc, IHubContext<DraftHub> hub) =>
{
    var (success, error) = svc.RespondToTrade(code, tradeId, req.PlayerId, req.Pin, PokemonDraftApi.Models.TradeStatus.Cancelled);
    if (!success) return Results.BadRequest(error);
    await BroadcastLeague(hub, svc, code);
    return Results.Ok();
});

app.Run();
