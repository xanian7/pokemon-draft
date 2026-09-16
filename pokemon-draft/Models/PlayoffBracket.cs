using System.ComponentModel.DataAnnotations;

namespace PokemonDraft.Models;

public class PlayoffBracket
{
    [Key]
    public string LeagueCode { get; set; } = string.Empty;
    public League League { get; set; } = null!;
    public string ConfigurationJson { get; set; } = string.Empty;
    [ConcurrencyCheck]
    public Guid Revision { get; set; }
}
