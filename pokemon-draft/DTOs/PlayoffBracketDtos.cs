namespace PokemonDraft.DTOs;

public record BracketSlot(string Source, int? Seed);
public record BracketMatch(string Id, List<BracketSlot> Slots, int? Winner);
public record BracketRound(string Id, string Name, List<BracketMatch> Matches);
public record BracketConfiguration(List<BracketRound> PlayIn, List<BracketRound> Playoffs);
public record SaveBracketRequest(string AdminPin, Guid? Revision, BracketConfiguration Configuration);
public record BracketResponse(Guid? Revision, BracketConfiguration? Configuration);
