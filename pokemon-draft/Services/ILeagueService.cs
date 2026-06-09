using PokemonDraft.DTOs;
using PokemonDraft.Models;

namespace PokemonDraft.Services;

/// <summary>Manages all league operations including player management, drafting, roster changes, and trades.</summary>
public interface ILeagueService
{
    // ── League ─────────────────────────────────────────────────────────────────

    /// <summary>Creates a new draft league and its first player (the commissioner).</summary>
    /// <returns>The response DTO and null error on success; null result and an error message on failure.</returns>
    (CreateLeagueResponse? result, string? error) CreateLeague(CreateLeagueRequest req);

    /// <summary>Retrieves a league by its code, projected to a client-safe DTO.</summary>
    /// <returns><see cref="LeagueResponse"/> on success, or <see langword="null"/> if not found.</returns>
    LeagueResponse? GetLeagueResponse(string code);

    /// <summary>Updates mutable league configuration fields.</summary>
    /// <returns>Success flag and optional error. A null error with false success means not found (404).</returns>
    (bool success, string? error) UpdateConfig(string code, UpdateLeagueConfigRequest req);

    // ── Auth ───────────────────────────────────────────────────────────────────

    /// <summary>Validates a player's PIN and returns session info on success.</summary>
    JoinResponse? ValidatePin(string leagueCode, string pin);

    // ── Players ────────────────────────────────────────────────────────────────

    /// <summary>Self-registers a new player via invite link (no admin auth required).</summary>
    (PlayerCreatedResponse? result, string? error) RegisterPlayer(string leagueCode, RegisterPlayerRequest req);

    /// <summary>Adds a player to the league (admin-managed).</summary>
    (PlayerCreatedResponse? result, string? error) AddPlayer(string leagueCode, AddPlayerRequest req);

    /// <summary>Removes a player from the league and reorders remaining players.</summary>
    /// <returns>Success flag and optional error. A null error with false success means not found (404).</returns>
    (bool success, string? error) RemovePlayer(string leagueCode, string playerId);

    /// <summary>Moves a player from one draft-order position to another.</summary>
    /// <returns>Success flag and optional error. A null error with false success means not found (404).</returns>
    (bool success, string? error) MovePlayer(string leagueCode, int fromIndex, int toIndex);

    // ── Point values ───────────────────────────────────────────────────────────

    /// <summary>Sets point values for one or more Pokémon. Values ≤ 0 remove the entry.</summary>
    /// <returns>Success flag and optional error. A null error with false success means not found (404).</returns>
    (bool success, string? error) SetPointValues(string leagueCode, Dictionary<int, int> values);

    // ── Draft ──────────────────────────────────────────────────────────────────

    /// <summary>Starts the draft, resetting any existing picks.</summary>
    /// <returns>Success flag and optional error. A null error with false success means not found (404).</returns>
    (bool success, string? error) StartDraft(string leagueCode);

    /// <summary>Resets the draft back to the Setup state and clears all picks.</summary>
    /// <returns>Success flag and optional error. A null error with false success means not found (404).</returns>
    (bool success, string? error) ResetDraft(string leagueCode);

    /// <summary>Records a draft pick for the current picker.</summary>
    (bool success, string? error, bool draftCompleted) MakePick(string leagueCode, string playerId, string pin, int pokemonId);

    // ── Schedule ───────────────────────────────────────────────────────────────

    /// <summary>Generates round-robin schedule. Called automatically when draft completes.</summary>
    void GenerateSchedule(string leagueCode);

    /// <summary>Returns full schedule and current standings.</summary>
    ScheduleResponse? GetSchedule(string leagueCode);

    /// <summary>Returns per-team playoff outlook (clinched/in-contention/eliminated, magic numbers).</summary>
    List<PlayoffOutlookEntry>? GetPlayoffOutlook(string leagueCode);

    /// <summary>Reports match result. Only a player in the matchup can report.</summary>
    (bool success, string? error) ReportMatchup(string leagueCode, int matchupId, string playerId, string pin, int player1Wins, int player2Wins, string? replayUrl, List<string>? replayUrls);

    /// <summary>Commissioner override — edits any matchup score using the admin PIN.</summary>
    (bool success, string? error) EditMatchup(string leagueCode, int matchupId, string adminPin, int player1Wins, int player2Wins, string? replayUrl, List<string>? replayUrls);

    // ── Roster ─────────────────────────────────────────────────────────────────

    /// <summary>Drops a Pokémon from a player's roster (post-draft roster management).</summary>
    (bool success, string? error) DropPokemon(string leagueCode, string playerId, string pin, int pokemonId);

    /// <summary>Adds a free-agent Pokémon to a player's roster (post-draft roster management).</summary>
    (bool success, string? error) AddPokemon(string leagueCode, string playerId, string pin, int pokemonId);

    /// <summary>Returns all add/drop transactions for a league ordered by most-recent first.</summary>
    List<RosterTransactionResponse> GetRosterTransactions(string leagueCode);

    // ── Trades ─────────────────────────────────────────────────────────────────

    /// <summary>Returns all trades for a league ordered by most-recent first.</summary>
    List<TradeResponse> GetTrades(string leagueCode);

    /// <summary>Proposes a trade between two players.</summary>
    (TradeResponse? trade, string? error) ProposeTrade(
        string leagueCode, string initiatorId, string initiatorPin,
        string targetId, List<int> offering, List<int> requesting);

    /// <summary>Accepts, rejects, or cancels a pending trade.</summary>
    (bool success, string? error) RespondToTrade(string leagueCode, int tradeId, string playerId, string pin, TradeStatus response);

    // ── Profile ────────────────────────────────────────────────────────────────

    /// <summary>Updates a player's team name and/or avatar image URL.</summary>
    (bool success, string? error) UpdatePlayerProfile(string leagueCode, string playerId, string pin, string? teamName, string? teamImageUrl);
}
