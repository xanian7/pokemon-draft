using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using PokemonDraft.Data;
using PokemonDraft.DTOs;
using PokemonDraft.Hubs;
using PokemonDraft.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<DraftDbContext>(opts =>
{
    opts.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"));
    opts.AddInterceptors(new SqlitePragmaInterceptor());
});
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

// Run DB initialisation in the background so the HTTP server starts immediately
// and Azure Container Apps health probes can reach port 8080.
//
// KEY: We use individual CREATE TABLE IF NOT EXISTS statements instead of
// EnsureCreated(). EnsureCreated() wraps everything in one deferred transaction
// whose COMMIT requires an EXCLUSIVE lock — which the old container holds for
// 30-60 s during the rolling deploy. Individual auto-committed DDL statements
// each wait up to 60 s (busy_timeout via SqlitePragmaInterceptor) and succeed
// as soon as they can acquire a brief write lock.
app.Lifetime.ApplicationStarted.Register(() => Task.Run(() =>
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    for (int attempt = 1; attempt <= 20; attempt++)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var conn = scope.ServiceProvider.GetRequiredService<DraftDbContext>().Database.GetDbConnection();
            conn.Open();

            // The EF Core interceptor only fires when EF Core opens the connection
            // internally — not when we call Open() on the raw DbConnection directly.
            // Set pragmas explicitly here so busy_timeout and WAL are active.
            foreach (var pragma in new[] { "PRAGMA busy_timeout=60000", "PRAGMA journal_mode=WAL" })
            {
                using var p = conn.CreateCommand();
                p.CommandText = pragma;
                p.ExecuteNonQuery();
            }

            // Each statement auto-commits individually — no large COMMIT to fail.
            // CommandTimeout=0 disables the .NET-level 30s cancellation so SQLite's
            // busy_timeout (60s) is what controls how long we wait for a write lock.
            foreach (var sql in new[]
            {
                """
                CREATE TABLE IF NOT EXISTS "Leagues" (
                    "Code" TEXT NOT NULL CONSTRAINT "PK_Leagues" PRIMARY KEY,
                    "Name" TEXT NOT NULL,
                    "AdminPin" TEXT NOT NULL,
                    "CommissionerPlayerId" TEXT NOT NULL,
                    "PointLimit" INTEGER NOT NULL,
                    "Rounds" INTEGER NOT NULL,
                    "RegulationSet" TEXT NOT NULL,
                    "CurrentPickNumber" INTEGER NOT NULL,
                    "DraftStatus" INTEGER NOT NULL,
                    "CreatedAt" TEXT NOT NULL
                )
                """,
                """
                CREATE TABLE IF NOT EXISTS "Players" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_Players" PRIMARY KEY,
                    "LeagueCode" TEXT NOT NULL,
                    "SortOrder" INTEGER NOT NULL,
                    "Name" TEXT NOT NULL,
                    "TeamName" TEXT NOT NULL DEFAULT '',
                    "TeamImageUrl" TEXT NOT NULL DEFAULT '',
                    "Pin" TEXT NOT NULL,
                    CONSTRAINT "FK_Players_Leagues_LeagueCode"
                        FOREIGN KEY ("LeagueCode") REFERENCES "Leagues" ("Code") ON DELETE CASCADE
                )
                """,
                """
                CREATE TABLE IF NOT EXISTS "Picks" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Picks" PRIMARY KEY AUTOINCREMENT,
                    "LeagueCode" TEXT NOT NULL,
                    "PickNumber" INTEGER NOT NULL,
                    "Round" INTEGER NOT NULL,
                    "PlayerId" TEXT NOT NULL,
                    "PokemonId" INTEGER NOT NULL,
                    CONSTRAINT "FK_Picks_Leagues_LeagueCode"
                        FOREIGN KEY ("LeagueCode") REFERENCES "Leagues" ("Code") ON DELETE CASCADE
                )
                """,
                """
                CREATE TABLE IF NOT EXISTS "PointValues" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_PointValues" PRIMARY KEY AUTOINCREMENT,
                    "LeagueCode" TEXT NOT NULL,
                    "PokemonId" INTEGER NOT NULL,
                    "Value" INTEGER NOT NULL,
                    CONSTRAINT "FK_PointValues_Leagues_LeagueCode"
                        FOREIGN KEY ("LeagueCode") REFERENCES "Leagues" ("Code") ON DELETE CASCADE
                )
                """,
                """
                CREATE TABLE IF NOT EXISTS "Trades" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Trades" PRIMARY KEY AUTOINCREMENT,
                    "LeagueCode" TEXT NOT NULL,
                    "InitiatorPlayerId" TEXT NOT NULL,
                    "TargetPlayerId" TEXT NOT NULL,
                    "Status" INTEGER NOT NULL,
                    "ProposedAt" TEXT NOT NULL,
                    CONSTRAINT "FK_Trades_Leagues_LeagueCode"
                        FOREIGN KEY ("LeagueCode") REFERENCES "Leagues" ("Code") ON DELETE CASCADE
                )
                """,
                """
                CREATE TABLE IF NOT EXISTS "TradeItems" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_TradeItems" PRIMARY KEY AUTOINCREMENT,
                    "TradeId" INTEGER NOT NULL,
                    "FromPlayerId" TEXT NOT NULL,
                    "PokemonId" INTEGER NOT NULL,
                    CONSTRAINT "FK_TradeItems_Trades_TradeId"
                        FOREIGN KEY ("TradeId") REFERENCES "Trades" ("Id") ON DELETE CASCADE
                )
                """,
                """
                CREATE TABLE IF NOT EXISTS "Matchups" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Matchups" PRIMARY KEY AUTOINCREMENT,
                    "LeagueCode" TEXT NOT NULL,
                    "Week" INTEGER NOT NULL,
                    "Player1Id" TEXT NOT NULL,
                    "Player2Id" TEXT NOT NULL,
                    "Player1Wins" INTEGER NULL,
                    "Player2Wins" INTEGER NULL,
                    "ReportedByPlayerId" TEXT NULL,
                    "ReportedAt" TEXT NULL,
                    CONSTRAINT "FK_Matchups_Leagues_LeagueCode"
                        FOREIGN KEY ("LeagueCode") REFERENCES "Leagues" ("Code") ON DELETE CASCADE
                )
                """,
                @"CREATE INDEX IF NOT EXISTS ""IX_Matchups_LeagueCode"" ON ""Matchups"" (""LeagueCode"")",
                @"CREATE INDEX IF NOT EXISTS ""IX_Picks_LeagueCode"" ON ""Picks"" (""LeagueCode"")",
                @"CREATE INDEX IF NOT EXISTS ""IX_Players_LeagueCode"" ON ""Players"" (""LeagueCode"")",
                @"CREATE INDEX IF NOT EXISTS ""IX_PointValues_LeagueCode"" ON ""PointValues"" (""LeagueCode"")",
                @"CREATE INDEX IF NOT EXISTS ""IX_TradeItems_TradeId"" ON ""TradeItems"" (""TradeId"")",
                @"CREATE INDEX IF NOT EXISTS ""IX_Trades_LeagueCode"" ON ""Trades"" (""LeagueCode"")",
            })
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.CommandTimeout = 0; // rely on SQLite busy_timeout (60s), not .NET's default 30s
                cmd.ExecuteNonQuery();
            }

            // Migration: add columns to existing tables that pre-date these columns.
            foreach (var colDef in new[] { "\"TeamName\" TEXT NOT NULL DEFAULT ''", "\"TeamImageUrl\" TEXT NOT NULL DEFAULT ''" })
            {
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"ALTER TABLE \"Players\" ADD COLUMN {colDef}";
                    cmd.CommandTimeout = 0;
                    cmd.ExecuteNonQuery();
                }
                catch { } // column already exists — ignore
            }

            conn.Close();
            logger.LogInformation("DB schema ready (attempt {Attempt})", attempt);
            break;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DB init attempt {Attempt}/20 failed, retrying in 5 s", attempt);
            Thread.Sleep(TimeSpan.FromSeconds(5));
        }
    }
}));

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

// Sets PRAGMA busy_timeout and journal_mode=WAL on every SQLite connection
// opened by EF Core so that brief lock contention during rolling deploys is
// handled at the SQLite level without application-level retry loops.
class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private const string Sql = "PRAGMA busy_timeout=60000; PRAGMA journal_mode=WAL;";

    public override void ConnectionOpened(System.Data.Common.DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = Sql;
        cmd.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(System.Data.Common.DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = Sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
