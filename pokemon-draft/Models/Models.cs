namespace PokemonDraft.Models;

public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? GoogleId { get; set; }
    public string? DiscordId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PictureUrl { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Player> Players { get; set; } = [];
}

public class League
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = "My Draft League";
    public string AdminPin { get; set; } = string.Empty;
    public string CommissionerPlayerId { get; set; } = string.Empty;
    public int PointLimit { get; set; } = 100;
    public int Rounds { get; set; } = 6;
    public int PlayoffSpots { get; set; } = 4;
    public string RegulationSet { get; set; } = "national";
    public int CurrentPickNumber { get; set; }
    public DraftStatus DraftStatus { get; set; } = DraftStatus.Setup;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Player> Players { get; set; } = [];
    public List<DraftPick> Picks { get; set; } = [];
    public List<PokemonPointValue> PointValues { get; set; } = [];
    public List<Trade> Trades { get; set; } = [];
    public List<Matchup> Matchups { get; set; } = [];
}

public class Player
{
    public string Id { get; set; } = string.Empty;
    public string LeagueCode { get; set; } = string.Empty;
    public League League { get; set; } = null!;
    public int SortOrder { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public string TeamImageUrl { get; set; } = string.Empty;
    public string Pin { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public AppUser? User { get; set; }
}

public class DraftPick
{
    public int Id { get; set; }
    public string LeagueCode { get; set; } = string.Empty;
    public League League { get; set; } = null!;
    public int PickNumber { get; set; }
    public int Round { get; set; }
    public string PlayerId { get; set; } = string.Empty;
    public int PokemonId { get; set; }
}

public class PokemonPointValue
{
    public int Id { get; set; }
    public string LeagueCode { get; set; } = string.Empty;
    public League League { get; set; } = null!;
    public int PokemonId { get; set; }
    public int Value { get; set; }
}

public class Trade
{
    public int Id { get; set; }
    public string LeagueCode { get; set; } = string.Empty;
    public League League { get; set; } = null!;
    public string InitiatorPlayerId { get; set; } = string.Empty;
    public string TargetPlayerId { get; set; } = string.Empty;
    public TradeStatus Status { get; set; } = TradeStatus.Pending;
    public DateTime ProposedAt { get; set; } = DateTime.UtcNow;
    public List<TradeItem> Items { get; set; } = [];
}

public class TradeItem
{
    public int Id { get; set; }
    public int TradeId { get; set; }
    public Trade Trade { get; set; } = null!;
    public string FromPlayerId { get; set; } = string.Empty;
    public int PokemonId { get; set; }
}

public class Matchup
{
    public int Id { get; set; }
    public string LeagueCode { get; set; } = string.Empty;
    public League League { get; set; } = null!;
    public int Week { get; set; }
    public string Player1Id { get; set; } = string.Empty;
    public string Player2Id { get; set; } = string.Empty;
    public int? Player1Wins { get; set; }
    public int? Player2Wins { get; set; }
    public string? ReplayUrl { get; set; }
    public string? ReportedByPlayerId { get; set; }
    public DateTime? ReportedAt { get; set; }
}

public enum DraftStatus
{
    Setup,
    Active,
    Complete
}

public enum TradeStatus
{
    Pending,
    Accepted,
    Rejected,
    Cancelled
}
