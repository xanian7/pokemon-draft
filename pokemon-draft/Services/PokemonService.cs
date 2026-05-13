using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using PokemonDraft.DTOs;

namespace PokemonDraft.Services;

public class PokemonService(HttpClient httpClient, IMemoryCache cache) : IPokemonService
{
    private const string CacheKey = "all-pokemon";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);
    private const string GraphQlEndpoint = "https://beta.pokeapi.co/graphql/v1beta";

    private static readonly string[] CosmeticFormPrefixes =
        ["pikachu-", "minior-", "squawkabilly-", "koraidon-", "miraidon-"];

    private static readonly HashSet<string> CosmeticFormNames =
        ["eevee-starter", "magearna-original", "zarude-dada", "gimmighoul-roaming", "keldeo-resolute"];

    private const string GraphQlQuery = """
        query {
          pokemon_v2_pokemon(
            where: {
              pokemon_v2_pokemonforms: {
                is_battle_only: { _eq: false }
                is_mega: { _eq: false }
              }
            }
            order_by: { id: asc }
          ) {
            id
            name
            is_default
            pokemon_v2_pokemonspecy {
              id
            }
            pokemon_v2_pokemontypes {
              pokemon_v2_type {
                name
              }
            }
            pokemon_v2_pokemonstats {
              base_stat
            }
          }
        }
        """;

    public async Task<List<PokemonResponse>> GetAllPokemon()
    {
        if (cache.TryGetValue(CacheKey, out List<PokemonResponse>? cached) && cached is not null)
            return cached;

        var payload = JsonSerializer.Serialize(new { query = GraphQlQuery });
        var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync(GraphQlEndpoint, content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var apiResponse = JsonSerializer.Deserialize<PokeApiResponse>(json)
            ?? throw new InvalidOperationException("Failed to deserialize PokeAPI response.");

        var pokemon = apiResponse.Data.Pokemon
            .Where(p => !IsCosmeticForm(p.Name, p.IsDefault))
            .Select(p => new PokemonResponse(
                Id: p.Id,
                SpeciesId: p.Species?.Id ?? p.Id,
                Name: p.Name,
                SpriteUrl: $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/{p.Id}.png",
                Types: p.Types.Select(t => t.Type.Name).ToList(),
                Bst: p.Stats.Sum(s => s.BaseStat)
            ))
            .ToList();

        cache.Set(CacheKey, pokemon, CacheDuration);
        return pokemon;
    }

    public async Task<PokemonResponse?> GetPokemonById(int id)
    {
        var all = await GetAllPokemon();
        return all.Find(p => p.Id == id);
    }

    private static bool IsCosmeticForm(string name, bool isDefault)
    {
        if (isDefault) return false;
        if (name.EndsWith("-gmax")) return true;
        if (name.EndsWith("-totem") || name.Contains("-totem-")) return true;
        if (name.Contains("-power-construct")) return true;
        if (CosmeticFormNames.Contains(name)) return true;
        return CosmeticFormPrefixes.Any(name.StartsWith);
    }

    // ---- Internal deserialization models ----

    private sealed class PokeApiResponse
    {
        [JsonPropertyName("data")]
        public PokeApiData Data { get; set; } = new();
    }

    private sealed class PokeApiData
    {
        [JsonPropertyName("pokemon_v2_pokemon")]
        public List<PokeApiPokemon> Pokemon { get; set; } = [];
    }

    private sealed class PokeApiPokemon
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("is_default")]
        public bool IsDefault { get; set; }

        [JsonPropertyName("pokemon_v2_pokemonspecy")]
        public PokeApiSpecies? Species { get; set; }

        [JsonPropertyName("pokemon_v2_pokemontypes")]
        public List<PokeApiTypeEntry> Types { get; set; } = [];

        [JsonPropertyName("pokemon_v2_pokemonstats")]
        public List<PokeApiStatEntry> Stats { get; set; } = [];
    }

    private sealed class PokeApiSpecies
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
    }

    private sealed class PokeApiTypeEntry
    {
        [JsonPropertyName("pokemon_v2_type")]
        public PokeApiType Type { get; set; } = new();
    }

    private sealed class PokeApiType
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class PokeApiStatEntry
    {
        [JsonPropertyName("base_stat")]
        public int BaseStat { get; set; }
    }
}
