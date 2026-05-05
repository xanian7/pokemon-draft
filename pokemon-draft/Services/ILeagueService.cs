using PokemonDraft.DTOs;
using PokemonDraft.Models;

namespace PokemonDraft.Services;

/// <summary>Manages all league operations including player management, drafting, roster changes, and trades.</summary>
public interface ILeagueService
{
    /// <summary>Creates a new draft league and its first player (the commissioner).</summary>
    /// <param name="name">Display name for the league.</param>
    /// <param name="commissionerName">Name of the commissioner player.</param>
    /// <param name="adminPin">Plain-text PIN used for admin/commissioner authentication.</param>
    /// <returns>The newly created <see cref="League"/>.</returns>
    League CreateLeague(string name, string commissionerName, string adminPin);

    /// <summary>Retrieves a league by its unique code.</summary>
    /// <param name="code">The league code (case-insensitive).</param>
    /// <returns>The matching <see cref="League"/>, or <see langword="null"/> if not found.</returns>
    League? GetLeague(string code);

    /// <summary>Updates mutable league configuration fields.</summary>
    /// <param name="code">The league code.</param>
    /// <param name="req">Fields to update; <see langword="null"/> values are ignored.</param>
    /// <returns><see langword="true"/> if updated; <see langword="false"/> if the league was not found.</returns>
    bool UpdateConfig(string code, UpdateLeagueConfigRequest req);

    /// <summary>Validates a player's PIN and returns session info on success.</summary>
    /// <param name="leagueCode">The league code.</param>
    /// <param name="pin">The plain-text PIN to check against the admin PIN or any player PIN.</param>
    /// <returns>A <see cref="JoinResponse"/> on success, or <see langword="null"/> if validation fails.</returns>
    JoinResponse? ValidatePin(string leagueCode, string pin);

    /// <summary>Self-registers a new player via invite link (no admin auth required).</summary>
    /// <param name="leagueCode">The league code.</param>
    /// <param name="name">Desired player name (must be unique).</param>
    /// <param name="pin">Plain-text PIN chosen by the player (must be unique within the league).</param>
    /// <returns>The created <see cref="Player"/> and a <see langword="null"/> error on success, or a <see langword="null"/> player and an error message on failure.</returns>
    (Player? player, string? error) RegisterPlayer(string leagueCode, string name, string pin);

    /// <summary>Adds a player to the league (admin-managed).</summary>
    /// <param name="leagueCode">The league code.</param>
    /// <param name="name">Player name.</param>
    /// <param name="pin">Plain-text PIN for the player.</param>
    /// <returns>The created <see cref="Player"/>, or <see langword="null"/> if the league was not found.</returns>
    Player? AddPlayer(string leagueCode, string name, string pin);

    /// <summary>Removes a player from the league and reorders remaining players.</summary>
    /// <param name="leagueCode">The league code.</param>
    /// <param name="playerId">ID of the player to remove.</param>
    /// <returns><see langword="true"/> if removed; <see langword="false"/> if league or player was not found.</returns>
    bool RemovePlayer(string leagueCode, string playerId);

    /// <summary>Moves a player from one draft-order position to another.</summary>
    /// <param name="leagueCode">The league code.</param>
    /// <param name="fromIndex">Current zero-based index of the player.</param>
    /// <param name="toIndex">Target zero-based index.</param>
    /// <returns><see langword="true"/> on success; <see langword="false"/> if the league or indices are invalid.</returns>
    bool MovePlayer(string leagueCode, int fromIndex, int toIndex);

    /// <summary>Sets point values for one or more Pokémon. Values ≤ 0 remove the entry.</summary>
    /// <param name="leagueCode">The league code.</param>
    /// <param name="values">Map of Pokémon ID to point value.</param>
    /// <returns><see langword="true"/> on success; <see langword="false"/> if the league was not found.</returns>
    bool SetPointValues(string leagueCode, Dictionary<int, int> values);

    /// <summary>Starts the draft, resetting any existing picks.</summary>
    /// <param name="leagueCode">The league code.</param>
    /// <returns><see langword="true"/> if started; <see langword="false"/> if the league was not found or has fewer than 2 players.</returns>
    bool StartDraft(string leagueCode);

    /// <summary>Resets the draft back to the Setup state and clears all picks.</summary>
    /// <param name="leagueCode">The league code.</param>
    /// <returns><see langword="true"/> on success; <see langword="false"/> if the league was not found.</returns>
    bool ResetDraft(string leagueCode);

    /// <summary>Records a draft pick for the current picker.</summary>
    /// <param name="leagueCode">The league code.</param>
    /// <param name="playerId">ID of the player making the pick.</param>
    /// <param name="pin">Plain-text PIN to authenticate the player.</param>
    /// <param name="pokemonId">ID of the Pokémon to draft.</param>
    /// <returns><see langword="true"/> and a <see langword="null"/> error on success; <see langword="false"/> and an error message on failure.</returns>
    (bool success, string? error) MakePick(string leagueCode, string playerId, string pin, int pokemonId);

    /// <summary>Drops a Pokémon from a player's roster (post-draft roster management).</summary>
    /// <param name="leagueCode">The league code.</param>
    /// <param name="playerId">ID of the player dropping the Pokémon.</param>
    /// <param name="pin">Plain-text PIN to authenticate the player.</param>
    /// <param name="pokemonId">ID of the Pokémon to drop.</param>
    /// <returns><see langword="true"/> and a <see langword="null"/> error on success; <see langword="false"/> and an error message on failure.</returns>
    (bool success, string? error) DropPokemon(string leagueCode, string playerId, string pin, int pokemonId);

    /// <summary>Adds a free-agent Pokémon to a player's roster (post-draft roster management).</summary>
    /// <param name="leagueCode">The league code.</param>
    /// <param name="playerId">ID of the player adding the Pokémon.</param>
    /// <param name="pin">Plain-text PIN to authenticate the player.</param>
    /// <param name="pokemonId">ID of the Pokémon to add.</param>
    /// <returns><see langword="true"/> and a <see langword="null"/> error on success; <see langword="false"/> and an error message on failure.</returns>
    (bool success, string? error) AddPokemon(string leagueCode, string playerId, string pin, int pokemonId);

    /// <summary>Returns all trades for a league ordered by most-recent first.</summary>
    /// <param name="leagueCode">The league code.</param>
    /// <returns>A list of <see cref="TradeResponse"/> objects, or an empty list if the league was not found.</returns>
    List<TradeResponse> GetTrades(string leagueCode);

    /// <summary>Proposes a trade between two players.</summary>
    /// <param name="leagueCode">The league code.</param>
    /// <param name="initiatorId">Player ID of the trade initiator.</param>
    /// <param name="initiatorPin">Plain-text PIN of the initiator.</param>
    /// <param name="targetId">Player ID of the trade target.</param>
    /// <param name="offering">Pokémon IDs the initiator is offering.</param>
    /// <param name="requesting">Pokémon IDs the initiator is requesting from the target.</param>
    /// <returns>The created <see cref="TradeResponse"/> and a <see langword="null"/> error on success; <see langword="null"/> trade and an error message on failure.</returns>
    (TradeResponse? trade, string? error) ProposeTrade(
        string leagueCode, string initiatorId, string initiatorPin,
        string targetId, List<int> offering, List<int> requesting);

    /// <summary>Accepts, rejects, or cancels a pending trade.</summary>
    /// <param name="leagueCode">The league code.</param>
    /// <param name="tradeId">ID of the trade to respond to.</param>
    /// <param name="playerId">Player ID responding to the trade.</param>
    /// <param name="pin">Plain-text PIN to authenticate the player.</param>
    /// <param name="response">The desired new status (<see cref="TradeStatus.Accepted"/>, <see cref="TradeStatus.Rejected"/>, or <see cref="TradeStatus.Cancelled"/>).</param>
    /// <returns><see langword="true"/> and a <see langword="null"/> error on success; <see langword="false"/> and an error message on failure.</returns>
    (bool success, string? error) RespondToTrade(string leagueCode, int tradeId, string playerId, string pin, TradeStatus response);

    /// <summary>Projects a <see cref="League"/> to a <see cref="LeagueResponse"/> suitable for sending to clients.</summary>
    /// <param name="league">The league to project.</param>
    /// <returns>A <see cref="LeagueResponse"/> representing the current league state.</returns>
    LeagueResponse ToResponse(League league);
}
