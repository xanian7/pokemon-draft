using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using PokemonDraft.Data;
using PokemonDraft.DTOs;
using PokemonDraft.Hubs;
using PokemonDraft.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<DraftDbContext>(opts =>
    opts.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<ILeagueService, LeagueService>();
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = new List<string> { "http://localhost:5173", "http://localhost:4173" };
        var extra = builder.Configuration["CorsOrigins"];
        if (!string.IsNullOrWhiteSpace(extra))
            origins.AddRange(extra.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        policy
            .WithOrigins([.. origins])
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

// Signal set when DB init completes. API middleware awaits this so requests
// don't hit uninitialized tables during the brief window after startup.
var dbReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

// Run DB initialisation AFTER the HTTP server starts so Azure Container Apps
// health probes can reach port 8080 immediately. Previously this blocked
// startup for up to 60 s, causing the new revision to fail its health check
// and the old revision to keep serving traffic.
app.Lifetime.ApplicationStarted.Register(() => Task.Run(() =>
{
    try
    {
        // Retry EnsureCreated — during rolling deploys both containers briefly
        // share /data/draft.db, causing SQLite error 5 "database is locked".
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var initScope = app.Services.CreateScope();
                initScope.ServiceProvider.GetRequiredService<DraftDbContext>().Database.EnsureCreated();
                break;
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 5 && attempt < 12)
            {
                Thread.Sleep(TimeSpan.FromSeconds(5));
            }
        }

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DraftDbContext>();
        var conn = db.Database.GetDbConnection();
        conn.Open();

        // Enable WAL mode for better concurrency and set busy timeout as a safety net.
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=10000;";
            pragma.ExecuteNonQuery();
        }
        foreach (var colDef in new[] { "TeamName TEXT NOT NULL DEFAULT ''", "TeamImageUrl TEXT NOT NULL DEFAULT ''" })
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"ALTER TABLE Players ADD COLUMN {colDef}";
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        try
        {
            using var cmd2 = conn.CreateCommand();
            cmd2.CommandText = """
                CREATE TABLE IF NOT EXISTS Matchups (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    LeagueCode TEXT NOT NULL,
                    Week INTEGER NOT NULL,
                    Player1Id TEXT NOT NULL,
                    Player2Id TEXT NOT NULL,
                    Player1Wins INTEGER,
                    Player2Wins INTEGER,
                    ReportedByPlayerId TEXT,
                    ReportedAt TEXT,
                    FOREIGN KEY (LeagueCode) REFERENCES Leagues(Code) ON DELETE CASCADE
                )
                """;
            cmd2.ExecuteNonQuery();
        }
        catch { }

        conn.Close();
    }
    finally
    {
        dbReady.TrySetResult();
    }
}));

app.UseCors();

// Hold API requests until DB is initialised (typically < 1 s after first request;
// up to ~60 s on first deploy while the old container releases the SQLite lock).
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && !dbReady.Task.IsCompleted)
        await dbReady.Task.WaitAsync(TimeSpan.FromSeconds(70));
    await next(context);
});

if (app.Environment.IsDevelopment())
{
    var clientAppDir = Path.Combine(app.Environment.ContentRootPath, "ClientApp");
    var vite = new System.Diagnostics.Process
    {
        StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c npm run dev",
            WorkingDirectory = clientAppDir,
            UseShellExecute = true,
        }
    };
    vite.Start();
    app.Lifetime.ApplicationStopping.Register(() => { try { vite.Kill(true); } catch { } });
}
else
{
    var spaRoot = Path.Combine(app.Environment.ContentRootPath, "ClientApp", "dist");
    if (Directory.Exists(spaRoot))
    {
        var fileProvider = new PhysicalFileProvider(spaRoot);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
    }
}

app.MapHub<DraftHub>("/hubs/draft");

if (app.Environment.IsDevelopment())
{
    app.MapFallback(() => Results.Content("""
        <!DOCTYPE html>
        <html>
        <head>
          <title>Starting...</title>
          <style>
            body { margin: 0; display: flex; align-items: center; justify-content: center;
                   height: 100vh; background: #1a1a2e; font-family: sans-serif; color: #fff; }
            p { color: #aaa; }
          </style>
        </head>
        <body>
          <div style="text-align:center">
            <h2>Starting dev server...</h2>
            <p>You'll be redirected automatically.</p>
          </div>
          <script>
            (async function poll() {
              try {
                await fetch('http://localhost:5173/', { mode: 'no-cors' });
                location.href = 'http://localhost:5173' + location.pathname + location.search;
              } catch {
                setTimeout(poll, 500);
              }
            })();
          </script>
        </body>
        </html>
        """, "text/html"));
}

async Task BroadcastLeague(IHubContext<DraftHub> hub, ILeagueService svc, string code)
{
    var league = svc.GetLeague(code);
    if (league is not null)
        await hub.Clients.Group(code.ToUpperInvariant()).SendAsync("LeagueState", svc.ToResponse(league));
}

// Auth
app.MapPost("/api/auth/join", (JoinRequest req, ILeagueService svc) =>
{
    var result = svc.ValidatePin(req.LeagueCode, req.Pin);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
});

// Leagues
app.MapPost("/api/leagues", (CreateLeagueRequest req, ILeagueService svc) =>
{
    if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.AdminPin) || string.IsNullOrWhiteSpace(req.CommissionerName))
        return Results.BadRequest("Name, commissioner name, and admin PIN are required.");

    var league = svc.CreateLeague(req.Name.Trim(), req.CommissionerName.Trim(), req.AdminPin);
    return Results.Ok(new { league.Code, league.Name });
});

app.MapGet("/api/leagues/{code}", (string code, ILeagueService svc) =>
{
    var league = svc.GetLeague(code);
    return league is null ? Results.NotFound() : Results.Ok(svc.ToResponse(league));
});

app.MapPatch("/api/leagues/{code}/config", async (
    string code, UpdateLeagueConfigRequest req,
    ILeagueService svc, IHubContext<DraftHub> hub) =>
{
    if (!svc.UpdateConfig(code, req))
        return Results.NotFound();
    await BroadcastLeague(hub, svc, code);
    return Results.Ok();
});

// Self-registration via invite link (no admin auth required)
app.MapPost("/api/leagues/{code}/players/register", async (
    string code, RegisterPlayerRequest req,
    ILeagueService svc, IHubContext<DraftHub> hub) =>
{
    if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Pin))
        return Results.BadRequest("Name and PIN are required.");

    var (player, error) = svc.RegisterPlayer(code, req.Name.Trim(), req.Pin, req.TeamName, req.TeamImageUrl);
    if (player is null) return Results.BadRequest(error);

    await BroadcastLeague(hub, svc, code);
    return Results.Ok(new { player.Id, player.Name });
});

// Players (admin-managed)
app.MapPost("/api/leagues/{code}/players", async (
    string code, AddPlayerRequest req,
    ILeagueService svc, IHubContext<DraftHub> hub) =>
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
    ILeagueService svc, IHubContext<DraftHub> hub) =>
{
    if (!svc.RemovePlayer(code, playerId)) return Results.NotFound();
    await BroadcastLeague(hub, svc, code);
    return Results.Ok();
});

app.MapPost("/api/leagues/{code}/players/move", async (
    string code, MovePlayerRequest req,
    ILeagueService svc, IHubContext<DraftHub> hub) =>
{
    if (!svc.MovePlayer(code, req.FromIndex, req.ToIndex)) return Results.BadRequest();
    await BroadcastLeague(hub, svc, code);
    return Results.Ok();
});

// Point values
app.MapPut("/api/leagues/{code}/points", async (
    string code, SetPointValuesRequest req,
    ILeagueService svc, IHubContext<DraftHub> hub) =>
{
    if (!svc.SetPointValues(code, req.Values)) return Results.NotFound();
    await BroadcastLeague(hub, svc, code);
    return Results.Ok();
});

// Draft
app.MapPost("/api/leagues/{code}/draft/start", async (
    string code, ILeagueService svc, IHubContext<DraftHub> hub) =>
{
    if (!svc.StartDraft(code))
        return Results.BadRequest("Could not start draft. Need at least 2 players.");
    await BroadcastLeague(hub, svc, code);
    return Results.Ok();
});

app.MapPost("/api/leagues/{code}/draft/reset", async (
    string code, ILeagueService svc, IHubContext<DraftHub> hub) =>
{
    if (!svc.ResetDraft(code)) return Results.NotFound();
    await BroadcastLeague(hub, svc, code);
    return Results.Ok();
});

app.MapPost("/api/leagues/{code}/draft/pick", async (
    string code, MakePickRequest req,
    ILeagueService svc, IHubContext<DraftHub> hub) =>
{
    var (success, error, draftCompleted) = svc.MakePick(code, req.PlayerId, req.Pin, req.PokemonId);
    if (!success) return Results.BadRequest(error);
    if (draftCompleted) svc.GenerateSchedule(code);
    await BroadcastLeague(hub, svc, code);
    return Results.Ok();
});

// Roster (add / drop post-draft)
app.MapPost("/api/leagues/{code}/roster/drop", async (
    string code, RosterChangeRequest req,
    ILeagueService svc, IHubContext<DraftHub> hub) =>
{
    var (success, error) = svc.DropPokemon(code, req.PlayerId, req.Pin, req.PokemonId);
    if (!success) return Results.BadRequest(error);
    await BroadcastLeague(hub, svc, code);
    return Results.Ok();
});

app.MapPost("/api/leagues/{code}/roster/add", async (
    string code, RosterChangeRequest req,
    ILeagueService svc, IHubContext<DraftHub> hub) =>
{
    var (success, error) = svc.AddPokemon(code, req.PlayerId, req.Pin, req.PokemonId);
    if (!success) return Results.BadRequest(error);
    await BroadcastLeague(hub, svc, code);
    return Results.Ok();
});

// Trades
app.MapGet("/api/leagues/{code}/trades", (string code, ILeagueService svc) =>
    Results.Ok(svc.GetTrades(code)));

app.MapPost("/api/leagues/{code}/trades", async (
    string code, ProposeTradeRequest req,
    ILeagueService svc, IHubContext<DraftHub> hub) =>
{
    var (trade, error) = svc.ProposeTrade(code, req.InitiatorPlayerId, req.InitiatorPin, req.TargetPlayerId, req.OfferingPokemonIds, req.RequestingPokemonIds);
    if (trade is null) return Results.BadRequest(error);
    await BroadcastLeague(hub, svc, code);
    return Results.Ok(trade);
});

app.MapPost("/api/leagues/{code}/trades/{tradeId}/accept", async (
    string code, int tradeId, RespondTradeRequest req,
    ILeagueService svc, IHubContext<DraftHub> hub) =>
{
    var (success, error) = svc.RespondToTrade(code, tradeId, req.PlayerId, req.Pin, PokemonDraft.Models.TradeStatus.Accepted);
    if (!success) return Results.BadRequest(error);
    await BroadcastLeague(hub, svc, code);
    return Results.Ok();
});

app.MapPost("/api/leagues/{code}/trades/{tradeId}/reject", async (
    string code, int tradeId, RespondTradeRequest req,
    ILeagueService svc, IHubContext<DraftHub> hub) =>
{
    var (success, error) = svc.RespondToTrade(code, tradeId, req.PlayerId, req.Pin, PokemonDraft.Models.TradeStatus.Rejected);
    if (!success) return Results.BadRequest(error);
    await BroadcastLeague(hub, svc, code);
    return Results.Ok();
});

app.MapPost("/api/leagues/{code}/trades/{tradeId}/cancel", async (
    string code, int tradeId, RespondTradeRequest req,
    ILeagueService svc, IHubContext<DraftHub> hub) =>
{
    var (success, error) = svc.RespondToTrade(code, tradeId, req.PlayerId, req.Pin, PokemonDraft.Models.TradeStatus.Cancelled);
    if (!success) return Results.BadRequest(error);
    await BroadcastLeague(hub, svc, code);
    return Results.Ok();
});

// Player profile
app.MapPatch("/api/leagues/{code}/players/{playerId}/profile", async (
    string code, string playerId, UpdatePlayerProfileRequest req,
    ILeagueService svc, IHubContext<DraftHub> hub) =>
{
    var (success, error) = svc.UpdatePlayerProfile(code, playerId, req.Pin, req.TeamName, req.TeamImageUrl);
    if (!success) return Results.BadRequest(error);
    await BroadcastLeague(hub, svc, code);
    return Results.Ok();
});

// Schedule
app.MapGet("/api/leagues/{code}/schedule", (string code, ILeagueService svc) =>
{
    var schedule = svc.GetSchedule(code);
    return schedule is null ? Results.NotFound() : Results.Ok(schedule);
});

app.MapPost("/api/leagues/{code}/schedule/{matchupId}/report", async (
    string code, int matchupId, ReportMatchupRequest req,
    ILeagueService svc, IHubContext<DraftHub> hub) =>
{
    var (success, error) = svc.ReportMatchup(code, matchupId, req.PlayerId, req.Pin, req.Player1Wins, req.Player2Wins);
    if (!success) return Results.BadRequest(error);
    await hub.Clients.Group(code.ToUpperInvariant()).SendAsync("ScheduleUpdate", svc.GetSchedule(code));
    return Results.Ok();
});

app.MapPatch("/api/leagues/{code}/schedule/{matchupId}/edit", async (
    string code, int matchupId, EditMatchupRequest req,
    ILeagueService svc, IHubContext<DraftHub> hub) =>
{
    var (success, error) = svc.EditMatchup(code, matchupId, req.AdminPin, req.Player1Wins, req.Player2Wins);
    if (!success) return Results.BadRequest(error);
    await hub.Clients.Group(code.ToUpperInvariant()).SendAsync("ScheduleUpdate", svc.GetSchedule(code));
    return Results.Ok();
});

if (!app.Environment.IsDevelopment())
{
    var spaRoot = Path.Combine(app.Environment.ContentRootPath, "ClientApp", "dist");
    if (Directory.Exists(spaRoot))
        app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(spaRoot) });
}

app.Run();

