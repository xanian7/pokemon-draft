namespace PokemonDraftApi.DTOs;

// --- Requests ---

public record CreateLeagueRequest(string Name, string CommissionerName, string AdminPin);

public record UpdateLeagueConfigRequest(string? Name, int? PointLimit, int? Rounds, string? RegulationSet);

public record AddPlayerRequest(string Name, string Pin);

public record MovePlayerRequest(int FromIndex, int ToIndex);

public record SetPointValuesRequest(Dictionary<int, int> Values);

public record MakePickRequest(string PlayerId, string Pin, int PokemonId);

public record RegisterPlayerRequest(string Name, string Pin);

public record JoinRequest(string LeagueCode, string Pin);

public record ProposeTradeRequest(string InitiatorPlayerId, string InitiatorPin, string TargetPlayerId, List<int> OfferingPokemonIds, List<int> RequestingPokemonIds);

public record RespondTradeRequest(string PlayerId, string Pin);

public record RosterChangeRequest(string PlayerId, string Pin, int PokemonId);

// --- Responses ---

public record JoinResponse(string PlayerId, string PlayerName, bool IsAdmin, string LeagueCode);

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

public record PlayerResponse(string Id, string Name);

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
