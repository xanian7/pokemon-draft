using BC = BCrypt.Net.BCrypt;
using Microsoft.EntityFrameworkCore;
using PokemonDraft.Data;
using PokemonDraft.DTOs;
using PokemonDraft.Models;

namespace PokemonDraft.Services;

/// <inheritdoc cref="ILeagueService"/>
public class LeagueService(DraftDbContext db) : ILeagueService
{
    /// <inheritdoc/>
    public League CreateLeague(string name, string commissionerName, string adminPin)
    {
        var code = GenerateCode();
        var commissioner = new Player
        {
            Id = Guid.NewGuid().ToString(),
            LeagueCode = code,
            Name = commissionerName.Trim(),
            Pin = BC.HashPassword(adminPin),
            SortOrder = 0,
        };

        var league = new League
        {
            Code = code,
            Name = name,
            AdminPin = BC.HashPassword(adminPin),
            CommissionerPlayerId = commissioner.Id,
            DraftStatus = DraftStatus.Setup,
            CurrentPickNumber = 0,
            Players = [commissioner],
        };

        db.Leagues.Add(league);
        db.SaveChanges();
        return LoadLeague(code)!;
    }

    /// <inheritdoc/>
    public League? GetLeague(string code) => LoadLeague(code);

    /// <inheritdoc/>
    public bool UpdateConfig(string code, UpdateLeagueConfigRequest req)
    {
        var league = LoadLeague(code);
        if (league is null) return false;
        if (req.Name is not null) league.Name = req.Name;
        if (req.PointLimit is not null) league.PointLimit = req.PointLimit.Value;
        if (req.Rounds is not null) league.Rounds = req.Rounds.Value;
        if (req.RegulationSet is not null) league.RegulationSet = req.RegulationSet;
        db.SaveChanges();
        return true;
    }

    /// <inheritdoc/>
    public Player? AddPlayer(string leagueCode, string name, string pin)
    {
        var league = LoadLeague(leagueCode);
        if (league is null) return null;

        var player = new Player
        {
            Id = Guid.NewGuid().ToString(),
            LeagueCode = league.Code,
            Name = name,
            Pin = BC.HashPassword(pin),
            SortOrder = GetOrderedPlayers(league).Count,
        };

        league.Players.Add(player);
        db.SaveChanges();
        return player;
    }

    /// <inheritdoc/>
    public (Player? player, string? error) RegisterPlayer(string leagueCode, string name, string pin)
    {
        var league = LoadLeague(leagueCode);
        if (league is null)
            return (null, "League not found.");

        var trimmedName = name.Trim();
        var orderedPlayers = GetOrderedPlayers(league);

        if (orderedPlayers.Any(p => p.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase)))
            return (null, "That name is already taken in this league.");

        if (orderedPlayers.Any(p => BC.Verify(pin, p.Pin)))
            return (null, "That PIN is already in use. Please choose a different one.");

        var player = new Player
        {
            Id = Guid.NewGuid().ToString(),
            LeagueCode = league.Code,
            Name = trimmedName,
            Pin = BC.HashPassword(pin),
            SortOrder = orderedPlayers.Count,
        };

        league.Players.Add(player);
        db.SaveChanges();
        return (player, null);
    }

    /// <inheritdoc/>
    public bool RemovePlayer(string leagueCode, string playerId)
    {
        var league = LoadLeague(leagueCode);
        if (league is null) return false;

        var player = league.Players.FirstOrDefault(p => p.Id == playerId);
        if (player is null) return false;

        db.Players.Remove(player);
        ReindexPlayers(league.Players.Where(p => p.Id != playerId).OrderBy(p => p.SortOrder).ToList());
        db.SaveChanges();
        return true;
    }

    /// <inheritdoc/>
    public bool MovePlayer(string leagueCode, int fromIndex, int toIndex)
    {
        var league = LoadLeague(leagueCode);
        if (league is null) return false;

        var players = GetOrderedPlayers(league);
        if (fromIndex < 0 || fromIndex >= players.Count) return false;
        if (toIndex < 0 || toIndex >= players.Count) return false;

        var player = players[fromIndex];
        players.RemoveAt(fromIndex);
        players.Insert(toIndex, player);
        ReindexPlayers(players);
        db.SaveChanges();
        return true;
    }

    /// <inheritdoc/>
    public bool SetPointValues(string leagueCode, Dictionary<int, int> values)
    {
        var league = LoadLeague(leagueCode);
        if (league is null) return false;

        var existing = league.PointValues.ToDictionary(pv => pv.PokemonId);
        foreach (var (pokemonId, pts) in values)
        {
            if (pts <= 0)
            {
                if (existing.TryGetValue(pokemonId, out var pointValue))
                    db.PointValues.Remove(pointValue);
                continue;
            }

            if (existing.TryGetValue(pokemonId, out var current))
            {
                current.Value = pts;
            }
            else
            {
                league.PointValues.Add(new PokemonPointValue
                {
                    LeagueCode = league.Code,
                    PokemonId = pokemonId,
                    Value = pts,
                });
            }
        }

        db.SaveChanges();
        return true;
    }

    /// <inheritdoc/>
    public JoinResponse? ValidatePin(string leagueCode, string pin)
    {
        var league = LoadLeague(leagueCode);
        if (league is null) return null;

        if (BC.Verify(pin, league.AdminPin))
        {
            var commissioner = GetOrderedPlayers(league).FirstOrDefault(p => p.Id == league.CommissionerPlayerId);
            var name = commissioner?.Name ?? "Commissioner";
            var playerId = commissioner?.Id ?? "admin";
            return new JoinResponse(playerId, name, true, league.Code);
        }

        var player = GetOrderedPlayers(league)
            .FirstOrDefault(p => p.Id != league.CommissionerPlayerId && BC.Verify(pin, p.Pin));
        if (player is null) return null;

        return new JoinResponse(player.Id, player.Name, false, league.Code);
    }

    /// <inheritdoc/>
    public bool StartDraft(string leagueCode)
    {
        var league = LoadLeague(leagueCode);
        if (league is null) return false;
        if (league.Players.Count < 2) return false;

        db.Picks.RemoveRange(league.Picks);
        league.Picks.Clear();
        league.DraftStatus = DraftStatus.Active;
        league.CurrentPickNumber = 0;
        db.SaveChanges();
        return true;
    }

    /// <inheritdoc/>
    public bool ResetDraft(string leagueCode)
    {
        var league = LoadLeague(leagueCode);
        if (league is null) return false;

        db.Picks.RemoveRange(league.Picks);
        league.Picks.Clear();
        league.DraftStatus = DraftStatus.Setup;
        league.CurrentPickNumber = 0;
        db.SaveChanges();
        return true;
    }

    /// <inheritdoc/>
    public (bool success, string? error) MakePick(string leagueCode, string playerId, string pin, int pokemonId)
    {
        var league = LoadLeague(leagueCode);
        if (league is null)
            return (false, "League not found.");

        if (league.DraftStatus != DraftStatus.Active)
            return (false, "Draft is not active.");

        var orderedPlayers = GetOrderedPlayers(league);
        var player = orderedPlayers.FirstOrDefault(p => p.Id == playerId);
        if (player is null || !BC.Verify(pin, player.Pin))
            return (false, "Invalid player or PIN.");

        var expectedPickerId = GetPlayerIdAtPick(league, league.CurrentPickNumber);
        if (expectedPickerId != playerId)
            return (false, "It is not your turn.");

        if (league.Picks.Any(p => p.PokemonId == pokemonId))
            return (false, "That Pokémon has already been drafted.");

        var totalPicks = orderedPlayers.Count * league.Rounds;
        var round = league.CurrentPickNumber / orderedPlayers.Count;

        league.Picks.Add(new DraftPick
        {
            LeagueCode = league.Code,
            PickNumber = league.CurrentPickNumber,
            Round = round,
            PlayerId = playerId,
            PokemonId = pokemonId,
        });

        league.CurrentPickNumber++;
        if (league.CurrentPickNumber >= totalPicks)
            league.DraftStatus = DraftStatus.Complete;

        db.SaveChanges();
        return (true, null);
    }

    /// <inheritdoc/>
    public LeagueResponse ToResponse(League league)
    {
        var players = GetOrderedPlayers(league);
        var picks = league.Picks.OrderBy(p => p.PickNumber).ToList();
        var totalPicks = players.Count * league.Rounds;
        string? currentPickerId = null;
        string? currentPickerName = null;

        if (league.DraftStatus == DraftStatus.Active && league.CurrentPickNumber < totalPicks)
        {
            currentPickerId = GetPlayerIdAtPick(league, league.CurrentPickNumber);
            currentPickerName = players.FirstOrDefault(p => p.Id == currentPickerId)?.Name;
        }

        return new LeagueResponse(
            Code: league.Code,
            Name: league.Name,
            PointLimit: league.PointLimit,
            Rounds: league.Rounds,
            RegulationSet: league.RegulationSet,
            Players: players.Select(p => new PlayerResponse(p.Id, p.Name)).ToList(),
            PointValues: league.PointValues.ToDictionary(pv => pv.PokemonId, pv => pv.Value),
            Draft: new DraftStateResponse(
                Status: league.DraftStatus.ToString(),
                CurrentPickNumber: league.CurrentPickNumber,
                TotalPicks: totalPicks,
                CurrentPickerId: currentPickerId,
                CurrentPickerName: currentPickerName,
                Picks: picks.Select(p => new DraftPickResponse(p.PickNumber, p.Round, p.PlayerId, p.PokemonId)).ToList()
            )
        );
    }

    private League? LoadLeague(string code)
    {
        var normalizedCode = NormalizeLeagueCode(code);
        var league = db.Leagues
            .Include(l => l.Players)
            .Include(l => l.Picks)
            .Include(l => l.PointValues)
            .Include(l => l.Trades).ThenInclude(t => t.Items)
            .FirstOrDefault(l => l.Code == normalizedCode);

        if (league is null) return null;

        league.Players = league.Players.OrderBy(p => p.SortOrder).ToList();
        league.Picks = league.Picks.OrderBy(p => p.PickNumber).ToList();
        league.PointValues = league.PointValues.OrderBy(p => p.PokemonId).ToList();
        return league;
    }

    private static List<Player> GetOrderedPlayers(League league) => league.Players.OrderBy(p => p.SortOrder).ToList();

    private static string GetPlayerIdAtPick(League league, int pickNumber)
    {
        var players = GetOrderedPlayers(league);
        var n = players.Count;
        if (n == 0) return string.Empty;
        var round = pickNumber / n;
        var posInRound = pickNumber % n;
        var idx = round % 2 == 0 ? posInRound : n - 1 - posInRound;
        return players[idx].Id;
    }

    private static void ReindexPlayers(List<Player> players)
    {
        for (var i = 0; i < players.Count; i++)
            players[i].SortOrder = i;
    }

    // ── Roster management ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public (bool success, string? error) DropPokemon(string leagueCode, string playerId, string pin, int pokemonId)
    {
        var league = LoadLeague(leagueCode);
        if (league is null) return (false, "League not found.");

        var player = league.Players.FirstOrDefault(p => p.Id == playerId);
        if (player is null || !BC.Verify(pin, player.Pin))
            return (false, "Invalid player or PIN.");

        var pick = league.Picks.FirstOrDefault(p => p.PlayerId == playerId && p.PokemonId == pokemonId);
        if (pick is null) return (false, "That Pokémon is not on your team.");

        db.Picks.Remove(pick);
        db.SaveChanges();
        return (true, null);
    }

    /// <inheritdoc/>
    public (bool success, string? error) AddPokemon(string leagueCode, string playerId, string pin, int pokemonId)
    {
        var league = LoadLeague(leagueCode);
        if (league is null) return (false, "League not found.");

        var player = league.Players.FirstOrDefault(p => p.Id == playerId);
        if (player is null || !BC.Verify(pin, player.Pin))
            return (false, "Invalid player or PIN.");

        if (league.Picks.Any(p => p.PokemonId == pokemonId))
            return (false, "That Pokémon is already on a team.");

        var playerPicks = league.Picks.Where(p => p.PlayerId == playerId).ToList();
        if (playerPicks.Count >= league.Rounds)
            return (false, "Your roster is full.");

        var nextPickNumber = league.Picks.Count > 0 ? league.Picks.Max(p => p.PickNumber) + 1 : 0;
        league.Picks.Add(new DraftPick
        {
            LeagueCode = league.Code,
            PickNumber = nextPickNumber,
            Round = playerPicks.Count,
            PlayerId = playerId,
            PokemonId = pokemonId,
        });

        db.SaveChanges();
        return (true, null);
    }

    // ── Trades ─────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public List<TradeResponse> GetTrades(string leagueCode)
    {
        var league = LoadLeague(leagueCode);
        if (league is null) return [];
        return league.Trades.OrderByDescending(t => t.ProposedAt).Select(ToTradeResponse).ToList();
    }

    /// <inheritdoc/>
    public (TradeResponse? trade, string? error) ProposeTrade(
        string leagueCode, string initiatorId, string initiatorPin,
        string targetId, List<int> offering, List<int> requesting)
    {
        var league = LoadLeague(leagueCode);
        if (league is null) return (null, "League not found.");

        var initiator = league.Players.FirstOrDefault(p => p.Id == initiatorId);
        if (initiator is null || !BC.Verify(initiatorPin, initiator.Pin))
            return (null, "Invalid player or PIN.");

        if (!league.Players.Any(p => p.Id == targetId))
            return (null, "Target player not found.");

        if (initiatorId == targetId)
            return (null, "You cannot trade with yourself.");

        foreach (var pid in offering)
            if (!league.Picks.Any(p => p.PlayerId == initiatorId && p.PokemonId == pid))
                return (null, $"Pokémon {pid} is not on your team.");

        foreach (var pid in requesting)
            if (!league.Picks.Any(p => p.PlayerId == targetId && p.PokemonId == pid))
                return (null, $"Pokémon {pid} is not on the target's team.");

        var trade = new Trade
        {
            LeagueCode = league.Code,
            InitiatorPlayerId = initiatorId,
            TargetPlayerId = targetId,
            Status = TradeStatus.Pending,
            ProposedAt = DateTime.UtcNow,
        };

        foreach (var pid in offering)
            trade.Items.Add(new TradeItem { FromPlayerId = initiatorId, PokemonId = pid });
        foreach (var pid in requesting)
            trade.Items.Add(new TradeItem { FromPlayerId = targetId, PokemonId = pid });

        db.Trades.Add(trade);
        db.SaveChanges();
        return (ToTradeResponse(trade), null);
    }

    /// <inheritdoc/>
    public (bool success, string? error) RespondToTrade(string leagueCode, int tradeId, string playerId, string pin, TradeStatus response)
    {
        var league = LoadLeague(leagueCode);
        if (league is null) return (false, "League not found.");

        var trade = league.Trades.FirstOrDefault(t => t.Id == tradeId);
        if (trade is null) return (false, "Trade not found.");

        if (trade.Status != TradeStatus.Pending)
            return (false, "Trade is no longer pending.");

        var player = league.Players.FirstOrDefault(p => p.Id == playerId);
        if (player is null || !BC.Verify(pin, player.Pin))
            return (false, "Invalid player or PIN.");

        if (response == TradeStatus.Cancelled)
        {
            if (playerId != trade.InitiatorPlayerId)
                return (false, "Only the initiator can cancel a trade.");
        }
        else
        {
            if (playerId != trade.TargetPlayerId)
                return (false, "Only the target player can accept or reject a trade.");
        }

        trade.Status = response;

        if (response == TradeStatus.Accepted)
        {
            foreach (var item in trade.Items)
            {
                var toPlayerId = item.FromPlayerId == trade.InitiatorPlayerId
                    ? trade.TargetPlayerId
                    : trade.InitiatorPlayerId;

                var pick = league.Picks.FirstOrDefault(p => p.PlayerId == item.FromPlayerId && p.PokemonId == item.PokemonId);
                if (pick is not null) pick.PlayerId = toPlayerId;
            }
        }

        db.SaveChanges();
        return (true, null);
    }

    private static TradeResponse ToTradeResponse(Trade trade) => new(
        Id: trade.Id,
        InitiatorPlayerId: trade.InitiatorPlayerId,
        TargetPlayerId: trade.TargetPlayerId,
        Status: trade.Status.ToString(),
        ProposedAt: trade.ProposedAt,
        Items: trade.Items.Select(i => new TradeItemResponse(i.FromPlayerId, i.PokemonId)).ToList()
    );

    private string GenerateCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        string code;
        do
        {
            code = new string(Enumerable.Range(0, 6).Select(_ => chars[Random.Shared.Next(chars.Length)]).ToArray());
        } while (db.Leagues.Any(l => l.Code == code));

        return code;
    }

    private static string NormalizeLeagueCode(string code) => code.Trim().ToUpperInvariant();
}
