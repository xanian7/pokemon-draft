using BC = BCrypt.Net.BCrypt;
using Microsoft.EntityFrameworkCore;
using PokemonDraft.Data;
using PokemonDraft.DTOs;
using PokemonDraft.Models;
using System.Text.Json;

namespace PokemonDraft.Services;

/// <inheritdoc cref="ILeagueService"/>
public class LeagueService(DraftDbContext db) : ILeagueService
{
    /// <inheritdoc/>
    public (CreateLeagueResponse? result, string? error) CreateLeague(CreateLeagueRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.CommissionerName))
            return (null, "Name and commissioner name are required.");

        bool isGoogleUser = req.UserId.HasValue;
        if (!isGoogleUser && string.IsNullOrWhiteSpace(req.AdminPin))
            return (null, "Admin PIN is required.");

        var name = req.Name.Trim();
        var commissionerName = req.CommissionerName.Trim();

        // For Google users, AdminPin is not set by the user. A random placeholder is hashed so the
        // column is never empty. EnterLeague() will rotate it to a real token on first login.
        var pinToHash = isGoogleUser ? Guid.NewGuid().ToString() : req.AdminPin!;

        var code = GenerateCode();
        var commissioner = new Player
        {
            Id = Guid.NewGuid().ToString(),
            LeagueCode = code,
            Name = commissionerName,
            Pin = BC.HashPassword(pinToHash),
            SortOrder = 0,
            UserId = req.UserId,
        };

        var league = new League
        {
            Code = code,
            Name = name,
            AdminPin = BC.HashPassword(pinToHash),
            CommissionerPlayerId = commissioner.Id,
            DraftStatus = DraftStatus.Setup,
            CurrentPickNumber = 0,
            Players = [commissioner],
        };

        db.Leagues.Add(league);
        db.SaveChanges();
        return (new CreateLeagueResponse(code, name), null);
    }

    public LeagueResponse? GetLeagueResponse(string code)
    {
        var league = LoadLeagueReadOnly(code);
        return league is null ? null : ToResponse(league);
    }

    /// <inheritdoc/>
    public (bool success, string? error) UpdateConfig(string code, UpdateLeagueConfigRequest req)
    {
        var league = LoadLeagueBase(code);
        if (league is null) return (false, null);
        if (req.Name is not null) league.Name = req.Name;
        if (req.PointLimit is not null) league.PointLimit = req.PointLimit.Value;
        if (req.Rounds is not null) league.Rounds = req.Rounds.Value;
        if (req.RegulationSet is not null) league.RegulationSet = req.RegulationSet;
        if (req.PlayoffSpots is not null) league.PlayoffSpots = req.PlayoffSpots.Value;
        db.SaveChanges();
        return (true, null);
    }

    /// <inheritdoc/>
    public (PlayerCreatedResponse? result, string? error) AddPlayer(string leagueCode, AddPlayerRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Pin))
            return (null, "Name and PIN are required.");

        var league = LoadLeagueWithPlayers(leagueCode);
        if (league is null) return (null, null);

        var player = new Player
        {
            Id = Guid.NewGuid().ToString(),
            LeagueCode = league.Code,
            Name = req.Name.Trim(),
            Pin = BC.HashPassword(req.Pin),
            SortOrder = GetOrderedPlayers(league).Count,
        };

        league.Players.Add(player);
        db.SaveChanges();
        return (new PlayerCreatedResponse(player.Id, player.Name), null);
    }

    /// <inheritdoc/>
    public (PlayerCreatedResponse? result, string? error) RegisterPlayer(string leagueCode, RegisterPlayerRequest req)
    {
        bool isGoogleUser = req.UserId.HasValue;
        if (!isGoogleUser && string.IsNullOrWhiteSpace(req.Pin))
            return (null, "Name and PIN are required.");
        if (string.IsNullOrWhiteSpace(req.Name))
            return (null, "Name is required.");

        var league = LoadLeagueWithPlayers(leagueCode);
        if (league is null)
            return (null, "League not found.");

        var trimmedName = req.Name.Trim();
        var orderedPlayers = GetOrderedPlayers(league);

        if (orderedPlayers.Any(p => p.Name.Equals(trimmedName, StringComparison.OrdinalIgnoreCase)))
            return (null, "That name is already taken in this league.");

        // Reject duplicate PINs only for PIN-based registrations
        if (!isGoogleUser && orderedPlayers.Any(p => BC.Verify(req.Pin!, p.Pin)))
            return (null, "That PIN is already in use. Please choose a different one.");

        // For Google users, a random placeholder is hashed so Pin is never empty.
        // EnterLeague() will rotate it to a real token on first login.
        var pinToHash = isGoogleUser ? Guid.NewGuid().ToString() : req.Pin!;

        var player = new Player
        {
            Id = Guid.NewGuid().ToString(),
            LeagueCode = league.Code,
            Name = trimmedName,
            Pin = BC.HashPassword(pinToHash),
            SortOrder = orderedPlayers.Count,
            TeamName = req.TeamName?.Trim() ?? string.Empty,
            TeamImageUrl = req.TeamImageUrl?.Trim() ?? string.Empty,
            UserId = req.UserId,
        };

        league.Players.Add(player);
        db.SaveChanges();
        return (new PlayerCreatedResponse(player.Id, player.Name), null);
    }

    /// <inheritdoc/>
    public (bool success, string? error) RemovePlayer(string leagueCode, string playerId)
    {
        var league = LoadLeagueWithPlayers(leagueCode);
        if (league is null) return (false, null);

        var player = league.Players.FirstOrDefault(p => p.Id == playerId);
        if (player is null) return (false, null);

        db.Players.Remove(player);
        ReindexPlayers(league.Players.Where(p => p.Id != playerId).OrderBy(p => p.SortOrder).ToList());
        db.SaveChanges();
        return (true, null);
    }

    /// <inheritdoc/>
    public (bool success, string? error) MovePlayer(string leagueCode, int fromIndex, int toIndex)
    {
        var league = LoadLeagueWithPlayers(leagueCode);
        if (league is null) return (false, null);

        var players = GetOrderedPlayers(league);
        if (fromIndex < 0 || fromIndex >= players.Count) return (false, "Invalid player index.");
        if (toIndex < 0 || toIndex >= players.Count) return (false, "Invalid player index.");

        var player = players[fromIndex];
        players.RemoveAt(fromIndex);
        players.Insert(toIndex, player);
        ReindexPlayers(players);
        db.SaveChanges();
        return (true, null);
    }

    /// <inheritdoc/>
    public (bool success, string? error) SetPointValues(string leagueCode, Dictionary<int, int> values)
    {
        var league = LoadLeagueWithPointValues(leagueCode);
        if (league is null) return (false, null);

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
        return (true, null);
    }

    /// <inheritdoc/>
    public JoinResponse? ValidatePin(string leagueCode, string pin)
    {
        var league = LoadLeagueWithPlayers(leagueCode);
        if (league is null) return null;

        if (VerifyAdminPin(league, pin))
        {
            var commissioner = GetOrderedPlayers(league).FirstOrDefault(p => p.Id == league.CommissionerPlayerId);
            var name = commissioner?.Name ?? "Commissioner";
            var playerId = commissioner?.Id ?? "admin";
            return new JoinResponse(playerId, name, true, league.Code, commissioner?.TeamName ?? string.Empty, commissioner?.TeamImageUrl ?? string.Empty, league.Name);
        }

        var player = GetOrderedPlayers(league)
            .FirstOrDefault(p => p.Id != league.CommissionerPlayerId && VerifyPin(p, pin));
        if (player is null) return null;

        return new JoinResponse(player.Id, player.Name, false, league.Code, player.TeamName, player.TeamImageUrl, league.Name);
    }

    /// <inheritdoc/>
    public (bool success, string? error) StartDraft(string leagueCode)
    {
        var league = LoadLeague(leagueCode);
        if (league is null) return (false, null);
        if (league.Players.Count < 2) return (false, "Need at least 2 players to start the draft.");

        db.Picks.RemoveRange(league.Picks);
        db.Matchups.RemoveRange(league.Matchups);
        league.Picks.Clear();
        league.Matchups.Clear();
        RandomizePlayerOrder(league);
        league.DraftStatus = DraftStatus.Active;
        league.CurrentPickNumber = 0;
        AdvanceToNextEligiblePick(league);
        db.SaveChanges();
        return (true, null);
    }

    /// <inheritdoc/>
    public (bool success, string? error) ResetDraft(string leagueCode)
    {
        var league = LoadLeague(leagueCode);
        if (league is null) return (false, null);

        db.Picks.RemoveRange(league.Picks);
        db.Matchups.RemoveRange(league.Matchups);
        league.Picks.Clear();
        league.Matchups.Clear();
        league.DraftStatus = DraftStatus.Setup;
        league.CurrentPickNumber = 0;
        db.SaveChanges();
        return (true, null);
    }

    /// <inheritdoc/>
    public (bool success, string? error, bool draftCompleted) MakePick(string leagueCode, string playerId, string pin, int pokemonId)
    {
        var league = LoadLeagueWithDraft(leagueCode);
        if (league is null)
            return (false, "League not found.", false);

        if (league.DraftStatus != DraftStatus.Active)
            return (false, "Draft is not active.", false);

        var orderedPlayers = GetOrderedPlayers(league);
        var player = orderedPlayers.FirstOrDefault(p => p.Id == playerId);
        if (player is null || !VerifyPin(player, pin))
            return (false, "Invalid player or PIN.", false);

        AdvanceToNextEligiblePick(league);
        if (league.DraftStatus == DraftStatus.Complete)
        {
            db.SaveChanges();
            return (false, "Draft is complete.", true);
        }

        var expectedPickerId = GetPlayerIdAtPick(league, league.CurrentPickNumber);
        if (expectedPickerId != playerId)
            return (false, "It is not your turn.", false);

        if (league.Picks.Any(p => p.PokemonId == pokemonId))
            return (false, "That Pokémon has already been drafted.", false);

        var pointValues = league.PointValues.ToDictionary(pv => pv.PokemonId, pv => pv.Value);
        var currentPointTotal = league.Picks
            .Where(p => p.PlayerId == playerId)
            .Sum(p => pointValues.GetValueOrDefault(p.PokemonId, 0));
        var requestedPointValue = pointValues.GetValueOrDefault(pokemonId, 0);
        if (currentPointTotal + requestedPointValue > league.PointLimit)
        {
            var remainingPoints = Math.Max(0, league.PointLimit - currentPointTotal);
            return (false, $"That Pokémon costs {requestedPointValue} points, but you only have {remainingPoints} points remaining.", false);
        }

        var totalPicks = orderedPlayers.Count * league.Rounds;
        var round = league.CurrentPickNumber / orderedPlayers.Count;
        var justCompleted = false;

        league.Picks.Add(new DraftPick
        {
            LeagueCode = league.Code,
            PickNumber = league.CurrentPickNumber,
            Round = round,
            PlayerId = playerId,
            PokemonId = pokemonId,
        });

        league.CurrentPickNumber++;
        AdvanceToNextEligiblePick(league);
        justCompleted = league.DraftStatus == DraftStatus.Complete;

        db.SaveChanges();
        return (true, null, justCompleted);
    }

    /// <inheritdoc/>
    private LeagueResponse ToResponse(League league)
    {
        var players = GetOrderedPlayers(league);
        var picks = league.Picks.OrderBy(p => p.PickNumber).ToList();
        var totalPicks = players.Count * league.Rounds;
        string? currentPickerId = null;
        string? currentPickerName = null;

        var currentPickNumber = FindNextEligiblePickNumber(league, league.CurrentPickNumber);

        if (league.DraftStatus == DraftStatus.Active && currentPickNumber < totalPicks)
        {
            currentPickerId = GetPlayerIdAtPick(league, currentPickNumber);
            currentPickerName = players.FirstOrDefault(p => p.Id == currentPickerId)?.Name;
        }

        return new LeagueResponse(
            Code: league.Code,
            Name: league.Name,
            PointLimit: league.PointLimit,
            Rounds: league.Rounds,
            PlayoffSpots: league.PlayoffSpots,
            RegulationSet: league.RegulationSet,
            Players: players.Select(p => new PlayerResponse(p.Id, p.Name, p.TeamName, p.TeamImageUrl, p.User?.DiscordId)).ToList(),
            PointValues: league.PointValues.ToDictionary(pv => pv.PokemonId, pv => pv.Value),
            Draft: new DraftStateResponse(
                Status: league.DraftStatus.ToString(),
                CurrentPickNumber: currentPickNumber,
                TotalPicks: totalPicks,
                CurrentPickerId: currentPickerId,
                CurrentPickerName: currentPickerName,
                Picks: picks.Select(p => new DraftPickResponse(p.PickNumber, p.Round, p.PlayerId, p.PokemonId)).ToList()
            )
        );
    }

    /// <inheritdoc/>
    public void GenerateSchedule(string leagueCode)
    {
        var league = LoadLeagueWithSchedule(leagueCode);
        if (league is null) return;

        db.Matchups.RemoveRange(league.Matchups);
        league.Matchups.Clear();

        var players = GetOrderedPlayers(league).Select(p => p.Id).ToList();
        if (players.Count < 2)
        {
            db.SaveChanges();
            return;
        }

        var hasBye = players.Count % 2 == 1;
        if (hasBye) players.Add("BYE");

        var numRounds = players.Count - 1;
        var half = players.Count / 2;

        for (var round = 0; round < numRounds; round++)
        {
            var week = round + 1;
            for (var i = 0; i < half; i++)
            {
                var p1 = players[i];
                var p2 = players[players.Count - 1 - i];
                if (p1 == "BYE" || p2 == "BYE") continue;

                league.Matchups.Add(new Matchup
                {
                    LeagueCode = league.Code,
                    Week = week,
                    Player1Id = p1,
                    Player2Id = p2,
                });
            }

            var last = players[^1];
            players.RemoveAt(players.Count - 1);
            players.Insert(1, last);
        }

        db.SaveChanges();
    }

    /// <inheritdoc/>
    public ScheduleResponse? GetSchedule(string leagueCode)
    {
        var league = LoadLeagueScheduleReadOnly(leagueCode);
        if (league is null) return null;

        var players = GetOrderedPlayers(league).ToDictionary(p => p.Id);

        static (int mp1, int mp2) CalcMatchPoints(int? p1w, int? p2w)
        {
            if (p1w is null || p2w is null) return (0, 0);
            if (p1w == 2 && p2w == 0) return (3, 0);
            if (p1w == 0 && p2w == 2) return (0, 3);
            if (p1w == 2) return (2, 1);
            if (p2w == 2) return (1, 2);
            return (0, 0);
        }

        MatchupResponse ToMatchupResponse(Matchup matchup)
        {
            players.TryGetValue(matchup.Player1Id, out var player1);
            players.TryGetValue(matchup.Player2Id, out var player2);

            int? player1MatchPoints = null;
            int? player2MatchPoints = null;
            if (matchup.Player1Wins.HasValue && matchup.Player2Wins.HasValue)
            {
                var (mp1, mp2) = CalcMatchPoints(matchup.Player1Wins, matchup.Player2Wins);
                player1MatchPoints = mp1;
                player2MatchPoints = mp2;
            }

            return new MatchupResponse(
                matchup.Id,
                matchup.Week,
                matchup.Player1Id,
                player1?.Name ?? "Unknown",
                player1?.TeamName ?? string.Empty,
                player1?.TeamImageUrl ?? string.Empty,
                matchup.Player2Id,
                player2?.Name ?? "Unknown",
                player2?.TeamName ?? string.Empty,
                player2?.TeamImageUrl ?? string.Empty,
                matchup.Player1Wins,
                matchup.Player2Wins,
                player1MatchPoints,
                player2MatchPoints,
                GetReplayUrls(matchup.ReplayUrl).FirstOrDefault(),
                GetReplayUrls(matchup.ReplayUrl));
        }

        var completedMatchups = league.Matchups.Where(m => m.Player1Wins.HasValue && m.Player2Wins.HasValue).ToList();

        var weeks = league.Matchups
            .OrderBy(m => m.Week)
            .ThenBy(m => m.Id)
            .GroupBy(m => m.Week)
            .Select(g => new WeekGroup(g.Key, g.Select(ToMatchupResponse).ToList()))
            .ToList();

        var standings = players.Values
            .Select(player =>
            {
                var wins = 0;
                var losses = 0;
                var matchPoints = 0;
                var gamesWon = 0;
                var gamesLost = 0;

                foreach (var matchup in completedMatchups.Where(m => m.Player1Id == player.Id || m.Player2Id == player.Id))
                {
                    var (player1MatchPoints, player2MatchPoints) = CalcMatchPoints(matchup.Player1Wins, matchup.Player2Wins);
                    if (matchup.Player1Id == player.Id)
                    {
                        matchPoints += player1MatchPoints;
                        gamesWon += matchup.Player1Wins!.Value;
                        gamesLost += matchup.Player2Wins!.Value;
                        if (matchup.Player1Wins > matchup.Player2Wins) wins++; else losses++;
                    }
                    else
                    {
                        matchPoints += player2MatchPoints;
                        gamesWon += matchup.Player2Wins!.Value;
                        gamesLost += matchup.Player1Wins!.Value;
                        if (matchup.Player2Wins > matchup.Player1Wins) wins++; else losses++;
                    }
                }

                return new StandingRow(
                    player.Id,
                    player.Name,
                    player.TeamName,
                    player.TeamImageUrl,
                    wins,
                    losses,
                    matchPoints,
                    gamesWon,
                    gamesLost);
            })
            .OrderByDescending(s => s.MatchPoints)
            .ThenByDescending(s => s.Wins)
            .ThenByDescending(s => s.GamesWon - s.GamesLost)
            .ToList();

        return new ScheduleResponse(weeks, standings);
    }

    /// <inheritdoc/>
    public List<PlayoffOutlookEntry>? GetPlayoffOutlook(string leagueCode)
    {
        var league = LoadLeagueScheduleReadOnly(leagueCode);
        if (league is null) return null;

        var players = GetOrderedPlayers(league).ToDictionary(p => p.Id);
        var allMatchups = league.Matchups.ToList();

        static (int mp1, int mp2) CalcMatchPoints(int? p1w, int? p2w)
        {
            if (p1w is null || p2w is null) return (0, 0);
            if (p1w == 2 && p2w == 0) return (3, 0);
            if (p1w == 0 && p2w == 2) return (0, 3);
            if (p1w == 2) return (2, 1);
            if (p2w == 2) return (1, 2);
            return (0, 0);
        }

        var stats = players.Values.Select(player =>
        {
            var myMatchups = allMatchups.Where(m => m.Player1Id == player.Id || m.Player2Id == player.Id).ToList();
            var completed = myMatchups.Where(m => m.Player1Wins.HasValue).ToList();
            var remaining = myMatchups.Count - completed.Count;

            int wins = 0, losses = 0, matchPoints = 0, gamesWon = 0, gamesLost = 0;
            foreach (var m in completed)
            {
                var (mp1, mp2) = CalcMatchPoints(m.Player1Wins, m.Player2Wins);
                if (m.Player1Id == player.Id)
                {
                    matchPoints += mp1;
                    gamesWon += m.Player1Wins!.Value;
                    gamesLost += m.Player2Wins!.Value;
                    if (m.Player1Wins > m.Player2Wins) wins++; else losses++;
                }
                else
                {
                    matchPoints += mp2;
                    gamesWon += m.Player2Wins!.Value;
                    gamesLost += m.Player1Wins!.Value;
                    if (m.Player2Wins > m.Player1Wins) wins++; else losses++;
                }
            }

            return new { player, wins, losses, matchPoints, gamesWon, gamesLost, remaining, maxWins = wins + remaining };
        })
        .OrderByDescending(s => s.wins)
        .ThenByDescending(s => s.matchPoints)
        .ThenByDescending(s => s.gamesWon - s.gamesLost)
        .ToList();

        var total = stats.Count;
        var spots = Math.Min(league.PlayoffSpots, total);

        // Pre-compute boundary values for efficient lookup
        int bestOutsiderMaxWins = spots < total ? stats.Skip(spots).Max(x => x.maxWins) : -1;
        int cutlineWins = spots > 0 ? stats[spots - 1].wins : 0;
        int cutlineMaxWins = spots > 0 ? stats[spots - 1].maxWins : 0;

        return stats.Select((s, rank) =>
        {
            string status;
            int? magicNumber;

            if (total <= spots)
            {
                // Fewer teams than playoff spots — everyone is in
                status = "Clinched";
                magicNumber = null;
            }
            else if (rank < spots)
            {
                // Currently in a playoff spot
                bool clinched = bestOutsiderMaxWins < s.wins;
                if (clinched)
                {
                    status = "Clinched";
                    magicNumber = null;
                }
                else
                {
                    status = "InContention";
                    var needed = Math.Max(0, bestOutsiderMaxWins - s.wins + 1);
                    magicNumber = needed <= s.remaining ? needed : null;
                }
            }
            else
            {
                // Currently outside playoff spots
                bool eliminated = s.maxWins < cutlineWins;
                if (eliminated)
                {
                    status = "Eliminated";
                    magicNumber = null;
                }
                else
                {
                    status = "InContention";
                    var needed = Math.Max(0, cutlineMaxWins - s.wins + 1);
                    magicNumber = needed <= s.remaining ? needed : null;
                }
            }

            return new PlayoffOutlookEntry(
                s.player.Id, s.player.Name, s.player.TeamName, s.player.TeamImageUrl,
                s.wins, s.losses, s.matchPoints, s.gamesWon, s.gamesLost,
                s.remaining, s.maxWins, magicNumber, status
            );
        }).ToList();
    }

    /// <inheritdoc/>
    public (bool success, string? error) ReportMatchup(string leagueCode, int matchupId, string playerId, string pin, int player1Wins, int player2Wins, string? replayUrl, List<string>? replayUrls)
    {
        var league = LoadLeagueWithSchedule(leagueCode);
        if (league is null) return (false, "League not found.");

        var matchup = league.Matchups.FirstOrDefault(m => m.Id == matchupId);
        if (matchup is null) return (false, "Matchup not found.");

        if (matchup.Player1Id != playerId && matchup.Player2Id != playerId)
            return (false, "You are not in this matchup.");

        if (matchup.Player1Wins.HasValue || matchup.Player2Wins.HasValue)
            return (false, "This matchup has already been reported.");

        var player = GetOrderedPlayers(league).FirstOrDefault(p => p.Id == playerId);
        if (player is null || !VerifyPin(player, pin))
            return (false, "Invalid PIN.");

        if (player1Wins < 0 || player2Wins < 0 || player1Wins > 2 || player2Wins > 2)
            return (false, "Wins must be between 0 and 2.");
        if (player1Wins + player2Wins > 3)
            return (false, "A best-of-3 cannot exceed 3 total games.");
        if (player1Wins != 2 && player2Wins != 2)
            return (false, "One player must have 2 wins.");
        if (player1Wins == 2 && player2Wins == 2)
            return (false, "Both players cannot have 2 wins.");

        var (normalizedReplayUrls, replayError) = NormalizeReplayUrls(replayUrl, replayUrls);
        if (replayError is not null) return (false, replayError);

        matchup.Player1Wins = player1Wins;
        matchup.Player2Wins = player2Wins;
        matchup.ReplayUrl = SerializeReplayUrls(normalizedReplayUrls);
        matchup.ReportedByPlayerId = playerId;
        matchup.ReportedAt = DateTime.UtcNow;
        db.SaveChanges();
        return (true, null);
    }

    /// <inheritdoc/>
    public (bool success, string? error) EditMatchup(string leagueCode, int matchupId, string adminPin, int player1Wins, int player2Wins, string? replayUrl, List<string>? replayUrls)
    {
        var league = LoadLeagueWithSchedule(leagueCode);
        if (league is null) return (false, "League not found.");

        if (!VerifyAdminPin(league, adminPin))
            return (false, "Invalid admin PIN.");

        var matchup = league.Matchups.FirstOrDefault(m => m.Id == matchupId);
        if (matchup is null) return (false, "Matchup not found.");

        if (player1Wins < 0 || player2Wins < 0 || player1Wins > 2 || player2Wins > 2)
            return (false, "Wins must be between 0 and 2.");
        if (player1Wins + player2Wins > 3)
            return (false, "A best-of-3 cannot exceed 3 total games.");
        if (player1Wins != 2 && player2Wins != 2)
            return (false, "One player must have 2 wins.");
        if (player1Wins == 2 && player2Wins == 2)
            return (false, "Both players cannot have 2 wins.");

        var (normalizedReplayUrls, replayError) = NormalizeReplayUrls(replayUrl, replayUrls);
        if (replayError is not null) return (false, replayError);

        matchup.Player1Wins = player1Wins;
        matchup.Player2Wins = player2Wins;
        matchup.ReplayUrl = SerializeReplayUrls(normalizedReplayUrls);
        matchup.ReportedAt = DateTime.UtcNow;
        db.SaveChanges();
        return (true, null);
    }

    private static List<string> GetReplayUrls(string? replayUrl)
    {
        var trimmed = replayUrl?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return [];

        if (trimmed.StartsWith("["))
        {
            try
            {
                return JsonSerializer.Deserialize<List<string>>(trimmed) ?? [];
            }
            catch
            {
                return [trimmed];
            }
        }

        return [trimmed];
    }

    private static string? SerializeReplayUrls(List<string> replayUrls)
    {
        if (replayUrls.Count == 0) return null;
        if (replayUrls.Count == 1) return replayUrls[0];
        return JsonSerializer.Serialize(replayUrls);
    }

    private static (List<string> replayUrls, string? error) NormalizeReplayUrls(string? replayUrl, List<string>? replayUrls)
    {
        var candidates = (replayUrls is { Count: > 0 } ? replayUrls : [replayUrl ?? string.Empty])
            .Select(url => url.Trim())
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .ToList();

        if (candidates.Count > 3) return ([], "A match report can include at most 3 replay links.");

        foreach (var url in candidates)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return ([], "Replay links must be valid http or https URLs.");
            }
        }

        return (candidates, null);
    }

    /// <inheritdoc/>
    public (bool success, string? error) UpdatePlayerProfile(string leagueCode, string playerId, string pin, string? teamName, string? teamImageUrl)
    {
        var league = LoadLeagueWithPlayers(leagueCode);
        if (league is null) return (false, "League not found.");

        var player = GetOrderedPlayers(league).FirstOrDefault(p => p.Id == playerId);
        if (player is null || !VerifyPin(player, pin))
            return (false, "Invalid player or PIN.");

        if (teamName is not null) player.TeamName = teamName.Trim();
        if (teamImageUrl is not null) player.TeamImageUrl = teamImageUrl.Trim();
        db.SaveChanges();
        return (true, null);
    }

    // Full tracked load with split queries — used for complex write operations (StartDraft, ResetDraft).
    private League? LoadLeague(string code)
    {
        var normalizedCode = NormalizeLeagueCode(code);
        var league = db.Leagues
            .AsSplitQuery()
            .Include(l => l.Players).ThenInclude(p => p.User)
            .Include(l => l.Picks)
            .Include(l => l.PointValues)
            .Include(l => l.Trades).ThenInclude(t => t.Items)
            .Include(l => l.Matchups)
            .FirstOrDefault(l => l.Code == normalizedCode);

        if (league is null) return null;
        SortLeagueCollections(league);
        return league;
    }

    // Full read-only load — no change tracking overhead, for GET response endpoints.
    private League? LoadLeagueReadOnly(string code)
    {
        var normalizedCode = NormalizeLeagueCode(code);
        var league = db.Leagues
            .AsNoTracking()
            .AsSplitQuery()
            .Include(l => l.Players).ThenInclude(p => p.User)
            .Include(l => l.Picks)
            .Include(l => l.PointValues)
            .Include(l => l.Trades).ThenInclude(t => t.Items)
            .Include(l => l.Matchups)
            .FirstOrDefault(l => l.Code == normalizedCode);

        if (league is null) return null;
        SortLeagueCollections(league);
        return league;
    }

    // League scalar fields only — for operations that only modify top-level properties.
    private League? LoadLeagueBase(string code)
    {
        var normalizedCode = NormalizeLeagueCode(code);
        return db.Leagues.FirstOrDefault(l => l.Code == normalizedCode);
    }

    // League + Players — for player management and PIN validation.
    private League? LoadLeagueWithPlayers(string code)
    {
        var normalizedCode = NormalizeLeagueCode(code);
        var league = db.Leagues
            .Include(l => l.Players)
            .FirstOrDefault(l => l.Code == normalizedCode);
        if (league is null) return null;
        league.Players = league.Players.OrderBy(p => p.SortOrder).ToList();
        return league;
    }

    // League + PointValues only — for SetPointValues.
    private League? LoadLeagueWithPointValues(string code)
    {
        var normalizedCode = NormalizeLeagueCode(code);
        return db.Leagues
            .Include(l => l.PointValues)
            .FirstOrDefault(l => l.Code == normalizedCode);
    }

    // League + Players + Picks + PointValues — for draft picks and post-draft roster changes.
    private League? LoadLeagueWithDraft(string code)
    {
        var normalizedCode = NormalizeLeagueCode(code);
        var league = db.Leagues
            .AsSplitQuery()
            .Include(l => l.Players)
            .Include(l => l.Picks)
            .Include(l => l.PointValues)
            .FirstOrDefault(l => l.Code == normalizedCode);
        if (league is null) return null;
        league.Players = league.Players.OrderBy(p => p.SortOrder).ToList();
        league.Picks = league.Picks.OrderBy(p => p.PickNumber).ToList();
        league.PointValues = league.PointValues.OrderBy(p => p.PokemonId).ToList();
        return league;
    }

    // League + Players + Matchups — for schedule generation and matchup reporting.
    private League? LoadLeagueWithSchedule(string code)
    {
        var normalizedCode = NormalizeLeagueCode(code);
        var league = db.Leagues
            .AsSplitQuery()
            .Include(l => l.Players)
            .Include(l => l.Matchups)
            .FirstOrDefault(l => l.Code == normalizedCode);
        if (league is null) return null;
        league.Players = league.Players.OrderBy(p => p.SortOrder).ToList();
        league.Matchups = league.Matchups.OrderBy(m => m.Week).ThenBy(m => m.Id).ToList();
        return league;
    }

    // Read-only schedule view — no change tracking, for GET schedule/standings endpoints.
    private League? LoadLeagueScheduleReadOnly(string code)
    {
        var normalizedCode = NormalizeLeagueCode(code);
        var league = db.Leagues
            .AsNoTracking()
            .AsSplitQuery()
            .Include(l => l.Players)
            .Include(l => l.Matchups)
            .FirstOrDefault(l => l.Code == normalizedCode);
        if (league is null) return null;
        league.Players = league.Players.OrderBy(p => p.SortOrder).ToList();
        league.Matchups = league.Matchups.OrderBy(m => m.Week).ThenBy(m => m.Id).ToList();
        return league;
    }

    // Read-only trades view — no change tracking, for GetTrades.
    private League? LoadLeagueTradesReadOnly(string code)
    {
        var normalizedCode = NormalizeLeagueCode(code);
        return db.Leagues
            .AsNoTracking()
            .AsSplitQuery()
            .Include(l => l.Trades).ThenInclude(t => t.Items)
            .FirstOrDefault(l => l.Code == normalizedCode);
    }

    // League + Players + Picks + Trades — for trade proposals and responses.
    private League? LoadLeagueWithTrades(string code)
    {
        var normalizedCode = NormalizeLeagueCode(code);
        var league = db.Leagues
            .AsSplitQuery()
            .Include(l => l.Players)
            .Include(l => l.Picks)
            .Include(l => l.Trades).ThenInclude(t => t.Items)
            .FirstOrDefault(l => l.Code == normalizedCode);
        if (league is null) return null;
        league.Players = league.Players.OrderBy(p => p.SortOrder).ToList();
        league.Picks = league.Picks.OrderBy(p => p.PickNumber).ToList();
        return league;
    }

    private static void SortLeagueCollections(League league)
    {
        league.Players = league.Players.OrderBy(p => p.SortOrder).ToList();
        league.Picks = league.Picks.OrderBy(p => p.PickNumber).ToList();
        league.PointValues = league.PointValues.OrderBy(p => p.PokemonId).ToList();
        league.Matchups = league.Matchups.OrderBy(m => m.Week).ThenBy(m => m.Id).ToList();
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

    private static bool IsPlayerAtPointLimit(League league, string playerId)
    {
        var pointValues = league.PointValues.ToDictionary(pv => pv.PokemonId, pv => pv.Value);
        var pointTotal = league.Picks
            .Where(p => p.PlayerId == playerId)
            .Sum(p => pointValues.GetValueOrDefault(p.PokemonId, 0));

        return pointTotal >= league.PointLimit;
    }

    private static int FindNextEligiblePickNumber(League league, int startPickNumber)
    {
        var players = GetOrderedPlayers(league);
        var totalPicks = players.Count * league.Rounds;
        var pickNumber = startPickNumber;

        while (pickNumber < totalPicks)
        {
            var playerId = GetPlayerIdAtPick(league, pickNumber);
            if (!IsPlayerAtPointLimit(league, playerId)) break;
            pickNumber++;
        }

        return pickNumber;
    }

    private static void AdvanceToNextEligiblePick(League league)
    {
        var players = GetOrderedPlayers(league);
        var totalPicks = players.Count * league.Rounds;
        league.CurrentPickNumber = FindNextEligiblePickNumber(league, league.CurrentPickNumber);

        if (league.CurrentPickNumber >= totalPicks)
            league.DraftStatus = DraftStatus.Complete;
    }

    private static void ReindexPlayers(List<Player> players)
    {
        for (var i = 0; i < players.Count; i++)
            players[i].SortOrder = i;
    }

    private static void RandomizePlayerOrder(League league)
    {
        var players = league.Players.ToList();
        for (var i = players.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (players[i], players[j]) = (players[j], players[i]);
        }

        ReindexPlayers(players);
    }

    // ── Roster management ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public (bool success, string? error) DropPokemon(string leagueCode, string playerId, string pin, int pokemonId)
    {
        var league = LoadLeagueWithDraft(leagueCode);
        if (league is null) return (false, "League not found.");

        var player = league.Players.FirstOrDefault(p => p.Id == playerId);
        if (player is null || !VerifyPin(player, pin))
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
        var league = LoadLeagueWithDraft(leagueCode);
        if (league is null) return (false, "League not found.");

        var player = league.Players.FirstOrDefault(p => p.Id == playerId);
        if (player is null || !VerifyPin(player, pin))
            return (false, "Invalid player or PIN.");

        if (league.Picks.Any(p => p.PokemonId == pokemonId))
            return (false, "That Pokémon is already on a team.");

        var playerPicks = league.Picks.Where(p => p.PlayerId == playerId).ToList();
        if (playerPicks.Count >= league.Rounds)
            return (false, "Your roster is full.");

        var pointValues = league.PointValues.ToDictionary(pv => pv.PokemonId, pv => pv.Value);
        var currentPointTotal = playerPicks.Sum(p => pointValues.GetValueOrDefault(p.PokemonId, 0));
        var requestedPointValue = pointValues.GetValueOrDefault(pokemonId, 0);
        if (currentPointTotal + requestedPointValue > league.PointLimit)
        {
            var remainingPoints = Math.Max(0, league.PointLimit - currentPointTotal);
            return (false, $"That Pokémon costs {requestedPointValue} points, but you only have {remainingPoints} points remaining.");
        }

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
        var league = LoadLeagueTradesReadOnly(leagueCode);
        if (league is null) return [];
        return league.Trades.OrderByDescending(t => t.ProposedAt).Select(ToTradeResponse).ToList();
    }

    /// <inheritdoc/>
    public (TradeResponse? trade, string? error) ProposeTrade(
        string leagueCode, string initiatorId, string initiatorPin,
        string targetId, List<int> offering, List<int> requesting)
    {
        var league = LoadLeagueWithTrades(leagueCode);
        if (league is null) return (null, "League not found.");

        var initiator = league.Players.FirstOrDefault(p => p.Id == initiatorId);
        if (initiator is null || !VerifyPin(initiator, initiatorPin))
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
        var league = LoadLeagueWithTrades(leagueCode);
        if (league is null) return (false, "League not found.");

        var trade = league.Trades.FirstOrDefault(t => t.Id == tradeId);
        if (trade is null) return (false, "Trade not found.");

        if (trade.Status != TradeStatus.Pending)
            return (false, "Trade is no longer pending.");

        var player = league.Players.FirstOrDefault(p => p.Id == playerId);
        if (player is null || !VerifyPin(player, pin))
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

    private static bool VerifyPin(Player player, string input) =>
        BC.Verify(input, player.Pin);

    private static bool VerifyAdminPin(League league, string input) =>
        BC.Verify(input, league.AdminPin);

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
