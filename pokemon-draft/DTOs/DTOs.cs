namespace PokemonDraft.DTOs;

// --- Requests ---

public record CreateLeagueRequest(string Name, string CommissionerName, string AdminPin);

public record UpdateLeagueConfigRequest(string? Name, int? PointLimit, int? Rounds, string? RegulationSet);

public record AddPlayerRequest(string Name, string Pin);

public record MovePlayerRequest(int FromIndex, int ToIndex);

public record SetPointValuesRequest(Dictionary<int, int> Values);

public record MakePickRequest(string PlayerId, string Pin, int PokemonId);

public record RegisterPlayerRequest(string Name, string Pin, string? TeamName, string? TeamImageUrl);

public record JoinRequest(string LeagueCode, string Pin);

public record UpdatePlayerProfileRequest(string PlayerId, string Pin, string? TeamName, string? TeamImageUrl);

public record ProposeTradeRequest(string InitiatorPlayerId, string InitiatorPin, string TargetPlayerId, List<int> OfferingPokemonIds, List<int> RequestingPokemonIds);

public record RespondTradeRequest(string PlayerId, string Pin);

public record RosterChangeRequest(string PlayerId, string Pin, int PokemonId);

public record ReportMatchupRequest(string PlayerId, string Pin, int Player1Wins, int Player2Wins);
public record EditMatchupRequest(string AdminPin, int Player1Wins, int Player2Wins);

// --- Responses ---

public record JoinResponse(string PlayerId, string PlayerName, bool IsAdmin, string LeagueCode, string TeamName, string TeamImageUrl, string LeagueName);

public record LeagueResponse(
    string Code,
    string Name,
    int PointLimit,
    int Rounds,
    string RegulationSet,
    List<PlayerResponse> Players,
    Dictionary<int, int> PointValues,
    DraftStateResponse Draft
);

public record PlayerResponse(string Id, string Name, string TeamName, string TeamImageUrl);

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
    int? Player1MatchPoints, int? Player2MatchPoints);

public record StandingRow(
    string PlayerId, string PlayerName, string TeamName, string TeamImageUrl,
    int Wins, int Losses, int MatchPoints, int GamesWon, int GamesLost);

public record WeekGroup(int Week, List<MatchupResponse> Matchups);

public record ScheduleResponse(List<WeekGroup> Weeks, List<StandingRow> Standings);
