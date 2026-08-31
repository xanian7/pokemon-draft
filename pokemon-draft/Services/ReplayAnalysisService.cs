using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PokemonDraft.Data;
using PokemonDraft.DTOs;
using PokemonDraft.Models;

namespace PokemonDraft.Services;

public interface IReplayAnalysisService
{
    Task AnalyzeMatchupAsync(string leagueCode, int matchupId, CancellationToken cancellationToken = default);
    Task AnalyzeMissingAsync(string leagueCode, CancellationToken cancellationToken = default);
    Task<ReplayStatsResponse?> GetStatsAsync(string leagueCode, CancellationToken cancellationToken = default);
}

public sealed class ReplayAnalysisService(
    DraftDbContext db,
    HttpClient http,
    ILogger<ReplayAnalysisService> logger) : IReplayAnalysisService
{
    private const int MaxReplayBytes = 2_000_000;
    private const int CurrentAnalysisVersion = 4;

    public async Task AnalyzeMissingAsync(string leagueCode, CancellationToken cancellationToken = default)
    {
        var normalizedCode = leagueCode.Trim().ToUpperInvariant();
        var matchupIds = await db.Matchups.AsNoTracking()
            .Where(m => m.LeagueCode == normalizedCode && m.ReplayUrl != null &&
                        (!db.ReplayGames.Any(g => g.MatchupId == m.Id) ||
                         db.ReplayGames.Any(g => g.MatchupId == m.Id &&
                                                   g.AnalysisVersion < CurrentAnalysisVersion)))
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);
        foreach (var matchupId in matchupIds)
            await AnalyzeMatchupAsync(normalizedCode, matchupId, cancellationToken);
    }

    public async Task AnalyzeMatchupAsync(
        string leagueCode, int matchupId, CancellationToken cancellationToken = default)
    {
        var normalizedCode = leagueCode.Trim().ToUpperInvariant();
        var matchup = await db.Matchups
            .Include(m => m.League).ThenInclude(l => l.Players)
            .Include(m => m.League).ThenInclude(l => l.Picks)
            .FirstOrDefaultAsync(m => m.Id == matchupId && m.LeagueCode == normalizedCode, cancellationToken);
        if (matchup is null) return;

        var urls = DeserializeReplayUrls(matchup.ReplayUrl);
        var oldGames = await db.ReplayGames
            .Where(g => g.MatchupId == matchupId)
            .ToListAsync(cancellationToken);
        db.ReplayGames.RemoveRange(oldGames);

        if (urls.Count == 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var cache = await db.PokemonCache.AsNoTracking().ToListAsync(cancellationToken);
        var cacheLookup = cache
            .GroupBy(p => NormalizeName(p.Name))
            .ToDictionary(g => g.Key, g => g.First());
        var results = await Task.WhenAll(urls.Select((url, index) =>
            DownloadAndParseAsync(url, index + 1, cancellationToken)));

        foreach (var result in results.OrderBy(r => r.GameNumber))
        {
            var game = new ReplayGame
            {
                MatchupId = matchup.Id,
                GameNumber = result.GameNumber,
                ReplayUrl = result.ReplayUrl,
                Status = result.Error is null ? "Complete" : "Failed",
                Error = result.Error,
                ShowdownPlayer1 = result.Player1,
                ShowdownPlayer2 = result.Player2,
                WinnerName = result.Winner,
                AnalyzedAt = DateTime.UtcNow,
                AnalysisVersion = CurrentAnalysisVersion,
            };

            if (result.Error is null)
            {
                foreach (var mon in result.Pokemon)
                    mon.PokemonId = ResolvePokemonId(mon.Name, cacheLookup, cache);

                var (p1PlayerId, p2PlayerId) = AssignPlayers(matchup, result);
                game.PokemonStats = result.Pokemon.Select(mon => new ReplayPokemonStat
                {
                    Side = mon.Side,
                    PlayerId = mon.Side == "p1" ? p1PlayerId : p2PlayerId,
                    PokemonId = mon.PokemonId,
                    PokemonName = mon.Name,
                    Kos = mon.Kos,
                    Deaths = mon.Deaths,
                    MovesJson = JsonSerializer.Serialize(mon.Moves.Values.OrderBy(move => move)),
                    MovesAreComplete = mon.MovesAreComplete,
                    HeldItem = mon.HeldItem,
                    Ability = mon.Ability,
                    Nature = mon.Nature,
                }).ToList();
            }

            db.ReplayGames.Add(game);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReplayStatsResponse?> GetStatsAsync(
        string leagueCode, CancellationToken cancellationToken = default)
    {
        var normalizedCode = leagueCode.Trim().ToUpperInvariant();
        if (!await db.Leagues.AsNoTracking().AnyAsync(l => l.Code == normalizedCode, cancellationToken))
            return null;

        var players = await db.Players.AsNoTracking()
            .Where(p => p.LeagueCode == normalizedCode)
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var games = await db.ReplayGames.AsNoTracking()
            .Include(g => g.PokemonStats)
            .Include(g => g.Matchup)
            .Where(g => g.Matchup.LeagueCode == normalizedCode)
            .OrderByDescending(g => g.Matchup.Week)
            .ThenBy(g => g.MatchupId)
            .ThenBy(g => g.GameNumber)
            .ToListAsync(cancellationToken);

        string TeamName(string playerId) =>
            players.TryGetValue(playerId, out var player) ? player.TeamName : string.Empty;

        var gameResponses = games.Select(g => new ReplayGameResponse(
            g.Id, g.MatchupId, g.Matchup.Week, g.GameNumber, g.ReplayUrl,
            g.Status, g.Error, g.ShowdownPlayer1, g.ShowdownPlayer2, g.WinnerName,
            g.Matchup.Player1Id, TeamName(g.Matchup.Player1Id),
            g.Matchup.Player2Id, TeamName(g.Matchup.Player2Id),
            g.AnalyzedAt,
            g.PokemonStats.OrderBy(s => s.Side).ThenByDescending(s => s.Kos)
                .Select(s => new ReplayPokemonStatResponse(
                    s.Side, s.PlayerId, s.PokemonId, s.PokemonName, s.Kos, s.Deaths,
                    DeserializeMoves(s.MovesJson), s.MovesAreComplete,
                    string.IsNullOrWhiteSpace(s.HeldItem) ? null : s.HeldItem,
                    string.IsNullOrWhiteSpace(s.Ability) ? null : s.Ability,
                    string.IsNullOrWhiteSpace(s.Nature) ? null : s.Nature))
                .ToList())).ToList();

        var totals = games.SelectMany(g => g.PokemonStats)
            .GroupBy(s => new { s.PlayerId, s.PokemonId, Name = NormalizeName(s.PokemonName) })
            .Select(group =>
            {
                var player = group.Key.PlayerId is not null && players.TryGetValue(group.Key.PlayerId, out var found)
                    ? found
                    : null;
                return new PokemonReplayTotalResponse(
                    group.Key.PlayerId,
                    player?.Name ?? "Unmatched player",
                    player?.TeamName ?? string.Empty,
                    group.Key.PokemonId,
                    group.First().PokemonName,
                    group.Count(),
                    group.Sum(s => s.Kos),
                    group.Sum(s => s.Deaths));
            })
            .OrderByDescending(s => s.Kos)
            .ThenBy(s => s.Deaths)
            .ThenBy(s => s.PokemonName)
            .ToList();

        return new ReplayStatsResponse(gameResponses, totals);
    }

    private async Task<ParsedReplay> DownloadAndParseAsync(
        string replayUrl, int gameNumber, CancellationToken cancellationToken)
    {
        if (!TryGetReplayLogUrl(replayUrl, out var logUrl, out var validationError))
            return ParsedReplay.Failure(gameNumber, replayUrl, validationError!);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await http.GetAsync(logUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return ParsedReplay.Failure(gameNumber, replayUrl, $"Showdown returned HTTP {(int)response.StatusCode}.");
            if (response.Content.Headers.ContentLength > MaxReplayBytes)
                return ParsedReplay.Failure(gameNumber, replayUrl, "Replay log is too large to analyze.");

            var log = await response.Content.ReadAsStringAsync(timeout.Token);
            if (log.Length > MaxReplayBytes)
                return ParsedReplay.Failure(gameNumber, replayUrl, "Replay log is too large to analyze.");
            return ShowdownReplayParser.Parse(log, gameNumber, replayUrl);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ParsedReplay.Failure(gameNumber, replayUrl, "Timed out downloading the replay from Showdown.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not analyze replay {ReplayUrl}", replayUrl);
            return ParsedReplay.Failure(gameNumber, replayUrl, "Could not download or parse this replay.");
        }
    }

    private static (string p1, string p2) AssignPlayers(Matchup matchup, ParsedReplay replay)
    {
        var player1Roster = matchup.League.Picks
            .Where(p => p.PlayerId == matchup.Player1Id).Select(p => p.PokemonId).ToHashSet();
        var player2Roster = matchup.League.Picks
            .Where(p => p.PlayerId == matchup.Player2Id).Select(p => p.PokemonId).ToHashSet();
        var side1 = replay.Pokemon.Where(p => p.Side == "p1" && p.PokemonId.HasValue)
            .Select(p => p.PokemonId!.Value).ToHashSet();
        var side2 = replay.Pokemon.Where(p => p.Side == "p2" && p.PokemonId.HasValue)
            .Select(p => p.PokemonId!.Value).ToHashSet();

        var normal = side1.Count(player1Roster.Contains) + side2.Count(player2Roster.Contains);
        var swapped = side1.Count(player2Roster.Contains) + side2.Count(player1Roster.Contains);
        if (swapped > normal) return (matchup.Player2Id, matchup.Player1Id);
        if (normal > swapped) return (matchup.Player1Id, matchup.Player2Id);

        var player1 = matchup.League.Players.FirstOrDefault(p => p.Id == matchup.Player1Id);
        var showdownP1 = NormalizeName(replay.Player1);
        if (player1 is not null &&
            (showdownP1 == NormalizeName(player1.Name) || showdownP1 == NormalizeName(player1.TeamName)))
            return (matchup.Player1Id, matchup.Player2Id);
        return (matchup.Player1Id, matchup.Player2Id);
    }

    private static int? ResolvePokemonId(
        string name, Dictionary<string, PokemonCache> lookup, List<PokemonCache> all)
    {
        var normalized = NormalizeName(name);
        if (lookup.TryGetValue(normalized, out var exact)) return exact.Id;
        var close = all.FirstOrDefault(p =>
        {
            var candidate = NormalizeName(p.Name);
            return candidate.StartsWith(normalized, StringComparison.Ordinal) ||
                   normalized.StartsWith(candidate, StringComparison.Ordinal);
        });
        return close?.Id;
    }

    private static bool TryGetReplayLogUrl(string value, out Uri? logUrl, out string? error)
    {
        logUrl = null;
        error = null;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "replay.pokemonshowdown.com", StringComparison.OrdinalIgnoreCase))
        {
            error = "Replay links must use https://replay.pokemonshowdown.com/.";
            return false;
        }

        var replayId = uri.AbsolutePath.Trim('/');
        if (replayId.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) replayId = replayId[..^4];
        if (replayId.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) replayId = replayId[..^5];
        if (string.IsNullOrWhiteSpace(replayId) || replayId.Contains('/'))
        {
            error = "Replay link does not contain a valid Showdown replay ID.";
            return false;
        }

        logUrl = new Uri($"https://replay.pokemonshowdown.com/{replayId}.log");
        return true;
    }

    private static List<string> DeserializeReplayUrls(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        if (!value.TrimStart().StartsWith('[')) return [value.Trim()];
        try { return JsonSerializer.Deserialize<List<string>>(value) ?? []; }
        catch { return [value.Trim()]; }
    }

    private static List<string> DeserializeMoves(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(value) ?? []; }
        catch { return []; }
    }

    internal static string NormalizeName(string value) =>
        Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]", string.Empty);
}

internal sealed record ParsedReplay(
    int GameNumber, string ReplayUrl, string Player1, string Player2, string Winner,
    List<ParsedPokemon> Pokemon, string? Error)
{
    public static ParsedReplay Failure(int gameNumber, string url, string error) =>
        new(gameNumber, url, string.Empty, string.Empty, string.Empty, [], error);
}

internal sealed class ParsedPokemon
{
    public required string Side { get; init; }
    public required string Name { get; set; }
    public string? Nickname { get; set; }
    public int? PokemonId { get; set; }
    public int Kos { get; set; }
    public int Deaths { get; set; }
    public Dictionary<string, string> Moves { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool MovesAreComplete { get; set; }
    public string HeldItem { get; set; } = string.Empty;
    public string Ability { get; set; } = string.Empty;
    public string Nature { get; set; } = string.Empty;
}

internal static class ShowdownReplayParser
{
    private static readonly HashSet<string> Hazards =
        ["stealthrock", "spikes", "toxicspikes", "gmaxsteelsurge", "gmaxstonesurge"];

    public static ParsedReplay Parse(string log, int gameNumber, string replayUrl)
    {
        var players = new Dictionary<string, string>();
        var pokemon = new List<ParsedPokemon>();
        var hazards = new Dictionary<string, ParsedPokemon>();
        var statuses = new Dictionary<string, ParsedPokemon>();
        var leechSeed = new Dictionary<string, ParsedPokemon>();
        var pendingKillers = new Dictionary<string, ParsedPokemon>();
        ParsedPokemon? currentActor = null;
        ParsedPokemon? weatherSetter = null;
        var winner = string.Empty;

        foreach (var rawLine in log.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.TrimEnd('\r');
            if (!line.StartsWith('|')) continue;
            var parts = line.Split('|');
            if (parts.Length < 2) continue;
            var command = parts[1];

            if ((command == "-damage" || command == "-heal") &&
                parts.Length > 4 && parts[4].StartsWith("[from] item: ", StringComparison.Ordinal))
            {
                var itemOwner = ExplicitSource(parts, pokemon) ?? FindMon(parts[2], pokemon);
                SetRevealedItem(itemOwner, parts[4][13..]);
            }

            var abilitySource = parts.FirstOrDefault(part =>
                part.StartsWith("[from] ability: ", StringComparison.Ordinal));
            if (abilitySource is not null)
            {
                var abilityOwner = ExplicitSource(parts, pokemon) ??
                    (parts.Length > 2 ? FindMon(parts[2], pokemon) : null);
                SetRevealedAbility(abilityOwner, abilitySource[16..]);
            }

            switch (command)
            {
                case "player" when parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]):
                    players[parts[2]] = parts[3];
                    break;
                case "poke" when parts.Length > 3:
                    var previewSpecies = Species(parts[3]);
                    if (!pokemon.Any(mon => mon.Side == parts[2] && Normalize(mon.Name) == Normalize(previewSpecies)))
                        pokemon.Add(new ParsedPokemon { Side = parts[2], Name = previewSpecies });
                    break;
                case "showteam" when parts.Length > 3:
                    ApplyOpenTeamSheet(parts[2], string.Join('|', parts.Skip(3)), pokemon);
                    break;
                case "switch":
                case "drag":
                case "replace":
                    if (parts.Length > 3) EnsureMon(parts[2], parts[3], pokemon);
                    break;
                case "detailschange":
                case "formechange":
                    if (parts.Length > 3)
                    {
                        var changed = FindMon(parts[2], pokemon);
                        if (changed is not null) changed.Name = Species(parts[3]);
                    }
                    break;
                case "move" when parts.Length > 3:
                    currentActor = FindMon(parts[2], pokemon);
                    AddMove(currentActor, parts[3], preferDisplayName: true);
                    break;
                case "-item" when parts.Length > 3:
                case "-enditem" when parts.Length > 3:
                    SetRevealedItem(FindMon(parts[2], pokemon), parts[3]);
                    break;
                case "-activate" when parts.Length > 3 && parts[3].StartsWith("item: ", StringComparison.Ordinal):
                    SetRevealedItem(FindMon(parts[2], pokemon), parts[3][6..]);
                    break;
                case "-ability" when parts.Length > 3:
                case "-endability" when parts.Length > 3:
                    SetRevealedAbility(FindMon(parts[2], pokemon), parts[3]);
                    break;
                case "-activate" when parts.Length > 3 && parts[3].StartsWith("ability: ", StringComparison.Ordinal):
                    SetRevealedAbility(FindMon(parts[2], pokemon), parts[3][9..]);
                    break;
                case "-sidestart" when parts.Length > 3:
                    var hazard = Normalize(parts[3].Replace("move: ", string.Empty));
                    if (currentActor is not null && Hazards.Contains(hazard))
                        hazards[$"{Side(parts[2])}:{hazard}"] = currentActor;
                    break;
                case "-status" when parts.Length > 3:
                    var statusTarget = FindMon(parts[2], pokemon);
                    var statusSetter = ExplicitSource(parts, pokemon) ?? currentActor;
                    if (statusTarget is not null && statusSetter is not null && statusTarget.Side != statusSetter.Side)
                        statuses[$"{MonKey(parts[2])}:{Normalize(parts[3])}"] = statusSetter;
                    break;
                case "-start" when parts.Length > 3 && Normalize(parts[3]).Contains("leechseed"):
                    var seeded = FindMon(parts[2], pokemon);
                    if (seeded is not null && currentActor is not null && seeded.Side != currentActor.Side)
                        leechSeed[MonKey(parts[2])] = currentActor;
                    break;
                case "-weather" when parts.Length > 2 && !parts.Contains("[upkeep]"):
                    weatherSetter = ExplicitSource(parts, pokemon) ?? currentActor;
                    break;
                case "-damage" when parts.Length > 3 && parts[3].Contains("fnt", StringComparison.OrdinalIgnoreCase):
                    var fainted = FindMon(parts[2], pokemon);
                    if (fainted is null) break;
                    var source = string.Join('|', parts.Skip(4));
                    var killer = ExplicitSource(parts, pokemon);
                    var normalizedSource = Normalize(source);
                    if (killer is null)
                    {
                        var hazardName = Hazards.FirstOrDefault(normalizedSource.Contains);
                        if (hazardName is not null)
                            hazards.TryGetValue($"{fainted.Side}:{hazardName}", out killer);
                        else if (normalizedSource.Contains("leechseed"))
                            leechSeed.TryGetValue(MonKey(parts[2]), out killer);
                        else if (normalizedSource.Contains("psn") || normalizedSource.Contains("tox"))
                        {
                            if (!statuses.TryGetValue($"{MonKey(parts[2])}:tox", out killer))
                                statuses.TryGetValue($"{MonKey(parts[2])}:psn", out killer);
                        }
                        else if (normalizedSource.Contains("brn"))
                            statuses.TryGetValue($"{MonKey(parts[2])}:brn", out killer);
                        else if (normalizedSource.Contains("sandstorm") || normalizedSource.Contains("hail"))
                            killer = weatherSetter;
                        else if (string.IsNullOrEmpty(normalizedSource))
                            killer = currentActor;
                    }
                    if (killer is not null && killer.Side != fainted.Side)
                        pendingKillers[MonKey(parts[2])] = killer;
                    break;
                case "faint" when parts.Length > 2:
                    var dead = FindMon(parts[2], pokemon);
                    if (dead is null || dead.Deaths > 0) break;
                    dead.Deaths = 1;
                    if (pendingKillers.Remove(MonKey(parts[2]), out var credited) && credited.Side != dead.Side)
                        credited.Kos++;
                    break;
                case "win" when parts.Length > 2:
                    winner = parts[2];
                    break;
                case "turn":
                case "upkeep":
                    currentActor = null;
                    break;
            }
        }

        var player1 = players.GetValueOrDefault("p1", string.Empty);
        var player2 = players.GetValueOrDefault("p2", string.Empty);
        if (string.IsNullOrWhiteSpace(player1) || string.IsNullOrWhiteSpace(player2) || pokemon.Count == 0)
            return ParsedReplay.Failure(gameNumber, replayUrl, "This does not look like a complete two-player Showdown replay.");

        return new ParsedReplay(gameNumber, replayUrl, player1, player2, winner, pokemon, null);
    }

    private static ParsedPokemon EnsureMon(string ident, string details, List<ParsedPokemon> pokemon)
    {
        var existing = FindMon(ident, pokemon);
        if (existing is not null) return existing;
        var side = Side(ident);
        var species = Species(details);
        var nickname = Nickname(ident);
        var preview = pokemon.FirstOrDefault(p => p.Side == side && p.Nickname is null &&
            Normalize(p.Name) == Normalize(species));
        if (preview is not null)
        {
            preview.Nickname = nickname;
            return preview;
        }
        var created = new ParsedPokemon { Side = side, Name = species, Nickname = nickname };
        pokemon.Add(created);
        return created;
    }

    private static ParsedPokemon? FindMon(string ident, List<ParsedPokemon> pokemon)
    {
        var side = Side(ident);
        var nickname = Normalize(Nickname(ident));
        return pokemon.LastOrDefault(p => p.Side == side && p.Nickname is not null && Normalize(p.Nickname) == nickname);
    }

    private static ParsedPokemon? ExplicitSource(string[] parts, List<ParsedPokemon> pokemon)
    {
        var source = parts.FirstOrDefault(p => p.StartsWith("[of] ", StringComparison.Ordinal));
        return source is null ? null : FindMon(source[5..], pokemon);
    }

    private static void SetRevealedItem(ParsedPokemon? pokemon, string item)
    {
        if (pokemon is not null && string.IsNullOrWhiteSpace(pokemon.HeldItem))
            pokemon.HeldItem = item.Trim();
    }

    private static void SetRevealedAbility(ParsedPokemon? pokemon, string ability)
    {
        if (pokemon is not null && string.IsNullOrWhiteSpace(pokemon.Ability))
            pokemon.Ability = ability.Trim();
    }

    private static void ApplyOpenTeamSheet(string side, string packedTeam, List<ParsedPokemon> pokemon)
    {
        foreach (var packedSet in packedTeam.Split(']', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = packedSet.Split('|');
            if (fields.Length < 6) continue;
            var nickname = fields[0];
            var species = string.IsNullOrWhiteSpace(fields[1]) ? nickname : UnpackName(fields[1]);
            var match = pokemon.FirstOrDefault(mon => mon.Side == side &&
                (Normalize(mon.Name) == Normalize(species) ||
                 (!string.IsNullOrWhiteSpace(mon.Nickname) && Normalize(mon.Nickname) == Normalize(nickname))));
            if (match is null)
            {
                match = new ParsedPokemon { Side = side, Name = species, Nickname = nickname };
                pokemon.Add(match);
            }

            if (!string.IsNullOrWhiteSpace(fields[2])) match.HeldItem = UnpackName(fields[2]);
            if (!string.IsNullOrWhiteSpace(fields[3])) match.Ability = UnpackName(fields[3]);
            foreach (var move in fields[4].Split(',', StringSplitOptions.RemoveEmptyEntries))
                AddMove(match, UnpackName(move), preferDisplayName: false);
            match.MovesAreComplete = !string.IsNullOrWhiteSpace(fields[4]);
            if (!string.IsNullOrWhiteSpace(fields[5])) match.Nature = UnpackName(fields[5]);
        }
    }

    private static void AddMove(ParsedPokemon? pokemon, string move, bool preferDisplayName)
    {
        if (pokemon is null || string.IsNullOrWhiteSpace(move)) return;
        var key = Normalize(move);
        if (preferDisplayName || !pokemon.Moves.ContainsKey(key)) pokemon.Moves[key] = move.Trim();
    }

    private static string UnpackName(string value) =>
        Regex.Replace(Regex.Replace(value, "([0-9]+)", " $1 "), "([A-Z])", " $1")
            .Replace("  ", " ").Trim();

    private static string Side(string ident) => ident.Length >= 2 ? ident[..2] : ident;
    private static string Nickname(string ident)
    {
        var separator = ident.IndexOf(": ", StringComparison.Ordinal);
        return separator >= 0 ? ident[(separator + 2)..] : ident;
    }
    private static string MonKey(string ident) => $"{Side(ident)}:{Normalize(Nickname(ident))}";
    private static string Species(string details) => details.Split(',', 2)[0].Trim();
    private static string Normalize(string value) => ReplayAnalysisService.NormalizeName(value);
}
