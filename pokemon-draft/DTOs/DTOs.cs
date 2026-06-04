namespace PokemonDraft.DTOs;

// --- Requests ---

public record CreateLeagueRequest(string Name, string CommissionerName, string? AdminPin, Guid? UserId = null);

public record UpdateLeagueConfigRequest(string? Name, int? PointLimit, int? Rounds, string? RegulationSet, int? PlayoffSpots = null);

public record AddPlayerRequest(string Name, string Pin);

public record MovePlayerRequest(int FromIndex, int ToIndex);

public record SetPointValuesRequest(Dictionary<int, int> Values);

public record MakePickRequest(string PlayerId, string Pin, int PokemonId);

public record RegisterPlayerRequest(string Name, string? Pin, string? TeamName, string? TeamImageUrl, Guid? UserId = null);

public record JoinRequest(string LeagueCode, string Pin);

public record UpdatePlayerProfileRequest(string PlayerId, string Pin, string? TeamName, string? TeamImageUrl);

public record ProposeTradeRequest(string InitiatorPlayerId, string InitiatorPin, string TargetPlayerId, List<int> OfferingPokemonIds, List<int> RequestingPokemonIds);

public record RespondTradeRequest(string PlayerId, string Pin);

public record RosterChangeRequest(string PlayerId, string Pin, int PokemonId);

public record ReportMatchupRequest(string PlayerId, string Pin, int Player1Wins, int Player2Wins, string? ReplayUrl = null, List<string>? ReplayUrls = null);
public record EditMatchupRequest(string AdminPin, int Player1Wins, int Player2Wins, string? ReplayUrl = null, List<string>? ReplayUrls = null);

// --- Auth ---

public record GoogleAuthRequest(string IdToken);
public record AuthUserResponse(Guid Id, string Email, string Name, string PictureUrl);
public record AuthTokenResponse(string Token, AuthUserResponse User);

public record MyLeagueResponse(string Code, string Name, string PlayerId, string PlayerName, string TeamName, string TeamImageUrl, bool IsCommissioner);
public record LinkPlayerRequest(string LeagueCode, string Pin);
public record EnterLeagueRequest(string LeagueCode);

// --- Responses ---

public record CreateLeagueResponse(string Code, string Name);
public record PlayerCreatedResponse(string Id, string Name);
public record PokemonResponse(int Id, int SpeciesId, string Name, string SpriteUrl, List<string> Types, int Bst);

public record PokemonDetailResponse(
    List<PokemonDetailStat> Stats,
    List<PokemonDetailAbility> Abilities,
    List<PokemonDetailMove> Moves
);
public record PokemonDetailStat(string Name, int BaseStat);
public record PokemonDetailAbility(string Name, bool IsHidden);
public record PokemonDetailMove(string Name, string Type, int? Power, int? Pp, string Category);

public record JoinResponse(string PlayerId, string PlayerName, bool IsAdmin, string LeagueCode, string TeamName, string TeamImageUrl, string LeagueName, string? SessionToken = null);

public record LeagueResponse(
    string Code,
    string Name,
    int PointLimit,
    int Rounds,
    int PlayoffSpots,
    string RegulationSet,
    List<PlayerResponse> Players,
    Dictionary<int, int> PointValues,
    DraftStateResponse Draft
);

public record PlayerResponse(string Id, string Name, string TeamName, string TeamImageUrl, string? DiscordId);

public record DraftStateResponse(
    string Status,
    int CurrentPickNumber,
    int TotalPicks,
    string? CurrentPickerId,
    string? CurrentPickerName,
    List<DraftPickResponse> Picks
);

public record DraftPickResponse(int PickNumber, int Round, string PlayerId, int PokemonId);

public record TradeResponse(
    int Id,
    string InitiatorPlayerId,
    string TargetPlayerId,
    string Status,
    DateTime ProposedAt,
    List<TradeItemResponse> Items
);

public record TradeItemResponse(string FromPlayerId, int PokemonId);

public record MatchupResponse(
    int Id, int Week,
    string Player1Id, string Player1Name, string Player1TeamName, string Player1TeamImageUrl,
    string Player2Id, string Player2Name, string Player2TeamName, string Player2TeamImageUrl,
    int? Player1Wins, int? Player2Wins,
    int? Player1MatchPoints, int? Player2MatchPoints,
    string? ReplayUrl,
    List<string> ReplayUrls);

public record StandingRow(
    string PlayerId, string PlayerName, string TeamName, string TeamImageUrl,
    int Wins, int Losses, int MatchPoints, int GamesWon, int GamesLost);

public record WeekGroup(int Week, List<MatchupResponse> Matchups);

public record ScheduleResponse(List<WeekGroup> Weeks, List<StandingRow> Standings);

public record PlayoffOutlookEntry(
    string PlayerId, string PlayerName, string TeamName, string TeamImageUrl,
    int Wins, int Losses, int MatchPoints, int GamesWon, int GamesLost,
    int RemainingMatchups, int MaxPossibleWins,
    int? MagicNumber,
    string Status
);
