using PokemonDraft.DTOs;

namespace PokemonDraft.Services;

/// <summary>Sources may only reference matches in earlier rounds.</summary>
public static class BracketValidator
{
    public static string? Validate(BracketConfiguration config, HashSet<string> players)
    {
        if (config.PlayIn is null || config.Playoffs is null || config.PlayIn.Count > 8 ||
            config.Playoffs.Count is < 1 or > 8)
            return "Use 1–8 playoff rounds and up to 8 play-in rounds.";
        var ids = new HashSet<string>();
        var results = new Dictionary<string, (string? Winner, string? Loser)>();
        foreach (var round in config.PlayIn.Concat(config.Playoffs))
        {
            if (round is null || string.IsNullOrWhiteSpace(round.Id) || round.Id.Length > 80 ||
                !ids.Add(round.Id) || string.IsNullOrWhiteSpace(round.Name) || round.Name.Length > 80 ||
                round.Matches is null || round.Matches.Count is < 1 or > 32)
                return "Each round needs a unique ID, a name, and 1–32 matches.";
            var roundResults = new Dictionary<string, (string? Winner, string? Loser)>();
            var usedSources = new HashSet<string>();
            var usedPlayers = new HashSet<string>();
            var usedSeeds = new HashSet<int>();
            foreach (var match in round.Matches)
            {
                if (match is null || string.IsNullOrWhiteSpace(match.Id) || match.Id.Length > 80 ||
                    !ids.Add(match.Id) || match.Slots is null || match.Slots.Count != 2 ||
                    match.Winner is < 0 or > 1)
                    return "Each match needs a unique ID and exactly two slots.";
                var resolved = new string?[2];
                for (var i = 0; i < 2; i++)
                {
                    var slot = match.Slots[i];
                    if (slot is null || slot.Source is null || slot.Source.Length > 100 ||
                        slot.Seed is < 1 or > 64 || (slot.Seed is int seed && !usedSeeds.Add(seed)))
                        return "Seeds must be unique within a round and between 1 and 64.";
                    var source = slot.Source;
                    if (source is "" or "bye") continue;
                    if (!usedSources.Add(source)) return "A participant or match outcome can only be used once per round.";
                    if (source.StartsWith("player:"))
                    {
                        resolved[i] = source[7..];
                        if (!players.Contains(resolved[i]!)) return "A selected player does not belong to this league.";
                    }
                    else
                    {
                        var parts = source.Split(':', 2);
                        if (parts.Length != 2 || parts[0] is not ("winner" or "loser") ||
                            !results.TryGetValue(parts[1], out var result))
                            return "Match sources must refer to a winner or loser from an earlier round.";
                        resolved[i] = parts[0] == "winner" ? result.Winner : result.Loser;
                    }
                    if (resolved[i] is string player && !usedPlayers.Add(player))
                        return "A player cannot appear twice in the same round.";
                }
                int? winner = match.Winner;
                if (winner is int side && (resolved[side] is null ||
                    (resolved[1 - side] is null && match.Slots[1 - side].Source != "bye")))
                    return "Choose a winner only after both participants are known (or one has a bye).";
                if (winner is null)
                {
                    if (resolved[0] is not null && match.Slots[1].Source == "bye") winner = 0;
                    if (resolved[1] is not null && match.Slots[0].Source == "bye") winner = 1;
                }
                roundResults[match.Id] = winner is int w ? (resolved[w], resolved[1 - w]) : (null, null);
            }
            foreach (var result in roundResults) results.Add(result.Key, result.Value);
        }
        return null;
    }
}
